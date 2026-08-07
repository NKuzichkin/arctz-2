using System;
using System.Linq;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.Device;
using ArctZ.Tests.Services.Program;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class ProgramViewModelPlaybackTests
{
    private static ProgramViewModel CreateViewModel(out FakeDeviceTransport transport) =>
        CreateViewModel(out transport, out _);

    private static ProgramViewModel CreateViewModel(out FakeDeviceTransport transport, out ManualPeriodicTimer progressTimer)
    {
        transport = new FakeDeviceTransport();
        var storage = new FakeProgramStorage();
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default));
        progressTimer = new ManualPeriodicTimer();
        return new ProgramViewModel(connection, storage, new TrajectoryCompiler(), progressTimer, TimeSpan.FromMilliseconds(100));
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

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - start > timeout)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(20);
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

        // A real machine reports Run while still moving after the last ack — feeding it here
        // forces a genuine state transition, matching what StatusPoller would produce, so the
        // subsequent Idle report below isn't a same-value repeat of the Idle status the machine
        // was already sitting in before Play started (which the fake transport, unlike a
        // StatusPoller-backed live session, would otherwise never actually change from).
        transport.SimulateReceivedLine("<Run|WPos:15.000,0.000,0.000,0.000|FS:500,0>");
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
        Assert.Equal(1, vm.CurrentSegmentIndex);
        Assert.Equal(1.0, vm.SegmentProgress);
        Assert.Equal(1.0, vm.OverallProgress);
    }

    [Fact]
    public async Task DisplayProgress_ForcesTo100Percent_WhenCompleted_EvenIfAcksArrivedBeforeAnyTick()
    {
        var vm = CreateViewModel(out var transport, out var progressTimer);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.DisplayProgress);

        // Ack both segments immediately, before any progressTimer tick — real ack timing
        // reflects buffer-drain speed, not motion time, so this is the common case, not an
        // edge case. DisplayProgress must not have anywhere else to get a value from except
        // the explicit Completed-forced snap.
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));

        // Forces a real Idle->Run->Idle transition (see the comment in the first test above for why).
        transport.SimulateReceivedLine("<Run|WPos:15.000,0.000,0.000,0.000|FS:500,0>");
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
        Assert.Equal(1.0, vm.DisplayProgress);
    }

    [Fact]
    public async Task DisplayProgress_FollowsItsOwnTimeline_UnaffectedByAnEarlyAck()
    {
        var vm = CreateViewModel(out var transport, out var progressTimer);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        // First segment's estimate is 1.2s = 12 ticks @ 100ms. Ack it after only 3 ticks.
        progressTimer.RaiseElapsed();
        progressTimer.RaiseElapsed();
        progressTimer.RaiseElapsed();

        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.CurrentSegmentIndex == 0, TimeSpan.FromSeconds(1));

        // OverallProgress (ack-confirmed truth) jumps to 0.5 immediately — DisplayProgress must
        // NOT snap to it, and must keep following the same first-segment animation.
        Assert.Equal(0.5, vm.OverallProgress);
        Assert.Equal(0.125, vm.DisplayProgress, 3); // 3/12 ticks toward the first segment's 0.5 target
        Assert.True(vm.DisplayProgress < 0.5);

        // A 4th tick continues the SAME first-segment animation — the ack that already arrived
        // changes nothing about its pace.
        progressTimer.RaiseElapsed();
        Assert.Equal(4.0 / 12 * 0.5, vm.DisplayProgress, 3);

        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));

        // Forces a real Idle->Run->Idle transition (see the comment in the first test above for why).
        transport.SimulateReceivedLine("<Run|WPos:15.000,0.000,0.000,0.000|FS:500,0>");
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
        Assert.Equal(1.0, vm.DisplayProgress);
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

        // Forces a real Idle->Run->Idle transition (see the comment in the first test above for why).
        transport.SimulateReceivedLine("<Run|WPos:15.000,0.000,0.000,0.000|FS:500,0>");
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

        // Forces a real Idle->Run->Idle transition (see the comment in the first test above for why).
        transport.SimulateReceivedLine("<Run|WPos:15.000,0.000,0.000,0.000|FS:500,0>");
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

        // Forces a real Idle->Run->Idle transition (see the comment in the first test above for why).
        transport.SimulateReceivedLine("<Run|WPos:15.000,0.000,0.000,0.000|FS:500,0>");
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

        // Forces a real Idle->Run->Idle transition (see the comment in the first test above for why).
        transport.SimulateReceivedLine("<Run|WPos:15.000,0.000,0.000,0.000|FS:500,0>");
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

        // Forces a real Idle->Run->Idle transition (see the comment in the first test above for why).
        transport.SimulateReceivedLine("<Run|WPos:15.000,0.000,0.000,0.000|FS:500,0>");
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    [Fact]
    public async Task PlayAsync_DoesNotComplete_UntilTheMachineReportsIdleAfterTheLastAck()
    {
        var vm = CreateViewModel(out var transport, out _);
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
        Assert.Equal(1.0, vm.DisplayProgress);
    }

    [Fact]
    public async Task Stop_DuringTheMotionTail_EndsTheRunWithoutWaitingForIdle()
    {
        var vm = CreateViewModel(out var transport, out _);
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
}
