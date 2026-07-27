using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Tests.Services.Device;

public class RealtimeCommandChannelTests
{
    [Fact]
    public async Task SendAsync_SendsRawByteThroughTransport()
    {
        var transport = new FakeDeviceTransport();
        var channel = new RealtimeCommandChannel(transport);

        await channel.SendAsync(RealtimeCommand.JogCancel);

        Assert.Single(transport.SentRawBytes);
        Assert.Equal((byte)0x85, transport.SentRawBytes[0]);
    }
}
