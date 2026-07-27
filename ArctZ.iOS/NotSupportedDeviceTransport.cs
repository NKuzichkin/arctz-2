using System;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.iOS;

/// <summary>
/// CoreBluetooth on iOS is BLE-only; classic SPP needs an MFi-certified
/// ExternalAccessory integration, out of scope for this plan. Demo mode
/// (Task 16) is fully usable regardless.
/// </summary>
public sealed class NotSupportedDeviceTransport : IDeviceTransport
{
    public bool IsConnected => false;

    public event Action<string>? LineReceived { add { } remove { } }

    public event Action? Disconnected { add { } remove { } }

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Real Bluetooth is not available on this platform yet. Use Demo mode."));

    public Task DisconnectAsync() => Task.CompletedTask;

    public Task SendLineAsync(string line, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
