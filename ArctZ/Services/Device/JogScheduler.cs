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
        _timer.Elapsed += OnElapsed;
    }

    public bool IsActive { get; private set; }

    public void Start()
    {
        IsActive = true;
        _timer.Start(_interval);
    }

    public void UpdateState(DualJoystickState state) => _latestState = state;

    public void UpdateCurrentPose(MachinePose pose) => _latestPose = pose;

    public void Stop()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        _timer.Stop();
        _latestState = null;
        _ = _realtimeChannel.SendAsync(RealtimeCommand.JogCancel);
    }

    private void OnElapsed()
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
