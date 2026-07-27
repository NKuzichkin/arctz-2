namespace ArctZ.Services.Device;

public readonly record struct JoystickAxisInput(double X, double Y, double Force);

/// <summary>Combined snapshot of both physical joysticks driving the 4-axis machine.</summary>
public readonly record struct DualJoystickState(JoystickAxisInput Left, JoystickAxisInput Right);
