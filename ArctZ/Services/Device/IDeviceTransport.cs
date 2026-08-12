using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

/// <summary>Byte-stream abstraction over whatever the platform gives us for the paired FluidNC device (BT SPP COM port, RFCOMM socket, ...).</summary>
public interface IDeviceTransport
{
    bool IsConnected { get; }

    /// <summary>
    /// Whether this transport can actually run in the current environment (e.g. false in a
    /// browser without Web Serial support). Platforms that are always usable don't override this.
    /// </summary>
    bool IsSupported => true;

    /// <summary>Raised for every line the device sends, newline already stripped.</summary>
    event Action<string>? LineReceived;

    /// <summary>Raised when the underlying link drops, whether requested or not.</summary>
    event Action? Disconnected;

    Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default);

    Task DisconnectAsync();

    /// <summary>Sends a newline-terminated G-code/$-command line.</summary>
    Task SendLineAsync(string line, CancellationToken cancellationToken = default);

    /// <summary>Sends a single realtime byte with no line terminator.</summary>
    Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default);
}
