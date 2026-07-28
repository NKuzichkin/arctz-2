using System;
using System.Linq;
using ArctZ.Services.Device;
using ArctZ.Services.Program;

namespace ArctZ.Tests.Services.Program;

public class JibProgramTests
{
    private static KeyPoint Point(int number, string label, MachinePose pose) =>
        new(Guid.NewGuid(), number, label, pose, DwellSeconds: 0, FeedRateUnitsPerMin: 500, EaseMode.None, ContinuousBlend: false);

    [Fact]
    public void Segments_ZipsConsecutiveKeyPointsInOrder()
    {
        var program = new JibProgram();
        var a = Point(1, "A", new MachinePose(0, 0, 0, 0));
        var b = Point(2, "B", new MachinePose(10, 0, 0, 0));
        var c = Point(3, "C", new MachinePose(20, 0, 0, 0));
        program.KeyPoints.AddRange(new[] { a, b, c });

        var segments = program.Segments().ToList();

        Assert.Equal(2, segments.Count);
        Assert.Equal((0, a, b), (segments[0].Index, segments[0].From, segments[0].To));
        Assert.Equal((1, b, c), (segments[1].Index, segments[1].From, segments[1].To));
    }

    [Fact]
    public void Segments_FewerThanTwoKeyPoints_IsEmpty()
    {
        var program = new JibProgram();
        program.KeyPoints.Add(Point(1, "A", MachinePose.Zero));

        Assert.Empty(program.Segments());
    }
}
