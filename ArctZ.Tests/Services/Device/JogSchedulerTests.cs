using System;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class JogSchedulerTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    private readonly FakeDeviceTransport _transport = new();
    private readonly ManualPeriodicTimer _timer = new();
    private readonly SerialEventQueue _eventQueue = new();
    private readonly JogScheduler _scheduler;

    private static readonly DualJoystickState FullLeftX =
        new(new JoystickAxisInput(1, 0, 1), new JoystickAxisInput(0, 0, 0));

    public JogSchedulerTests()
    {
        _scheduler = new JogScheduler(
            new JogCommandFactory(MachineLimits.Default, Interval, maxFeedUnitsPerMin: 1000.0, lookaheadFactor: 1.5),
            new FluidNcCommandSerializer(),
            _transport,
            new RealtimeCommandChannel(_transport),
            _timer,
            Interval,
            _eventQueue);
    }

    private static DeviceStatus Status(int plannerBlocksAvailable) =>
        new(MachineState.Jog, MachinePose.Zero, plannerBlocksAvailable, RxBytesAvailable: 128);

    [Fact]
    public void Start_StartsTimerAtConfiguredInterval()
    {
        _scheduler.Start();

        Assert.True(_scheduler.IsActive);
        Assert.True(_timer.IsRunning);
        Assert.Equal(Interval, _timer.LastInterval);
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
        _scheduler.UpdateState(FullLeftX);

        _timer.RaiseElapsed();

        Assert.Equal(new[] { "$J=G91 G21 X2.5 Y0 Z0 A0 F1000" }, _transport.SentLines);
    }

    [Fact]
    public void Tick_UsesLatestKnownPoseForClamping()
    {
        _scheduler.Start();
        _scheduler.UpdateStatus(new DeviceStatus(
            MachineState.Jog,
            new MachinePose(X: 64, Y: 0, Z: 0, A: 0),
            PlannerBlocksAvailable: null,
            RxBytesAvailable: null));
        _scheduler.UpdateState(FullLeftX);

        _timer.RaiseElapsed();

        Assert.Equal(new[] { "$J=G91 G21 X1 Y0 Z0 A0 F1000" }, _transport.SentLines);
    }

    [Fact]
    public void Tick_WithCenteredSticks_SendsNothing()
    {
        _scheduler.Start();
        _scheduler.UpdateState(new DualJoystickState(default, default));

        _timer.RaiseElapsed();

        Assert.Empty(_transport.SentLines);
    }

    [Fact]
    public void Tick_StopsSendingOnceMaxJogsAwaitAck()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);

        _timer.RaiseElapsed();
        _timer.RaiseElapsed();
        _timer.RaiseElapsed();
        _timer.RaiseElapsed();

        Assert.Equal(2, _transport.SentLines.Count);
    }

    [Fact]
    public void TryHandleAck_ReleasesOneOutstandingSlot()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);
        _timer.RaiseElapsed();
        _timer.RaiseElapsed();
        _timer.RaiseElapsed();

        Assert.True(_scheduler.TryHandleAck());
        _timer.RaiseElapsed();

        Assert.Equal(3, _transport.SentLines.Count);
    }

    [Fact]
    public void TryHandleAck_WithNoOutstandingJog_ReturnsFalse()
    {
        _scheduler.Start();

        Assert.False(_scheduler.TryHandleAck());
    }

    [Fact]
    public void Tick_WithDeepPlannerQueue_SendsNothing()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);
        _scheduler.UpdateStatus(Status(plannerBlocksAvailable: 15));
        _scheduler.UpdateStatus(Status(plannerBlocksAvailable: 11));

        _timer.RaiseElapsed();

        Assert.Empty(_transport.SentLines);
    }

    [Fact]
    public void Tick_WithShallowPlannerQueue_Sends()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);
        _scheduler.UpdateStatus(Status(plannerBlocksAvailable: 15));
        _scheduler.UpdateStatus(Status(plannerBlocksAvailable: 14));

        _timer.RaiseElapsed();

        Assert.Single(_transport.SentLines);
    }

    [Fact]
    public void Stop_StopsTimerAndSendsJogCancel()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);

        _scheduler.Stop();

        Assert.False(_scheduler.IsActive);
        Assert.False(_timer.IsRunning);
        Assert.Equal(new byte[] { 0x85 }, _transport.SentRawBytes);
    }

    [Fact]
    public void Stop_WhenNotActive_SendsNoJogCancel()
    {
        _scheduler.Stop();

        Assert.Empty(_transport.SentRawBytes);
    }

    /// <summary>DeviceSession stops the scheduler from inside its own event-queue actions (the
    /// disconnect handler), where an enqueued action is deferred rather than run inline.</summary>
    [Fact]
    public void Stop_CalledFromInsideTheEventQueue_StillSendsJogCancel()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);

        _eventQueue.Enqueue(() => _scheduler.Stop());

        Assert.Equal(new byte[] { 0x85 }, _transport.SentRawBytes);
        Assert.False(_scheduler.IsActive);
    }

    [Fact]
    public void Stop_ClearsOutstandingAcksSoTheNextJogStartsUnthrottled()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);
        _timer.RaiseElapsed();
        _timer.RaiseElapsed();
        _scheduler.Stop();

        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);
        _timer.RaiseElapsed();

        Assert.Equal(3, _transport.SentLines.Count);
    }

    [Fact]
    public void Tick_AfterStop_SendsNothing()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);
        _scheduler.Stop();

        _timer.RaiseElapsed();

        Assert.Empty(_transport.SentLines);
    }
}
