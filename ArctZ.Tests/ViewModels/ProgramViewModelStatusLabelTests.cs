using System;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.Device;
using ArctZ.Tests.Services.Program;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class ProgramViewModelStatusLabelTests
{
    private static ProgramViewModel CreateViewModel(out FakeDeviceTransport transport)
    {
        transport = new FakeDeviceTransport();
        var storage = new FakeProgramStorage();
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default));
        return new ProgramViewModel(connection, storage, new TrajectoryCompiler());
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
            vm.KeyPoints[i] = vm.KeyPoints[i] with { FeedRateUnitsPerMin = 500, DwellSeconds = 0, Ease = EaseMode.None, ContinuousBlend = true };
        }
    }

    [Fact]
    public async Task StatusLabel_Idle_ByDefault()
    {
        var vm = CreateViewModel(out _);
        await vm.Connection.ConnectCommand.Execute();

        Assert.Equal("Ожидание", vm.StatusLabel);
    }

    [Fact]
    public async Task StatusLabel_Running_WhilePlaybackRunning()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        Assert.Equal("Выполнение", vm.StatusLabel);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await playTask;
    }

    [Fact]
    public async Task StatusLabel_Paused_WhilePlaybackPaused()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        await vm.PauseCommand.ExecuteAsync(null);

        Assert.Equal("Пауза", vm.StatusLabel);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await playTask;
    }

    [Fact]
    public async Task StatusLabel_Faulted_OnError()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("error:9");
        await playTask;

        Assert.Equal("Ошибка", vm.StatusLabel);
    }

    [Fact]
    public async Task StatusLabel_Completed_AfterProgramFinishes()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await playTask;

        Assert.Equal("Завершено", vm.StatusLabel);
    }

    [Fact]
    public async Task StatusLabel_Stopped_AfterStop()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.Equal("Остановлено", vm.StatusLabel);

        // Both compiled G1 lines were already sent (fit the default RX buffer in one shot,
        // per PlayAsync_DispatchesAllStepsBeforeAwaitingAcks_ThenTracksProgress) and are still
        // in-flight — AbortPendingCommands only drains not-yet-sent commands, so both need an
        // "ok" to fully drain the queue, same as PlayAsync_AfterStop_SendsResumeBeforeDispatchingFreshProgram.
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await playTask;
    }

    [Fact]
    public async Task StatusLabel_Jog_WhenMachineJoggingAndPlaybackIdle()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();

        transport.SimulateReceivedLine("<Jog|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        Assert.Equal("Джог", vm.StatusLabel);
    }

    [Fact]
    public async Task StatusLabel_Homing_WhenMachineHomingAndPlaybackIdle()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();

        transport.SimulateReceivedLine("<Home|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        Assert.Equal("Homing", vm.StatusLabel);
    }

    [Fact]
    public async Task StatusLabel_Completed_ResetsToIdleAfterDelay()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        vm.TerminalStatusResetDelay = TimeSpan.FromMilliseconds(20);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await playTask;
        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);

        await WaitUntilAsync(() => vm.PlaybackState == PlaybackState.Idle, TimeSpan.FromSeconds(1));
        Assert.Equal("Ожидание", vm.StatusLabel);
    }

    [Fact]
    public async Task StatusLabel_Stopped_ResetsToIdleAfterDelay()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        vm.TerminalStatusResetDelay = TimeSpan.FromMilliseconds(20);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(PlaybackState.Stopped, vm.PlaybackState);

        await WaitUntilAsync(() => vm.PlaybackState == PlaybackState.Idle, TimeSpan.FromSeconds(1));
        Assert.Equal("Ожидание", vm.StatusLabel);

        // Both dispatched G1 lines are still in-flight (see StatusLabel_Stopped_AfterStop) —
        // drain both so playTask actually completes.
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await playTask;
    }

    [Fact]
    public async Task TerminalStatusReset_CancelledIfPlayPressedAgainBeforeDelayElapses()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        vm.TerminalStatusResetDelay = TimeSpan.FromMilliseconds(200);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        // Both dispatched G1 lines are still in-flight (see StatusLabel_Stopped_AfterStop) —
        // drain both so playTask actually completes.
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await playTask;
        Assert.Equal(PlaybackState.Stopped, vm.PlaybackState);

        // Re-play well before the original 200ms terminal-reset delay elapses.
        var secondPlayTask = vm.PlayCommand.ExecuteAsync(null);
        Assert.Equal(PlaybackState.Running, vm.PlaybackState);

        // Wait past the original delay window — the stale reset must not fire and stomp
        // the freshly-started Running back to Idle.
        await Task.Delay(400);
        Assert.Equal(PlaybackState.Running, vm.PlaybackState);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await secondPlayTask;
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
}
