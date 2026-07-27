namespace ArctZ.Services.Device;

public enum MachineState
{
    Idle,
    Run,
    Jog,
    Hold,
    Home,
    Alarm,
    Unknown
}

public readonly record struct DeviceStatus(
    MachineState State,
    MachinePose WPos,
    int? PlannerBlocksAvailable,
    int? RxBytesAvailable);
