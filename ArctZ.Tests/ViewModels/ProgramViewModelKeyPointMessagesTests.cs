using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.App;
using ArctZ.Tests.Services.Device;
using ArctZ.Tests.Services.Program;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class ProgramViewModelKeyPointMessagesTests
{
    private static ProgramViewModel CreateViewModel(out FakeDeviceTransport transport)
    {
        transport = new FakeDeviceTransport();
        var storage = new FakeProgramStorage();
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default), new SingleRealDeviceEndpointProvider());
        return new ProgramViewModel(connection, storage, new TrajectoryCompiler(), new FakeAppExitService());
    }

    private static async Task<KeyPoint> CaptureOnePointAsync(ProgramViewModel vm, FakeDeviceTransport transport)
    {
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);
        return vm.KeyPoints[0];
    }

    [Fact]
    public async Task ShowKeyPointMessages_NoMessagesForThatPointYet_OpensModalWithAnEmptyList()
    {
        var vm = CreateViewModel(out var transport);
        var point = await CaptureOnePointAsync(vm, transport);

        vm.ShowKeyPointMessagesCommand.Execute(point);

        Assert.True(vm.IsShowingKeyPointMessages);
        Assert.Empty(vm.SelectedKeyPointMessages);
        Assert.True(vm.HasNoKeyPointMessages);
    }

    [Fact]
    public async Task ShowKeyPointMessages_TitleIdentifiesTheSelectedPoint()
    {
        var vm = CreateViewModel(out var transport);
        var point = await CaptureOnePointAsync(vm, transport);

        vm.ShowKeyPointMessagesCommand.Execute(point);

        Assert.Contains(point.Label!, vm.KeyPointMessagesTitle);
    }

    [Fact]
    public async Task CloseKeyPointMessages_ClosesTheModalAndClearsTheSelection()
    {
        var vm = CreateViewModel(out var transport);
        var point = await CaptureOnePointAsync(vm, transport);
        vm.ShowKeyPointMessagesCommand.Execute(point);

        vm.CloseKeyPointMessagesCommand.Execute(null);

        Assert.False(vm.IsShowingKeyPointMessages);
        Assert.Empty(vm.SelectedKeyPointMessages);
    }

    [Fact]
    public void GetKeyPointMessages_UnknownKeyPointId_ReturnsEmptyList()
    {
        var vm = CreateViewModel(out _);

        Assert.Empty(vm.GetKeyPointMessages(System.Guid.NewGuid()));
    }
}
