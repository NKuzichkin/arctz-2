using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Simulation;

namespace ArctZ.Tests.Services.Device;

public class MockDeviceTransportTests
{
    private readonly ManualPeriodicTimer _ticker = new();
    private readonly MockDeviceTransport _mock;
    private readonly FluidNcStatusParser _parser = new();

    public MockDeviceTransportTests()
    {
        _mock = new MockDeviceTransport(MachineLimits.Default, _ticker, TimeSpan.FromMilliseconds(100));
    }

    private DeviceStatus QueryStatus()
    {
        StatusReportLine? report = null;
        void Handler(string line)
        {
            if (_parser.Parse(line) is StatusReportLine status)
            {
                report = status;
            }
        }

        _mock.LineReceived += Handler;
        _ = _mock.SendRawByteAsync((byte)'?');
        _mock.LineReceived -= Handler;

        return report!.Status;
    }

    /// <summary>Мягкий сброс (Ctrl-X) — единственная команда, которая реально опустошает буферы
    /// прошивки; на нём держится безопасный выход из приложения.</summary>
    [Fact]
    public async Task SendRawByteAsync_SoftReset_DropsQueuedLinesAndStopsMotion()
    {
        await _mock.ConnectAsync("demo");
        await _mock.SendLineAsync("G1 X100 F600");
        await _mock.SendLineAsync("G1 X200 F600");
        _ticker.RaiseElapsed(); // первая строка уходит в исполнение, станок трогается
        Assert.Equal(MachineState.Run, QueryStatus().State);

        await _mock.SendRawByteAsync(0x18);

        var status = QueryStatus();
        Assert.Equal(MachineState.Idle, status.State);
        Assert.Equal(15, status.PlannerBlocksAvailable);
        Assert.Equal(128, status.RxBytesAvailable);
    }

    [Fact]
    public async Task SendRawByteAsync_SoftResetAfterFeedHold_ClearsTheHold()
    {
        await _mock.ConnectAsync("demo");
        await _mock.SendRawByteAsync((byte)'!');
        Assert.Equal(MachineState.Hold, QueryStatus().State);

        await _mock.SendRawByteAsync(0x18);

        Assert.Equal(MachineState.Idle, QueryStatus().State);
    }

    [Fact]
    public async Task ConnectAsync_SetsIsConnectedAndStartsMotionTicker()
    {
        await _mock.ConnectAsync("demo");

        Assert.True(_mock.IsConnected);
        Assert.True(_ticker.IsRunning);
    }

    [Fact]
    public async Task SendRawByteAsync_StatusQuery_RepliesWithIdleAtOriginAndFullBuffer()
    {
        await _mock.ConnectAsync("demo");

        var status = QueryStatus();

        Assert.Equal(MachineState.Idle, status.State);
        Assert.Equal(MachinePose.Zero, status.WPos);
        Assert.Equal(15, status.PlannerBlocksAvailable);
        Assert.Equal(128, status.RxBytesAvailable);
    }

    [Fact]
    public async Task SendLineAsync_JogCommand_AcksThenMovesTowardTargetOverTicks()
    {
        await _mock.ConnectAsync("demo");
        string? firstReply = null;
        _mock.LineReceived += line => firstReply ??= line;

        await _mock.SendLineAsync("$J=G91 G21 X10 Y0 Z0 A0 F600");
        _ticker.RaiseElapsed(); // dequeues + acks; F600 units/min = 10/sec, tick=0.1s -> 1 unit/tick

        Assert.Equal("ok", firstReply);

        for (var i = 0; i < 20; i++)
        {
            _ticker.RaiseElapsed();
        }

        var status = QueryStatus();
        Assert.Equal(new MachinePose(10, 0, 0, 0), status.WPos);
        Assert.Equal(MachineState.Idle, status.State);
    }

    [Fact]
    public async Task SendRawByteAsync_JogCancel_StopsMotionImmediately()
    {
        await _mock.ConnectAsync("demo");
        await _mock.SendLineAsync("$J=G91 G21 X10 Y0 Z0 A0 F600");
        _ticker.RaiseElapsed(); // ack + first 1-unit step

        await _mock.SendRawByteAsync(0x85);
        var afterCancel = QueryStatus();

        _ticker.RaiseElapsed();
        _ticker.RaiseElapsed();
        var afterMoreTicks = QueryStatus();

        Assert.Equal(afterCancel.WPos, afterMoreTicks.WPos);
        Assert.Equal(MachineState.Idle, afterMoreTicks.State);
    }

    [Fact]
    public async Task SendLineAsync_Homing_ResetsPoseToZero()
    {
        await _mock.ConnectAsync("demo");
        await _mock.SendLineAsync("$J=G91 G21 X10 Y0 Z0 A0 F600");
        for (var i = 0; i < 21; i++)
        {
            _ticker.RaiseElapsed();
        }

        await _mock.SendLineAsync("$H");
        _ticker.RaiseElapsed();

        var status = QueryStatus();
        Assert.Equal(MachinePose.Zero, status.WPos);
    }

    [Fact]
    public async Task ForceNextCommandError_ReportsErrorInsteadOfOkAndSkipsEffect()
    {
        await _mock.ConnectAsync("demo");
        _mock.ForceNextCommandError(9);
        string? reply = null;
        _mock.LineReceived += line => reply ??= line;

        await _mock.SendLineAsync("$J=G91 G21 X10 Y0 Z0 A0 F600");
        _ticker.RaiseElapsed();

        Assert.Equal("error:9", reply);
        var status = QueryStatus();
        Assert.Equal(MachinePose.Zero, status.WPos);
    }

