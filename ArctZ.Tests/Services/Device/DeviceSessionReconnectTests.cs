using System;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class DeviceSessionReconnectTests
{
    private readonly FakeDeviceTransport _transport = new();
    private readonly ManualPeriodicTimer _jogTimer = new();
    private readonly ManualPeriodicTimer _pollTimer = new();
    private readonly DeviceSession _session;

    public DeviceSessionReconnectTests()
    {
        var serializer = new FluidNcCommandSerializer();
        var realtimeChannel = new RealtimeCommandChannel(_transport);
        var commandQueue = new BufferAwareCommandQueue(_transport);
        var jogScheduler = new JogScheduler(
            new JogCommandFactory(MachineLimits.Default), serializer, _transport, realtimeChannel, _jogTimer, TimeSpan.FromMilliseconds(100));
        var statusPoller = new StatusPoller(realtimeChannel, _pollTimer, TimeSpan.FromMilliseconds(250));
        var reconnectPolicy = new FixedDelayReconnectPolicy(maxAttempts: 3, delay: TimeSpan.FromMilliseconds(1));

        _session = new DeviceSession(_transport, commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller, reconnectPolicy);
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
}
