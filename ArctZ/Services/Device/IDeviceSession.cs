using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

public interface IDeviceSession
{
    ConnectionState ConnectionState { get; }

    DeviceStatus? DeviceStatus { get; }

    string? LastError { get; }

    event Action? ConnectionStateChanged;

    event Action? DeviceStatusChanged;

    event Action<CommandRejectedEventArgs>? CommandRejected;

    event Action<int>? AlarmTriggered;

    Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default);

    Task DisconnectAsync();

    void BeginJog();

    void UpdateJog(DualJoystickState state);

    void EndJog();

    Task<CommandResult> SendGCodeAsync(string line, CancellationToken cancellationToken = default);

    /// <summary>Discards all queued-but-not-yet-sent commands, resolving each as Aborted.</summary>
    void AbortPendingCommands();

    Task<CommandResult> HomeAsync(CancellationToken cancellationToken = default);

    Task<CommandResult> ResetAlarmAsync(CancellationToken cancellationToken = default);

    Task FeedHoldAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Halts the machine for good: cancels jog motion, drops everything still queued in the app,
    /// feed-holds, waits (up to <paramref name="timeout"/>) for a status report confirming the
    /// firmware buffer is empty, then soft-resets. Every command goes out unconditionally —
    /// whether or not a jog or a program was running, and whether or not the drain was confirmed.
    /// Deliberately takes no CancellationToken: the timeout is the only way out, so no caller can
    /// leave the machine half-stopped.
    /// </summary>
    /// <returns>True when the device confirmed an empty buffer before the timeout.</returns>
    Task<bool> StopAndDrainAsync(TimeSpan timeout);

    Task ResumeAsync(CancellationToken cancellationToken = default);
}
