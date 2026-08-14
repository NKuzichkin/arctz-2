using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.Bluetooth;
using Android.Content;
using ArctZ.Services.Device;

namespace ArctZ.Android;

/// <summary>
/// IDeviceEndpointProvider поверх BluetoothAdapter: спаренные устройства через
/// BondedDevices, поиск через ACTION_FOUND/ACTION_DISCOVERY_FINISHED, спаривание
/// через CreateBond + ACTION_BOND_STATE_CHANGED.
/// </summary>
public sealed class AndroidBluetoothEndpointProvider : IDeviceEndpointProvider
{
    private static readonly TimeSpan PairTimeout = TimeSpan.FromSeconds(60);

    private readonly AndroidPermissions _permissions;

    public AndroidBluetoothEndpointProvider(AndroidPermissions permissions)
    {
        _permissions = permissions;
    }

    public bool SupportsDiscovery => true;

    public async Task<IReadOnlyList<DeviceEndpointInfo>> GetKnownEndpointsAsync(CancellationToken cancellationToken = default)
    {
        var granted = await _permissions.RequestAsync(ConnectPermissions()).ConfigureAwait(false);
        if (!granted)
        {
            throw new InvalidOperationException("Нет разрешения на использование Bluetooth.");
        }

        var adapter = BluetoothAdapter.DefaultAdapter
            ?? throw new InvalidOperationException("Bluetooth недоступен на этом устройстве.");

        return adapter.BondedDevices?
            .Select(d => new DeviceEndpointInfo(d.Address!, d.Name ?? d.Address!, true))
            .ToList<DeviceEndpointInfo>()
            ?? new List<DeviceEndpointInfo>();
    }

    public IObservable<DeviceEndpointInfo> Discover() => Observable.Create<DeviceEndpointInfo>(observer =>
    {
        var adapter = BluetoothAdapter.DefaultAdapter;
        if (adapter is null)
        {
            observer.OnCompleted();
            return () => { };
        }

        // StartDiscovery() требует BLUETOOTH_SCAN/ACCESS_FINE_LOCATION, а сам метод
        // Observable.Create синхронный — запрос разрешения и остальная работа уходят
        // в фоновую задачу, отмена подписки коммуницируется через cts.
        var cts = new CancellationTokenSource();
        _ = StartDiscoveryAsync(observer, adapter, cts.Token);
        return () => cts.Cancel();
    });

    private async Task StartDiscoveryAsync(IObserver<DeviceEndpointInfo> observer, BluetoothAdapter adapter, CancellationToken cancellationToken)
    {
        var granted = await _permissions.RequestAsync(ScanPermissions()).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
        {
            // подписчик уже отписался, пока висел запрос разрешения — ни регистрировать
            // receiver, ни стартовать скан не нужно.
            return;
        }

        if (!granted)
        {
            observer.OnError(new InvalidOperationException("Нет разрешения на поиск Bluetooth-устройств."));
            return;
        }

        var context = global::Android.App.Application.Context;
        var receiver = new DiscoveryReceiver(observer);
        var registered = false;

        try
        {
            var filter = new IntentFilter();
            filter.AddAction(BluetoothDevice.ActionFound!);
            filter.AddAction(BluetoothAdapter.ActionDiscoveryFinished!);

            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                // ACTION_FOUND/ACTION_DISCOVERY_FINISHED приходят от процесса com.android.bluetooth
                // (свой UID, не "android"), поэтому система на API 33+ требует именно Exported —
                // с NotExported broadcast тихо отбрасывается ("Exported Denial" в BroadcastQueue),
                // без единого исключения на нашей стороне.
                context.RegisterReceiver(receiver, filter, ReceiverFlags.Exported);
            }
            else
            {
                context.RegisterReceiver(receiver, filter);
            }

            registered = true;

            if (cancellationToken.IsCancellationRequested)
            {
                context.UnregisterReceiver(receiver);
                return;
            }

            if (!adapter.StartDiscovery())
            {
                // StartDiscovery() возвращает false без исключения (адаптер выключен, занят
                // другой операцией и т.п.) — без этой проверки receiver остаётся висеть, а
                // список устройств молча так и не наполняется без единой ошибки в UI.
                context.UnregisterReceiver(receiver);
                observer.OnError(new InvalidOperationException("Не удалось запустить поиск Bluetooth-устройств. Проверьте, включён ли Bluetooth."));
                return;
            }
        }
        catch (Exception ex)
        {
            // Метод запущен как fire-and-forget Task (см. вызывающий код) — без этого catch
            // любое исключение здесь (например, из RegisterReceiver) навсегда теряется как
            // необработанное исключение задачи, наблюдатель не получает ни OnError, ни
            // OnCompleted, и UI зависает на "Стоп" без единой ошибки.
            if (registered)
            {
                context.UnregisterReceiver(receiver);
            }

            observer.OnError(ex);
            return;
        }

