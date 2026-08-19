using System;
using System.Threading.Tasks;
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

    [Fact]
    public async Task WhilePositionAdvancesDuringRun_HostIsUpdatedOnlyWhenTheRoundedPercentChanges()
    {
        var currentTime = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var transport = new FakeDeviceTransport();
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default), new SingleRealDeviceEndpointProvider());
        var program = new ProgramViewModel(connection, new FakeProgramStorage(), new TrajectoryCompiler(), new FakeAppExitService(), () => currentTime);
        using var coordinator = new BackgroundSessionCoordinator(program, _host);

        await program.Connection.ConnectCommand.Execute();
        foreach (var pose in new[] { "0,0,0,0", "100,0,0,0" })
        {
            transport.SimulateReceivedLine($"<Idle|WPos:{pose}|FS:0,0>");
            program.CaptureKeyPointCommand.Execute(null);
        }
        for (var i = 0; i < program.KeyPoints.Count; i++)
        {
            program.KeyPoints[i] = program.KeyPoints[i] with { TransitionSeconds = 5, DwellSeconds = 0, Ease = EaseMode.None, ContinuousBlend = true };
        }
        program.ProgramId = Guid.NewGuid();
        program.IsDirty = false;

        // Capturing left the simulated machine at the last captured pose (100,0,0,0) — reset it
        // to the program's actual starting pose before Play, so the tracker's captured starting
        // vertex matches what these assertions assume.
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = program.PlayCommand.ExecuteAsync(null);
        var updatesBeforeMotion = _host.Updates.Count;

        // 2 key points at TransitionSeconds=5 each = 10s total estimate for the pass (segment 0's
        // zero-distance self-move included, same as segment 1's real move — both cost 5s).
        currentTime = currentTime.AddSeconds(0.1); // 0.1 of 10s = 1%: rounds to 0%
        transport.SimulateReceivedLine("<Run|WPos:1.000,0.000,0.000,0.000|FS:0,0>");
        Assert.Equal(updatesBeforeMotion, _host.Updates.Count);

        currentTime = currentTime.AddSeconds(0.4); // 0.5 of 10s = 5%: rounds to 5%
        transport.SimulateReceivedLine("<Run|WPos:5.000,0.000,0.000,0.000|FS:0,0>");
        Assert.True(_host.Updates.Count > updatesBeforeMotion);
        Assert.Equal(5, _host.LastUpdate!.Value.ProgressPercent);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await program.StopCommand.ExecuteAsync(null);
        await playTask;
    }

    [Fact]
    public async Task WhileStationaryDuringADwell_HostStillUpdatesAsTimeElapses()
    {
        var currentTime = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var transport = new FakeDeviceTransport();
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default), new SingleRealDeviceEndpointProvider());
        var program = new ProgramViewModel(connection, new FakeProgramStorage(), new TrajectoryCompiler(), new FakeAppExitService(), () => currentTime);
        using var coordinator = new BackgroundSessionCoordinator(program, _host);

        await program.Connection.ConnectCommand.Execute();
        foreach (var pose in new[] { "0,0,0,0", "100,0,0,0" })
        {
            transport.SimulateReceivedLine($"<Idle|WPos:{pose}|FS:0,0>");
            program.CaptureKeyPointCommand.Execute(null);
        }
        for (var i = 0; i < program.KeyPoints.Count; i++)
        {
            program.KeyPoints[i] = program.KeyPoints[i] with { TransitionSeconds = 5, DwellSeconds = 0, Ease = EaseMode.None, ContinuousBlend = true };
        }
        program.ProgramId = Guid.NewGuid();
        program.IsDirty = false;
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = program.PlayCommand.ExecuteAsync(null);

        // No position change at all between these two ticks (as if the machine were dwelling) —
        // only elapsed time moves. 10s total estimate (2 points x 5s); 0.5s = 5%.
        currentTime = currentTime.AddSeconds(0.5);
        program.OnClockTickForTests();

        Assert.Equal(5, _host.LastUpdate!.Value.ProgressPercent);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await program.StopCommand.ExecuteAsync(null);
        await playTask;
    }
}
