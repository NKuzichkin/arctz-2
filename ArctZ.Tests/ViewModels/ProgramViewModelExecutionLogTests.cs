using System;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.App;
using ArctZ.Tests.Services.Device;
using ArctZ.Tests.Services.Program;
using ArctZ.ViewModels;
using static ArctZ.Tests.TestSupport.AsyncAssert;

namespace ArctZ.Tests.ViewModels;

public class ProgramViewModelExecutionLogTests
{
    private static ProgramViewModel CreateViewModel(out FakeDeviceTransport transport, out ManualPeriodicTimer progressTimer, Func<DateTimeOffset>? now = null)
    {
        transport = new FakeDeviceTransport();
        var storage = new FakeProgramStorage();
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default), new SingleRealDeviceEndpointProvider());
        progressTimer = new ManualPeriodicTimer();
        return new ProgramViewModel(connection, storage, new TrajectoryCompiler(), new FakeAppExitService(), now, progressTimer: progressTimer);
    }

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

        vm.ProgramId = Guid.NewGuid();
        vm.IsDirty = false;
    }

    [Fact]
    public async Task PlayAsync_ColdStart_CreatesTheLogWithAHeaderLine()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        // Renamed before seeding (not after) so Seed's trailing IsDirty = false is what sticks —
        // renaming afterward would re-dirty the program and make EnsureProgramSavedAsync block
        // forever on an unanswered "save before starting?" confirm dialog.
        vm.ProgramName = "Тестовая программа";
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        Assert.Contains("Программа запущена: «Тестовая программа», 3 точек", vm.ExecutionLogText);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    [Fact]
    public async Task PlayAsync_ResumeAfterPause_DoesNotStartANewLog()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        var textAtStart = vm.ExecutionLogText!;

        await vm.PauseCommand.ExecuteAsync(null);
        await vm.PlayCommand.ExecuteAsync(null); // resume

        Assert.StartsWith(textAtStart, vm.ExecutionLogText);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    [Fact]
    public async Task PlayAsync_Completed_AppendsAProgramEndedLineWithFinalProgress()
    {
        // PhysicalOverallProgress/PhysicalPointRemainingFraction are purely time-based (see
        // TimeProgressTracker), so reaching "100%, 100%" requires actually advancing a fake clock
        // to the estimated total — the default real-clock CreateViewModel() overload finishes this
        // whole scenario in well under a second of wall time, nowhere near the program's 15s
        // estimate (3 points x 5s TransitionSeconds).
        var currentTime = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));

        // Jump straight to the final point after 10 of the 15s estimate (segments 0+1's worth) —
        // this lands the physical tracker in the final segment with its entry clock starting at
        // that moment, matching what real, gradually-arriving status reports would produce.
        // "Run" (not "Idle") so this doesn't complete the program yet.
        currentTime = currentTime.AddSeconds(10);
        transport.SimulateReceivedLine("<Run|WPos:20.000,0.000,0.000,0.000|FS:0,0>");

        // The final segment's own 5s estimate elapses, saturating both the overall estimate
        // (10 + 5 = 15 of 15s) and the current-step fraction (5 of 5s) at exactly 100% each.
        currentTime = currentTime.AddSeconds(5);
        progressTimer.RaiseElapsed();

        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Contains("Программа завершена: Завершено — общий 100%, шаг 100%", vm.ExecutionLogText);
    }

    [Fact]
    public async Task StopAsync_DuringMotion_LogsTheActualProgressAtTheMomentOfStopping_NotZero()
    {
        var currentTime = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        currentTime = currentTime.AddSeconds(5); // 5 of 15s total estimate (3 points x 5s) = 33%
        progressTimer.RaiseElapsed();
        Assert.True(vm.PhysicalOverallProgress > 0);

        await vm.StopCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok"); // resolves the command already in flight
        await playTask;

        // Regression guard: without capturing progress before ClearProgressTracker() runs (which
        // this Stopped/Faulted branch calls), this would read "общий 0%" instead of the real value.
        Assert.Contains("Программа завершена: Остановлено — общий 33%", vm.ExecutionLogText);
    }

    [Fact]
    public async Task StopAsync_DuringMotion_DoesNotLogASpuriousZeroProgressMovementEndedLine()
    {
        // ClearProgressTracker() (called on Stopped/Faulted) nulls _progressTracker BEFORE calling
        // OnProgressTrackerChanged() — so if LogPhysicalMovementTransitionIfChanged() tried to log
        // an "ended" line for this transition, it would read PhysicalOverallProgress/step-progress
        // as 0%/0% instead of the run's actual progress at the moment of stopping. The correct
        // progress for this instant is already reported by the "Программа завершена" bookend
        // (captured before the clear) — a transition straight to null must not log anything.
        var currentTime = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        currentTime = currentTime.AddSeconds(5); // 5 of 15s total estimate (3 points x 5s) = 33%
        progressTimer.RaiseElapsed();
        Assert.True(vm.PhysicalOverallProgress > 0);

        await vm.StopCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok"); // resolves the command already in flight
        await playTask;

        Assert.DoesNotContain("Окончание движения к точке", vm.ExecutionLogText);
        Assert.Contains("Программа завершена: Остановлено — общий 33%", vm.ExecutionLogText);
    }

    [Fact]
    public async Task Pause_ThenResume_LogsBothEventsWithProgressAtTheMomentOfEach()
    {
        var currentTime = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        currentTime = currentTime.AddSeconds(3); // 3 of 15s = 20%
        progressTimer.RaiseElapsed();

        await vm.PauseCommand.ExecuteAsync(null);
        Assert.Contains("Пауза — общий 20%", vm.ExecutionLogText);

        currentTime = currentTime.AddSeconds(100); // a long pause
        await vm.PlayCommand.ExecuteAsync(null); // resume
        Assert.Contains("Возобновление — общий 20%", vm.ExecutionLogText);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    [Fact]
    public async Task PlayAsync_FirstPhysicalSegment_LogsMovementStartedForTheFirstKeyPoint()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");

        await WaitUntilAsync(() => vm.PhysicallyExecutingKeyPointId == vm.KeyPoints[0].Id, TimeSpan.FromSeconds(1));
        Assert.Contains($"Начало движения к точке «{vm.KeyPoints[0].Label}»", vm.ExecutionLogText);

        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    [Fact]
    public async Task PlayAsync_PhysicalMovementBetweenPoints_LogsEndThenStartPairInOrder()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.PhysicallyExecutingKeyPointId == vm.KeyPoints[0].Id, TimeSpan.FromSeconds(1));

        // Real motion well into the KeyPoints[0]->KeyPoints[1] leg (x=6 of 10) but short of
        // KeyPoints[1] itself (x=10) — landing past it (e.g. x=15) would instead project onto the
        // NEXT leg (KeyPoints[1]->KeyPoints[2], whose closer distance wins the tracker's nearest-edge
        // projection) and skip straight to targeting KeyPoints[2], never passing through KeyPoints[1].
        transport.SimulateReceivedLine("<Run|WPos:6.000,0.000,0.000,0.000|FS:500,0>");
        await WaitUntilAsync(() => vm.PhysicallyExecutingKeyPointId == vm.KeyPoints[1].Id, TimeSpan.FromSeconds(1));

        var lines = vm.ExecutionLogText!.Split(Environment.NewLine);
        var endedIndex = Array.FindIndex(lines, l => l.Contains($"Окончание движения к точке «{vm.KeyPoints[0].Label}»"));
        var startedIndex = Array.FindIndex(lines, l => l.Contains($"Начало движения к точке «{vm.KeyPoints[1].Label}»"));
        Assert.True(endedIndex >= 0, "expected an 'ended' line for the first key point");
        Assert.True(startedIndex > endedIndex, "expected the 'started' line for the second key point right after it");

        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    [Fact]
    public async Task PlayAsync_AckOutrunsPhysicalPositionByMoreThanOnePoint_LogsAckDesyncOnce()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        // All three acks land before any position update — ack jumps to segment 2 while the
        // physical tracker (position never moved) stays on segment 0. Gap of 2 exceeds the
        // >1-point desync threshold. Same scenario as
        // PlayAsync_PhysicallyExecutingKeyPointId_CanLagTheAckBasedHighlightWhenTheBufferRunsAhead
        // in ProgramViewModelPlaybackTests.
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.CurrentlyExecutingKeyPointId == vm.KeyPoints[2].Id, TimeSpan.FromSeconds(1));

        // The desync check runs inside OnProgressTrackerChanged, which only fires off the PHYSICAL
        // tracker (a position update or the periodic progress-clock tick) — never off CurrentSegmentIndex
        // (ack) alone. In real operation the next ~200ms timer tick or status report picks this up
        // almost immediately; here, with acks the only thing that changed so far, an explicit clock
        // tick stands in for that next recompute (see BackgroundSessionCoordinatorTests for the same
        // OnClockTickForTests idiom).
        vm.OnClockTickForTests();

        Assert.Equal(1, CountOccurrences(vm.ExecutionLogText!, "Рассинхронизация: буфер контроллера опережает факт"));

        // A further recompute while the position still hasn't moved must not duplicate the line.
        transport.SimulateReceivedLine("<Run|WPos:0.000,0.000,0.000,0.000|FS:500,0>");
        Assert.Equal(1, CountOccurrences(vm.ExecutionLogText!, "Рассинхронизация: буфер контроллера опережает факт"));

        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    private static int CountOccurrences(string text, string substring)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(substring, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += substring.Length;
        }

        return count;
    }

    [Fact]
    public async Task PlayAsync_SegmentEndsOverTime_LogsATimeOverageDesyncLine()
    {
        var currentTime = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport); // each key point's TransitionSeconds is 5
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        currentTime = currentTime.AddSeconds(6); // 20% over the 5s estimate for segment 0
        progressTimer.RaiseElapsed();

        transport.SimulateReceivedLine("<Run|WPos:5.000,0.000,0.000,0.000|FS:0,0>"); // leaves segment 0 while over time

        Assert.Contains(
            $"Рассинхронизация: превышение расчётного времени точки «{vm.KeyPoints[0].Label}» (6.0с факт / 5.0с расчёт)",
            vm.ExecutionLogText);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    [Fact]
    public async Task ShutdownAsync_AfterProgramCompleted_DoesNotAppendASpuriousStoppedBookendOrOverage()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
        var logAfterCompletion = vm.ExecutionLogText!;

        var shutdownTask = vm.ShutdownAsync(confirmIfRunning: false);

        // DeviceSession.StopAndDrainAsync's WaitForEmptyBufferAsync only counts a status report
        // that arrives after it starts listening (the pre-existing one from the run's own
        // completion above doesn't count) — this unblocks it right away instead of waiting out the
        // full 2s DeviceStopTimeout.
        await WaitUntilAsync(() => transport.SentRawBytes.Count > 0, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await shutdownTask;

        // Without the IsProgramLocked guard, this would force PlaybackState back to Stopped,
        // appending a second, contradictory "Программа завершена: Остановлено" line and — via
        // ClearProgressTracker's FlushCurrentSegment — a fabricated time-overage line computed
        // against however long real wall-clock time elapsed since the run actually finished.
        Assert.Equal(logAfterCompletion, vm.ExecutionLogText);
    }
}
