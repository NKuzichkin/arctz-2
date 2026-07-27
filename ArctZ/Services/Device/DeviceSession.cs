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
    private string? _lastDeviceId;

    public DeviceSession(
        IDeviceTransport transport,
        IBufferAwareCommandQueue commandQueue,
        IStatusParser statusParser,
        IJogScheduler jogScheduler,
        IStatusPoller statusPoller,
        IReconnectPolicy reconnectPolicy)
    {
        _transport = transport;
        _commandQueue = commandQueue;
        _statusParser = statusParser;
        _jogScheduler = jogScheduler;
        _statusPoller = statusPoller;
        _reconnectPolicy = reconnectPolicy;

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
        SetConnectionState(ConnectionState.Connecting);

        _transport.LineReceived += OnLineReceived;
        _transport.Disconnected += OnTransportDisconnected;

        await _transport.ConnectAsync(deviceId, cancellationToken).ConfigureAwait(false);

        SetConnectionState(ConnectionState.Connected);
        _statusPoller.Start();
    }

    public async Task DisconnectAsync()
    {
        _statusPoller.Stop();
        _jogScheduler.Stop();

        _transport.Disconnected -= OnTransportDisconnected;
        await _transport.DisconnectAsync().ConfigureAwait(false);
        _transport.LineReceived -= OnLineReceived;

        SetConnectionState(ConnectionState.Disconnected);
    }

    public void BeginJog() => _jogScheduler.Start();

    public void UpdateJog(DualJoystickState state) => _jogScheduler.UpdateState(state);

    public void EndJog() => _jogScheduler.Stop();

    public Task<CommandResult> SendGCodeAsync(string line, CancellationToken cancellationToken = default) =>
        _commandQueue.EnqueueAsync(new GCodeLineCommand(line), cancellationToken);

    public Task<CommandResult> HomeAsync(CancellationToken cancellationToken = default) =>
        _commandQueue.EnqueueAsync(new GCodeLineCommand("$H"), cancellationToken);

    public Task<CommandResult> ResetAlarmAsync(CancellationToken cancellationToken = default) =>
        _commandQueue.EnqueueAsync(new GCodeLineCommand("$X"), cancellationToken);

    private void SetConnectionState(ConnectionState state)
    {
        ConnectionState = state;
        ConnectionStateChanged?.Invoke();
    }

    private async void OnTransportDisconnected()
    {
        _statusPoller.Stop();
        _jogScheduler.Stop();
        SetConnectionState(ConnectionState.Reconnecting);

        for (var attempt = 1; attempt <= _reconnectPolicy.MaxAttempts; attempt++)
        {
            await _reconnectPolicy.WaitBeforeRetryAsync(attempt).ConfigureAwait(false);

            try
            {
                await _transport.ConnectAsync(_lastDeviceId!).ConfigureAwait(false);
                LastError = null;
                SetConnectionState(ConnectionState.Connected);
                _statusPoller.Start();
                return;
            }
            catch
            {
                // try again
            }
        }

        LastError = $"Reconnect failed after {_reconnectPolicy.MaxAttempts} attempts";
        SetConnectionState(ConnectionState.Disconnected);
    }

    private void OnCommandCompleted(GCodeLineCommand command, CommandResult result)
    {
        if (result.Outcome is CommandOutcome.Rejected or CommandOutcome.Aborted)
        {
            CommandRejected?.Invoke(new CommandRejectedEventArgs(command, result.ErrorCode));
        }
    }

    private void OnLineReceived(string rawLine)
    {
        switch (_statusParser.Parse(rawLine))
        {
            case OkLine:
                _commandQueue.HandleOk();
                break;
            case ErrorLine error:
                _commandQueue.HandleError(error.Code);
                break;
            case AlarmLine alarm:
                AlarmTriggered?.Invoke(alarm.Code);
                break;
            case StatusReportLine report:
                DeviceStatus = report.Status;
                if (report.Status.PlannerBlocksAvailable is { } planner && report.Status.RxBytesAvailable is { } rx)
                {
                    _commandQueue.UpdateBufferCapacity(rx, planner);
                }

                _jogScheduler.UpdateCurrentPose(report.Status.WPos);
                DeviceStatusChanged?.Invoke();
                break;
            case UnrecognizedLine:
                break;
        }
    }
}
