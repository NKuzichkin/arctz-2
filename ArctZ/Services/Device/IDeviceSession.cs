using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

public interface IDeviceSession
{
    ConnectionState ConnectionState { get; }

    DeviceStatus? DeviceStatus { get; }

    string? LastError { get; }

    event Action? ConnectionStateChanged;

    event Action? DeviceStatusChanged;

    event Action<CommandRejectedEventArgs>? CommandRejected;

    event Action<int>? AlarmTriggered;

    Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default);

    Task DisconnectAsync();

    void BeginJog();

    void UpdateJog(DualJoystickState state);

    void EndJog();

    Task<CommandResult> SendGCodeAsync(string line, CancellationToken cancellationToken = default);

    Task<CommandResult> HomeAsync(CancellationToken cancellationToken = default);

    Task<CommandResult> ResetAlarmAsync(CancellationToken cancellationToken = default);

    Task FeedHoldAsync(CancellationToken cancellationToken = default);

    Task ResumeAsync(CancellationToken cancellationToken = default);
}
