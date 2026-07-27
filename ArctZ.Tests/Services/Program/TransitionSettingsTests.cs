using ArctZ.Services.Program;

namespace ArctZ.Tests.Services.Program;

public class TransitionSettingsTests
{
    [Fact]
    public void StopsAtWaypoint_NotContinuousBlend_IsTrue()
    {
        var transition = new TransitionSettings(FeedRateUnitsPerMin: 500, DwellSeconds: 0, EaseMode.None, ContinuousBlend: false);

        Assert.True(transition.StopsAtWaypoint);
    }

    [Fact]
    public void StopsAtWaypoint_ContinuousBlendButPositiveDwell_IsTrue()
    {
        var transition = new TransitionSettings(FeedRateUnitsPerMin: 500, DwellSeconds: 2, EaseMode.None, ContinuousBlend: true);

        Assert.True(transition.StopsAtWaypoint);
    }

    [Fact]
    public void StopsAtWaypoint_ContinuousBlendAndNoDwell_IsFalse()
    {
        var transition = new TransitionSettings(FeedRateUnitsPerMin: 500, DwellSeconds: 0, EaseMode.None, ContinuousBlend: true);

        Assert.False(transition.StopsAtWaypoint);
    }
}
