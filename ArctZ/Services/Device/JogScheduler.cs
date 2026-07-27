using System;
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
    private readonly ISerialEventQueue _eventQueue;

    private DualJoystickState? _latestState;
    private MachinePose _latestPose = MachinePose.Zero;

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

    public bool IsActive { get; private set; }

    public void Start()
    {
        _eventQueue.Enqueue(() => IsActive = true);
        _timer.Start(_interval);
    }

    public void UpdateState(DualJoystickState state) => _eventQueue.Enqueue(() => _latestState = state);

    public void UpdateCurrentPose(MachinePose pose) => _eventQueue.Enqueue(() => _latestPose = pose);

    public void Stop()
    {
        _eventQueue.Enqueue(() =>
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
}
