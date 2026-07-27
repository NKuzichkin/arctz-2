using System.Linq;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Tests.Services.Device;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class ConnectionViewModelTests
{
    [Fact]
    public void Constructor_DefaultsToFirstEndpointAndListsRealAndDemo()
    {
        var vm = new ConnectionViewModel(new FakeDeviceTransport(), () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default));

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
        var vm = new ConnectionViewModel(realTransport, () => demoTransport, new DeviceSessionFactory(MachineLimits.Default));
        vm.SelectedEndpoint = vm.AvailableEndpoints.Single(e => e.Kind == ConnectionEndpointKind.Demo);

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.True(demoTransport.IsConnected);
        Assert.False(realTransport.IsConnected);
        Assert.Equal(ConnectionState.Connected, vm.Session!.ConnectionState);
    }

    [Fact]
    public async Task ConnectCommand_RealDeviceSelected_ConnectsUsingRealTransport()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = new ConnectionViewModel(realTransport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default));

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.True(realTransport.IsConnected);
    }

    [Fact]
    public async Task DisconnectCommand_DisconnectsActiveSession()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = new ConnectionViewModel(realTransport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default));
        await vm.ConnectCommand.ExecuteAsync(null);

        await vm.DisconnectCommand.ExecuteAsync(null);

        Assert.False(realTransport.IsConnected);
        Assert.Equal(ConnectionState.Disconnected, vm.Session!.ConnectionState);
    }
}
