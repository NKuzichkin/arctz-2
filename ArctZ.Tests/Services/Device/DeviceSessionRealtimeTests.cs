using System;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Tests.Services.Device;

public class DeviceSessionRealtimeTests
{
    [Fact]
    public async Task FeedHoldAsync_SendsFeedHoldByte()
    {
        var transport = new FakeDeviceTransport();
        var serializer = new FluidNcCommandSerializer();
        var realtimeChannel = new RealtimeCommandChannel(transport);
        var commandQueue = new BufferAwareCommandQueue(transport);
        var eventQueue = new SerialEventQueue();
        var jogScheduler = new JogScheduler(
            new JogCommandFactory(MachineLimits.Default, TimeSpan.FromMilliseconds(100)), serializer, transport, realtimeChannel, new ManualPeriodicTimer(), TimeSpan.FromMilliseconds(100), eventQueue);
        var statusPoller = new StatusPoller(realtimeChannel, new ManualPeriodicTimer(), TimeSpan.FromMilliseconds(250));
        var reconnectPolicy = new FixedDelayReconnectPolicy(3, TimeSpan.FromMilliseconds(1));
        var session = new DeviceSession(transport, commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller, reconnectPolicy, eventQueue, realtimeChannel);
        await session.ConnectAsync("COM5");

        await session.FeedHoldAsync();

        Assert.Contains((byte)'!', transport.SentRawBytes);
    }

    [Fact]
    public async Task ResumeAsync_SendsCycleStartResumeByte()
    {
        var transport = new FakeDeviceTransport();
        var serializer = new FluidNcCommandSerializer();
        var realtimeChannel = new RealtimeCommandChannel(transport);
        var commandQueue = new BufferAwareCommandQueue(transport);
        var eventQueue = new SerialEventQueue();
        var jogScheduler = new JogScheduler(
            new JogCommandFactory(MachineLimits.Default, TimeSpan.FromMilliseconds(100)), serializer, transport, realtimeChannel, new ManualPeriodicTimer(), TimeSpan.FromMilliseconds(100), eventQueue);
        var statusPoller = new StatusPoller(realtimeChannel, new ManualPeriodicTimer(), TimeSpan.FromMilliseconds(250));
        var reconnectPolicy = new FixedDelayReconnectPolicy(3, TimeSpan.FromMilliseconds(1));
        var session = new DeviceSession(transport, commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller, reconnectPolicy, eventQueue, realtimeChannel);
        await session.ConnectAsync("COM5");

        await session.ResumeAsync();

        Assert.Contains((byte)'~', transport.SentRawBytes);
    }
}
