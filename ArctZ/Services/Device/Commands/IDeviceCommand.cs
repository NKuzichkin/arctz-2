using System;

namespace ArctZ.Services.Device.Commands;

public interface IDeviceCommand
{
}

/// <summary>A relative jog move built from the joystick each throttle tick.</summary>
public sealed record JogCommand(MachinePose Deltas, double Feed) : IDeviceCommand;

/// <summary>A single queued G-code or $-settings line (e.g. "$H", "G28").</summary>
public sealed record GCodeLineCommand(string Line) : IDeviceCommand
{
    /// <summary>
    /// $-prefixed lines (settings, $H, $X, ...) touch EEPROM and must never
    /// be pipelined with other commands — see BufferAwareCommandQueue.
    /// </summary>
    public bool IsExclusive => Line.StartsWith("$", StringComparison.Ordinal);
}

/// <summary>A single-byte realtime command sent immediately, outside the buffered queue.</summary>
public sealed record RealtimeCommand(byte Value) : IDeviceCommand
{
    public static readonly RealtimeCommand StatusQuery = new((byte)'?');
    public static readonly RealtimeCommand FeedHold = new((byte)'!');
    public static readonly RealtimeCommand CycleStartResume = new((byte)'~');
    public static readonly RealtimeCommand JogCancel = new(0x85);
}
