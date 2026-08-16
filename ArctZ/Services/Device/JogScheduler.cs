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

    private readonly IJogCommandFactory _commandFactory;
    private readonly ICommandSerializer _serializer;
    private readonly IDeviceTransport _transport;
    private readonly IRealtimeCommandChannel _realtimeChannel;
    private readonly IPeriodicTimer _timer;
    private readonly TimeSpan _interval;
    private readonly ISerialEventQueue _eventQueue;

    private DualJoystickState? _latestState;
    private MachinePose _latestPose = MachinePose.Zero;
    // Read outside the event queue by Stop(), so it must not be cached in a register there.
    private volatile bool _isActive;
    private int _outstandingJogs;
    private int _queuedPlannerBlocks;
    private int _plannerCapacity;

    public JogScheduler(
        IJogCommandFactory commandFactory,
        ICommandSerializer serializer,
        IDeviceTransport transport,
        IRealtimeCommandChannel realtimeChannel,
        IPeriodicTimer timer,
        TimeSpan interval,
        ISerialEventQueue eventQueue)
    {
        _commandFactory = commandFactory;
        _serializer = serializer;
        _transport = transport;
        _realtimeChannel = realtimeChannel;
        _timer = timer;
        _interval = interval;
        _eventQueue = eventQueue;
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
        });
        _timer.Start(_interval);
    }

    public void UpdateState(DualJoystickState state) => _eventQueue.Enqueue(() => _latestState = state);

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

        // Sent outside the enqueued action: the cancel is the one command that must not wait
        // behind a jog write blocked on a saturated link.
        _ = _realtimeChannel.SendAsync(RealtimeCommand.JogCancel);

        _eventQueue.Enqueue(() =>
        {
            _timer.Stop();
            _latestState = null;
            _outstandingJogs = 0;
            _queuedPlannerBlocks = 0;
        });
    }

    private void OnElapsedCore()
    {
        if (!_isActive || _latestState is null)
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
        var text = _serializer.Serialize(command);
        _ = _transport.SendLineAsync(text);
    }

    private static bool HasMotion(MachinePose deltas) =>
        Math.Abs(deltas.X) >= MinStep ||
        Math.Abs(deltas.Y) >= MinStep ||
        Math.Abs(deltas.Z) >= MinStep ||
        Math.Abs(deltas.A) >= MinStep;
}
