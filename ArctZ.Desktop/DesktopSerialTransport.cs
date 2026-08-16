using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Desktop;

/// <summary>
/// Real transport for Desktop: the OS exposes a paired Bluetooth Classic
/// SPP device as an ordinary COM port, so this is a thin SerialPort
/// wrapper. `deviceId` passed to ConnectAsync is the COM port name
/// (e.g. "COM5").
/// </summary>
public sealed class DesktopSerialTransport : IDeviceTransport
{
    private const int ReadBufferSize = 1024;

    private readonly LineAssembler _lineAssembler = new();
    private readonly byte[] _readBuffer = new byte[ReadBufferSize];

    // Serializes writes between SendLineAsync and SendRawByteAsync so a realtime byte (?, !, ~,
    // jog cancel) can never land in the middle of a G-code line, and so it is ordered strictly
    // after any line already handed over — a jog arriving after a jog-cancel restarts the motion
    // the cancel just stopped.
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private SerialPort? _port;
    private int _disconnectedRaised;

    public bool IsConnected => _port?.IsOpen ?? false;

    public event Action<string>? LineReceived;

    public event Action? Disconnected;

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        // DeviceSession's reconnect loop calls ConnectAsync again without an
        // intervening DisconnectAsync, so an already-open port would still hold
        // the OS handle and make every reconnect attempt fail.
        if (_port is not null)
        {
            _port.DataReceived -= OnDataReceived;
            _port.ErrorReceived -= OnErrorReceived;
            if (_port.IsOpen)
            {
                _port.Close();
            }

            _port.Dispose();
            _port = null;
        }

        _port = new SerialPort(deviceId, 115200) { NewLine = "\n", ReadTimeout = 200 };
        _port.DataReceived += OnDataReceived;
        _port.ErrorReceived += OnErrorReceived;
        _port.Open();
        Interlocked.Exchange(ref _disconnectedRaised, 0);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        if (_port is not null)
        {
            _port.DataReceived -= OnDataReceived;
            _port.ErrorReceived -= OnErrorReceived;
            _port.Close();
            _port.Dispose();
            _port = null;
        }

        return Task.CompletedTask;
    }

    public Task SendLineAsync(string line, CancellationToken cancellationToken = default) =>
        WriteAsync(port => port.WriteLine(line), cancellationToken);

    public Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default) =>
        WriteAsync(port => port.Write(new[] { value }, 0, 1), cancellationToken);

    /// <summary>
    /// Performs the blocking SerialPort write off the calling thread. Callers reach this from
    /// inside the shared SerialEventQueue lock, and over a Bluetooth SPP port a write can stall
    /// for tens of milliseconds — holding that lock also blocks the read path that delivers acks
    /// and status reports, starving the jog scheduler's flow control.
    /// </summary>
    private async Task WriteAsync(Action<SerialPort> write, CancellationToken cancellationToken)
    {
        var port = _port;
        if (port is null)
        {
            return;
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(
                () =>
                {
                    try
                    {
                        write(port);
                    }
                    catch (IOException)
                    {
                        RaiseDisconnectedOnce();
                    }
                    catch (InvalidOperationException)
                    {
                        RaiseDisconnectedOnce();
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        var port = _port;
        if (port is null)
        {
            return;
        }

        // Reads only what has already arrived and assembles lines incrementally. ReadLine() would
        // block here for up to ReadTimeout whenever the buffer holds a partial line, and this
        // handler is the only path delivering acks and status reports — stalling it throttles the
        // jog scheduler into starving the controller's planner, which the operator feels as
        // stuttering motion.
        try
        {
            while (port.BytesToRead > 0)
            {
                var read = port.Read(_readBuffer, 0, Math.Min(_readBuffer.Length, port.BytesToRead));
                if (read <= 0)
                {
                    break;
                }

                foreach (var line in _lineAssembler.Append(_readBuffer, read))
                {
                    LineReceived?.Invoke(line);
                }
            }
        }
        catch (TimeoutException)
        {
        }
        catch (IOException)
        {
            RaiseDisconnectedOnce();
        }
        catch (InvalidOperationException)
        {
            RaiseDisconnectedOnce();
        }
    }

    private void OnErrorReceived(object sender, SerialErrorReceivedEventArgs e) => RaiseDisconnectedOnce();

    /// <summary>Raises Disconnected at most once per ConnectAsync — a device going away can be
    /// detected from multiple call sites nearly simultaneously (a failing write AND a failing
    /// read), and DeviceSession.OnTransportDisconnected starts an independent reconnect loop per
    /// invocation with no generation bump of its own, so an unguarded second raise would drive
    /// two concurrent reconnect loops against the same SerialPort field.</summary>
    private void RaiseDisconnectedOnce()
    {
        if (Interlocked.Exchange(ref _disconnectedRaised, 1) == 0)
        {
            Disconnected?.Invoke();
        }
    }
}
