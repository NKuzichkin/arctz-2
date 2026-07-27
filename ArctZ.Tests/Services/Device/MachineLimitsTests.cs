using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class MachineLimitsTests
{
    [Fact]
    public void Default_MatchesDocumentedAxisRanges()
    {
        var limits = MachineLimits.Default;

        Assert.Equal(65, limits.X.Clamp(90));
        Assert.Equal(-15, limits.X.Clamp(-90));
        Assert.Equal(999, limits.Y.Clamp(999));
        Assert.Equal(10, limits.Z.Clamp(370));
        Assert.Equal(10, limits.A.Clamp(370));
    }

    [Fact]
    public void Clamp_AppliesPerAxis()
    {
        var limits = MachineLimits.Default;
        var pose = new MachinePose(X: 90, Y: 999, Z: 370, A: -10);

        var clamped = limits.Clamp(pose);

        Assert.Equal(new MachinePose(65, 999, 10, 350), clamped);
    }

    [Fact]
    public void ClampDelta_AppliesPerAxis()
    {
        var limits = MachineLimits.Default;
        var current = new MachinePose(X: 63, Y: 0, Z: 359, A: 0);
        var delta = new MachinePose(X: 5, Y: 5, Z: 5, A: 5);

        var clampedDelta = limits.ClampDelta(current, delta);

        Assert.Equal(new MachinePose(2, 5, 5, 5), clampedDelta);
    }
}
