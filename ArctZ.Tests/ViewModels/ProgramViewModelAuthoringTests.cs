using System.Linq;
using System.Threading.Tasks;
using ArctZ.Components.VirtualJoystick;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.Device;
using ArctZ.Tests.Services.Program;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class ProgramViewModelAuthoringTests
{
    private static ProgramViewModel CreateViewModel(out FakeDeviceTransport transport, out FakeProgramStorage storage)
    {
        transport = new FakeDeviceTransport();
        storage = new FakeProgramStorage();
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default));
        return new ProgramViewModel(connection, storage, new TrajectoryCompiler());
    }

    [Fact]
    public async Task CaptureWaypoint_UsesCurrentDeviceStatusPosition()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("<Idle|WPos:1,2,3,4|FS:0,0>");

        vm.CaptureWaypointCommand.Execute(null);

        Assert.Single(vm.Waypoints);
        Assert.Equal(new MachinePose(1, 2, 3, 4), vm.Waypoints[0].Pose);
    }

    [Fact]
    public async Task CaptureWaypoint_SecondPoint_AddsDefaultTransition()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureWaypointCommand.Execute(null);

        transport.SimulateReceivedLine("<Idle|WPos:10,0,0,0|FS:0,0>");
        vm.CaptureWaypointCommand.Execute(null);

        Assert.Equal(2, vm.Waypoints.Count);
        Assert.Single(vm.Transitions);
    }

    [Fact]
    public void CaptureWaypoint_NoActiveSession_DoesNothing()
    {
        var vm = CreateViewModel(out _, out _);

        vm.CaptureWaypointCommand.Execute(null);

        Assert.Empty(vm.Waypoints);
    }

    [Fact]
    public async Task RemoveWaypoint_MiddlePoint_RemovesItAndKeepsTransitionsInSync()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.ExecuteAsync(null);
        foreach (var pose in new[] { "0,0,0,0", "10,0,0,0", "20,0,0,0" })
        {
            transport.SimulateReceivedLine($"<Idle|WPos:{pose}|FS:0,0>");
            vm.CaptureWaypointCommand.Execute(null);
        }

        var middle = vm.Waypoints[1];
        vm.RemoveWaypointCommand.Execute(middle);

        Assert.Equal(2, vm.Waypoints.Count);
        Assert.Single(vm.Transitions);
        Assert.DoesNotContain(middle, vm.Waypoints);
    }

    [Fact]
    public async Task SaveProgramAsync_ThenRefreshLibrary_ListsSavedProgram()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureWaypointCommand.Execute(null);
        vm.ProgramName = "Тест";

        await vm.SaveProgramCommand.ExecuteAsync(null);
        await vm.RefreshLibraryCommand.ExecuteAsync(null);

        Assert.Contains(vm.Library, s => s.Name == "Тест");
    }

    [Fact]
    public async Task LeftAndRightJoystick_EndJogOnlyAfterBothSticksReleased()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.ExecuteAsync(null);

        vm.OnLeftJoystickDown(new JoystickEventArgs { Force = 1, AngleDeg = 0 });
        vm.OnRightJoystickDown(new JoystickEventArgs { Force = 1, AngleDeg = 90 });
        vm.OnLeftJoystickUp(new JoystickEventArgs { Force = 0, AngleDeg = 0 });

        Assert.DoesNotContain((byte)0x85, transport.SentRawBytes);

        vm.OnRightJoystickUp(new JoystickEventArgs { Force = 0, AngleDeg = 90 });

        Assert.Contains((byte)0x85, transport.SentRawBytes);
    }
}