    [Fact]
    public async Task SendRawByteAsync_FeedHold_FreezesMotionAndReportsHold()
    {
        await _mock.ConnectAsync("demo");
        await _mock.SendLineAsync("$J=G91 G21 X10 Y0 Z0 A0 F600");
        _ticker.RaiseElapsed(); // ack + first 1-unit step

        await _mock.SendRawByteAsync((byte)'!');
        var atHold = QueryStatus();

        _ticker.RaiseElapsed();
        _ticker.RaiseElapsed();
        _ticker.RaiseElapsed();
        var afterHeldTicks = QueryStatus();

        Assert.Equal(MachineState.Hold, afterHeldTicks.State);
        Assert.Equal(atHold.WPos, afterHeldTicks.WPos);
    }

    [Fact]
    public async Task SendRawByteAsync_ResumeAfterFeedHold_ResumesMotion()
    {
        await _mock.ConnectAsync("demo");
        await _mock.SendLineAsync("$J=G91 G21 X10 Y0 Z0 A0 F600");
        _ticker.RaiseElapsed();

        await _mock.SendRawByteAsync((byte)'!');
        _ticker.RaiseElapsed();
        var whileHeld = QueryStatus();

        await _mock.SendRawByteAsync((byte)'~');
        _ticker.RaiseElapsed();
        var afterResume = QueryStatus();

        Assert.NotEqual(whileHeld.WPos, afterResume.WPos);
        Assert.Equal(MachineState.Run, afterResume.State);
    }

    [Fact]
    public async Task SendLineAsync_Dwell_BlocksMotionWithoutMovingUntilElapsed()
    {
        await _mock.ConnectAsync("demo");
        await _mock.SendLineAsync("G4 P1");
        _ticker.RaiseElapsed(); // ack + starts 1s dwell; this tick consumes 0.1s -> 0.9s remaining

        var duringDwell = QueryStatus();
        Assert.Equal(MachineState.Run, duringDwell.State);

        for (var i = 0; i < 9; i++)
        {
            _ticker.RaiseElapsed();
        }

        var afterDwell = QueryStatus();
        Assert.Equal(MachineState.Idle, afterDwell.State);
    }

    [Fact]
    public async Task TriggerAlarm_SetsAlarmStateAndRaisesAlarmLineWithCode()
    {
        await _mock.ConnectAsync("demo");
        string? raisedLine = null;
        _mock.LineReceived += line => raisedLine ??= line;

        _mock.TriggerAlarm(1);

        Assert.Equal("ALARM:1", raisedLine);
        var status = QueryStatus();
        Assert.Equal(MachineState.Alarm, status.State);
    }

    [Fact]
    public async Task TriggerAlarm_StopsInFlightMotionImmediately()
    {
        await _mock.ConnectAsync("demo");
        await _mock.SendLineAsync("$J=G91 G21 X10 Y0 Z0 A0 F600");
        _ticker.RaiseElapsed(); // ack + first 1-unit step

        _mock.TriggerAlarm(1);
        var atAlarm = QueryStatus();

        _ticker.RaiseElapsed();
        _ticker.RaiseElapsed();
        var afterMoreTicks = QueryStatus();

        Assert.Equal(atAlarm.WPos, afterMoreTicks.WPos);
        Assert.Equal(MachineState.Alarm, afterMoreTicks.State);
    }

    [Fact]
    public async Task SetResponseDelay_CalledBeforeSending_DelaysFirstCommandByConfiguredTicks()
    {
        await _mock.ConnectAsync("demo");
        _mock.SetResponseDelay(TimeSpan.FromMilliseconds(300)); // 300ms / 100ms tick = 3 ticks
        string? reply = null;
        _mock.LineReceived += line => reply ??= line;

        await _mock.SendLineAsync("G4 P0");

        _ticker.RaiseElapsed();
        Assert.Null(reply);
        _ticker.RaiseElapsed();
        Assert.Null(reply);
        _ticker.RaiseElapsed();
        Assert.Null(reply);
        _ticker.RaiseElapsed();
        Assert.Equal("ok", reply);
    }

    [Fact]
    public async Task TriggerAlarm_RejectsQueuedCommandsWithErrorSoTheyCannotResumeMotion()
    {
        await _mock.ConnectAsync("demo");
        await _mock.SendLineAsync("$J=G91 G21 X10 Y0 Z0 A0 F600");
        await _mock.SendLineAsync("$J=G91 G21 X10 Y0 Z0 A0 F600"); // second line still queued behind the first
        _ticker.RaiseElapsed(); // dequeues+acks the FIRST line, steps 1 unit

        _mock.TriggerAlarm(1);
        var atAlarm = QueryStatus();

        var repliesAfterAlarm = new List<string>();
        _mock.LineReceived += line => repliesAfterAlarm.Add(line);
        _ticker.RaiseElapsed();
        _ticker.RaiseElapsed();
        var afterMoreTicks = QueryStatus();

        Assert.Equal(atAlarm.WPos, afterMoreTicks.WPos);
        Assert.Equal(MachineState.Alarm, afterMoreTicks.State);
        Assert.DoesNotContain("ok", repliesAfterAlarm);
        Assert.Contains("error:9", repliesAfterAlarm); // the second queued line is rejected, not silently dropped
    }

    [Fact]
    public async Task TriggerAlarm_StillAllowsResetViaXCommand()
    {
        await _mock.ConnectAsync("demo");
        await _mock.SendLineAsync("$J=G91 G21 X10 Y0 Z0 A0 F600");
        _ticker.RaiseElapsed(); // ack + first 1-unit step

        _mock.TriggerAlarm(1);

        await _mock.SendLineAsync("$X");
        string? resetReply = null;
        _mock.LineReceived += line => resetReply ??= line;
        _ticker.RaiseElapsed();

        Assert.Equal("ok", resetReply);
        var status = QueryStatus();
        Assert.Equal(MachineState.Idle, status.State);
    }
}
