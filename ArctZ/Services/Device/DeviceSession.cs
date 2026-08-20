using System;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public sealed class DeviceSession : IDeviceSession
{
    private readonly IDeviceTransport _transport;
    private readonly IBufferAwareCommandQueue _commandQueue;
    private readonly IStatusParser _statusParser;
    private readonly IJogScheduler _jogScheduler;
    private readonly IStatusPoller _statusPoller;
    private readonly IReconnectPolicy _reconnectPolicy;
    private readonly ISerialEventQueue _eventQueue;
    private readonly IRealtimeCommandChannel _realtimeChannel;
    private string? _lastDeviceId;
    private int _connectionGeneration;

    /// <summary>The firmware reports free planner slots, not the total, so the largest free count
    /// seen — which happens when the machine is idle — stands in for the planner's capacity.</summary>
    private int _plannerCapacity;

    public DeviceSession(
        IDeviceTransport transport,
        IBufferAwareCommandQueue commandQueue,
        IStatusParser statusParser,
        IJogScheduler jogScheduler,
        IStatusPoller statusPoller,
        IReconnectPolicy reconnectPolicy,
        ISerialEventQueue eventQueue,
        IRealtimeCommandChannel realtimeChannel)
    {
        _transport = transport;
        _commandQueue = commandQueue;
        _statusParser = statusParser;
        _jogScheduler = jogScheduler;
        _statusPoller = statusPoller;
        _reconnectPolicy = reconnectPolicy;
        _eventQueue = eventQueue;
        _realtimeChannel = realtimeChannel;

        _commandQueue.CommandCompleted += OnCommandCompleted;
    }

    public ConnectionState ConnectionState { get; private set; } = ConnectionState.Disconnected;

    public DeviceStatus? DeviceStatus { get; private set; }

    public string? LastError { get; private set; }

    public event Action? ConnectionStateChanged;

    public event Action? DeviceStatusChanged;

    public event Action<CommandRejectedEventArgs>? CommandRejected;

    public event Action<int>? AlarmTriggered;

    public async Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        _lastDeviceId = deviceId;
        _eventQueue.Enqueue(() =>
        {
            _connectionGeneration++;
            SetConnectionState(ConnectionState.Connecting);
        });

        _transport.LineReceived += OnLineReceived;
        _transport.Disconnected += OnTransportDisconnected;

        await _transport.ConnectAsync(deviceId, cancellationToken).ConfigureAwait(false);

        _eventQueue.Enqueue(() =>
        {
            _statusPoller.Start();
            SetConnectionState(ConnectionState.Connected);
        });
    }

    public async Task DisconnectAsync()
    {
        _eventQueue.Enqueue(() => _connectionGeneration++);

        _statusPoller.Stop();
        _jogScheduler.Stop();

        _transport.Disconnected -= OnTransportDisconnected;
        await _transport.DisconnectAsync().ConfigureAwait(false);
        _transport.LineReceived -= OnLineReceived;

        _eventQueue.Enqueue(() => SetConnectionState(ConnectionState.Disconnected));
    }

    public void BeginJog() => _jogScheduler.Start();

    public void UpdateJog(DualJoystickState state) => _jogScheduler.UpdateState(state);

    public void EndJog() => _jogScheduler.Stop();

    public Task<CommandResult> SendGCodeAsync(string line, CancellationToken cancellationToken = default) =>
        _commandQueue.EnqueueAsync(new GCodeLineCommand(line), cancellationToken);

    public void AbortPendingCommands() => _commandQueue.AbortPending();

    public Task<CommandResult> HomeAsync(CancellationToken cancellationToken = default) =>
        _commandQueue.EnqueueAsync(new GCodeLineCommand("$H"), cancellationToken);

    public Task<CommandResult> ResetAlarmAsync(CancellationToken cancellationToken = default) =>
        _commandQueue.EnqueueAsync(new GCodeLineCommand("$X"), cancellationToken);

    public async Task<bool> StopAndDrainAsync(TimeSpan timeout)
    {
        // The jog cancel goes out directly rather than only through the scheduler: Stop() returns
        // on its first line when no jog is active, and this path may not skip anything.
        _jogScheduler.Stop();
        await _realtimeChannel.SendAsync(RealtimeCommand.JogCancel).ConfigureAwait(false);

        _commandQueue.AbortPending();
        await _realtimeChannel.SendAsync(RealtimeCommand.FeedHold).ConfigureAwait(false);

        var drained = await WaitForEmptyBufferAsync(timeout).ConfigureAwait(false);

        // Unconditional, drained or not: a feed hold only parks the motion, leaving the rest of it
        // in the planner to resume on the next '~'. The soft reset is what empties the buffer.
        await _realtimeChannel.SendAsync(RealtimeCommand.SoftReset).ConfigureAwait(false);

        return drained;
    }

    /// <summary>Waits for a status report showing a stopped machine with an empty planner. Only
    /// reports that arrive after the feed hold count — the last one before it was sampled while
    /// the machine was still moving, and can show an idle machine from before the planner filled.
    /// </summary>
    private async Task<bool> WaitForEmptyBufferAsync(TimeSpan timeout)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnStatusChanged()
        {
            if (IsStoppedWithEmptyBuffer(DeviceStatus))
            {
                completion.TrySetResult(true);
            }
        }

        DeviceStatusChanged += OnStatusChanged;
        try
        {
            using var timeoutCts = new CancellationTokenSource();
            var timeoutTask = Task.Delay(timeout, timeoutCts.Token);
            var finished = await Task.WhenAny(completion.Task, timeoutTask).ConfigureAwait(false);
            if (finished == timeoutTask)
            {
                return false;
            }

            timeoutCts.Cancel();
            return true;
        }
        finally
        {
            DeviceStatusChanged -= OnStatusChanged;
        }
    }

    private bool IsStoppedWithEmptyBuffer(DeviceStatus? status) => status switch
    {
        // Idle означает, что планировщик отработан до конца; свободные слоты это подтверждают.
        // Их отсутствие в отчёте (прошивку можно собрать без поля Bf) оставляет состояние
        // единственным свидетельством — принять его лучше, чем всегда упираться в таймаут.
        { State: MachineState.Idle } idle =>
            idle.PlannerBlocksAvailable is not { } available || available >= _plannerCapacity,

        // Удержание не опустошает планировщик — остаток движения лежит именно в нём, — поэтому
        // ждать пустых слотов здесь бессмысленно. Признак остановки — завершённое торможение:
        // "Hold:1" по спецификации grbl 1.1 значит «ещё тормозим», и сброс в этот момент
        // выбросит аварию с потерей позиции.
        { State: MachineState.Hold } hold => hold.SubState is not 1,

        // Авария — тоже остановка: станок не движется и не сдвинется до сброса.
        { State: MachineState.Alarm } => true,

        _ => false,
    };

    public Task FeedHoldAsync(CancellationToken cancellationToken = default) =>
        _realtimeChannel.SendAsync(RealtimeCommand.FeedHold, cancellationToken);

    public Task ResumeAsync(CancellationToken cancellationToken = default) =>
        _realtimeChannel.SendAsync(RealtimeCommand.CycleStartResume, cancellationToken);

    /// <summary>
    /// Fires ConnectionStateChanged synchronously, so a subscriber's
    /// continuation (e.g. an awaiter on a TaskCompletionSource without
    /// RunContinuationsAsynchronously) can resume inline before this call
    /// returns. Call this last within an enqueued action, after any other
    /// state a subscriber might read (e.g. _statusPoller having started)
    /// is already in place.
    /// </summary>
    private void SetConnectionState(ConnectionState state)
    {
        ConnectionState = state;
        ConnectionStateChanged?.Invoke();
    }

    private async void OnTransportDisconnected()
    {
        var myGeneration = 0;
        _eventQueue.Enqueue(() =>
        {
            myGeneration = _connectionGeneration;
            _statusPoller.Stop();
            _jogScheduler.Stop();
            SetConnectionState(ConnectionState.Reconnecting);
        });

        for (var attempt = 1; attempt <= _reconnectPolicy.MaxAttempts; attempt++)
        {
            await _reconnectPolicy.WaitBeforeRetryAsync(attempt).ConfigureAwait(false);

            try
            {
                await _transport.ConnectAsync(_lastDeviceId!).ConfigureAwait(false);

                var stale = false;
                _eventQueue.Enqueue(() =>
                {
                    if (myGeneration != _connectionGeneration)
                    {
                        stale = true;
                        return;
                    }

                    LastError = null;
                    _statusPoller.Start();
                    SetConnectionState(ConnectionState.Connected);
                });

                if (stale)
                {
                    await _transport.DisconnectAsync().ConfigureAwait(false);
                }

                return;
            }
            catch
            {
                // try again
            }
        }

        _eventQueue.Enqueue(() =>
        {
            if (myGeneration != _connectionGeneration)
            {
                return;
            }

            LastError = $"Reconnect failed after {_reconnectPolicy.MaxAttempts} attempts";
            SetConnectionState(ConnectionState.Disconnected);
        });
    }

    private void OnCommandCompleted(GCodeLineCommand command, CommandResult result)
    {
        if (result.Outcome is CommandOutcome.Rejected or CommandOutcome.Aborted)
        {
            _eventQueue.Enqueue(() => CommandRejected?.Invoke(new CommandRejectedEventArgs(command, result.ErrorCode)));
        }
    }

    // _jogScheduler.TryHandleAck()/UpdateStatus() and _commandQueue.HandleOk()/HandleError()/
    // UpdateBufferCapacity() are left as direct top-level calls below rather than folded into an
    // _eventQueue.Enqueue(...) here: they already serialize themselves (JogScheduler through this
    // same _eventQueue, BufferAwareCommandQueue through its own lock), and TryHandleAck()'s return
    // value relies on its enqueued action running inline — nesting it inside another Enqueue call
    // would defer it and make TryHandleAck() always report "not consumed". Only the state this
    // class owns directly (DeviceStatus/_plannerCapacity, and the events it raises) needs to move
    // into the queue, to serialize against the ConnectionState/_connectionGeneration mutations
    // that ConnectAsync/DisconnectAsync/OnTransportDisconnected already enqueue.
    private void OnLineReceived(string rawLine)
    {
        switch (_statusParser.Parse(rawLine))
        {
            // Jog lines bypass the command queue, so their acknowledgements must not resolve a
            // queued command — and a jog error must not abort the pending program.
            case OkLine:
                if (!_jogScheduler.TryHandleAck())
                {
                    _commandQueue.HandleOk();
                }

                break;
            case ErrorLine error:
                if (!_jogScheduler.TryHandleAck())
                {
                    _commandQueue.HandleError(error.Code);
                }

                break;
            case AlarmLine alarm:
                _eventQueue.Enqueue(() => AlarmTriggered?.Invoke(alarm.Code));
                break;
            case StatusReportLine report:
                _eventQueue.Enqueue(() =>
                {
                    DeviceStatus = report.Status;
                    if (report.Status.PlannerBlocksAvailable is { } free)
                    {
                        _plannerCapacity = Math.Max(_plannerCapacity, free);
                    }
                });

                if (report.Status.PlannerBlocksAvailable is { } planner && report.Status.RxBytesAvailable is { } rx)
                {
                    _commandQueue.UpdateBufferCapacity(rx, planner);
                }

                _jogScheduler.UpdateStatus(report.Status);
                _eventQueue.Enqueue(() => DeviceStatusChanged?.Invoke());
                break;
            case UnrecognizedLine:
                break;
        }
    }
}
