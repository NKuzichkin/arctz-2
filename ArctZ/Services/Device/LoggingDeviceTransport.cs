using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

/// <summary>
/// Decorates an IDeviceTransport to expose every G-code/$-command line sent
/// to the device, for a demo-mode diagnostic log. Realtime bytes
/// (SendRawByteAsync: '?', '!', '~', jog-cancel) are not text G-code lines
/// and are intentionally not raised as LineSent.
/// </summary>
public sealed class LoggingDeviceTransport : IDeviceTransport
{
    private readonly IDeviceTransport _inner;

    public LoggingDeviceTransport(IDeviceTransport inner)
    {
        _inner = inner;
    }

    /// <summary>Raised synchronously on the caller's thread for every line passed to SendLineAsync.</summary>
    public event Action<string>? LineSent;

    public bool IsConnected => _inner.IsConnected;

    public event Action<string>? LineReceived
    {
        add => _inner.LineReceived += value;
        remove => _inner.LineReceived -= value;
    }

    public event Action? Disconnected
    {
        add => _inner.Disconnected += value;
        remove => _inner.Disconnected -= value;
    }

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default) =>
        _inner.ConnectAsync(deviceId, cancellationToken);

    public Task DisconnectAsync() => _inner.DisconnectAsync();

    public Task SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        LineSent?.Invoke(line);
        return _inner.SendLineAsync(line, cancellationToken);
    }

    public Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default) =>
        _inner.SendRawByteAsync(value, cancellationToken);
}
