using System.Linq;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Tests.Services.Device;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class ConnectionViewModelTests
{
    private static ConnectionViewModel CreateVm(IDeviceTransport realTransport, IDeviceTransport? demoTransport = null) =>
        new(realTransport, () => demoTransport ?? new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default));

    [Fact]
    public void Constructor_DefaultsToFirstEndpointAndListsRealAndDemo()
    {
        var vm = CreateVm(new FakeDeviceTransport());

        Assert.Equal(2, vm.AvailableEndpoints.Count);
        Assert.Contains(vm.AvailableEndpoints, e => e.Kind == ConnectionEndpointKind.RealDevice);
        Assert.Contains(vm.AvailableEndpoints, e => e.Kind == ConnectionEndpointKind.Demo);
        Assert.Equal(ConnectionEndpointKind.RealDevice, vm.SelectedEndpoint!.Kind);
    }

    [Fact]
    public async Task ConnectCommand_DemoSelected_ConnectsUsingDemoTransportNotRealTransport()
    {
        var realTransport = new FakeDeviceTransport();
        var demoTransport = new FakeDeviceTransport();
        var vm = CreateVm(realTransport, demoTransport);
        vm.SelectedEndpoint = vm.AvailableEndpoints.Single(e => e.Kind == ConnectionEndpointKind.Demo);

        await vm.ConnectCommand.Execute();

        Assert.True(demoTransport.IsConnected);
        Assert.False(realTransport.IsConnected);
        Assert.Equal(ConnectionState.Connected, vm.Session!.ConnectionState);
    }

    [Fact]
    public async Task ConnectCommand_RealDeviceSelected_ConnectsUsingRealTransport()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = CreateVm(realTransport);

        await vm.ConnectCommand.Execute();

        Assert.True(realTransport.IsConnected);
    }

    [Fact]
    public async Task DisconnectCommand_DisconnectsActiveSessionAndClearsIt()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = CreateVm(realTransport);
        await vm.ConnectCommand.Execute();

        await vm.DisconnectCommand.Execute();

        Assert.False(realTransport.IsConnected);
        Assert.Null(vm.Session);
    }

    [Fact]
    public async Task ConnectCommand_WhileAlreadyConnected_DisconnectsPreviousSessionBeforeCreatingNewOne()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = CreateVm(realTransport);
        await vm.ConnectCommand.Execute();
        var firstSession = vm.Session;

        await vm.ConnectCommand.Execute();

        Assert.NotNull(firstSession);
        Assert.NotSame(firstSession, vm.Session);
        Assert.Equal(ConnectionState.Disconnected, firstSession!.ConnectionState);
        Assert.Equal(ConnectionState.Connected, vm.Session!.ConnectionState);
        Assert.True(realTransport.IsConnected);
    }

    [Fact]
    public async Task ConnectCommand_SwitchingEndpointWhileConnected_TearsDownTheRealTransportSession()
    {
        var realTransport = new FakeDeviceTransport();
        var demoTransport = new FakeDeviceTransport();
        var vm = CreateVm(realTransport, demoTransport);
        await vm.ConnectCommand.Execute();

        vm.SelectedEndpoint = vm.AvailableEndpoints.Single(e => e.Kind == ConnectionEndpointKind.Demo);
        await vm.ConnectCommand.Execute();

        Assert.False(realTransport.IsConnected);
        Assert.True(demoTransport.IsConnected);
        Assert.Equal(ConnectionState.Connected, vm.Session!.ConnectionState);
    }

    [Fact]
    public async Task IsConnectionModalVisible_TracksSessionLifecycle()
    {
        var vm = CreateVm(new FakeDeviceTransport());

        Assert.True(vm.IsConnectionModalVisible);

        await vm.ConnectCommand.Execute();
        Assert.False(vm.IsConnectionModalVisible);

        await vm.DisconnectCommand.Execute();
        Assert.True(vm.IsConnectionModalVisible);
    }

    [Fact]
    public async Task ConnectCommand_TransportThrows_ResetsSessionAndReenablesRetry()
    {
        var realTransport = new FakeDeviceTransport { ConnectFailuresRemaining = 1 };
        var vm = CreateVm(realTransport);

        await vm.ConnectCommand.Execute();

        Assert.Null(vm.Session);
        Assert.True(vm.IsConnectionModalVisible);
        Assert.True(vm.ConnectCommand.CanExecute(null));

        // Retry succeeds now that ConnectFailuresRemaining is exhausted.
        await vm.ConnectCommand.Execute();
        Assert.NotNull(vm.Session);
        Assert.False(vm.IsConnectionModalVisible);
        Assert.Equal(ConnectionState.Connected, vm.Session!.ConnectionState);
    }
}
