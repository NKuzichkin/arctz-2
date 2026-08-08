using System;
using System.Linq;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.Device;
using ArctZ.Tests.Services.Program;
using ArctZ.ViewModels;
using static ArctZ.Tests.TestSupport.AsyncAssert;

namespace ArctZ.Tests.ViewModels;

public class ProgramViewModelPlaybackTests
{
    private static ProgramViewModel CreateViewModel(out FakeDeviceTransport transport)
    {
        transport = new FakeDeviceTransport();
        var storage = new FakeProgramStorage();
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default));
        return new ProgramViewModel(connection, storage, new TrajectoryCompiler());
    }

    /// <summary>3 key points, 2 continuous-blend segments -> 2 compiled G1 steps, no G4.</summary>
    private static void SeedTwoSegmentProgram(ProgramViewModel vm, FakeDeviceTransport transport)
    {
        foreach (var pose in new[] { "0,0,0,0", "10,0,0,0", "20,0,0,0" })
        {
            transport.SimulateReceivedLine($"<Idle|WPos:{pose}|FS:0,0>");
            vm.CaptureKeyPointCommand.Execute(null);
        }

        for (var i = 0; i < vm.KeyPoints.Count; i++)
        {
            vm.KeyPoints[i] = vm.KeyPoints[i] with { FeedRateUnitsPerMin = 500, DwellSeconds = 0, Ease = EaseMode.None, ContinuousBlend = true };
        }
    }

    [Fact]
    public async Task PlayAsync_DispatchesAllStepsBeforeAwaitingAcks_ThenTracksProgress()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        Assert.Equal(2, transport.SentLines.Count(l => l.StartsWith("G1", StringComparison.Ordinal)));

        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.CurrentSegmentIndex == 0, TimeSpan.FromSeconds(1));
        Assert.Equal(0.5, vm.OverallProgress);

        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
        Assert.Equal(1, vm.CurrentSegmentIndex);
        Assert.Equal(1.0, vm.SegmentProgress);
        Assert.Equal(1.0, vm.OverallProgress);
    }

    [Fact]
    public async Task PlayAsync_ErrorOnFirstStep_MarksFaultedWithItsSegmentIndex()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("error:9");
        await playTask;

        Assert.Equal(PlaybackState.Faulted, vm.PlaybackState);
        Assert.Equal(0, vm.FaultedAtSegmentIndex);
        Assert.False(vm.IsProgramLocked);
    }

    [Fact]
    public async Task Pause_SendsFeedHold_PlayAgainSendsResumeWithoutRedispatching()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        var sentLinesBeforePause = transport.SentLines.Count;

        await vm.PauseCommand.ExecuteAsync(null);
        Assert.Contains((byte)'!', transport.SentRawBytes);
        Assert.Equal(PlaybackState.Paused, vm.PlaybackState);

        await vm.PlayCommand.ExecuteAsync(null);
        Assert.Contains((byte)'~', transport.SentRawBytes);
        Assert.Equal(sentLinesBeforePause, transport.SentLines.Count);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
    }

    [Fact]
    public async Task IsProgramLocked_TrueWhileRunningOrPaused_FalseOnceStopped()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        Assert.False(vm.IsProgramLocked);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        Assert.True(vm.IsProgramLocked);

        await vm.PauseCommand.ExecuteAsync(null);
        Assert.True(vm.IsProgramLocked);

        await vm.StopCommand.ExecuteAsync(null);
        Assert.False(vm.IsProgramLocked);

        transport.SimulateReceivedLine("ok"); // resolves the command already in flight so playTask completes
        await playTask;
    }

    [Fact]
    public async Task Stop_DiscardsQueuedButUnsentSteps_SoTheyAreNeverResentAfterTheInFlightAck()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        // Report an RX buffer that only fits one compiled line, so the second step
        // stays pending in the queue instead of being sent straight away.
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|Bf:15,25|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        Assert.Equal(1, transport.SentLines.Count(l => l.StartsWith("G1", StringComparison.Ordinal)));

        await vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(PlaybackState.Stopped, vm.PlaybackState);

        transport.SimulateReceivedLine("ok"); // resolves the one command that was already in flight
        await playTask;

        // Without AbortPendingCommands the ack would have pumped the leftover step out to the controller.
        Assert.Equal(1, transport.SentLines.Count(l => l.StartsWith("G1", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PlayAsync_AfterStop_SendsResumeBeforeDispatchingFreshProgram()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var firstPlayTask = vm.PlayCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok"); // resolves the command that was already in flight
        transport.SimulateReceivedLine("ok");
        await firstPlayTask;
        Assert.Equal(PlaybackState.Stopped, vm.PlaybackState);

        var rawBytesBeforeSecondPlay = transport.SentRawBytes.Count;
        var secondPlayTask = vm.PlayCommand.ExecuteAsync(null);

        // Without clearing the hold left by Stop's feed-hold, the controller
        // would keep ignoring motion commands even though PlaybackState looks Running.
        Assert.Contains((byte)'~', transport.SentRawBytes.Skip(rawBytesBeforeSecondPlay));
        Assert.Equal(4, transport.SentLines.Count(l => l.StartsWith("G1", StringComparison.Ordinal)));

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await secondPlayTask;
        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
    }

    [Fact]
    public async Task LinkLoss_DuringPlayback_PausesImmediatelyThenFaultsIfReconnectExhausted()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.ConnectFailuresRemaining = 10;

        _ = vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateDisconnect();

        Assert.Equal(PlaybackState.Paused, vm.PlaybackState);

        await WaitUntilAsync(() => vm.PlaybackState == PlaybackState.Faulted, TimeSpan.FromSeconds(3));

        Assert.Equal(PlaybackState.Faulted, vm.PlaybackState);
    }

    [Fact]
    public async Task PlayWhileReconnecting_IsIgnored_AndFaultedStillFiresOnceExhausted()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.ConnectFailuresRemaining = 10;

        _ = vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateDisconnect();
        Assert.Equal(PlaybackState.Paused, vm.PlaybackState);
        var sentLinesWhileReconnecting = transport.SentLines.Count;

        await vm.PlayCommand.ExecuteAsync(null); // ignored: still Reconnecting, not actually back
        Assert.Equal(PlaybackState.Paused, vm.PlaybackState);
        Assert.Equal(sentLinesWhileReconnecting, transport.SentLines.Count);

        await WaitUntilAsync(() => vm.PlaybackState == PlaybackState.Faulted, TimeSpan.FromSeconds(3));

        Assert.Equal(PlaybackState.Faulted, vm.PlaybackState);
    }

    [Fact]
    public void CurrentlyExecutingKeyPointId_IsNull_WhileIdle()
    {
        var vm = CreateViewModel(out var transport);
        SeedTwoSegmentProgram(vm, transport);

        Assert.Null(vm.CurrentlyExecutingKeyPointId);
    }

    [Fact]
    public async Task CurrentlyExecutingKeyPointId_TargetsFirstKeyPoint_AsSoonAsPlayStarts_BeforeAnyAck()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        Assert.Equal(vm.KeyPoints[0].Id, vm.CurrentlyExecutingKeyPointId);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    [Fact]
    public async Task CurrentlyExecutingKeyPointId_AdvancesWithEachSegmentAck_ThenClearsOnCompletion()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        Assert.Equal(vm.KeyPoints[0].Id, vm.CurrentlyExecutingKeyPointId);

        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.CurrentSegmentIndex == 0, TimeSpan.FromSeconds(1));
        Assert.Equal(vm.KeyPoints[1].Id, vm.CurrentlyExecutingKeyPointId);

        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
        Assert.Null(vm.CurrentlyExecutingKeyPointId);
    }

    [Fact]
    public async Task CurrentlyExecutingKeyPointId_StaysOnTarget_WhilePaused()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        await vm.PauseCommand.ExecuteAsync(null);

        Assert.Equal(PlaybackState.Paused, vm.PlaybackState);
        Assert.Equal(vm.KeyPoints[0].Id, vm.CurrentlyExecutingKeyPointId);

        await vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    [Fact]
    public async Task PlayAsync_DoesNotComplete_UntilTheMachineReportsIdleAfterTheLastAck()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));

        // Every line is acknowledged, but the controller acks on buffering — the machine is
        // still executing the moves, so the program is not finished.
        transport.SimulateReceivedLine("<Run|WPos:5.000,0.000,0.000,0.000|FS:500,0>");
        Assert.False(playTask.IsCompleted);
        Assert.Equal(PlaybackState.Running, vm.PlaybackState);
        Assert.True(vm.IsProgramLocked);

        // The first Idle report after the acks is what actually ends the run.
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
    }

    [Fact]
    public async Task Stop_DuringTheMotionTail_EndsTheRunWithoutWaitingForIdle()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Run|WPos:5.000,0.000,0.000,0.000|FS:500,0>");
        Assert.False(playTask.IsCompleted);

        // Stop is the operator's escape hatch while the machine finishes moving — it must not
        // hang waiting for an Idle report that a stopped run may never produce.
        await vm.StopCommand.ExecuteAsync(null);
        await playTask;

        Assert.Equal(PlaybackState.Stopped, vm.PlaybackState);
    }

    [Fact]
    public async Task Pause_DuringTheMotionTail_DoesNotCancelTheWait_AndResumeCompletesTheRun()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Run|WPos:5.000,0.000,0.000,0.000|FS:500,0>");

        // Pause is legal here (still Running) and must NOT cancel the motion-idle wait — the
        // machine reports Hold, not Idle, while held, so the same wait simply continues once
        // the operator resumes. If Pause instead cancelled/replaced the wait, playTask would
        // never resolve from the later Idle report below.
        await vm.PauseCommand.ExecuteAsync(null);
        Assert.False(playTask.IsCompleted);
        Assert.Equal(PlaybackState.Paused, vm.PlaybackState);

        // Resume needs no bookkeeping of its own: it just flips PlaybackState back to Running.
        await vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
    }

    [Fact]
    public async Task LinkLoss_DuringTheMotionTail_FaultsTheRunAndResolvesTheWait()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.ConnectFailuresRemaining = 10;

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));

        // Link loss while waiting for the machine to go Idle: no status report can ever resolve
        // the wait once the transport is gone, so Faulted is the only escape hatch besides Stop.
        transport.SimulateDisconnect();

        await WaitUntilAsync(() => vm.PlaybackState == PlaybackState.Faulted, TimeSpan.FromSeconds(3));

        // Must not hang: OnPlaybackStateChanged's Faulted branch resolves _motionIdleSignal.
        await playTask;
        Assert.True(playTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task PlayAsync_Completes_WhenTheProgramNeverLeavesIdle_AndTheFinalReportRepeatsTheStartingOne()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();

        // All three key points share one pose (the user's saved program that motivated this test
        // had 7 of 8 key points at an identical pose). A real controller running such a program
        // never leaves Idle and WPos never changes, so the completion report below is
        // byte-identical to the Idle report captures were seeded from. ConnectionViewModel's
        // [Reactive] DeviceStatus would silently dedup that and never raise PropertyChanged —
        // the motion-idle wait must instead be driven off IDeviceSession.DeviceStatusChanged,
        // which fires on every report regardless of value equality.
        for (var i = 0; i < 3; i++)
        {
            transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");
            vm.CaptureKeyPointCommand.Execute(null);
        }

        for (var i = 0; i < vm.KeyPoints.Count; i++)
        {
            vm.KeyPoints[i] = vm.KeyPoints[i] with { FeedRateUnitsPerMin = 500, DwellSeconds = 0, Ease = EaseMode.None, ContinuousBlend = true };
        }

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        // Don't hard-code the compiled step count — derive it from what the transport actually
        // received, since it depends on compiler internals (zero-distance segments, dwell lines)
        // this test isn't asserting about.
        var dispatchedLineCount = transport.SentLines.Count(l =>
            l.StartsWith("G1", StringComparison.Ordinal) || l.StartsWith("G4", StringComparison.Ordinal));
        Assert.True(dispatchedLineCount > 0, "Expected at least one compiled step for a 3-key-point program.");

        for (var i = 0; i < dispatchedLineCount; i++)
        {
            transport.SimulateReceivedLine("ok");
        }

        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));

        // Byte-identical to the Idle report fed during capture above.
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
    }

    [Fact]
    public async Task PlayAsync_PingPongMode_RunsForwardThenBackward_HighlightingPointsInReverseOnTheReturnLeg()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        vm.CompletionMode = ProgramCompletionMode.PingPong;
        vm.RepeatCount = 1;

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        Assert.Equal(vm.KeyPoints[0].Id, vm.CurrentlyExecutingKeyPointId);

        // Forward leg: 2 acks, no idle wait in between passes.
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.CurrentSegmentIndex == 0, TimeSpan.FromSeconds(1));
        Assert.Equal(vm.KeyPoints[1].Id, vm.CurrentlyExecutingKeyPointId);

        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(
            () => transport.SentLines.Count(l => l.StartsWith("G1", StringComparison.Ordinal)) == 4,
            TimeSpan.FromSeconds(1));
        Assert.False(vm.IsAwaitingMotionIdle, "no physical-idle wait should happen between the forward and backward legs");

        // Backward leg: highlight now counts down from the last key point.
        Assert.Equal(vm.KeyPoints[2].Id, vm.CurrentlyExecutingKeyPointId);

        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.CurrentSegmentIndex == 0 && vm.SegmentProgress == 1.0, TimeSpan.FromSeconds(1));
        Assert.Equal(vm.KeyPoints[1].Id, vm.CurrentlyExecutingKeyPointId);

        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        Assert.Equal(vm.KeyPoints[0].Id, vm.CurrentlyExecutingKeyPointId);

        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
        Assert.Null(vm.CurrentlyExecutingKeyPointId);
    }

    [Fact]
    public async Task PlayAsync_PingPongMode_RepeatsForwardBackwardPairUpToRepeatCount()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        vm.CompletionMode = ProgramCompletionMode.PingPong;
        vm.RepeatCount = 2;

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        // First pair, forward leg (2 acks) — backward leg is only dispatched once the forward
        // pass's own RunPassAsync call returns, so wait for its G1 lines to appear before
        // acking them (acking blind would race the dispatch and the "ok" would be dropped:
        // BufferAwareCommandQueue.Complete no-ops when nothing is in-flight yet).
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(
            () => transport.SentLines.Count(l => l.StartsWith("G1", StringComparison.Ordinal)) == 4,
            TimeSpan.FromSeconds(1));

        // First pair, backward leg (2 acks) — completes cycle 1. Not the last cycle
        // (RepeatCount = 2), so the second pair's forward leg dispatches immediately after,
        // with no idle wait in between.
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(
            () => transport.SentLines.Count(l => l.StartsWith("G1", StringComparison.Ordinal)) == 6,
            TimeSpan.FromSeconds(1));

        // Second pair, forward leg (2 acks) — its own backward leg dispatches right after.
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(
            () => transport.SentLines.Count(l => l.StartsWith("G1", StringComparison.Ordinal)) == 8,
            TimeSpan.FromSeconds(1));

        // Second (last) pair, backward leg (2 acks) — cycle 2 reaches RepeatCount, so the run
        // now waits for real motion to finish instead of dispatching a third pair.
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
        Assert.Equal(8, transport.SentLines.Count(l => l.StartsWith("G1", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PlayAsync_LoopMode_SendsReturnToStartMoveBetweenCyclesButNotAfterTheLastOne()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        vm.CompletionMode = ProgramCompletionMode.Loop;
        vm.RepeatCount = 2;

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        // Cycle 1: 2 forward G1 lines.
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");

        // The implicit return-to-start move is dispatched right after cycle 1's acks.
        await WaitUntilAsync(
            () => transport.SentLines.Contains("G1 X0 Y0 Z0 A0 F500"),
            TimeSpan.FromSeconds(1));
        Assert.False(vm.IsAwaitingMotionIdle, "the return-to-start move between cycles must not wait for physical idle");

        transport.SimulateReceivedLine("ok"); // acks the return-to-start move

        // Cycle 2 (the last one, RepeatCount == 2): 2 more forward G1 lines, no further return move.
        await WaitUntilAsync(
            () => transport.SentLines.Count(l => l.StartsWith("G1", StringComparison.Ordinal)) == 5,
            TimeSpan.FromSeconds(1));

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
        Assert.Equal(5, transport.SentLines.Count(l => l.StartsWith("G1", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PlayAsync_LoopMode_UnlimitedRepeatCount_KeepsRunningUntilStopped()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        vm.CompletionMode = ProgramCompletionMode.Loop;
        vm.RepeatCount = null;

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        // Run through 3 full cycles (2 forward acks + 1 return-move ack each) without ever completing.
        for (var i = 0; i < 3; i++)
        {
            // The forward pass for this cycle is only dispatched once the previous cycle's
            // return-to-start move ack has been processed and control has looped back around
            // (an async gap after the first cycle) — wait for its G1 lines to appear before
            // acking them, or the "ok"s race the dispatch and get dropped (BufferAwareCommandQueue.Complete
            // no-ops when nothing is in-flight yet).
            await WaitUntilAsync(
                () => transport.SentLines.Count(l => l.StartsWith("G1", StringComparison.Ordinal)) == (3 * i) + 2,
                TimeSpan.FromSeconds(1));
            transport.SimulateReceivedLine("ok");
            transport.SimulateReceivedLine("ok");
            await WaitUntilAsync(
                () => transport.SentLines.Count(l => l == "G1 X0 Y0 Z0 A0 F500") == i + 1,
                TimeSpan.FromSeconds(1));
            transport.SimulateReceivedLine("ok");
        }

        Assert.False(playTask.IsCompleted);
        Assert.Equal(PlaybackState.Running, vm.PlaybackState);

        await vm.StopCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok"); // resolves the command already in flight
        await playTask;

        Assert.Equal(PlaybackState.Stopped, vm.PlaybackState);
    }

    [Fact]
    public async Task PlayAsync_ReturnToStartOnFinish_MovesToFirstKeyPointAfterNaturalCompletion()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        vm.ReturnToStartOnFinish = true;

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");

        await WaitUntilAsync(() => transport.SentLines.Contains("G1 X0 Y0 Z0 A0 F500"), TimeSpan.FromSeconds(1));
        Assert.False(playTask.IsCompleted, "must wait for the return-to-start move's own physical completion before finishing");

        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
    }

    [Fact]
    public async Task Stop_WithReturnToStartOnFinishEnabled_DoesNotTriggerTheReturnMove()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        vm.ReturnToStartOnFinish = true;

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok"); // resolves the command already in flight
        await playTask;

        Assert.Equal(PlaybackState.Stopped, vm.PlaybackState);
        Assert.DoesNotContain("G1 X0 Y0 Z0 A0 F500", transport.SentLines);
    }
}
