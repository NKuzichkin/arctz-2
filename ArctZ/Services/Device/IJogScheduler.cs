namespace ArctZ.Services.Device;

public interface IJogScheduler
{
    bool IsActive { get; }

    void Start();

    void UpdateState(DualJoystickState state);

    void UpdateStatus(DeviceStatus status);

    /// <summary>Attributes one ok/error line to an outstanding jog. Returns false when no jog is
    /// awaiting acknowledgement, meaning the line belongs to the queued-command stream instead.</summary>
    bool TryHandleAck();

    void Stop();
}
