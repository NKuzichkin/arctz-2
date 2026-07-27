using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public interface IJogCommandFactory
{
    JogCommand Create(DualJoystickState state, MachinePose currentPose);
}
