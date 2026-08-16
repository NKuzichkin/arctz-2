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

    public Task SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        try
        {
            _port?.WriteLine(line);
        }
        catch (IOException)
        {
            RaiseDisconnectedOnce();
        }
        catch (InvalidOperationException)
        {
            RaiseDisconnectedOnce();
        }

        return Task.CompletedTask;
    }

    public Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default)
    {
        try
        {
            _port?.Write(new[] { value }, 0, 1);
        }
        catch (IOException)
        {
            RaiseDisconnectedOnce();
        }
        catch (InvalidOperationException)
        {
            RaiseDisconnectedOnce();
        }

        return Task.CompletedTask;
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        var port = _port;
        if (port is null)
        {
            return;
        }

        try
        {
            while (port.BytesToRead > 0)
            {
                LineReceived?.Invoke(port.ReadLine());
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
