using System;
using System.Collections.Generic;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public sealed class JogScheduler : IJogScheduler
{
    private readonly IJogCommandFactory _commandFactory;
    private readonly ICommandSerializer _serializer;
    private readonly IDeviceTransport _transport;
    private readonly IRealtimeCommandChannel _realtimeChannel;
    private readonly IPeriodicTimer _timer;
    private readonly TimeSpan _interval;

    private readonly object _queueLock = new();
    private readonly Queue<Action> _eventQueue = new();
    private bool _isDraining;

    private DualJoystickState? _latestState;
    private MachinePose _latestPose = MachinePose.Zero;

    public JogScheduler(
        IJogCommandFactory commandFactory,
        ICommandSerializer serializer,
        IDeviceTransport transport,
        IRealtimeCommandChannel realtimeChannel,
        IPeriodicTimer timer,
        TimeSpan interval)
    {
        _commandFactory = commandFactory;
        _serializer = serializer;
        _transport = transport;
        _realtimeChannel = realtimeChannel;
        _timer = timer;
        _interval = interval;
        _timer.Elapsed += () => Enqueue(OnElapsedCore);
    }

    public bool IsActive { get; private set; }

    public void Start()
    {
        Enqueue(() => IsActive = true);
        _timer.Start(_interval);
    }

    public void UpdateState(DualJoystickState state) => Enqueue(() => _latestState = state);

    public void UpdateCurrentPose(MachinePose pose) => Enqueue(() => _latestPose = pose);

    public void Stop()
    {
        Enqueue(() =>
        {
            if (!IsActive)
            {
                return;
            }

            IsActive = false;
            _timer.Stop();
            _latestState = null;
            _ = _realtimeChannel.SendAsync(RealtimeCommand.JogCancel);
        });
    }

    private void OnElapsedCore()
    {
        if (!IsActive || _latestState is null)
        {
            return;
        }

        var command = _commandFactory.Create(_latestState.Value, _latestPose);
        var text = _serializer.Serialize(command);
        _ = _transport.SendLineAsync(text);
    }

    /// <summary>
    /// All state mutation and command dispatch is funneled through this single
    /// queue so the timer's background-thread callback and the UI thread's
    /// UpdateState/UpdateCurrentPose/Stop calls never touch _latestState/
    /// _latestPose concurrently. Whichever thread enqueues work also drains
    /// the queue (under the lock) if nothing else is already draining it, so
    /// callers observe their own action's effects synchronously — no
    /// dedicated worker thread or async API needed.
    /// </summary>
    private void Enqueue(Action action)
    {
        lock (_queueLock)
        {
            _eventQueue.Enqueue(action);
            if (_isDraining)
            {
                return;
            }

            _isDraining = true;
            try
            {
                while (_eventQueue.Count > 0)
                {
                    _eventQueue.Dequeue()();
                }
            }
            finally
            {
                _isDraining = false;
            }
        }
    }
}