        // Срабатывает либо на Dispose() подписки, либо на естественное завершение скана:
        // DiscoveryReceiver.OnReceive вызывает observer.OnCompleted(), Rx автоматически
        // диспозит подписку (см. Observable.Create), что вызывает cts.Cancel() и этот колбэк.
        // CancellationTokenSource гарантирует однократный вызов колбэка независимо от того,
        // сколько раз был вызван Cancel().
        cancellationToken.Register(() =>
        {
            adapter.CancelDiscovery();
            context.UnregisterReceiver(receiver);
        });
    }

    public async Task<bool> PairAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var granted = await _permissions.RequestAsync(ConnectPermissions()).ConfigureAwait(false);
        if (!granted)
        {
            throw new InvalidOperationException("Нет разрешения на использование Bluetooth.");
        }

        var adapter = BluetoothAdapter.DefaultAdapter
            ?? throw new InvalidOperationException("Bluetooth недоступен на этом устройстве.");

        var device = adapter.GetRemoteDevice(deviceId)
            ?? throw new InvalidOperationException("Устройство не найдено.");

        if (device.BondState == Bond.Bonded)
        {
            return true;
        }

        var tcs = new TaskCompletionSource<bool>();
        var context = global::Android.App.Application.Context;
        var receiver = new BondStateReceiver(deviceId, tcs);
        var bondFilter = new IntentFilter(BluetoothDevice.ActionBondStateChanged);

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            // ACTION_BOND_STATE_CHANGED тоже приходит от com.android.bluetooth, а не "android" —
            // тот же случай, что и в Discover(): нужен Exported, иначе broadcast отбрасывается
            // системой без исключения, и PairAsync() зависал бы до PairTimeout без единой ошибки.
            context.RegisterReceiver(receiver, bondFilter, ReceiverFlags.Exported);
        }
        else
        {
            context.RegisterReceiver(receiver, bondFilter);
        }

        try
        {
            device.CreateBond();
            using var timeoutCts = new CancellationTokenSource(PairTimeout);
            using var registration = timeoutCts.Token.Register(() => tcs.TrySetResult(false));
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            context.UnregisterReceiver(receiver);
        }
    }

    private static string[] ConnectPermissions() =>
        OperatingSystem.IsAndroidVersionAtLeast(31)
            ? new[] { "android.permission.BLUETOOTH_CONNECT" }
            : new[] { "android.permission.BLUETOOTH" };

    // На API 31+ startDiscovery() требует BLUETOOTH_SCAN; на API ≤30 классический
    // discovery без ACCESS_FINE_LOCATION не отдаёт устройств.
    private static string[] ScanPermissions() =>
        OperatingSystem.IsAndroidVersionAtLeast(31)
            ? new[] { "android.permission.BLUETOOTH_SCAN" }
            : new[] { "android.permission.ACCESS_FINE_LOCATION" };

    private sealed class DiscoveryReceiver : BroadcastReceiver
    {
        private readonly IObserver<DeviceEndpointInfo> _observer;

        public DiscoveryReceiver(IObserver<DeviceEndpointInfo> observer)
        {
            _observer = observer;
        }

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent is null)
            {
                return;
            }

            if (intent.Action == BluetoothAdapter.ActionDiscoveryFinished)
            {
                _observer.OnCompleted();
                return;
            }

            if (intent.Action != BluetoothDevice.ActionFound)
            {
                return;
            }

#pragma warning disable CA1422, CS0618
            var device = intent.GetParcelableExtra(BluetoothDevice.ExtraDevice) as BluetoothDevice;
#pragma warning restore CA1422, CS0618

            if (device?.Address is null)
            {
                return;
            }

            _observer.OnNext(new DeviceEndpointInfo(device.Address, device.Name ?? device.Address, device.BondState == Bond.Bonded));
        }
    }

    private sealed class BondStateReceiver : BroadcastReceiver
    {
        private readonly string _deviceId;
        private readonly TaskCompletionSource<bool> _completion;

        public BondStateReceiver(string deviceId, TaskCompletionSource<bool> completion)
        {
            _deviceId = deviceId;
            _completion = completion;
        }

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action != BluetoothDevice.ActionBondStateChanged)
            {
                return;
            }

#pragma warning disable CA1422, CS0618
            var device = intent.GetParcelableExtra(BluetoothDevice.ExtraDevice) as BluetoothDevice;
#pragma warning restore CA1422, CS0618

            if (device?.Address != _deviceId)
            {
                return;
            }

            var bondState = (Bond)intent.GetIntExtra(BluetoothDevice.ExtraBondState, (int)Bond.None);
            if (bondState == Bond.Bonded)
            {
                _completion.TrySetResult(true);
            }
            else if (bondState == Bond.None)
            {
                _completion.TrySetResult(false);
            }
        }
    }
}
