using System.Collections.Generic;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class LoggingDeviceTransportTests
{
    [Fact]
    public async Task SendLineAsync_RaisesLineSentAndForwardsToInner()
    {
        var inner = new FakeDeviceTransport();
        var transport = new LoggingDeviceTransport(inner);
        var raised = new List<string>();
        transport.LineSent += raised.Add;

        await transport.SendLineAsync("G1 X10 Y20 F500");

        Assert.Equal(new[] { "G1 X10 Y20 F500" }, raised);
        Assert.Equal(new[] { "G1 X10 Y20 F500" }, inner.SentLines);
    }

    [Fact]
    public async Task SendRawByteAsync_ForwardsToInnerAndDoesNotRaiseLineSent()
    {
        var inner = new FakeDeviceTransport();
        var transport = new LoggingDeviceTransport(inner);
        var raised = new List<string>();
        transport.LineSent += raised.Add;

        await transport.SendRawByteAsync((byte)'?');

        Assert.Empty(raised);
        Assert.Equal(new byte[] { (byte)'?' }, inner.SentRawBytes);
    }

    [Fact]
    public async Task ConnectAsyncAndDisconnectAsync_ForwardToInner()
    {
        var inner = new FakeDeviceTransport();
        var transport = new LoggingDeviceTransport(inner);

        await transport.ConnectAsync("device-1");
        Assert.True(inner.IsConnected);
        Assert.True(transport.IsConnected);

        await transport.DisconnectAsync();
        Assert.False(inner.IsConnected);
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public void LineReceivedAndDisconnected_ForwardFromInner()
    {
        var inner = new FakeDeviceTransport();
        var transport = new LoggingDeviceTransport(inner);
        string? receivedLine = null;
        var disconnectedRaised = false;
        transport.LineReceived += line => receivedLine = line;
        transport.Disconnected += () => disconnectedRaised = true;

        inner.SimulateReceivedLine("ok");
        inner.SimulateDisconnect();

        Assert.Equal("ok", receivedLine);
        Assert.True(disconnectedRaised);
    }
}
