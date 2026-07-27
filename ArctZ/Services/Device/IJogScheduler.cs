namespace ArctZ.Services.Device;

public interface IJogScheduler
{
    bool IsActive { get; }

    void Start();

    void UpdateState(DualJoystickState state);

    void UpdateCurrentPose(MachinePose pose);

    void Stop();
}
