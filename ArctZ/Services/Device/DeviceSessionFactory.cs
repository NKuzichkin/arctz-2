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
    private static readonly TimeSpan DefaultStatusPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromMilliseconds(200);
    private const int ReconnectMaxAttempts = 3;

    private readonly MachineLimits _limits;
    private readonly TimeSpan _statusPollInterval;

    /// <param name="statusPollInterval">How often '?' goes out. Raising the rate is only useful for
    /// diagnostics that need dense WPos samples; it costs link bandwidth the jog stream competes
    /// for, so the default stays at 250 ms.</param>
    public DeviceSessionFactory(MachineLimits limits, TimeSpan? statusPollInterval = null)
    {
        _limits = limits;
        _statusPollInterval = statusPollInterval ?? DefaultStatusPollInterval;
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
        var statusPoller = new StatusPoller(realtimeChannel, new SystemPeriodicTimer(), _statusPollInterval);
        var reconnectPolicy = new FixedDelayReconnectPolicy(ReconnectMaxAttempts, ReconnectDelay);

        return new DeviceSession(transport, commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller, reconnectPolicy, eventQueue, realtimeChannel);
    }
}
