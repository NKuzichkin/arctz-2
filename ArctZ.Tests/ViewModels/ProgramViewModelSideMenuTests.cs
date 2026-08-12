using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.Device;
using ArctZ.Tests.Services.Program;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class ProgramViewModelSideMenuTests
{
    private static ProgramViewModel CreateViewModel()
    {
        var transport = new FakeDeviceTransport();
        var storage = new FakeProgramStorage();
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default), new SingleRealDeviceEndpointProvider());
        return new ProgramViewModel(connection, storage, new TrajectoryCompiler());
    }

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
}
