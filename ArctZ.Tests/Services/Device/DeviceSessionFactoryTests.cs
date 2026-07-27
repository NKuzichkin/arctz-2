using System;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class DeviceSessionFactoryTests
{
    [Fact]
    public void Create_ReturnsSessionBoundToGivenTransport()
    {
        var transport = new FakeDeviceTransport();
        var factory = new DeviceSessionFactory(MachineLimits.Default);

        var session = factory.Create(transport);

        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
    }
}
