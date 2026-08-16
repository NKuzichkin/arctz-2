using System;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class DeviceSessionReconnectTests
{
    private readonly FakeDeviceTransport _transport = new();
    private readonly ManualPeriodicTimer _jogTimer = new();
    private readonly ManualPeriodicTimer _pollTimer = new();
    private readonly SerialEventQueue _eventQueue = new();
    private readonly DeviceSession _session;

    public DeviceSessionReconnectTests()
    {
        var serializer = new FluidNcCommandSerializer();
        var realtimeChannel = new RealtimeCommandChannel(_transport);
        var commandQueue = new BufferAwareCommandQueue(_transport);
        var jogScheduler = new JogScheduler(
            new JogCommandFactory(MachineLimits.Default, TimeSpan.FromMilliseconds(100)), serializer, _transport, realtimeChannel, _jogTimer, TimeSpan.FromMilliseconds(100), _eventQueue);
        var statusPoller = new StatusPoller(realtimeChannel, _pollTimer, TimeSpan.FromMilliseconds(250));
        var reconnectPolicy = new FixedDelayReconnectPolicy(maxAttempts: 3, delay: TimeSpan.FromMilliseconds(1));

        _session = new DeviceSession(_transport, commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller, reconnectPolicy, _eventQueue, realtimeChannel);
    }

    private Task WaitForConnectionStateAsync(ConnectionState target)
    {
        if (_session.ConnectionState == target)
        {
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource();
        void Handler()
        {
            if (_session.ConnectionState == target)
            {
                _session.ConnectionStateChanged -= Handler;
                tcs.TrySetResult();
            }
        }

        _session.ConnectionStateChanged += Handler;
        return tcs.Task;
    }

    [Fact]
    public async Task UnexpectedDisconnect_EntersReconnectingStateImmediately()
    {
        await _session.ConnectAsync("COM5");

        _transport.SimulateDisconnect();

        Assert.Equal(ConnectionState.Reconnecting, _session.ConnectionState);

        await WaitForConnectionStateAsync(ConnectionState.Connected).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task SuccessfulReconnect_AfterTransientFailure_ReturnsToConnected()
    {
        await _session.ConnectAsync("COM5");
        _transport.ConnectFailuresRemaining = 1;

        _transport.SimulateDisconnect();
        await WaitForConnectionStateAsync(ConnectionState.Connected).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ConnectionState.Connected, _session.ConnectionState);
        Assert.True(_pollTimer.IsRunning);
    }

    [Fact]
    public async Task ExhaustedRetries_EndsDisconnectedWithLastError()
    {
        await _session.ConnectAsync("COM5");
        _transport.ConnectFailuresRemaining = 10;

        _transport.SimulateDisconnect();
        await WaitForConnectionStateAsync(ConnectionState.Disconnected).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ConnectionState.Disconnected, _session.ConnectionState);
        Assert.NotNull(_session.LastError);
    }

    [Fact]
    public async Task ManualDisconnect_UnsubscribesFromTransportDisconnectedEvent()
    {
        await _session.ConnectAsync("COM5");
        await _session.DisconnectAsync();

        _transport.SimulateDisconnect();

        Assert.Equal(ConnectionState.Disconnected, _session.ConnectionState);
    }

    [Fact]
    public async Task DisconnectDuringInFlightReconnect_StaysDisconnectedAndDoesNotGetClobbered()
    {
        var reconnectPolicy = new ManualReconnectPolicy();
        var serializer = new FluidNcCommandSerializer();
        var realtimeChannel = new RealtimeCommandChannel(_transport);
        var commandQueue = new BufferAwareCommandQueue(_transport);
        var jogScheduler = new JogScheduler(
            new JogCommandFactory(MachineLimits.Default, TimeSpan.FromMilliseconds(100)), serializer, _transport, realtimeChannel, _jogTimer, TimeSpan.FromMilliseconds(100), _eventQueue);
        var statusPoller = new StatusPoller(realtimeChannel, _pollTimer, TimeSpan.FromMilliseconds(250));
        var session = new DeviceSession(_transport, commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller, reconnectPolicy, _eventQueue, realtimeChannel);

        await session.ConnectAsync("COM5");

        // Unexpected disconnect starts the reconnect loop; it immediately blocks
        // on WaitBeforeRetryAsync because ManualReconnectPolicy's gate is not yet released.
        _transport.SimulateDisconnect();
        Assert.Equal(ConnectionState.Reconnecting, session.ConnectionState);

        // The user manually disconnects while that reconnect attempt is still in flight.
        await session.DisconnectAsync();
        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);

        // Now let the stale reconnect attempt proceed. It will successfully reconnect
        // the transport (FakeDeviceTransport.ConnectFailuresRemaining is 0 by default),
        // but the generation guard must recognize it's stale, discard the result, and
        // tear the transport-level reconnect back down rather than clobbering the
        // session back to Connected.
        reconnectPolicy.ReleaseCurrentWait();
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
        Assert.False(_pollTimer.IsRunning);
        Assert.False(_transport.IsConnected);
    }
}
