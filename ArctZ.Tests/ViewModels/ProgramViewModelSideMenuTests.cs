using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.App;
using ArctZ.Tests.Services.Device;
using ArctZ.Tests.Services.Program;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class ProgramViewModelSideMenuTests
{
    private static ProgramViewModel CreateViewModel(out FakeAppExitService exitService)
    {
        var transport = new FakeDeviceTransport();
        var storage = new FakeProgramStorage();
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default), new SingleRealDeviceEndpointProvider());
        exitService = new FakeAppExitService();
        return new ProgramViewModel(connection, storage, new TrajectoryCompiler(), exitService);
    }

    private static ProgramViewModel CreateViewModel() => CreateViewModel(out _);

    [Fact]
    public void ToggleSideMenuCommand_TogglesIsSideMenuOpen()
    {
        var vm = CreateViewModel();
        Assert.False(vm.IsSideMenuOpen);

        vm.ToggleSideMenuCommand.Execute(null);
        Assert.True(vm.IsSideMenuOpen);

        vm.ToggleSideMenuCommand.Execute(null);
        Assert.False(vm.IsSideMenuOpen);
    }

    [Fact]
    public void CloseSideMenuCommand_ClosesOpenMenu()
    {
        var vm = CreateViewModel();
        vm.ToggleSideMenuCommand.Execute(null);
        Assert.True(vm.IsSideMenuOpen);

        vm.CloseSideMenuCommand.Execute(null);

        Assert.False(vm.IsSideMenuOpen);
    }

    [Fact]
    public void OpenGCodeLogCommand_OpensLogAndClosesMenu()
    {
        var vm = CreateViewModel();
        vm.ToggleSideMenuCommand.Execute(null);
        Assert.False(vm.Connection.IsGCodeLogOpen);

        vm.OpenGCodeLogCommand.Execute(null);

        Assert.True(vm.Connection.IsGCodeLogOpen);
        Assert.False(vm.IsSideMenuOpen);
    }

    [Fact]
    public void OpenGCodeLogCommand_WhenLogAlreadyOpen_LeavesItOpen()
    {
        var vm = CreateViewModel();
        vm.OpenGCodeLogCommand.Execute(null);
        vm.OpenGCodeLogCommand.Execute(null);
        Assert.True(vm.Connection.IsGCodeLogOpen);
    }

    [Fact]
    public void OpenMockSettingsCommand_OpensDialogAndClosesMenu()
    {
        var vm = CreateViewModel();
        vm.ToggleSideMenuCommand.Execute(null);
        Assert.False(vm.Connection.IsMockSettingsOpen);

        vm.OpenMockSettingsCommand.Execute(null);

        Assert.True(vm.Connection.IsMockSettingsOpen);
        Assert.False(vm.IsSideMenuOpen);
    }

    [Fact]
    public void OpenMockSettingsCommand_WhenDialogAlreadyOpen_LeavesItOpen()
    {
        var vm = CreateViewModel();
        vm.OpenMockSettingsCommand.Execute(null);
        vm.OpenMockSettingsCommand.Execute(null);
        Assert.True(vm.Connection.IsMockSettingsOpen);
    }

    [Fact]
    public async Task ExitCommand_WhenIdle_ExitsImmediatelyAndClosesMenu()
    {
        var vm = CreateViewModel(out var exitService);
        vm.ToggleSideMenuCommand.Execute(null);

        await vm.ExitCommand.ExecuteAsync(null);

        Assert.Null(vm.PendingConfirmation);
        Assert.False(vm.IsSideMenuOpen);
        Assert.Equal(1, exitService.ExitCallCount);
    }

    [Fact]
    public async Task ExitCommand_WhileProgramRunning_AsksForConfirmationBeforeExiting()
    {
        var vm = CreateViewModel(out var exitService);
        vm.PlaybackState = PlaybackState.Running;

        var exitTask = vm.ExitCommand.ExecuteAsync(null);

        Assert.NotNull(vm.PendingConfirmation);
        Assert.Equal(0, exitService.ExitCallCount);

        vm.ConfirmYesCommand.Execute(null);
        await exitTask;

        Assert.Equal(1, exitService.ExitCallCount);
    }

    [Fact]
    public async Task ExitCommand_WhileProgramRunning_DecliningConfirmationDoesNotExit()
    {
        var vm = CreateViewModel(out var exitService);
        vm.PlaybackState = PlaybackState.Running;

        var exitTask = vm.ExitCommand.ExecuteAsync(null);

        Assert.NotNull(vm.PendingConfirmation);
        vm.ConfirmNoCommand.Execute(null);
        await exitTask;

        Assert.Null(vm.PendingConfirmation);
        Assert.Equal(0, exitService.ExitCallCount);
    }
}
