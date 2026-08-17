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

    private static readonly DualJoystickState ReversedLeftX =
        new(new JoystickAxisInput(-1, 0, 1), new JoystickAxisInput(0, 0, 0));

    /// <summary>A 20-degree turn at unchanged force — inside both of the detector's thresholds.</summary>
    private static readonly DualJoystickState GentlyTurnedLeftX = new(
        new JoystickAxisInput(Math.Cos(20 * Math.PI / 180), Math.Sin(20 * Math.PI / 180), 1),
        new JoystickAxisInput(0, 0, 0));

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

    /// <summary>Same grbl #837 hazard as a mid-sweep cancel: releasing the stick must not let the
    /// cancel overtake a jog line the firmware has not acknowledged, or that line is planned after
    /// the flush and the machine runs one more block past the release.</summary>
    [Fact]
    public void Stop_WithJogsAwaitingAck_DefersCancelUntilTheyAreAcknowledged()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);
        _timer.RaiseElapsed();
        _timer.RaiseElapsed();

        _scheduler.Stop();
        Assert.Empty(_transport.SentRawBytes);

        Assert.True(_scheduler.TryHandleAck());
        Assert.Empty(_transport.SentRawBytes);

        Assert.True(_scheduler.TryHandleAck());
        Assert.Equal(new byte[] { 0x85 }, _transport.SentRawBytes);
    }

    /// <summary>The release is the last chance to flush the committed motion, so it must not be
    /// swallowed by the cooldown that throttles mid-sweep cancels.</summary>
    [Fact]
    public void Stop_WithinTheCancelCooldown_StillSendsJogCancel()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);
        _timer.RaiseElapsed();
        _scheduler.TryHandleAck();
        _scheduler.UpdateState(ReversedLeftX);

        _scheduler.Stop();

        Assert.Equal(new byte[] { 0x85, 0x85 }, _transport.SentRawBytes);
    }

    [Fact]
    public void Stop_WhileAMidSweepCancelIsDeferred_SendsOnlyOneJogCancel()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);
        _timer.RaiseElapsed();
        _timer.RaiseElapsed();
        _scheduler.UpdateState(ReversedLeftX);

        _scheduler.Stop();
        _scheduler.TryHandleAck();
        _scheduler.TryHandleAck();

        Assert.Equal(new byte[] { 0x85 }, _transport.SentRawBytes);
    }

    [Fact]
    public void Stop_ThenStart_StartsUnthrottledDespiteJogsLeftAwaitingAck()
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

    [Fact]
    public void UpdateState_ReversingWithNothingAwaitingAck_SendsJogCancelImmediately()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);
        _timer.RaiseElapsed();
        _scheduler.TryHandleAck();

        _scheduler.UpdateState(ReversedLeftX);

        Assert.Equal(new byte[] { 0x85 }, _transport.SentRawBytes);
    }

    /// <summary>A cancel that overtakes an unacknowledged jog leaves that jog inside the firmware's
    /// mc_line(), where it is planned after the flush and drives one more block in the direction
    /// just abandoned (grbl issues #95 and #837).</summary>
    [Fact]
    public void UpdateState_ReversingWithJogsAwaitingAck_DefersCancelUntilTheyAreAcknowledged()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);
        _timer.RaiseElapsed();
        _timer.RaiseElapsed();

        _scheduler.UpdateState(ReversedLeftX);
        Assert.Empty(_transport.SentRawBytes);

        _scheduler.TryHandleAck();
        Assert.Empty(_transport.SentRawBytes);

        _scheduler.TryHandleAck();
        Assert.Equal(new byte[] { 0x85 }, _transport.SentRawBytes);
    }

    [Fact]
    public void Tick_WhileACancelIsDeferred_SendsNoFurtherJog()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);
        _timer.RaiseElapsed();
        _scheduler.UpdateState(ReversedLeftX);

        _timer.RaiseElapsed();

        Assert.Single(_transport.SentLines);
    }

    [Fact]
    public void UpdateState_WithGradualTurn_SendsNoJogCancel()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);
        _timer.RaiseElapsed();
        _scheduler.TryHandleAck();

        _scheduler.UpdateState(GentlyTurnedLeftX);

        Assert.Empty(_transport.SentRawBytes);
    }

    [Fact]
    public void UpdateState_ReversingBeforeAnyJogWasSent_SendsNoJogCancel()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);

        _scheduler.UpdateState(ReversedLeftX);

        Assert.Empty(_transport.SentRawBytes);
    }

    [Fact]
    public void Tick_AfterAJogCancel_SendsTheNewDirection()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);
        _timer.RaiseElapsed();
        _scheduler.TryHandleAck();
        _scheduler.UpdateState(ReversedLeftX);

        _timer.RaiseElapsed();

        Assert.Equal(
            new[] { "$J=G91 G21 X2.5 Y0 Z0 A0 F1000", "$J=G91 G21 X-2.5 Y0 Z0 A0 F1000" },
            _transport.SentLines);
    }

    [Fact]
    public void UpdateState_ReversingAgainWithinTheCooldown_SendsNoSecondJogCancel()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);
        _timer.RaiseElapsed();
        _scheduler.TryHandleAck();
        _scheduler.UpdateState(ReversedLeftX);
        _timer.RaiseElapsed();
        _scheduler.TryHandleAck();

        _scheduler.UpdateState(FullLeftX);

        Assert.Equal(new byte[] { 0x85 }, _transport.SentRawBytes);
    }

    [Fact]
    public void UpdateState_ReversingAgainAfterTheCooldown_SendsASecondJogCancel()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);
        _timer.RaiseElapsed();
        _scheduler.TryHandleAck();
        _scheduler.UpdateState(ReversedLeftX);

        for (var tick = 0; tick < 3; tick++)
        {
            _timer.RaiseElapsed();
            _scheduler.TryHandleAck();
        }

        _scheduler.UpdateState(FullLeftX);

        Assert.Equal(new byte[] { 0x85, 0x85 }, _transport.SentRawBytes);
    }

    /// <summary>Nothing new has reached the machine since the first cancel, so there is no committed
    /// motion left for a second one to flush.</summary>
    [Fact]
    public void UpdateState_ReversingTwiceWithNoJogInBetween_SendsOnlyOneJogCancel()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);
        _timer.RaiseElapsed();
        _scheduler.TryHandleAck();

        _scheduler.UpdateState(ReversedLeftX);
        _scheduler.UpdateState(FullLeftX);

        Assert.Equal(new byte[] { 0x85 }, _transport.SentRawBytes);
    }

    /// <summary>Dragging the stick to dead centre without lifting the finger never reaches Stop(),
    /// so the cancel is the only thing that keeps the machine from coasting out the queued blocks.</summary>
    [Fact]
    public void UpdateState_ReturningTheStickToCentre_SendsJogCancel()
    {
        _scheduler.Start();
        _scheduler.UpdateState(FullLeftX);
        _timer.RaiseElapsed();
        _scheduler.TryHandleAck();

        _scheduler.UpdateState(new DualJoystickState(default, default));

        Assert.Equal(new byte[] { 0x85 }, _transport.SentRawBytes);
    }
}
