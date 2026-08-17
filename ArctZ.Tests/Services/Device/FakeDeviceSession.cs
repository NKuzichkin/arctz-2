using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Tests.Services.Device;

/// <summary>Синхронный дублёр IDeviceSession: тесты читают UpdateJog-вызовы
/// напрямую, без реального JogScheduler/таймера/транспорта.</summary>
public sealed class FakeDeviceSession : ArctZ.Services.Device.IDeviceSession
{
    public ArctZ.Services.Device.ConnectionState ConnectionState => ArctZ.Services.Device.ConnectionState.Connected;

    public ArctZ.Services.Device.DeviceStatus? DeviceStatus => null;

    public string? LastError => null;

    public event Action? ConnectionStateChanged { add { } remove { } }

    public event Action? DeviceStatusChanged { add { } remove { } }

    public event Action<ArctZ.Services.Device.CommandRejectedEventArgs>? CommandRejected { add { } remove { } }

    public event Action<int>? AlarmTriggered { add { } remove { } }

    public ArctZ.Services.Device.DualJoystickState? LastJogState { get; private set; }

    public int UpdateJogCallCount { get; private set; }

    public int StopAndDrainCallCount { get; private set; }

    public int DisconnectCallCount { get; private set; }

    /// <summary>Задать, чтобы StopAndDrainAsync повис до разрешения — так тест успевает
    /// проверить состояние приложения, пока остановка ещё идёт.</summary>
    public TaskCompletionSource<bool>? StopAndDrainGate { get; set; }

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DisconnectAsync()
    {
        DisconnectCallCount++;
        return Task.CompletedTask;
    }

    public void BeginJog()
    {
    }

    public void UpdateJog(ArctZ.Services.Device.DualJoystickState state)
    {
        LastJogState = state;
        UpdateJogCallCount++;
    }

    public void EndJog() => LastJogState = null;

    private static readonly ArctZ.Services.Device.CommandResult Acknowledged =
        new(ArctZ.Services.Device.CommandOutcome.Acknowledged, null);

    public Task<ArctZ.Services.Device.CommandResult> SendGCodeAsync(string line, CancellationToken cancellationToken = default) =>
        Task.FromResult(Acknowledged);

    public void AbortPendingCommands()
    {
    }

    public Task<ArctZ.Services.Device.CommandResult> HomeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Acknowledged);

    public Task<ArctZ.Services.Device.CommandResult> ResetAlarmAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Acknowledged);

    public Task FeedHoldAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task<bool> StopAndDrainAsync(TimeSpan timeout)
    {
        StopAndDrainCallCount++;
        if (StopAndDrainGate is { } gate)
        {
            await gate.Task;
        }

        return true;
    }

    public Task ResumeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
