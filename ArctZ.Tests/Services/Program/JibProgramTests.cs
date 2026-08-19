using System;
using System.Collections.Generic;
using System.Linq;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using Xunit;

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

    private static KeyPoint SimplePoint(int number) =>
        new(Guid.NewGuid(), number, $"P{number}", MachinePose.Zero, DwellSeconds: 0, TransitionSeconds: 1, EaseMode.None, ContinuousBlend: true);

    [Fact]
    public void TargetKeyPoint_Forward_SegmentIndexIndexesDirectlyIntoTheList()
    {
        var points = new List<KeyPoint> { SimplePoint(1), SimplePoint(2), SimplePoint(3) };

        Assert.Equal(points[0].Id, JibProgram.TargetKeyPoint(points, segmentIndex: 0, backward: false));
        Assert.Equal(points[2].Id, JibProgram.TargetKeyPoint(points, segmentIndex: 2, backward: false));
    }

    [Fact]
    public void TargetKeyPoint_Backward_SegmentIndexCountsFromTheEndOfTheList()
    {
        var points = new List<KeyPoint> { SimplePoint(1), SimplePoint(2), SimplePoint(3) };

        Assert.Equal(points[2].Id, JibProgram.TargetKeyPoint(points, segmentIndex: 0, backward: true));
        Assert.Equal(points[0].Id, JibProgram.TargetKeyPoint(points, segmentIndex: 2, backward: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(3)]
    public void TargetKeyPoint_NullOrOutOfRangeIndex_ReturnsNull(int? segmentIndex)
    {
        var points = new List<KeyPoint> { SimplePoint(1), SimplePoint(2), SimplePoint(3) };

        Assert.Null(JibProgram.TargetKeyPoint(points, segmentIndex, backward: false));
    }
}
