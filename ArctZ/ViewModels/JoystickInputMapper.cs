using System;
using ArctZ.Components.VirtualJoystick;
using ArctZ.Services.Device;

namespace ArctZ.ViewModels;

/// <summary>
/// Converts VirtualJoystick's Force/AngleDeg into the normalized -1..1
/// X/Y axis pair Services.Device expects. Sign of Y assumes "stick
/// pushed up" should read as positive despite screen Y growing downward
/// — not yet visually verified against the real control (see Task 23).
/// </summary>
public static class JoystickInputMapper
{
    public static JoystickAxisInput ToAxisInput(JoystickEventArgs e)
    {
        var radians = e.AngleDeg * Math.PI / 180.0;
        return new JoystickAxisInput(
            X: e.Force * Math.Cos(radians),
            Y: -e.Force * Math.Sin(radians),
            Force: e.Force);
    }
}
