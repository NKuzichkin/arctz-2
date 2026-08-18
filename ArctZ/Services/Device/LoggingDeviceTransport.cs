using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

/// <summary>
/// Decorates an IDeviceTransport to expose every G-code/$-command line sent
/// to the device and every line the device answers with, for the diagnostic
/// logs. Realtime bytes (SendRawByteAsync: '?', '!', '~', jog-cancel) are not
/// text G-code lines and are intentionally not raised as LineSent.
/// </summary>
public sealed class LoggingDeviceTransport : IDeviceTransport, IDisposable
{
    private readonly IDeviceTransport _inner;

    public LoggingDeviceTransport(IDeviceTransport inner)
    {
        _inner = inner;

        // Subscribed here rather than passed through, so that LineReceivedLogged
        // fires whether or not anyone subscribed to LineReceived — the diagnostic
        // log must not depend on a session being wired up. The flip side is that
        // this handler outlives DeviceSession's own subscribe/unsubscribe cycle on
        // a transport that is a singleton for the real device, hence Dispose().
        _inner.LineReceived += OnInnerLineReceived;
    }

    /// <summary>Raised synchronously on the caller's thread for every line passed to SendLineAsync.</summary>
    public event Action<string>? LineSent;

    /// <summary>Raised synchronously for every line the device sent back, independently of LineReceived subscribers.</summary>
    public event Action<string>? LineReceivedLogged;

    public bool IsConnected => _inner.IsConnected;

    public bool IsSupported => _inner.IsSupported;

    public event Action<string>? LineReceived;

    /// <summary>Detaches from the wrapped transport. Required because the real-device
    /// transport is a singleton reused by every connect: without this, each new
    /// decorator would leave one more handler attached to it forever.</summary>
    public void Dispose()
    {
        _inner.LineReceived -= OnInnerLineReceived;
        LineReceived = null;
        LineReceivedLogged = null;
        LineSent = null;
    }

    private void OnInnerLineReceived(string line)
    {
        LineReceivedLogged?.Invoke(line);
        LineReceived?.Invoke(line);
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
