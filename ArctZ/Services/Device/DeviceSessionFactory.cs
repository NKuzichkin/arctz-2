using System;

namespace ArctZ.Services.Device;

/// <summary>
/// Builds a fully-wired DeviceSession around a caller-supplied transport
/// (real or MockDeviceTransport). Each call gets its own timers/scheduler/
/// poller/event queue so switching endpoints never reuses stale state.
/// </summary>
public sealed class DeviceSessionFactory : IDeviceSessionFactory
{
    private static readonly TimeSpan JogInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromMilliseconds(200);
    private const int ReconnectMaxAttempts = 3;

    private readonly MachineLimits _limits;

    public DeviceSessionFactory(MachineLimits limits)
    {
        _limits = limits;
    }

    public IDeviceSession Create(IDeviceTransport transport)
    {
        var serializer = new FluidNcCommandSerializer();
        var realtimeChannel = new RealtimeCommandChannel(transport);
        var commandQueue = new BufferAwareCommandQueue(transport);
        var eventQueue = new SerialEventQueue();
        var jogScheduler = new JogScheduler(
            new JogCommandFactory(_limits, JogInterval),
            serializer,
            transport,
            realtimeChannel,
            new SystemPeriodicTimer(),
            JogInterval,
            eventQueue);
        var statusPoller = new StatusPoller(realtimeChannel, new SystemPeriodicTimer(), StatusPollInterval);
        var reconnectPolicy = new FixedDelayReconnectPolicy(ReconnectMaxAttempts, ReconnectDelay);

        return new DeviceSession(transport, commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller, reconnectPolicy, eventQueue, realtimeChannel);
    }
}
