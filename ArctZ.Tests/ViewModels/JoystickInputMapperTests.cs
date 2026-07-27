using System;
using ArctZ.Components.VirtualJoystick;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class JoystickInputMapperTests
{
    [Fact]
    public void ToAxisInput_ZeroDegrees_ProducesPositiveXZeroY()
    {
        var result = JoystickInputMapper.ToAxisInput(new JoystickEventArgs { Force = 1.0, AngleDeg = 0 });

        Assert.Equal(1.0, result.X, 3);
        Assert.Equal(0.0, result.Y, 3);
        Assert.Equal(1.0, result.Force);
    }

    [Fact]
    public void ToAxisInput_NinetyDegrees_ProducesNegativeY()
    {
        var result = JoystickInputMapper.ToAxisInput(new JoystickEventArgs { Force = 1.0, AngleDeg = 90 });

        Assert.Equal(0.0, result.X, 3);
        Assert.Equal(-1.0, result.Y, 3);
    }

    [Fact]
    public void ToAxisInput_ZeroForce_ProducesZeroXAndY()
    {
        var result = JoystickInputMapper.ToAxisInput(new JoystickEventArgs { Force = 0, AngleDeg = 45 });

        Assert.Equal(0.0, result.X, 3);
        Assert.Equal(0.0, result.Y, 3);
    }
}
