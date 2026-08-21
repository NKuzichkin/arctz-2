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
}
