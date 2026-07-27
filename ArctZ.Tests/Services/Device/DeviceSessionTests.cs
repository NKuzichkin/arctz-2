using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Tests.Services.Device;

public class DeviceSessionTests
{
    private readonly FakeDeviceTransport _transport = new();
    private readonly ManualPeriodicTimer _jogTimer = new();
    private readonly ManualPeriodicTimer _pollTimer = new();
    private readonly BufferAwareCommandQueue _commandQueue;
    private readonly DeviceSession _session;

    public DeviceSessionTests()
    {
        var serializer = new FluidNcCommandSerializer();
        var realtimeChannel = new RealtimeCommandChannel(_transport);
        _commandQueue = new BufferAwareCommandQueue(_transport);
        var jogScheduler = new JogScheduler(
            new JogCommandFactory(MachineLimits.Default), serializer, _transport, realtimeChannel, _jogTimer, TimeSpan.FromMilliseconds(100));
        var statusPoller = new StatusPoller(realtimeChannel, _pollTimer, TimeSpan.FromMilliseconds(250));
        var reconnectPolicy = new FixedDelayReconnectPolicy(maxAttempts: 3, delay: TimeSpan.FromMilliseconds(1));

        _session = new DeviceSession(_transport, _commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller, reconnectPolicy);
    }

    [Fact]
    public async Task ConnectAsync_TransitionsThroughConnectingToConnected()
    {
        var states = new List<ConnectionState>();
        _session.ConnectionStateChanged += () => states.Add(_session.ConnectionState);

        await _session.ConnectAsync("COM5");

        Assert.Equal(new[] { ConnectionState.Connecting, ConnectionState.Connected }, states);
        Assert.True(_transport.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_StartsStatusPolling()
    {
        await _session.ConnectAsync("COM5");

        Assert.True(_pollTimer.IsRunning);
    }

    [Fact]
    public async Task OnStatusReportLine_UpdatesDeviceStatusAndRaisesEvent()
    {
        await _session.ConnectAsync("COM5");
        var raised = false;
        _session.DeviceStatusChanged += () => raised = true;

        _transport.SimulateReceivedLine("<Idle|WPos:0.000,-80.000,-10.540,45.000|FS:0,0>");

        Assert.True(raised);
        Assert.Equal(MachineState.Idle, _session.DeviceStatus!.Value.State);
        Assert.Equal(new MachinePose(0.000, -80.000, -10.540, 45.000), _session.DeviceStatus.Value.WPos);
    }

    [Fact]
    public async Task OnStatusReportLine_UpdatesCommandQueueBufferCapacity()
    {
        await _session.ConnectAsync("COM5");
        _transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|Bf:1,6>");

        _ = _session.SendGCodeAsync("G1 X1"); // len 6, budget (6-1)=5 -> blocked

        Assert.DoesNotContain("G1 X1", _transport.SentLines);

        _transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|Bf:1,20>"); // budget 19 -> unblocks

        Assert.Contains("G1 X1", _transport.SentLines);
    }

    [Fact]
    public async Task OnErrorLine_RaisesCommandRejectedWithErrorCode()
    {
        await _session.ConnectAsync("COM5");
        CommandRejectedEventArgs? rejected = null;
        _session.CommandRejected += args => rejected = args;

        _ = _session.SendGCodeAsync("G0 X1000");
        _transport.SimulateReceivedLine("error:9");

        Assert.NotNull(rejected);
        Assert.Equal("G0 X1000", rejected!.Command.Line);
        Assert.Equal(9, rejected.ErrorCode);
    }

    [Fact]
    public async Task OnAlarmLine_RaisesAlarmTriggered()
    {
        await _session.ConnectAsync("COM5");
        int? alarmCode = null;
        _session.AlarmTriggered += code => alarmCode = code;

        _transport.SimulateReceivedLine("ALARM:1");

        Assert.Equal(1, alarmCode);
    }

    [Fact]
    public async Task BeginUpdateEndJog_DelegatesToJogSchedulerWithDualJoystickState()
    {
        await _session.ConnectAsync("COM5");

        _session.BeginJog();
        _session.UpdateJog(new DualJoystickState(new JoystickAxisInput(1, 0, 1), new JoystickAxisInput(0, 0, 0)));
        _jogTimer.RaiseElapsed();

        Assert.Contains(_transport.SentLines, line => line.StartsWith("$J=", StringComparison.Ordinal));

        _session.EndJog();

        Assert.Contains((byte)0x85, _transport.SentRawBytes);
    }

    [Fact]
    public async Task HomeAsync_EnqueuesHomingCommand()
    {
        await _session.ConnectAsync("COM5");

        _ = _session.HomeAsync();

        Assert.Contains("$H", _transport.SentLines);
    }

    [Fact]
    public async Task ResetAlarmAsync_EnqueuesAlarmResetCommand()
    {
        await _session.ConnectAsync("COM5");

        _ = _session.ResetAlarmAsync();

        Assert.Contains("$X", _transport.SentLines);
    }

    [Fact]
    public async Task DisconnectAsync_StopsPollingAndTransitionsToDisconnected()
    {
        await _session.ConnectAsync("COM5");

        await _session.DisconnectAsync();

        Assert.Equal(ConnectionState.Disconnected, _session.ConnectionState);
        Assert.False(_pollTimer.IsRunning);
        Assert.False(_transport.IsConnected);
    }
}
