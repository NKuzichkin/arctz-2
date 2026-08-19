using ArctZ.Services.App;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.Device;
using ArctZ.Tests.Services.Program;
using ArctZ.ViewModels;

namespace ArctZ.Tests.Services.App;

public class BackgroundSessionCoordinatorTests
{
    private readonly FakeBackgroundSessionHost _host = new();
    private readonly ProgramViewModel _program;

    public BackgroundSessionCoordinatorTests()
    {
        var connection = new ConnectionViewModel(
            new FakeDeviceTransport(),
            () => new FakeDeviceTransport(),
            new DeviceSessionFactory(MachineLimits.Default),
            new SingleRealDeviceEndpointProvider());
        _program = new ProgramViewModel(
            connection,
            new FakeProgramStorage(),
            new TrajectoryCompiler(),
            new FakeAppExitService());
    }

    private BackgroundSessionCoordinator CreateCoordinator() => new(_program, _host);

    private void Connect() => _program.Connection.Session = new FakeDeviceSession();

    [Fact]
    public void WhileDisconnected_NothingIsShown()
    {
        using var coordinator = CreateCoordinator();

        Assert.Empty(_host.Updates);
    }

    [Fact]
    public void OnConnect_TheSessionIsShown()
    {
        using var coordinator = CreateCoordinator();

        Connect();

        Assert.NotNull(_host.LastUpdate);
        Assert.False(_host.LastUpdate!.Value.CanStop);
    }

    [Fact]
    public void WhenPlaybackStarts_TheSessionOffersPauseAndStop()
    {
        using var coordinator = CreateCoordinator();
        Connect();

        _program.PlaybackState = PlaybackState.Running;

        Assert.True(_host.LastUpdate!.Value.CanPause);
        Assert.True(_host.LastUpdate.Value.CanStop);
    }

    [Fact]
    public void WhenTheProgramIsRenamed_TheSessionTitleFollows()
    {
        using var coordinator = CreateCoordinator();
        Connect();

        _program.ProgramName = "Проезд по цеху";

        Assert.Equal("Проезд по цеху", _host.LastUpdate!.Value.Title);
    }

    [Fact]
    public void OnDisconnect_TheSessionIsStopped()
    {
        using var coordinator = CreateCoordinator();
        Connect();

        _program.Connection.Session = null;

        Assert.Equal(1, _host.StopCallCount);
    }

    /// <summary>Разрыв связи при уже убранном сеансе не должен снова дёргать платформу:
    /// каждый вызов Stop() на Android — это обращение к системному сервису.</summary>
    [Fact]
    public void OnDisconnect_WhenNothingWasShown_TheHostIsLeftAlone()
    {
        using var coordinator = CreateCoordinator();

        _program.Connection.Session = null;

        Assert.Equal(0, _host.StopCallCount);
    }

    /// <summary>Во время выполнения программы DeviceStatus меняется на каждый статус-репорт
    /// (позиция движется), и ProgramViewModel безусловно перевызывает StatusLabel — но пока
    /// PlaybackState остаётся Running, сам текст метки не меняется. Без дедупликации это
    /// дёргало Android-уведомление (пересоздание StartForeground) на каждый статус-репорт.</summary>
    [Fact]
    public void WhilePositionChangesDuringRun_UnchangedProjectionDoesNotReUpdateTheHost()
    {
        using var coordinator = CreateCoordinator();
        Connect();
        _program.PlaybackState = PlaybackState.Running;
        var updatesAfterRunningStarted = _host.Updates.Count;

        _program.Connection.DeviceStatus = new DeviceStatus(MachineState.Run, new MachinePose(1, 0, 0, 0), null, null);
        _program.Connection.DeviceStatus = new DeviceStatus(MachineState.Run, new MachinePose(2, 0, 0, 0), null, null);

        Assert.Equal(updatesAfterRunningStarted, _host.Updates.Count);
    }

    [Fact]
    public void AfterDispose_ViewModelChangesAreIgnored()
    {
        var coordinator = CreateCoordinator();
        Connect();
        var updatesBeforeDispose = _host.Updates.Count;

        coordinator.Dispose();
        _program.PlaybackState = PlaybackState.Running;

        Assert.Equal(updatesBeforeDispose, _host.Updates.Count);
    }
}
