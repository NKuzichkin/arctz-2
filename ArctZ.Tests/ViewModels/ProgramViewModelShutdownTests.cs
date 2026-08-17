using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.App;
using ArctZ.Tests.Services.Device;
using ArctZ.Tests.Services.Program;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

/// <summary>Выход из приложения обязан остановить станок, а не просто закрыть окно.</summary>
public class ProgramViewModelShutdownTests
{
    private static ProgramViewModel CreateViewModel(
        out FakeAppExitService exitService,
        out FakeDeviceSession session)
    {
        var connection = new ConnectionViewModel(
            new FakeDeviceTransport(),
            () => new FakeDeviceTransport(),
            new DeviceSessionFactory(MachineLimits.Default),
            new SingleRealDeviceEndpointProvider());
        session = new FakeDeviceSession();
        connection.Session = session;
        exitService = new FakeAppExitService();
        return new ProgramViewModel(connection, new FakeProgramStorage(), new TrajectoryCompiler(), exitService);
    }

    [Fact]
    public async Task ExitCommand_StopsTheDeviceAndDisconnectsBeforeExiting()
    {
        var vm = CreateViewModel(out var exitService, out var session);

        await vm.ExitCommand.ExecuteAsync(null);

        Assert.Equal(1, session.StopAndDrainCallCount);
        Assert.Equal(1, session.DisconnectCallCount);
        Assert.Equal(1, exitService.ExitCallCount);
    }

    /// <summary>Разрыв связи сам по себе будит автоподключение — на выходе оно успело бы
    /// начать переподключение к станку, который мы только что остановили.</summary>
    [Fact]
    public async Task ExitCommand_DropsTheSessionSoAutoConnectDoesNotRestartIt()
    {
        var vm = CreateViewModel(out _, out _);

        await vm.ExitCommand.ExecuteAsync(null);

        Assert.Null(vm.Connection.Session);
    }

    [Fact]
    public async Task ExitCommand_WhileTheDeviceIsStillStopping_DoesNotExitYet()
    {
        var vm = CreateViewModel(out var exitService, out var session);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StopAndDrainGate = gate;

        var exitTask = vm.ExitCommand.ExecuteAsync(null);

        Assert.True(vm.IsShuttingDown);
        Assert.Equal(0, exitService.ExitCallCount);

        gate.SetResult(true);
        await exitTask;

        Assert.Equal(1, exitService.ExitCallCount);
    }

    [Fact]
    public async Task ExitCommand_WhenConfirmationIsDeclined_LeavesTheDeviceRunning()
    {
        var vm = CreateViewModel(out var exitService, out var session);
        vm.PlaybackState = PlaybackState.Running;

        var exitTask = vm.ExitCommand.ExecuteAsync(null);
        vm.ConfirmNoCommand.Execute(null);
        await exitTask;

        Assert.Equal(0, session.StopAndDrainCallCount);
        Assert.Equal(0, session.DisconnectCallCount);
        Assert.Equal(0, exitService.ExitCallCount);
        Assert.False(vm.IsShuttingDown);
    }

    [Fact]
    public async Task ExitCommand_WhileAProgramIsRunning_StopsPlaybackBeforeStoppingTheDevice()
    {
        var vm = CreateViewModel(out _, out var session);
        vm.PlaybackState = PlaybackState.Running;
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StopAndDrainGate = gate;

        var exitTask = vm.ExitCommand.ExecuteAsync(null);
        vm.ConfirmYesCommand.Execute(null);
        await Task.Yield();

        Assert.Equal(PlaybackState.Stopped, vm.PlaybackState);

        gate.SetResult(true);
        await exitTask;
    }

    /// <summary>Принудительное закрытие приложения (смахивание из недавних на Android) не
    /// может ничего спросить у пользователя: показывать некому и некогда.</summary>
    [Fact]
    public async Task ShutdownAsync_WithoutConfirmation_StopsARunningProgramWithoutAskingAnything()
    {
        var vm = CreateViewModel(out _, out var session);
        vm.PlaybackState = PlaybackState.Running;

        var stopped = await vm.ShutdownAsync(confirmIfRunning: false);

        Assert.True(stopped);
        Assert.Null(vm.PendingConfirmation);
        Assert.Equal(1, session.StopAndDrainCallCount);
        Assert.Equal(PlaybackState.Stopped, vm.PlaybackState);
    }

    /// <summary>Смахивание из недавних на Android не убивает процесс: StopSelf() лишь отпускает
    /// сервис, а следующий запуск приложения Android отдаёт тому же процессу — вместе с этой самой
    /// ViewModel. Оставленный взведённым флаг закрыл бы новый запуск оверлеем «Остановка
    /// устройства…» навсегда.</summary>
    [Fact]
    public async Task ShutdownAsync_WhenFinished_LeavesTheViewModelFitForAnotherRun()
    {
        var vm = CreateViewModel(out _, out _);
        vm.PlaybackState = PlaybackState.Running;

        await vm.ShutdownAsync(confirmIfRunning: false);

        Assert.False(vm.IsShuttingDown);
    }

    /// <summary>Закрытие окна на Desktop идёт двумя заходами: первый отменяется ради остановки
    /// станка, второй должен пройти. Признак «станок уже остановлен» обязан пережить завершение
    /// остановки — в отличие от оверлея.</summary>
    [Fact]
    public async Task ShutdownAsync_WhenFinished_ReportsTheShutdownComplete()
    {
        var vm = CreateViewModel(out _, out _);

        await vm.ShutdownAsync();

        Assert.True(vm.IsShutdownComplete);
    }

    [Fact]
    public async Task ShutdownAsync_WhenConfirmationIsDeclined_DoesNotReportTheShutdownComplete()
    {
        var vm = CreateViewModel(out _, out _);
        vm.PlaybackState = PlaybackState.Running;

        var shutdownTask = vm.ShutdownAsync();
        vm.ConfirmNoCommand.Execute(null);
        await shutdownTask;

        Assert.False(vm.IsShutdownComplete);
    }

    /// <summary>Не подключено — выходим без обращения к устройству, а не падаем на null.</summary>
    [Fact]
    public async Task ExitCommand_WhenNoSessionIsConnected_StillExits()
    {
        var vm = CreateViewModel(out var exitService, out _);
        vm.Connection.Session = null;

        await vm.ExitCommand.ExecuteAsync(null);

        Assert.Equal(1, exitService.ExitCallCount);
    }
}
