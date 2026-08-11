using ArctZ.Services.Device;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class JoystickSpeedScalerTests
{
    [Fact]
    public void Scale_HundredPercent_ReturnsInputUnchanged()
    {
        var input = new JoystickAxisInput(0.8, -0.5, 0.9);

        var result = JoystickSpeedScaler.Scale(input, 100);

        Assert.Equal(0.8, result.X, 3);
        Assert.Equal(-0.5, result.Y, 3);
        Assert.Equal(0.9, result.Force, 3);
    }

    [Fact]
    public void Scale_FiftyPercent_HalvesXYAndForce()
    {
        var input = new JoystickAxisInput(0.8, -0.5, 0.9);

        var result = JoystickSpeedScaler.Scale(input, 50);

        Assert.Equal(0.4, result.X, 3);
        Assert.Equal(-0.25, result.Y, 3);
        Assert.Equal(0.45, result.Force, 3);
    }

    [Fact]
    public void Scale_FivePercent_ScalesToOneTwentieth()
    {
        var input = new JoystickAxisInput(1.0, 1.0, 1.0);

        var result = JoystickSpeedScaler.Scale(input, 5);

        Assert.Equal(0.05, result.X, 3);
        Assert.Equal(0.05, result.Y, 3);
        Assert.Equal(0.05, result.Force, 3);
    }

    [Fact]
    public void Scale_ZeroInput_RemainsZeroRegardlessOfPercent()
    {
        var result = JoystickSpeedScaler.Scale(default, 42);

        Assert.Equal(default, result);
    }
}
