using System;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class JogDirectionChangeDetectorTests
{
    private readonly JogDirectionChangeDetector _detector = new();

    private static DualJoystickState LeftStick(double angleDegrees, double force)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new DualJoystickState(
            new JoystickAxisInput(force * Math.Cos(radians), force * Math.Sin(radians), force),
            default);
    }

    private static DualJoystickState RightStick(double angleDegrees, double force)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new DualJoystickState(
            default,
            new JoystickAxisInput(force * Math.Cos(radians), force * Math.Sin(radians), force));
    }

    [Fact]
    public void IsSharpChange_WithUnchangedStick_ReturnsFalse()
    {
        var state = LeftStick(0, 1.0);

        Assert.False(_detector.IsSharpChange(state, state));
    }

    [Fact]
    public void IsSharpChange_WithReversedDirection_ReturnsTrue()
    {
        Assert.True(_detector.IsSharpChange(LeftStick(0, 1.0), LeftStick(180, 1.0)));
    }

    [Fact]
    public void IsSharpChange_JustInsideTheAngleThreshold_ReturnsFalse()
    {
        Assert.False(_detector.IsSharpChange(LeftStick(0, 1.0), LeftStick(25, 1.0)));
    }

    [Fact]
    public void IsSharpChange_JustPastTheAngleThreshold_ReturnsTrue()
    {
        Assert.True(_detector.IsSharpChange(LeftStick(0, 1.0), LeftStick(35, 1.0)));
    }

    /// <summary>Left and right sticks drive different machine axes, so handing the motion from one
    /// to the other is a right-angle turn in the 4-axis direction vector.</summary>
    [Fact]
    public void IsSharpChange_MovingFromOneStickToTheOther_ReturnsTrue()
    {
        Assert.True(_detector.IsSharpChange(LeftStick(0, 1.0), RightStick(0, 1.0)));
    }

    [Fact]
    public void IsSharpChange_JustInsideTheForceThreshold_ReturnsFalse()
    {
        Assert.False(_detector.IsSharpChange(LeftStick(0, 0.8), LeftStick(0, 0.88)));
    }

    [Fact]
    public void IsSharpChange_WithForceDroppedByAQuarter_ReturnsTrue()
    {
        Assert.True(_detector.IsSharpChange(LeftStick(0, 0.8), LeftStick(0, 0.6)));
    }

    [Fact]
    public void IsSharpChange_WithForceRaisedByAQuarter_ReturnsTrue()
    {
        Assert.True(_detector.IsSharpChange(LeftStick(0, 0.6), LeftStick(0, 0.8)));
    }

    /// <summary>Dragging the stick back to dead centre without lifting the finger never reaches
    /// Stop(), so the drop to zero force has to register as a sharp change on its own.</summary>
    [Fact]
    public void IsSharpChange_WithStickReturnedToCentre_ReturnsTrue()
    {
        Assert.True(_detector.IsSharpChange(LeftStick(0, 1.0), new DualJoystickState(default, default)));
    }

    [Fact]
    public void IsSharpChange_StartingFromCentre_ReturnsTrue()
    {
        Assert.True(_detector.IsSharpChange(new DualJoystickState(default, default), LeftStick(0, 1.0)));
    }

    [Fact]
    public void IsSharpChange_WithBothStatesCentred_ReturnsFalse()
    {
        var centre = new DualJoystickState(default, default);

        Assert.False(_detector.IsSharpChange(centre, centre));
    }

    [Fact]
    public void IsSharpChange_HonoursCustomThresholds()
    {
        var lenient = new JogDirectionChangeDetector(maxAngleDegrees: 90.0, forceChangeFraction: 0.5);

        Assert.False(lenient.IsSharpChange(LeftStick(0, 1.0), LeftStick(60, 1.0)));
        Assert.True(lenient.IsSharpChange(LeftStick(0, 1.0), LeftStick(120, 1.0)));
    }
}
