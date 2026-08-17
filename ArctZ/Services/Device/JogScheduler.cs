using System;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public sealed class JogScheduler : IJogScheduler
{
    /// <summary>Jog lines the device has not acknowledged yet. Bounds how far the send loop can
    /// run ahead between status reports, which are too infrequent to throttle on their own.</summary>
    private const int MaxOutstandingJogs = 2;

    /// <summary>Blocks we allow to sit in the device's planner. Deep enough that link jitter
    /// cannot empty it, shallow enough that the machine stays responsive to the stick.</summary>
    private const int MaxQueuedPlannerBlocks = 3;

    /// <summary>Below this the serializer's 3-decimal format emits a zero-distance jog, which
    /// the firmware rejects with an error.</summary>
    private const double MinStep = 0.001;

    /// <summary>Ticks that must pass between two jog cancels. Sweeping the stick smoothly through a
    /// circle turns roughly one threshold's worth of angle per tick, and cancelling that often would
    /// keep the planner empty — the stutter the buffered send loop exists to avoid.</summary>
    private const int CancelCooldownTicks = 3;

    private readonly IJogCommandFactory _commandFactory;
    private readonly ICommandSerializer _serializer;
    private readonly IDeviceTransport _transport;
    private readonly IRealtimeCommandChannel _realtimeChannel;
    private readonly IPeriodicTimer _timer;
    private readonly TimeSpan _interval;
    private readonly ISerialEventQueue _eventQueue;
    private readonly JogDirectionChangeDetector _changeDetector;

    private DualJoystickState? _latestState;
    private MachinePose _latestPose = MachinePose.Zero;
    // Read outside the event queue by Stop(), so it must not be cached in a register there.
    private volatile bool _isActive;
    private int _outstandingJogs;
    private int _queuedPlannerBlocks;
    private int _plannerCapacity;

    /// <summary>Stick state behind the last jog line sent — what the machine is actually executing,
    /// which is what a new stick position has to be judged against.</summary>
    private DualJoystickState? _committedState;
    private bool _cancelPending;
    private bool _hasUncancelledMotion;
    private long _tickCount;
    private long _lastCancelTick = -CancelCooldownTicks;

    public JogScheduler(
        IJogCommandFactory commandFactory,
        ICommandSerializer serializer,
        IDeviceTransport transport,
        IRealtimeCommandChannel realtimeChannel,
        IPeriodicTimer timer,
        TimeSpan interval,
        ISerialEventQueue eventQueue,
        JogDirectionChangeDetector? changeDetector = null)
    {
        _commandFactory = commandFactory;
        _serializer = serializer;
        _transport = transport;
        _realtimeChannel = realtimeChannel;
        _timer = timer;
        _interval = interval;
        _eventQueue = eventQueue;
        _changeDetector = changeDetector ?? new JogDirectionChangeDetector();
        _timer.Elapsed += () => _eventQueue.Enqueue(OnElapsedCore);
    }

    public bool IsActive => _isActive;

    public void Start()
    {
        _eventQueue.Enqueue(() =>
        {
            _isActive = true;
            _outstandingJogs = 0;
            _queuedPlannerBlocks = 0;
            _committedState = null;
            _cancelPending = false;
            _hasUncancelledMotion = false;
            _tickCount = 0;
            _lastCancelTick = -CancelCooldownTicks;
        });
        _timer.Start(_interval);
    }

    public void UpdateState(DualJoystickState state) => _eventQueue.Enqueue(() =>
    {
        _latestState = state;

        if (!_isActive || _committedState is not { } committed)
        {
            return;
        }

        if (!_changeDetector.IsSharpChange(committed, state))
        {
            return;
        }

        // Adopt the new position as the reference right away: the rest of the swing would otherwise
        // keep measuring against the abandoned direction and ask for a cancel on every sample.
        _committedState = state;
        RequestCancel();
    });

    public void UpdateStatus(DeviceStatus status) => _eventQueue.Enqueue(() =>
    {
        _latestPose = status.WPos;

        if (status.PlannerBlocksAvailable is not { } available)
        {
            return;
        }

        // The firmware reports free slots, not the total, so the largest free count seen (which
        // occurs when the machine is idle) stands in for the planner's capacity.
        _plannerCapacity = Math.Max(_plannerCapacity, available);
        _queuedPlannerBlocks = _plannerCapacity - available;
    });

    public bool TryHandleAck()
    {
        var consumed = false;
        _eventQueue.Enqueue(() =>
        {
            if (_outstandingJogs > 0)
            {
                _outstandingJogs--;
                consumed = true;
            }

            TrySendPendingCancel();
        });

        return consumed;
    }

    public void Stop()
    {
        if (!_isActive)
        {
            return;
        }

        // Cleared synchronously, before the cancel goes out: the enqueued reset below runs a whole
        // timer tick later at worst (and not at all until the queue drains, for callers already
        // inside it), leaving a window where OnElapsedCore would emit one more jog that reaches
        // the controller after the cancel — a lurch right after the machine has smoothly stopped.
        _isActive = false;

        _eventQueue.Enqueue(() =>
        {
            _timer.Stop();
            _latestState = null;
            _committedState = null;

            // Unconditional, unlike RequestCancel: the release is the last chance to flush the
            // committed motion, so neither the cooldown nor an empty _hasUncancelledMotion may
            // swallow it — a redundant cancel on an already-flushed machine costs nothing.
            // _outstandingJogs is deliberately left alone so the same ack gate applies here; the
            // next Start() resets it.
            _cancelPending = true;
            TrySendPendingCancel();
        });
    }

    /// <summary>Asks for the committed motion to be flushed. Does nothing when the machine has
    /// nothing of ours left to flush, or when the previous cancel is too recent.</summary>
    private void RequestCancel()
    {
        if (_cancelPending || !_hasUncancelledMotion || _tickCount - _lastCancelTick < CancelCooldownTicks)
        {
            return;
        }

        _cancelPending = true;
        TrySendPendingCancel();
    }

    /// <summary>Holds the cancel back until every jog line has been acknowledged. A cancel that
    /// overtakes an unacknowledged jog leaves that jog inside the firmware's mc_line(), which plans
    /// it after the flush and drives one more block in the direction just abandoned (grbl issues
    /// #95 and #837) — the fix upstream settled on is to send the cancel only after the ok.</summary>
    private void TrySendPendingCancel()
    {
        if (!_cancelPending || _outstandingJogs > 0)
        {
            return;
        }

        _cancelPending = false;
        _hasUncancelledMotion = false;
        // The cancel empties the planner, so the stale free-slot count from the last status report
        // must not keep gating the send loop until the next one arrives.
        _queuedPlannerBlocks = 0;
        _lastCancelTick = _tickCount;

        // Sent outside any queued write: the cancel must not wait behind a jog blocked on a
        // saturated link.
        _ = _realtimeChannel.SendAsync(RealtimeCommand.JogCancel);
    }

    private void OnElapsedCore()
    {
        _tickCount++;

        if (!_isActive || _latestState is null)
        {
            return;
        }

        // Sending now would both keep _outstandingJogs above zero — starving the cancel that is
        // waiting on it — and add a block the cancel is about to flush anyway.
        if (_cancelPending)
        {
            return;
        }

        if (_outstandingJogs >= MaxOutstandingJogs || _queuedPlannerBlocks >= MaxQueuedPlannerBlocks)
        {
            return;
        }

        var command = _commandFactory.Create(_latestState.Value, _latestPose);
        if (!HasMotion(command.Deltas))
        {
            return;
        }

        _outstandingJogs++;
        _committedState = _latestState;
        _hasUncancelledMotion = true;
        var text = _serializer.Serialize(command);
        _ = _transport.SendLineAsync(text);
    }

    private static bool HasMotion(MachinePose deltas) =>
        Math.Abs(deltas.X) >= MinStep ||
        Math.Abs(deltas.Y) >= MinStep ||
        Math.Abs(deltas.Z) >= MinStep ||
        Math.Abs(deltas.A) >= MinStep;
}
