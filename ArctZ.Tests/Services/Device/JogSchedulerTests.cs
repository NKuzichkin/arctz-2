using System;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class JogSchedulerTests
{
    private readonly FakeDeviceTransport _transport = new();
    private readonly ManualPeriodicTimer _timer = new();
    private readonly SerialEventQueue _eventQueue = new();
    private readonly JogScheduler _scheduler;

    public JogSchedulerTests()
    {
        _scheduler = new JogScheduler(
            new JogCommandFactory(MachineLimits.Default, maxStepDegrees: 5.0, maxFeedUnitsPerMin: 1000.0),
            new FluidNcCommandSerializer(),
            _transport,
            new RealtimeCommandChannel(_transport),
            _timer,
            TimeSpan.FromMilliseconds(100),
            _eventQueue);
    }

    [Fact]
    public void Start_StartsTimerAtConfiguredInterval()
    {
        _scheduler.Start();

        Assert.True(_scheduler.IsActive);
        Assert.True(_timer.IsRunning);
        Assert.Equal(TimeSpan.FromMilliseconds(100), _timer.LastInterval);
    }

    [Fact]
    public void Tick_WithNoState_SendsNothing()
    {
        _scheduler.Start();

        _timer.RaiseElapsed();

        Assert.Empty(_transport.SentLines);
    }

    [Fact]
    public void Tick_WithState_SendsSerializedJogLineForAllFourAxes()
    {
        _scheduler.Start();
        _scheduler.UpdateState(new DualJoystickState(new JoystickAxisInput(1, 0, 1), new JoystickAxisInput(0, 0, 0)));

        _timer.RaiseElapsed();

        Assert.Equal(new[] { "$J=G91 G21 X5 Y0 Z0 A0 F1000" }, _transport.SentLines);
    }

    [Fact]
    public void Tick_UsesLatestKnownPoseForClamping()
    {
        _scheduler.Start();
        _scheduler.UpdateCurrentPose(new MachinePose(X: 63, Y: 0, Z: 0, A: 0));
        _scheduler.UpdateState(new DualJoystickState(new JoystickAxisInput(1, 0, 1), new JoystickAxisInput(0, 0, 0)));

        _timer.RaiseElapsed();

        Assert.Equal(new[] { "$J=G91 G21 X2 Y0 Z0 A0 F1000" }, _transport.SentLines);
    }

    [Fact]
    public void Stop_StopsTimerAndSendsJogCancel()
    {
        _scheduler.Start();
        _scheduler.UpdateState(new DualJoystickState(new JoystickAxisInput(1, 0, 1), new JoystickAxisInput(0, 0, 0)));

        _scheduler.Stop();

        Assert.False(_scheduler.IsActive);
        Assert.False(_timer.IsRunning);
        Assert.Equal(new byte[] { 0x85 }, _transport.SentRawBytes);
    }

    [Fact]
    public void Tick_AfterStop_SendsNothing()
    {
        _scheduler.Start();
        _scheduler.UpdateState(new DualJoystickState(new JoystickAxisInput(1, 0, 1), new JoystickAxisInput(0, 0, 0)));
        _scheduler.Stop();

        _timer.RaiseElapsed();

        Assert.Empty(_transport.SentLines);
    }
}
