using System;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public enum CommandOutcome
{
    Acknowledged,
    Rejected,
    Aborted
}

public readonly record struct CommandResult(CommandOutcome Outcome, int? ErrorCode);

public interface IBufferAwareCommandQueue
{
    event Action<GCodeLineCommand, CommandResult>? CommandCompleted;

    Task<CommandResult> EnqueueAsync(GCodeLineCommand command, CancellationToken cancellationToken = default);

    /// <summary>Called whenever a status report carries a fresh Bf: reading.</summary>
    void UpdateBufferCapacity(int rxBytesAvailable, int plannerBlocksAvailable);

    /// <summary>Call when the transport receives a plain "ok" line.</summary>
    void HandleOk();

    /// <summary>Call when the transport receives an "error:N" line.</summary>
    void HandleError(int code);

    /// <summary>Discards all not-yet-sent pending commands, resolving each as Aborted. Does not affect a command already in flight.</summary>
    void AbortPending();
}
