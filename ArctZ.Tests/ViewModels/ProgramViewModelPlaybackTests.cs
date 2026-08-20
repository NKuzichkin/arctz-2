using System;
using System.Linq;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.App;
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
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default), new SingleRealDeviceEndpointProvider());
        return new ProgramViewModel(connection, storage, new TrajectoryCompiler(), new FakeAppExitService());
    }

    /// <summary>
    /// 3 key points, all continuous-blend -> 3 compiled G1 steps, no G4: segment 0 is the
    /// self-move confirming the machine is at the first key point, segments 1-2 are the real
    /// moves between the points (JibProgram.Segments()).
    /// </summary>
    private static void SeedTwoSegmentProgram(ProgramViewModel vm, FakeDeviceTransport transport)
    {
        foreach (var pose in new[] { "0,0,0,0", "10,0,0,0", "20,0,0,0" })
        {
            transport.SimulateReceivedLine($"<Idle|WPos:{pose}|FS:0,0>");
            vm.CaptureKeyPointCommand.Execute(null);
        }

        for (var i = 0; i < vm.KeyPoints.Count; i++)
        {
            vm.KeyPoints[i] = vm.KeyPoints[i] with { TransitionSeconds = 5, DwellSeconds = 0, Ease = EaseMode.None, ContinuousBlend = true };
        }

        // Play now requires a saved, clean program (EnsureProgramSavedAsync); mark it
        // saved here so playback tests don't block forever on the unanswered save dialog.
        vm.ProgramId = Guid.NewGuid();
        vm.IsDirty = false;
    }

    [Fact]
    public async Task PlayAsync_DispatchesAllStepsBeforeAwaitingAcks_ThenTracksProgress()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        Assert.Equal(3, transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)));

        transport.SimulateReceivedLine("ok"); // acks segment 0 (self-move to the first key point)
        await WaitUntilAsync(() => vm.CurrentSegmentIndex == 0, TimeSpan.FromSeconds(1));
        Assert.Equal(1.0 / 3, vm.OverallProgress);

        transport.SimulateReceivedLine("ok"); // acks segment 1 (move to the second key point)
        await WaitUntilAsync(() => vm.CurrentSegmentIndex == 1, TimeSpan.FromSeconds(1));
        Assert.Equal(2.0 / 3, vm.OverallProgress);

        transport.SimulateReceivedLine("ok"); // acks segment 2 (move to the third key point)
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
        Assert.Equal(2, vm.CurrentSegmentIndex);
        Assert.Equal(1.0, vm.SegmentProgress);
        Assert.Equal(1.0, vm.OverallProgress);
    }

    /// <summary>
    /// A 3-point program (each point: dwell 3s, transition 3s) dispatches one G93+G4 pair
    /// per key point, including the first — see JibProgram.Segments().
    /// </summary>
    [Fact]
    public async Task PlayAsync_ThreePointProgram_SendsThreeG93PlusG4PairsWithCorrectParameters()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();

        foreach (var pose in new[] { "0,0,0,0", "10,10,0,0", "20,20,0,0" })
        {
            transport.SimulateReceivedLine($"<Idle|WPos:{pose}|FS:0,0>");
            vm.CaptureKeyPointCommand.Execute(null);
        }

        for (var i = 0; i < vm.KeyPoints.Count; i++)
        {
            vm.KeyPoints[i] = vm.KeyPoints[i] with { TransitionSeconds = 3, DwellSeconds = 3, Ease = EaseMode.None, ContinuousBlend = false };
        }

        vm.ProgramId = Guid.NewGuid();
        vm.IsDirty = false;

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        // F = 60 / TransitionSeconds = 60 / 3 = 20; dwell = "G4 P3" (DwellSeconds = 3).
        var expectedLines = new[]
        {
            "G93 G1 X0 Y0 Z0 A0 F20", "G4 P3",
            "G93 G1 X10 Y10 Z0 A0 F20", "G4 P3",
            "G93 G1 X20 Y20 Z0 A0 F20", "G4 P3",
        };
        var sentMoveAndDwellLines = transport.SentLines
            .Where(l => l.StartsWith("G93", StringComparison.Ordinal) || l.StartsWith("G4", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(expectedLines, sentMoveAndDwellLines);

        foreach (var _ in sentMoveAndDwellLines)
        {
            transport.SimulateReceivedLine("ok");
        }

        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,20.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
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
        Assert.Equal(1, transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)));

        await vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(PlaybackState.Stopped, vm.PlaybackState);

        transport.SimulateReceivedLine("ok"); // resolves the one command that was already in flight
        await playTask;

        // Without AbortPendingCommands the ack would have pumped the leftover step out to the controller.
        Assert.Equal(1, transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PlayAsync_AfterStop_SendsResumeBeforeDispatchingFreshProgram()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var firstPlayTask = vm.PlayCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        // Drains all 3 lines the first play had already dispatched (synchronously, before Stop
        // could intervene) — otherwise a leftover pending ack would consume one of the second
        // play's "ok"s below, throwing off its own count.
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await firstPlayTask;
        Assert.Equal(PlaybackState.Stopped, vm.PlaybackState);

        var rawBytesBeforeSecondPlay = transport.SentRawBytes.Count;
        var secondPlayTask = vm.PlayCommand.ExecuteAsync(null);

        // Without clearing the hold left by Stop's feed-hold, the controller
        // would keep ignoring motion commands even though PlaybackState looks Running.
        Assert.Contains((byte)'~', transport.SentRawBytes.Skip(rawBytesBeforeSecondPlay));
        Assert.Equal(6, transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal))); // 3 (first play) + 3 (second play)

        transport.SimulateReceivedLine("ok");
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

    /// <summary>
    /// Link loss while nothing has ever run leaves PlaybackState at Idle (ApplySessionConnectionState
    /// only reacts to Reconnecting while Running). CanPlay() must still refuse Play in that state,
    /// not only the Paused-resume branch, or a stale Play dispatches G-code into a dead transport
    /// and hangs on an ack that never arrives.
    /// </summary>
    [Fact]
    public async Task Play_IsRefused_WhenReconnectingBeforeAnyPlaybackHasStarted()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.ConnectFailuresRemaining = 10;

        transport.SimulateDisconnect();

        Assert.Equal(PlaybackState.Idle, vm.PlaybackState);
        Assert.False(vm.PlayCommand.CanExecute(null));

        var sentLinesBeforePlay = transport.SentLines.Count;
        await vm.PlayCommand.ExecuteAsync(null);

        Assert.Equal(PlaybackState.Idle, vm.PlaybackState);
        Assert.Equal(sentLinesBeforePlay, transport.SentLines.Count);
    }

    [Fact]
    public void CurrentlyExecutingKeyPointId_IsNull_WhileIdle()
    {
        var vm = CreateViewModel(out var transport);
        SeedTwoSegmentProgram(vm, transport);

        Assert.Null(vm.CurrentlyExecutingKeyPointId);
    }

    /// <summary>
    /// Segment 0 (the self-move confirming the machine is at the first key point) hasn't been
    /// acknowledged yet right after Play, so nothing is confirmed as "current" until its ack lands.
    /// </summary>
    [Fact]
    public async Task CurrentlyExecutingKeyPointId_IsNullBeforeAnyAck_ThenTargetsFirstKeyPointOnceItsSelfMoveIsAcked()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        Assert.Null(vm.CurrentlyExecutingKeyPointId);

        transport.SimulateReceivedLine("ok"); // acks segment 0 (self-move to the first key point)
        await WaitUntilAsync(() => vm.CurrentSegmentIndex == 0, TimeSpan.FromSeconds(1));
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
        Assert.Null(vm.CurrentlyExecutingKeyPointId);

        transport.SimulateReceivedLine("ok"); // acks segment 0 (self-move to the first key point)
        await WaitUntilAsync(() => vm.CurrentSegmentIndex == 0, TimeSpan.FromSeconds(1));
        Assert.Equal(vm.KeyPoints[0].Id, vm.CurrentlyExecutingKeyPointId);

        transport.SimulateReceivedLine("ok"); // acks segment 1 (move to the second key point)
        await WaitUntilAsync(() => vm.CurrentSegmentIndex == 1, TimeSpan.FromSeconds(1));
        Assert.Equal(vm.KeyPoints[1].Id, vm.CurrentlyExecutingKeyPointId);

        transport.SimulateReceivedLine("ok"); // acks segment 2 (move to the third key point)
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
        transport.SimulateReceivedLine("ok"); // acks segment 0, confirming we're at the first key point
        await WaitUntilAsync(() => vm.CurrentSegmentIndex == 0, TimeSpan.FromSeconds(1));

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
            vm.KeyPoints[i] = vm.KeyPoints[i] with { TransitionSeconds = 5, DwellSeconds = 0, Ease = EaseMode.None, ContinuousBlend = true };
        }

        // Play now requires a saved, clean program (EnsureProgramSavedAsync); mark it saved
        // here so this test doesn't block forever on the unanswered save dialog.
        vm.ProgramId = Guid.NewGuid();
        vm.IsDirty = false;

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        // Don't hard-code the compiled step count — derive it from what the transport actually
        // received, since it depends on compiler internals (zero-distance segments, dwell lines)
        // this test isn't asserting about.
        var dispatchedLineCount = transport.SentLines.Count(l =>
            l.StartsWith("G93", StringComparison.Ordinal) || l.StartsWith("G4", StringComparison.Ordinal));
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
        vm.IsDirty = false; // completion-settings changes above must not re-trigger the save gate

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        Assert.Null(vm.CurrentlyExecutingKeyPointId); // segment 0's self-move ack hasn't landed yet

        // Forward leg: 3 acks (self-move to point 0, then the two real moves), no idle wait in between passes.
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.CurrentSegmentIndex == 0, TimeSpan.FromSeconds(1));
        Assert.Equal(vm.KeyPoints[0].Id, vm.CurrentlyExecutingKeyPointId);

        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.CurrentSegmentIndex == 1, TimeSpan.FromSeconds(1));
        Assert.Equal(vm.KeyPoints[1].Id, vm.CurrentlyExecutingKeyPointId);

        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(
            () => transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)) == 6,
            TimeSpan.FromSeconds(1));
        Assert.False(vm.IsAwaitingMotionIdle, "no physical-idle wait should happen between the forward and backward legs");

        // Backward leg dispatches its own self-move to the last key point first, so nothing is
        // confirmed as "current" again until that ack lands.
        Assert.Null(vm.CurrentlyExecutingKeyPointId);

        transport.SimulateReceivedLine("ok"); // acks the backward leg's self-move to the last key point
        await WaitUntilAsync(() => vm.CurrentlyExecutingKeyPointId == vm.KeyPoints[2].Id, TimeSpan.FromSeconds(1));

        transport.SimulateReceivedLine("ok"); // acks the backward leg's move to the middle key point
        await WaitUntilAsync(() => vm.CurrentlyExecutingKeyPointId == vm.KeyPoints[1].Id, TimeSpan.FromSeconds(1));

        transport.SimulateReceivedLine("ok"); // acks the backward leg's move to the first key point
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
        vm.IsDirty = false; // completion-settings changes above must not re-trigger the save gate

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        // First pair, forward leg (3 acks: self-move to the first key point + the two real
        // moves) — backward leg is only dispatched once the forward pass's own RunPassAsync call
        // returns, so wait for its G1 lines to appear before acking them (acking blind would race
        // the dispatch and the "ok" would be dropped: BufferAwareCommandQueue.Complete no-ops
        // when nothing is in-flight yet).
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(
            () => transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)) == 6,
            TimeSpan.FromSeconds(1));

        // First pair, backward leg (3 acks) — completes cycle 1. Not the last cycle
        // (RepeatCount = 2), so the second pair's forward leg dispatches immediately after,
        // with no idle wait in between.
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(
            () => transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)) == 9,
            TimeSpan.FromSeconds(1));

        // Second pair, forward leg (3 acks) — its own backward leg dispatches right after.
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(
            () => transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)) == 12,
            TimeSpan.FromSeconds(1));

        // Second (last) pair, backward leg (3 acks) — cycle 2 reaches RepeatCount, so the run
        // now waits for real motion to finish instead of dispatching a third pair.
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
        Assert.Equal(12, transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PlayAsync_LoopMode_SendsReturnToStartMoveBetweenCyclesButNotAfterTheLastOne()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        vm.CompletionMode = ProgramCompletionMode.Loop;
        vm.RepeatCount = 2;
        vm.IsDirty = false; // completion-settings changes above must not re-trigger the save gate

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        // Cycle 1: 3 forward G1 lines (self-move to the first key point + the two real moves).
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");

        // The implicit return-to-start move is dispatched right after cycle 1's acks. It happens
        // to be textually identical to the forward pass's own self-move (both target the first
        // key point with its own transition), so detect it by count, not by content.
        await WaitUntilAsync(
            () => transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)) == 4,
            TimeSpan.FromSeconds(1));
        Assert.False(vm.IsAwaitingMotionIdle, "the return-to-start move between cycles must not wait for physical idle");

        transport.SimulateReceivedLine("ok"); // acks the return-to-start move

        // Cycle 2 (the last one, RepeatCount == 2): 3 more forward G1 lines, no further return move.
        await WaitUntilAsync(
            () => transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)) == 7,
            TimeSpan.FromSeconds(1));

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
        Assert.Equal(7, transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PlayAsync_LoopMode_UnlimitedRepeatCount_KeepsRunningUntilStopped()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        vm.CompletionMode = ProgramCompletionMode.Loop;
        vm.RepeatCount = null;
        vm.IsDirty = false; // completion-settings changes above must not re-trigger the save gate

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        // Run through 3 full cycles (3 forward acks + 1 return-move ack each, 4 G93 lines per
        // cycle) without ever completing.
        for (var i = 0; i < 3; i++)
        {
            // The forward pass for this cycle is only dispatched once the previous cycle's
            // return-to-start move ack has been processed and control has looped back around
            // (an async gap after the first cycle) — wait for its G1 lines to appear before
            // acking them, or the "ok"s race the dispatch and get dropped (BufferAwareCommandQueue.Complete
            // no-ops when nothing is in-flight yet).
            await WaitUntilAsync(
                () => transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)) == (4 * i) + 3,
                TimeSpan.FromSeconds(1));
            transport.SimulateReceivedLine("ok");
            transport.SimulateReceivedLine("ok");
            transport.SimulateReceivedLine("ok");
            await WaitUntilAsync(
                () => transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)) == (4 * i) + 4,
                TimeSpan.FromSeconds(1));
            transport.SimulateReceivedLine("ok"); // acks the return-to-start move
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
        vm.IsDirty = false; // completion-settings change above must not re-trigger the save gate

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");

        // The return-to-start move is textually identical to the forward pass's own self-move
        // (both target the first key point with its own transition), so detect it by count.
        await WaitUntilAsync(
            () => transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)) == 4,
            TimeSpan.FromSeconds(1));
        Assert.False(playTask.IsCompleted, "must wait for the return-to-start move's own physical completion before finishing");

        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
    }

    /// <summary>
    /// A 3-key-point program with ReturnToStartOnFinish must be treated as 4 steps (1-2-3-1):
    /// TotalSegments counts the return move, and while it is in flight the UI must highlight the
    /// first key point as currently executing again — not show the run as already 100% done.
    /// </summary>
    [Fact]
    public async Task PlayAsync_ReturnToStartOnFinish_TreatsTheReturnMoveAsAFourthStepTargetingTheFirstKeyPoint()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        vm.ReturnToStartOnFinish = true;
        vm.IsDirty = false; // completion-settings change above must not re-trigger the save gate

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        Assert.Equal(4, vm.TotalSegments); // 3 key points + the return-to-start step

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        Assert.Equal(3.0 / 4, vm.OverallProgress); // 3 of 4 steps acked, before the return move is even dispatched
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");

        await WaitUntilAsync(
            () => transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)) == 4,
            TimeSpan.FromSeconds(1));

        transport.SimulateReceivedLine("ok"); // acks the return-to-start move
        await WaitUntilAsync(() => vm.CurrentSegmentIndex == 3, TimeSpan.FromSeconds(1));
        Assert.Equal(vm.KeyPoints[0].Id, vm.CurrentlyExecutingKeyPointId);
        Assert.Equal(1.0, vm.OverallProgress);

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
        vm.IsDirty = false; // completion-settings change above must not re-trigger the save gate

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok"); // resolves the command already in flight
        await playTask;

        Assert.Equal(PlaybackState.Stopped, vm.PlaybackState);
        // Only the forward pass's 3 lines (dispatched synchronously before Stop could intervene) —
        // no extra line for a return-to-start move, which Stop must suppress.
        Assert.Equal(3, transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Drives a Loop-mode run up to the point where cycle 1's forward pass has had every ack
    /// processed while PlaybackState is Paused — the pass boundary. The cycle loop must be parked
    /// there (not abandoned), so playTask is still pending and the return-to-start move has not
    /// been dispatched.
    /// </summary>
    private static async Task ParkAtPausedPassBoundaryAsync(ProgramViewModel vm, FakeDeviceTransport transport)
    {
        transport.SimulateReceivedLine("ok"); // acks segment 0 (self-move to the first key point)
        await WaitUntilAsync(() => vm.CurrentSegmentIndex == 0, TimeSpan.FromSeconds(1));

        // Pause is legal here (still Running) and lands before the pass's last ack is processed,
        // so the boundary check at the end of the pass observes Paused deterministically — no
        // dependence on winning a race against the continuation thread.
        await vm.PauseCommand.ExecuteAsync(null);
        Assert.Equal(PlaybackState.Paused, vm.PlaybackState);

        transport.SimulateReceivedLine("ok"); // acks segment 1
        await WaitUntilAsync(() => vm.CurrentSegmentIndex == 1, TimeSpan.FromSeconds(1));

        transport.SimulateReceivedLine("ok"); // acks segment 2 — the pass's last segment
        await WaitUntilAsync(
            () => vm.CurrentSegmentIndex == 2 && vm.SegmentProgress == 1.0,
            TimeSpan.FromSeconds(1));

        // Give the continuation thread room to run past the boundary if it were going to.
        await Task.Delay(100);
    }

    [Fact]
    public async Task Pause_AtAPassBoundary_ParksTheRunInsteadOfAbandoningIt_AndPlayResumesTheCycleLoop()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        vm.CompletionMode = ProgramCompletionMode.Loop;
        vm.RepeatCount = 2;
        vm.IsDirty = false; // completion-settings changes above must not re-trigger the save gate

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        await ParkAtPausedPassBoundaryAsync(vm, transport);

        // Before the fix the pass helper returned "PlaybackState == Running" here, which is false
        // while Paused, so PlayAsync returned outright: the run was silently abandoned with
        // PlaybackState stuck on Paused, IsProgramLocked never clearing, and no coroutine left for
        // a later Play click to resume.
        Assert.False(playTask.IsCompleted, "a Pause at the pass boundary must park the run, not abandon it");
        Assert.Equal(PlaybackState.Paused, vm.PlaybackState);
        Assert.True(vm.IsProgramLocked);
        // Only the forward pass's 3 lines so far — no return-to-start move dispatched yet.
        Assert.Equal(3, transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)));

        // The resume click must actually wake the parked cycle loop.
        await vm.PlayCommand.ExecuteAsync(null);
        Assert.Equal(PlaybackState.Running, vm.PlaybackState);

        await WaitUntilAsync(
            () => transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)) == 4,
            TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("ok"); // acks the return-to-start move

        // Cycle 2's forward pass: 3 more G1 lines (7 in total, incl. the return move).
        await WaitUntilAsync(
            () => transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)) == 7,
            TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");

        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
        Assert.Equal(7, transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Stop_WhileParkedAtAPausedPassBoundary_EndsTheRunWithoutDispatchingAnotherMove()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        vm.CompletionMode = ProgramCompletionMode.Loop;
        vm.RepeatCount = 2;
        vm.IsDirty = false; // completion-settings changes above must not re-trigger the save gate

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        await ParkAtPausedPassBoundaryAsync(vm, transport);

        Assert.False(playTask.IsCompleted, "a Pause at the pass boundary must park the run, not abandon it");

        // Stop must wake the parked boundary wait too, not only a resume — otherwise the run
        // would hang there forever. And once aborted, nothing further may be dispatched to a
        // controller that was just told to abort: those lines survive the abort and would execute
        // on the next Play's ResumeAsync().
        await vm.StopCommand.ExecuteAsync(null);

        await WaitUntilAsync(() => playTask.IsCompleted, TimeSpan.FromSeconds(2));
        await playTask;

        Assert.Equal(PlaybackState.Stopped, vm.PlaybackState);
        Assert.False(vm.IsProgramLocked);
        // Only the forward pass's 3 lines — no return-to-start move dispatched.
        Assert.Equal(3, transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Pause_AtACycleBoundary_AfterTheReturnToStartAck_ParksTheRunAndPlayResumesIt()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        vm.CompletionMode = ProgramCompletionMode.Loop;
        vm.RepeatCount = 2;
        vm.IsDirty = false; // completion-settings changes above must not re-trigger the save gate

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        // Cycle 1's forward pass (3 acks), then the implicit return-to-start move it triggers.
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(
            () => transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)) == 4,
            TimeSpan.FromSeconds(1));

        // Pause while the return-to-start move is still in flight, so its own boundary check —
        // a different call site from the pass helper's — observes Paused once the ack lands.
        await vm.PauseCommand.ExecuteAsync(null);
        Assert.Equal(PlaybackState.Paused, vm.PlaybackState);

        transport.SimulateReceivedLine("ok"); // acks the return-to-start move
        await Task.Delay(100);

        Assert.False(playTask.IsCompleted, "a Pause at the cycle boundary must park the run, not abandon it");
        Assert.Equal(PlaybackState.Paused, vm.PlaybackState);
        Assert.Equal(4, transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)));

        await vm.PlayCommand.ExecuteAsync(null);

        // Cycle 2's forward pass only dispatches if the parked cycle loop actually woke up.
        await WaitUntilAsync(
            () => transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)) == 7,
            TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");

        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
    }

    private static ProgramViewModel CreateViewModel(out FakeDeviceTransport transport, out ManualPeriodicTimer progressTimer, Func<DateTimeOffset>? now = null)
    {
        transport = new FakeDeviceTransport();
        var storage = new FakeProgramStorage();
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default), new SingleRealDeviceEndpointProvider());
        progressTimer = new ManualPeriodicTimer();
        return new ProgramViewModel(connection, storage, new TrajectoryCompiler(), new FakeAppExitService(), now, progressTimer: progressTimer);
    }

    [Fact]
    public async Task PlayAsync_StartsTheProgressTimerAtTheConfiguredInterval()
    {
        var vm = CreateViewModel(out var transport, out var progressTimer);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        Assert.True(progressTimer.IsRunning);
        Assert.Equal(TimeSpan.FromMilliseconds(200), progressTimer.LastInterval);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    [Fact]
    public async Task StopAsync_StopsTheProgressTimer()
    {
        var vm = CreateViewModel(out var transport, out var progressTimer);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        Assert.True(progressTimer.IsRunning);

        await vm.StopCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok"); // resolves the command already in flight so playTask completes
        await playTask;

        Assert.False(progressTimer.IsRunning);
    }

    [Fact]
    public async Task PlayAsync_AsTimeElapsesDuringThePass_PhysicalOverallProgressTracksIt()
    {
        var currentTime = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        // SeedTwoSegmentProgram leaves the simulated machine at the last captured pose (20,0,0,0) —
        // reset it to the program's actual starting pose before Play, so the tracker's captured
        // starting vertex matches what these assertions assume.
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.PhysicalOverallProgress);

        // 3 key points at TransitionSeconds=5 each (SeedTwoSegmentProgram) = 15s total estimate for
        // the whole pass, including segment 0's zero-distance self-move; halfway is 7.5s.
        currentTime = currentTime.AddSeconds(7.5);
        progressTimer.RaiseElapsed();

        Assert.Equal(0.5, vm.PhysicalOverallProgress);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    [Fact]
    public async Task PlayAsync_PhysicallyExecutingKeyPointId_CanLagTheAckBasedHighlightWhenTheBufferRunsAhead()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        // SeedTwoSegmentProgram leaves the simulated machine at the last captured pose (20,0,0,0) —
        // reset it to the program's actual starting pose before Play, so the tracker's captured
        // starting vertex matches what these assertions assume (a clean 0->10->20 path, 20 units total).
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        // Both remaining acks land before any position update — the ack-based highlight jumps to the
        // last point, but the physically-executing point stays at the first (position never moved).
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.CurrentlyExecutingKeyPointId == vm.KeyPoints[2].Id, TimeSpan.FromSeconds(1));

        Assert.Equal(vm.KeyPoints[0].Id, vm.PhysicallyExecutingKeyPointId);

        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    [Fact]
    public async Task PlayAsync_EachNewPass_ResetsPhysicalOverallProgressToZero()
    {
        var currentTime = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");
        // Setting CompletionMode/RepeatCount marks the program dirty again (MarkDirtyIfTracking),
        // which would make PlayAsync's EnsureProgramSavedAsync await an unanswered save-confirmation
        // dialog forever (see CLAUDE.md's async-dialog-gate note) — re-clear IsDirty after.
        vm.CompletionMode = ProgramCompletionMode.PingPong;
        vm.RepeatCount = 1;
        vm.IsDirty = false;

        // Timing between the forward pass's last ack and the backward pass's tracker Reset isn't
        // pinned to a single observable predicate (both happen inside the same async continuation),
        // so this records the whole PhysicalOverallProgress sequence instead of polling one instant:
        // a reset-to-zero occurring only after the value has already gone positive is exactly the
        // "each new pass starts over" behavior this test exists to catch a regression in.
        var sawPositive = false;
        var sawResetAfterPositive = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(vm.PhysicalOverallProgress))
            {
                return;
            }

            if (vm.PhysicalOverallProgress > 0)
            {
                sawPositive = true;
            }
            else if (sawPositive)
            {
                sawResetAfterPositive = true;
            }
        };

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        currentTime = currentTime.AddSeconds(7.5); // forward pass, well underway (half its 15s estimate)
        progressTimer.RaiseElapsed();

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok"); // forward pass fully acked; backward pass starts and its tracker resets
        await WaitUntilAsync(() => sawResetAfterPositive, TimeSpan.FromSeconds(1));

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.True(sawResetAfterPositive);
    }

    /// <summary>
    /// Unlike the pass-to-pass boundary above (which resets to a fresh 0-100% run), the
    /// ReturnToStartOnFinish move is the SAME pass's own extra (N+1)th step — its progress must
    /// extend the current estimate rather than reset the bar to 0%.
    /// </summary>
    [Fact]
    public async Task PlayAsync_ReturnToStartOnFinish_ExtendsPhysicalProgressInsteadOfResettingIt()
    {
        var currentTime = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");
        vm.ReturnToStartOnFinish = true;
        vm.IsDirty = false; // completion-settings change above must not re-trigger the save gate

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        // Forward pass: 3 key points x 5s transition = 15s total estimate; advancing exactly that
        // far saturates OverallFraction to 1.0 (it is purely time-based, independent of acks).
        currentTime = currentTime.AddSeconds(15);
        progressTimer.RaiseElapsed();
        Assert.Equal(1.0, vm.PhysicalOverallProgress);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));

        // The return-to-start step (KeyPoints[0]'s own 5s transition) must extend the SAME 15s
        // estimate to 20s BEFORE this wait for physical idle, not only once the move is actually
        // dispatched afterward — otherwise the bar sits pinned at 100% for however long real
        // motion takes to catch up with the already-elapsed 15s estimate (the bug this guards:
        // 15/20 = 0.75 here, never back toward 0, and never falsely pinned at 1.0 either).
        Assert.Equal(0.75, vm.PhysicalOverallProgress);

        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");

        await WaitUntilAsync(
            () => transport.SentLines.Count(l => l.StartsWith("G93", StringComparison.Ordinal)) == 4,
            TimeSpan.FromSeconds(1));

        // Dispatching the return move itself doesn't extend the estimate again.
        Assert.Equal(0.75, vm.PhysicalOverallProgress);

        currentTime = currentTime.AddSeconds(5);
        progressTimer.RaiseElapsed();
        Assert.Equal(1.0, vm.PhysicalOverallProgress);

        transport.SimulateReceivedLine("ok"); // acks the return-to-start move
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
    }

    [Fact]
    public async Task StopAsync_ClearsPhysicalProgress()
    {
        var currentTime = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        currentTime = currentTime.AddSeconds(5);
        progressTimer.RaiseElapsed();
        Assert.True(vm.PhysicalOverallProgress > 0);

        await vm.StopCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok"); // resolves the command already in flight so playTask completes
        await playTask;

        Assert.Equal(0, vm.PhysicalOverallProgress);
        Assert.Null(vm.PhysicallyExecutingKeyPointId);
    }

    [Fact]
    public async Task PauseAsync_ThenResume_ExcludesThePauseDurationFromElapsedProgress()
    {
        var currentTime = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        currentTime = currentTime.AddSeconds(3);
        progressTimer.RaiseElapsed();
        Assert.Equal(0.2, vm.PhysicalOverallProgress); // 3 of the 15s total estimate (3 points x 5s)

        await vm.PauseCommand.ExecuteAsync(null);
        currentTime = currentTime.AddSeconds(100); // a long pause / reconnect
        await vm.PlayCommand.ExecuteAsync(null); // resume — same idiom as this file's other pause/resume tests
        progressTimer.RaiseElapsed();

        Assert.Equal(0.2, vm.PhysicalOverallProgress); // the 100s pause must not count as elapsed progress

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    [Fact]
    public async Task WhileStatusReportsArriveDuringAPause_ProgressDoesNotAdvance()
    {
        var currentTime = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        currentTime = currentTime.AddSeconds(3);
        progressTimer.RaiseElapsed();
        Assert.Equal(0.2, vm.PhysicalOverallProgress); // 3 of the 15s total estimate (3 points x 5s)

        await vm.PauseCommand.ExecuteAsync(null);
        currentTime = currentTime.AddSeconds(50);
        // StatusPoller keeps polling through a real Pause — this simulates one of those reports
        // arriving mid-pause. It must NOT feed the tracker, or PhysicalOverallProgress would climb
        // even though the machine hasn't moved and this run isn't "supposed" to be progressing.
        transport.SimulateReceivedLine("<Hold|WPos:5.000,0.000,0.000,0.000|FS:0,0>");
        Assert.Equal(0.2, vm.PhysicalOverallProgress); // unchanged despite the report and the elapsed 50s

        await vm.PlayCommand.ExecuteAsync(null); // resume — shifts the tracker's clocks by the 50s pause
        currentTime = currentTime.AddSeconds(4.5);
        progressTimer.RaiseElapsed();
        Assert.Equal(0.5, vm.PhysicalOverallProgress); // 3s (before pause) + 4.5s (after resume) = 7.5 of 15s — the 50s paused gap and the mid-pause status report must not count

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    [Fact]
    public void PhysicalOverallProgress_WithNoActiveTracker_DefaultsToZero()
    {
        var vm = CreateViewModel(out _, out _);

        Assert.Equal(0, vm.PhysicalOverallProgress);
        Assert.Equal(1.0, vm.PhysicalPointRemainingFraction);
        Assert.False(vm.PhysicalPointHasTimeWarning);
        Assert.Null(vm.PhysicallyExecutingKeyPointId);
    }

    [Fact]
    public async Task PlayAsync_WhenASegmentTakesTooLong_PhysicalPointHasTimeWarningBecomesTrue()
    {
        var currentTime = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport); // each key point's TransitionSeconds is 5
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        Assert.False(vm.PhysicalPointHasTimeWarning);

        // Segment 0's estimate is 5s; 6s elapsed is 20% over, past the 15% warning threshold.
        currentTime = currentTime.AddSeconds(6);
        progressTimer.RaiseElapsed();

        Assert.True(vm.PhysicalPointHasTimeWarning);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    [Fact]
    public async Task PlayAsync_SegmentEndsOverTime_RecordsAKeyPointMessageForThatPoint()
    {
        var currentTime = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport); // each key point's TransitionSeconds is 5
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");
        var firstPointId = vm.KeyPoints[0].Id;

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        // Segment 0's estimate is 5s; 6s elapsed is 20% over, past the 15% warning threshold.
        currentTime = currentTime.AddSeconds(6);
        progressTimer.RaiseElapsed();
        Assert.Empty(vm.GetKeyPointMessages(firstPointId)); // still inside the segment — nothing recorded yet

        // Real motion into segment 1 — leaving segment 0 while it was still over time.
        transport.SimulateReceivedLine("<Run|WPos:5.000,0.000,0.000,0.000|FS:0,0>");

        var messages = vm.GetKeyPointMessages(firstPointId);
        var message = Assert.Single(messages);
        Assert.Equal(MessageLevel.Warning, message.Level);
        Assert.Equal("Превышение фактического времени перемещения (6 сек.) над установленным (5 сек)", message.Text);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    [Fact]
    public async Task PlayAsync_RepeatedIdenticalOverageAcrossTwoRuns_DoesNotDuplicateTheMessage()
    {
        var currentTime = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");
        var firstPointId = vm.KeyPoints[0].Id;

        async Task RunOnePassWithTheSameOverageAsync()
        {
            var playTask = vm.PlayCommand.ExecuteAsync(null);
            currentTime = currentTime.AddSeconds(6); // identical overage every run: 6 of 5s estimated
            progressTimer.RaiseElapsed();
            transport.SimulateReceivedLine("<Run|WPos:5.000,0.000,0.000,0.000|FS:0,0>");

            transport.SimulateReceivedLine("ok");
            transport.SimulateReceivedLine("ok");
            transport.SimulateReceivedLine("ok");
            await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
            transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
            await playTask;
        }

        await RunOnePassWithTheSameOverageAsync();
        Assert.Single(vm.GetKeyPointMessages(firstPointId));

        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>"); // machine back at the start for the next run
        await RunOnePassWithTheSameOverageAsync();

        Assert.Single(vm.GetKeyPointMessages(firstPointId)); // identical text both times — deduped, not appended again
    }

    [Fact]
    public async Task StopAsync_LastPointStillOverTime_FlushesAMessageForThatPoint()
    {
        var currentTime = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");
        var firstPointId = vm.KeyPoints[0].Id;

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        currentTime = currentTime.AddSeconds(6); // still mid-segment 0 — no natural transition to report this
        progressTimer.RaiseElapsed();
        Assert.Empty(vm.GetKeyPointMessages(firstPointId));

        await vm.StopCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok"); // resolves the command already in flight so playTask completes
        await playTask;

        var messages = vm.GetKeyPointMessages(firstPointId);
        var message = Assert.Single(messages);
        Assert.Equal("Превышение фактического времени перемещения (6 сек.) над установленным (5 сек)", message.Text);
    }
}
