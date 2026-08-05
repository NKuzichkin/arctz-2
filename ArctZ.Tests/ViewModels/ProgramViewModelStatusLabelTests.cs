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
}
