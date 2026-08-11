using ArctZ.Services.Device;

namespace ArctZ.ViewModels;

/// <summary>
/// Масштабирует вход джойстика единым коэффициентом (0..100 -> 0..1),
/// применяемым к X/Y/Force сразу — так шаг перемещения и feed-rate
/// (оба производные от Force в JogCommandFactory) меняются согласованно.
/// </summary>
public static class JoystickSpeedScaler
{
    public static JoystickAxisInput Scale(JoystickAxisInput input, double speedPercent)
    {
        var factor = speedPercent / 100.0;
        return new JoystickAxisInput(input.X * factor, input.Y * factor, input.Force * factor);
    }
}
