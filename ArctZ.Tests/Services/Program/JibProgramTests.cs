using System;
using System.Linq;
using ArctZ.Services.Device;
using ArctZ.Services.Program;

namespace ArctZ.Tests.Services.Program;

public class JibProgramTests
{
    private static KeyPoint Point(int number, string label, MachinePose pose) =>
        new(Guid.NewGuid(), number, label, pose, DwellSeconds: 0, TransitionSeconds: 5, EaseMode.None, ContinuousBlend: false);

    [Fact]
    public void Segments_ZipsConsecutiveKeyPointsInOrder_WithASelfSegmentForTheFirstPoint()
    {
        var program = new JibProgram();
        var a = Point(1, "A", new MachinePose(0, 0, 0, 0));
        var b = Point(2, "B", new MachinePose(10, 0, 0, 0));
        var c = Point(3, "C", new MachinePose(20, 0, 0, 0));
        program.KeyPoints.AddRange(new[] { a, b, c });

        var segments = program.Segments().ToList();

        Assert.Equal(3, segments.Count);
        Assert.Equal((0, a, a), (segments[0].Index, segments[0].From, segments[0].To));
        Assert.Equal((1, a, b), (segments[1].Index, segments[1].From, segments[1].To));
        Assert.Equal((2, b, c), (segments[2].Index, segments[2].From, segments[2].To));
    }

    [Fact]
    public void Segments_SingleKeyPoint_YieldsOneSelfSegment()
    {
        var program = new JibProgram();
        var a = Point(1, "A", MachinePose.Zero);
        program.KeyPoints.Add(a);

        var segments = program.Segments().ToList();

        Assert.Single(segments);
        Assert.Equal((0, a, a), (segments[0].Index, segments[0].From, segments[0].To));
    }

    [Fact]
    public void Segments_NoKeyPoints_IsEmpty()
    {
        var program = new JibProgram();

        Assert.Empty(program.Segments());
    }

    [Fact]
    public void NewProgram_DefaultsToStopModeNoReturnNoRepeatLimit()
    {
        var program = new JibProgram();

        Assert.Equal(ProgramCompletionMode.Stop, program.CompletionMode);
        Assert.False(program.ReturnToStartOnFinish);
        Assert.Null(program.RepeatCount);
    }
}
