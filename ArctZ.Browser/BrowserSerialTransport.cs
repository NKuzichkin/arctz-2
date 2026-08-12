using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Browser;

/// <summary>
/// Real transport for the browser head: navigator.serial reaches the same OS-level
/// virtual COM port that a paired Bluetooth Classic SPP FluidNC device exposes on
/// Desktop (see DesktopSerialTransport) - just through JS interop instead of
/// System.IO.Ports. `deviceId` passed to ConnectAsync is unused: Web Serial has no
/// stable string port identifier, selection happens through the browser's picker
/// and its remembered per-origin permissions instead.
/// </summary>
public sealed class BrowserSerialTransport : IDeviceTransport
{
    private static BrowserSerialTransport? _active;

    public BrowserSerialTransport()
    {
        _active = this;
        IsSupported = SerialInterop.IsSupported();
    }

    public bool IsSupported { get; }

    public bool IsConnected { get; private set; }

    public event Action<string>? LineReceived;

    public event Action? Disconnected;

    public async Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        // DeviceSession's reconnect loop calls ConnectAsync again without a user
        // gesture, so re-showing the picker would silently fail (browsers require
        // a gesture for requestPort()). Reusing the already-granted port via
        // reopenSavedPort() covers both the first connect after a previous grant
        // and every automatic reconnect after that.
        var reopened = await SerialInterop.ReopenSavedPortAsync();
        if (!reopened)
        {
            var requested = await SerialInterop.RequestPortAsync();
            if (!requested)
            {
                throw new InvalidOperationException("No serial port was selected.");
            }
        }

        IsConnected = true;
    }

    public async Task DisconnectAsync()
    {
        await SerialInterop.ClosePortAsync();
        IsConnected = false;
    }

    public Task SendLineAsync(string line, CancellationToken cancellationToken = default) =>
        SerialInterop.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"));

    public Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default) =>
        SerialInterop.WriteAsync(new[] { value });

    internal static void RaiseLineReceived(string line) => _active?.OnLineReceived(line);

    internal static void RaiseDisconnected() => _active?.OnDisconnected();

    private void OnLineReceived(string line) => LineReceived?.Invoke(line);

    private void OnDisconnected()
    {
        IsConnected = false;
        Disconnected?.Invoke();
    }
}
