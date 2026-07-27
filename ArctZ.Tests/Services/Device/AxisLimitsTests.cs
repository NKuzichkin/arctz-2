using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class AxisLimitsTests
{
    [Fact]
    public void Clamp_ValueWithinBounds_ReturnsUnchanged()
    {
        var limits = new AxisLimits(-15, 65, WrapsAt360: false);

        Assert.Equal(30, limits.Clamp(30));
    }

    [Fact]
    public void Clamp_ValueAboveMax_ReturnsMax()
    {
        var limits = new AxisLimits(-15, 65, WrapsAt360: false);

        Assert.Equal(65, limits.Clamp(90));
    }

    [Fact]
    public void Clamp_ValueBelowMin_ReturnsMin()
    {
        var limits = new AxisLimits(-15, 65, WrapsAt360: false);

        Assert.Equal(-15, limits.Clamp(-40));
    }

    [Fact]
    public void Clamp_NoBounds_ReturnsUnchanged()
    {
        var limits = new AxisLimits(null, null, WrapsAt360: false);

        Assert.Equal(12345, limits.Clamp(12345));
    }

    [Fact]
    public void Clamp_WrappingAxis_NormalizesIntoZeroTo360()
    {
        var limits = new AxisLimits(0, 360, WrapsAt360: true);

        Assert.Equal(10, limits.Clamp(370));
        Assert.Equal(350, limits.Clamp(-10));
    }

    [Fact]
    public void ClampDelta_WrappingAxis_PassesThroughUnchanged()
    {
        var limits = new AxisLimits(0, 360, WrapsAt360: true);

        Assert.Equal(5, limits.ClampDelta(currentValue: 359, delta: 5));
    }

    [Fact]
    public void ClampDelta_UnboundedAxis_PassesThroughUnchanged()
    {
        var limits = new AxisLimits(null, null, WrapsAt360: false);

        Assert.Equal(500, limits.ClampDelta(currentValue: 1_000_000, delta: 500));
    }

    [Fact]
    public void ClampDelta_BoundedAxis_WithinRange_PassesThroughUnchanged()
    {
        var limits = new AxisLimits(-15, 65, WrapsAt360: false);

        Assert.Equal(5, limits.ClampDelta(currentValue: 30, delta: 5));
    }

    [Fact]
    public void ClampDelta_BoundedAxis_WouldExceedMax_TruncatesToRemainingRoom()
    {
        var limits = new AxisLimits(-15, 65, WrapsAt360: false);

        Assert.Equal(2, limits.ClampDelta(currentValue: 63, delta: 5));
    }

    [Fact]
    public void ClampDelta_BoundedAxis_AlreadyAtMax_ReturnsZero()
    {
        var limits = new AxisLimits(-15, 65, WrapsAt360: false);

        Assert.Equal(0, limits.ClampDelta(currentValue: 65, delta: 5));
    }
}
