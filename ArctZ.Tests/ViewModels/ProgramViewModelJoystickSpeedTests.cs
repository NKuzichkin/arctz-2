using ArctZ.Components.VirtualJoystick;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.Device;
using ArctZ.Tests.Services.Program;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class ProgramViewModelJoystickSpeedTests
{
    private static (ProgramViewModel vm, FakeDeviceSession session) CreateViewModelWithFakeSession()
    {
        var transport = new FakeDeviceTransport();
        var storage = new FakeProgramStorage();
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default), new SingleRealDeviceEndpointProvider());
        var session = new FakeDeviceSession();
        connection.Session = session;
        var vm = new ProgramViewModel(connection, storage, new TrajectoryCompiler());
        return (vm, session);
    }

    [Fact]
    public void OnLeftJoystickMove_DefaultSpeed_SendsUnscaledInput()
    {
        var (vm, session) = CreateViewModelWithFakeSession();

        vm.OnLeftJoystickDown(new JoystickEventArgs { Force = 1.0, AngleDeg = 0 });

        Assert.Equal(1.0, session.LastJogState!.Value.Left.X, 3);
        Assert.Equal(1.0, session.LastJogState!.Value.Left.Force, 3);
    }

    [Fact]
    public void OnLeftJoystickMove_FiftyPercentSpeed_SendsHalvedInput()
    {
        var (vm, session) = CreateViewModelWithFakeSession();
        vm.JoystickSpeedPercent = 50;

        vm.OnLeftJoystickDown(new JoystickEventArgs { Force = 1.0, AngleDeg = 0 });

        Assert.Equal(0.5, session.LastJogState!.Value.Left.X, 3);
        Assert.Equal(0.5, session.LastJogState!.Value.Left.Force, 3);
    }

    [Fact]
    public void ChangingSpeedWhileStickHeld_ResendsScaledStateImmediately()
    {
        var (vm, session) = CreateViewModelWithFakeSession();
        vm.OnLeftJoystickDown(new JoystickEventArgs { Force = 1.0, AngleDeg = 0 });
        var callCountBefore = session.UpdateJogCallCount;

        vm.JoystickSpeedPercent = 25;

        Assert.True(session.UpdateJogCallCount > callCountBefore);
        Assert.Equal(0.25, session.LastJogState!.Value.Left.X, 3);
    }

    [Fact]
    public void ChangingSpeedWithNoStickHeld_DoesNotCallUpdateJog()
    {
        var (vm, session) = CreateViewModelWithFakeSession();

        vm.JoystickSpeedPercent = 25;

        Assert.Equal(0, session.UpdateJogCallCount);
    }
}
