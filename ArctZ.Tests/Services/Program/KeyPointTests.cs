using System;
using ArctZ.Services.Device;
using ArctZ.Services.Program;

namespace ArctZ.Tests.Services.Program;

public class KeyPointTests
{
    private static KeyPoint Point(double dwellSeconds, bool continuousBlend) =>
        new(Guid.NewGuid(), Number: 1, Label: null, MachinePose.Zero, dwellSeconds, TransitionSeconds: 5, EaseMode.None, continuousBlend);

    [Fact]
    public void StopsAtWaypoint_NotContinuousBlend_IsTrue()
    {
        var point = Point(dwellSeconds: 0, continuousBlend: false);

        Assert.True(point.StopsAtWaypoint);
    }

    [Fact]
    public void StopsAtWaypoint_ContinuousBlendButPositiveDwell_IsTrue()
    {
        var point = Point(dwellSeconds: 2, continuousBlend: true);

        Assert.True(point.StopsAtWaypoint);
    }

    [Fact]
    public void StopsAtWaypoint_ContinuousBlendAndNoDwell_IsFalse()
    {
        var point = Point(dwellSeconds: 0, continuousBlend: true);

        Assert.False(point.StopsAtWaypoint);
    }
}
