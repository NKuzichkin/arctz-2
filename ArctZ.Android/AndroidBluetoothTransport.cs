using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android.Bluetooth;
using ArctZ.Services.Device;
using Java.Util;

namespace ArctZ.Android;

/// <summary>
/// Реальный транспорт для Android: Bluetooth Classic RFCOMM/SPP к FluidNC
/// (только ESP32 WROOM/WROVER отдают BT-SPP; BLE тут не подходит).
/// `deviceId`, передаваемый в ConnectAsync, — MAC-адрес устройства.
/// </summary>
public sealed class AndroidBluetoothTransport : IDeviceTransport
{
    private static readonly UUID SppUuid = UUID.FromString("00001101-0000-1000-8000-00805F9B34FB")!;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    private const int ReadBufferSize = 1024;

    private readonly AndroidPermissions _permissions;
    private readonly LineAssembler _lineAssembler = new();

    // Сериализует запись в OutputStream между SendLineAsync и SendRawByteAsync, чтобы
    // realtime-байты (?, !, ~, 0x18) не вклинивались в середину G-code-строки — оба метода
    // делят один и тот же семафор.
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private BluetoothSocket? _socket;
    private CancellationTokenSource? _readLoopCts;

    public AndroidBluetoothTransport(AndroidPermissions permissions)
    {
        _permissions = permissions;
    }

    public bool IsSupported => BluetoothAdapter.DefaultAdapter is not null;

    public bool IsConnected => _socket?.IsConnected ?? false;

    public event Action<string>? LineReceived;

    public event Action? Disconnected;

    public async Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        // DeviceSession вызывает ConnectAsync повторно в цикле переподключения без
        // промежуточного DisconnectAsync — тот же приём, что в DesktopSerialTransport.
        CloseSocket();

        var granted = await _permissions.RequestAsync(ConnectPermissions()).ConfigureAwait(false);
        if (!granted)
        {
            throw new InvalidOperationException("Нет разрешения на подключение по Bluetooth.");
        }

        var adapter = BluetoothAdapter.DefaultAdapter
            ?? throw new InvalidOperationException("Bluetooth недоступен на этом устройстве.");

        adapter.CancelDiscovery();

        var device = adapter.GetRemoteDevice(deviceId)
            ?? throw new InvalidOperationException("Устройство не найдено.");

        var socket = device.CreateRfcommSocketToServiceRecord(SppUuid)
            ?? throw new InvalidOperationException("Не удалось создать соединение с устройством.");

        // Connect() блокируется без собственного таймаута: если контроллер не отвечает на
        // установку RFCOMM-канала, поток висит бесконечно, и команда подключения в UI не
        // завершается никогда. Ограничиваем ожидание и сообщаем об этом пользователю.
        var connectTask = Task.Run(() => socket.Connect(), cancellationToken);
        var completed = await Task.WhenAny(connectTask, Task.Delay(ConnectTimeout, cancellationToken)).ConfigureAwait(false);

        if (completed != connectTask)
        {
            // socket.Connect() — блокирующий Java-вызов, C#-таймаут его не прерывает; штатный
            // способ прервать зависшее RFCOMM-подключение — закрыть сокет из другого потока
            // (см. документацию BluetoothSocket.connect()), это разблокирует поток connectTask.
            try
            {
                socket.Close();
            }
            catch (Java.IO.IOException)
            {
            }

            throw new InvalidOperationException("Устройство не отвечает на подключение. Проверьте, включён ли Bluetooth на контроллере, и попробуйте перезагрузить его.");
        }

        await connectTask.ConfigureAwait(false);

        _socket = socket;
        var cts = new CancellationTokenSource();
        _readLoopCts = cts;
        _ = Task.Factory.StartNew(
            () => ReadLoop(socket, cts.Token),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public Task DisconnectAsync()
    {
        CloseSocket();
        return Task.CompletedTask;
    }

    public async Task SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        var socket = _socket;
        if (socket?.OutputStream is null)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                socket.OutputStream.Write(bytes, 0, bytes.Length);
                socket.OutputStream.Flush();
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default)
    {
        var socket = _socket;
        if (socket?.OutputStream is null)
        {
            return;
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                socket.OutputStream.Write(new[] { value }, 0, 1);
                socket.OutputStream.Flush();
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // POST_NOTIFICATIONS идёт вместе с разрешением на Bluetooth: подключение — единственный
    // момент, когда пользователь заведомо смотрит на экран, а уведомление фонового сеанса нужно
    // ровно с этого момента. Отказ ничего не ломает: сервис работает, просто уведомления не видно.
    private static string[] ConnectPermissions()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            return new[] { "android.permission.BLUETOOTH" };
        }

        return OperatingSystem.IsAndroidVersionAtLeast(33)
            ? new[] { "android.permission.BLUETOOTH_CONNECT", "android.permission.POST_NOTIFICATIONS" }
            : new[] { "android.permission.BLUETOOTH_CONNECT" };
    }

    private void ReadLoop(BluetoothSocket socket, CancellationToken cancellationToken)
    {
        var stream = socket.InputStream;
        if (stream is null)
        {
            RaiseDisconnected();
            return;
        }

        var buffer = new byte[ReadBufferSize];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                foreach (var line in _lineAssembler.Append(buffer, read))
                {
                    LineReceived?.Invoke(line);
                }
            }
        }
        catch (Java.IO.IOException)
        {
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            RaiseDisconnected();
        }
    }

    private void RaiseDisconnected()
    {
        CloseSocket();
        Disconnected?.Invoke();
    }

    private void CloseSocket()
    {
        _readLoopCts?.Cancel();
        _readLoopCts = null;

        var socket = _socket;
        _socket = null;
        if (socket is null)
        {
            return;
        }

        try
        {
            socket.Close();
        }
        catch (Java.IO.IOException)
        {
        }
    }
}
