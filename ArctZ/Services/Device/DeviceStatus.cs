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

/// <param name="SubState">Число после двоеточия в поле состояния, если оно было. Для Hold по
/// спецификации grbl 1.1: 0 — торможение завершено, 1 — ещё идёт (и сброс в этот момент
/// выбросит аварию с потерей позиции).</param>
public readonly record struct DeviceStatus(
    MachineState State,
    MachinePose WPos,
    int? PlannerBlocksAvailable,
    int? RxBytesAvailable,
    int? SubState = null);
