using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public sealed class BufferAwareCommandQueue : IBufferAwareCommandQueue
{
    private const int DefaultRxBytesAvailable = 128;

    private readonly IDeviceTransport _transport;
    private readonly object _lock = new();
    private readonly Queue<Entry> _pending = new();
    private readonly Queue<Entry> _inFlight = new();

    private int _rxBytesAvailable = DefaultRxBytesAvailable;
    private int _inFlightCharCount;
    private bool _exclusiveInFlight;

    public BufferAwareCommandQueue(IDeviceTransport transport)
    {
        _transport = transport;
    }

    public event Action<GCodeLineCommand, CommandResult>? CommandCompleted;

    public Task<CommandResult> EnqueueAsync(GCodeLineCommand command, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _pending.Enqueue(new Entry(command, completion));
            Pump();
        }

        return completion.Task;
    }

    public void UpdateBufferCapacity(int rxBytesAvailable, int plannerBlocksAvailable)
    {
        lock (_lock)
        {
            _rxBytesAvailable = rxBytesAvailable;
            Pump();
        }
    }

    public void HandleOk() => Complete(new CommandResult(CommandOutcome.Acknowledged, null), abortPending: false);

    public void HandleError(int code) => Complete(new CommandResult(CommandOutcome.Rejected, code), abortPending: true);

    public void AbortPending()
    {
        List<(GCodeLineCommand Command, CommandResult Result)> toNotify;

        lock (_lock)
        {
            toNotify = DrainPendingAsAborted();
        }

        foreach (var (command, result) in toNotify)
        {
            CommandCompleted?.Invoke(command, result);
        }
    }

    private void Complete(CommandResult inFlightResult, bool abortPending)
    {
        var toNotify = new List<(GCodeLineCommand Command, CommandResult Result)>();

        lock (_lock)
        {
            if (_inFlight.Count == 0)
            {
                return;
            }

            var resolved = _inFlight.Dequeue();
            _inFlightCharCount -= LineLength(resolved.Command);
            if (resolved.Command.IsExclusive)
            {
                _exclusiveInFlight = false;
            }

            resolved.Completion.SetResult(inFlightResult);
            toNotify.Add((resolved.Command, inFlightResult));

            if (abortPending)
            {
                toNotify.AddRange(DrainPendingAsAborted());
            }

            Pump();
        }

        foreach (var (command, result) in toNotify)
        {
            CommandCompleted?.Invoke(command, result);
        }
    }

    /// <summary>Caller must hold `_lock`. Resolves every pending (not-yet-sent) command as Aborted and returns them for notification after the lock is released.</summary>
    private List<(GCodeLineCommand Command, CommandResult Result)> DrainPendingAsAborted()
    {
        var aborted = new List<(GCodeLineCommand Command, CommandResult Result)>();

        while (_pending.Count > 0)
        {
            var entry = _pending.Dequeue();
            var result = new CommandResult(CommandOutcome.Aborted, null);
            entry.Completion.SetResult(result);
            aborted.Add((entry.Command, result));
        }

        return aborted;
    }

    /// <summary>Caller must hold `_lock`.</summary>
    private void Pump()
    {
        while (_pending.Count > 0 && !_exclusiveInFlight)
        {
            var next = _pending.Peek();

            if (next.Command.IsExclusive)
            {
                if (_inFlight.Count > 0)
                {
                    break;
                }

                _pending.Dequeue();
                _inFlight.Enqueue(next);
                _inFlightCharCount += LineLength(next.Command);
                _exclusiveInFlight = true;
                _ = _transport.SendLineAsync(next.Command.Line);
                break;
            }

            var length = LineLength(next.Command);
            if (_inFlightCharCount + length > _rxBytesAvailable - 1)
            {
                break;
            }

            _pending.Dequeue();
            _inFlight.Enqueue(next);
            _inFlightCharCount += length;
            _ = _transport.SendLineAsync(next.Command.Line);
        }
    }

    private static int LineLength(GCodeLineCommand command) => command.Line.Length + 1;

    private readonly record struct Entry(GCodeLineCommand Command, TaskCompletionSource<CommandResult> Completion);
}
