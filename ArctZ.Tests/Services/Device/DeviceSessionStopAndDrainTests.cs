using System;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Tests.Services.Device;

/// <summary>Остановка перед выходом из приложения: команды уходят безусловно,
/// а ожидание пустого буфера прошивки ограничено таймаутом.</summary>
public class DeviceSessionStopAndDrainTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan LongTimeout = TimeSpan.FromSeconds(30);

    private readonly FakeDeviceTransport _transport = new();
    private readonly ManualPeriodicTimer _jogTimer = new();
    private readonly ManualPeriodicTimer _pollTimer = new();
    private readonly BufferAwareCommandQueue _commandQueue;
    private readonly DeviceSession _session;

    public DeviceSessionStopAndDrainTests()
    {
        var serializer = new FluidNcCommandSerializer();
        var realtimeChannel = new RealtimeCommandChannel(_transport);
        var eventQueue = new SerialEventQueue();
        _commandQueue = new BufferAwareCommandQueue(_transport);
        var jogScheduler = new JogScheduler(
            new JogCommandFactory(MachineLimits.Default, TimeSpan.FromMilliseconds(100)), serializer, _transport, realtimeChannel, _jogTimer, TimeSpan.FromMilliseconds(100), eventQueue);
        var statusPoller = new StatusPoller(realtimeChannel, _pollTimer, TimeSpan.FromMilliseconds(250));
        var reconnectPolicy = new FixedDelayReconnectPolicy(maxAttempts: 3, delay: TimeSpan.FromMilliseconds(1));

        _session = new DeviceSession(_transport, _commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller, reconnectPolicy, eventQueue, realtimeChannel);
    }

    [Fact]
    public async Task StopAndDrainAsync_WhenNothingIsRunning_StillSendsEveryStopCommand()
    {
        await _session.ConnectAsync("COM5");

        await _session.StopAndDrainAsync(ShortTimeout);

        Assert.Equal(
            new[] { RealtimeCommand.JogCancel.Value, RealtimeCommand.FeedHold.Value, RealtimeCommand.SoftReset.Value },
            _transport.SentRawBytes);
    }

    [Fact]
    public async Task StopAndDrainAsync_SendsSoftResetAfterTheBufferReportsEmpty()
    {
        await _session.ConnectAsync("COM5");

        var stopTask = _session.StopAndDrainAsync(LongTimeout);
        Assert.DoesNotContain(RealtimeCommand.SoftReset.Value, _transport.SentRawBytes);

        _transport.SimulateReceivedLine("<Idle|MPos:0.000,0.000,0.000,0.000|Bf:15,128>");

        Assert.True(await stopTask);
        Assert.Equal(RealtimeCommand.SoftReset.Value, _transport.SentRawBytes[^1]);
    }

    [Fact]
    public async Task StopAndDrainAsync_AbortsCommandsStillWaitingInTheAppQueue()
    {
        await _session.ConnectAsync("COM5");
        _transport.SimulateReceivedLine("<Idle|MPos:0,0,0,0|Bf:15,20>");
        var first = _session.SendGCodeAsync("G1 X10 F500");
        var queued = _session.SendGCodeAsync("G1 X999999 Y999999 Z999999 A999999 F500");

        await _session.StopAndDrainAsync(ShortTimeout);

        Assert.False(first.IsCompleted);
        Assert.Equal(CommandOutcome.Aborted, (await queued).Outcome);
    }

    [Fact]
    public async Task StopAndDrainAsync_WhenTheDeviceStaysSilent_GivesUpAfterTheTimeout()
    {
        await _session.ConnectAsync("COM5");

        var drained = await _session.StopAndDrainAsync(ShortTimeout);

        Assert.False(drained);
        Assert.Contains(RealtimeCommand.SoftReset.Value, _transport.SentRawBytes);
    }

    [Fact]
    public async Task StopAndDrainAsync_WhileThePlannerStillHoldsBlocks_KeepsWaiting()
    {
        await _session.ConnectAsync("COM5");
        // Устанавливает ёмкость планировщика: прошивка сообщает только число
        // свободных слотов, поэтому за ёмкость принимается максимум виденного.
        _transport.SimulateReceivedLine("<Idle|MPos:0,0,0,0|Bf:15,128>");

        var stopTask = _session.StopAndDrainAsync(ShortTimeout);
        _transport.SimulateReceivedLine("<Idle|MPos:0,0,0,0|Bf:9,128>");

        Assert.False(await stopTask);
    }

    [Fact]
    public async Task StopAndDrainAsync_OnceThePlannerEmpties_ReportsDrained()
    {
        await _session.ConnectAsync("COM5");
        _transport.SimulateReceivedLine("<Idle|MPos:0,0,0,0|Bf:15,128>");

        var stopTask = _session.StopAndDrainAsync(LongTimeout);
        _transport.SimulateReceivedLine("<Idle|MPos:0,0,0,0|Bf:9,128>");
        _transport.SimulateReceivedLine("<Idle|MPos:0,0,0,0|Bf:15,128>");

        Assert.True(await stopTask);
    }

    [Fact]
    public async Task StopAndDrainAsync_IgnoresTheStatusReportThatPrecededTheFeedHold()
    {
        await _session.ConnectAsync("COM5");
        _transport.SimulateReceivedLine("<Idle|MPos:0,0,0,0|Bf:15,128>");

        var drained = await _session.StopAndDrainAsync(ShortTimeout);

        Assert.False(drained);
    }

    /// <summary>"Hold:1" — станок ещё тормозит; по спецификации grbl 1.1 сброс в этот момент
    /// выбрасывает аварию с потерей позиции. Ждём "Hold:0", когда торможение закончено.</summary>
    [Fact]
    public async Task StopAndDrainAsync_WhileTheHoldIsStillInProgress_KeepsWaiting()
    {
        await _session.ConnectAsync("COM5");

        var stopTask = _session.StopAndDrainAsync(ShortTimeout);
        _transport.SimulateReceivedLine("<Hold:1|MPos:0,0,0,0|Bf:9,128>");

        Assert.False(await stopTask);
    }

    /// <summary>Планировщик после удержания остаётся непустым — остаток движения лежит именно
    /// в нём, — поэтому признаком остановки служит завершённое торможение, а не свободные слоты.
    /// </summary>
    [Fact]
    public async Task StopAndDrainAsync_WhenTheHoldCompletes_ReportsStoppedEvenWithBlocksLeftInThePlanner()
    {
        await _session.ConnectAsync("COM5");

        var stopTask = _session.StopAndDrainAsync(LongTimeout);
        _transport.SimulateReceivedLine("<Hold:1|MPos:0,0,0,0|Bf:9,128>");
        _transport.SimulateReceivedLine("<Hold:0|MPos:0,0,0,0|Bf:9,128>");

        Assert.True(await stopTask);
    }

    [Fact]
    public async Task StopAndDrainAsync_WhenTheMachineIsAlreadyInAlarm_TreatsItAsStopped()
    {
        await _session.ConnectAsync("COM5");

        var stopTask = _session.StopAndDrainAsync(LongTimeout);

        _transport.SimulateReceivedLine("<Alarm|MPos:0,0,0,0|Bf:15,128>");

        Assert.True(await stopTask);
    }
}
