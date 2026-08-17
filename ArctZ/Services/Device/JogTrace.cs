using System;

namespace ArctZ.Services.Device;

/// <summary>
/// TEMPORARY diagnostic hook. The one thing a transport decorator cannot observe is when the
/// operator let go of the stick — it only sees bytes, and the gap between the last jog line and
/// the cancel has two possible causes (release, or flow control holding the send loop back).
/// JogScheduler writes the release here so the log can tell them apart. Remove with
/// JogDiagnosticsTransport.
/// </summary>
public static class JogTrace
{
    /// <summary>Null in production and in tests; set by the diagnostics transport while measuring.</summary>
    public static Action<string>? Sink;

    public static void Write(string message) => Sink?.Invoke(message);
}
