using System;
using System.Globalization;
using System.Linq;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;
using ArctZ.Services.Program;

namespace ArctZ.Tests.Services.Program;

public class TrajectoryCompilerTests
{
    private readonly TrajectoryCompiler _compiler = new();

    private static JibProgram SingleSegmentProgram(TransitionSettings transition)
    {
        var program = new JibProgram();
        program.Waypoints.Add(new Waypoint(Guid.NewGuid(), "A", MachinePose.Zero));
        program.Waypoints.Add(new Waypoint(Guid.NewGuid(), "B", new MachinePose(60, 0, 0, 0)));
        program.Transitions.Add(transition);
        return program;
    }

    private static double ParseFeed(string line)
    {
        var token = line.Split(' ').Single(t => t.StartsWith("F", StringComparison.Ordinal));
        return double.Parse(token[1..], CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Compile_NoEase_ProducesSingleG1StepAtFullProgress()
    {
        var transition = new TransitionSettings(FeedRateUnitsPerMin: 1000, DwellSeconds: 0, EaseMode.None, ContinuousBlend: false);
        var program = SingleSegmentProgram(transition);

        var steps = _compiler.Compile(program);

        var motionSteps = steps.Where(s => ((GCodeLineCommand)s.Command).Line.StartsWith("G1", StringComparison.Ordinal)).ToList();
        Assert.Single(motionSteps);
        Assert.Equal("G1 X60 Y0 Z0 A0 F1000", ((GCodeLineCommand)motionSteps[0].Command).Line);
        Assert.Equal(1.0, motionSteps[0].SegmentProgress);
    }

    [Fact]
    public void Compile_EaseInOut_ProducesSixSubstepsWithRampedFeedAndIncreasingProgress()
    {
        var transition = new TransitionSettings(FeedRateUnitsPerMin: 1000, DwellSeconds: 0, EaseMode.EaseInOut, ContinuousBlend: false);
        var program = SingleSegmentProgram(transition);

        var steps = _compiler.Compile(program);
        var motionSteps = steps.Where(s => ((GCodeLineCommand)s.Command).Line.StartsWith("G1", StringComparison.Ordinal)).ToList();

        Assert.Equal(6, motionSteps.Count);

        var roundedFeeds = motionSteps.Select(s => Math.Round(ParseFeed(((GCodeLineCommand)s.Command).Line))).ToArray();
        Assert.Equal(new[] { 650.0, 1000.0, 1000.0, 1000.0, 650.0, 300.0 }, roundedFeeds);

        var roundedProgress = motionSteps.Select(s => Math.Round(s.SegmentProgress, 3)).ToArray();
        Assert.Equal(new[] { 0.167, 0.333, 0.5, 0.667, 0.833, 1.0 }, roundedProgress);

        Assert.Equal("G1 X60 Y0 Z0 A0 F300", ((GCodeLineCommand)motionSteps[5].Command).Line);
    }

    [Fact]
    public void Compile_DwellPositive_AppendsG4AfterMotionAtFullProgress()
    {
        var transition = new TransitionSettings(FeedRateUnitsPerMin: 1000, DwellSeconds: 2.5, EaseMode.None, ContinuousBlend: true);
        var program = SingleSegmentProgram(transition);

        var steps = _compiler.Compile(program);

        Assert.Equal(2, steps.Count);
        var dwellStep = steps[1];
        Assert.Equal("G4 P2.5", ((GCodeLineCommand)dwellStep.Command).Line);
        Assert.Equal(1.0, dwellStep.SegmentProgress);
        Assert.Equal(0, dwellStep.SegmentIndex);
    }

    [Fact]
    public void Compile_ContinuousBlendNoDwell_DoesNotAppendDwell()
    {
        var transition = new TransitionSettings(FeedRateUnitsPerMin: 1000, DwellSeconds: 0, EaseMode.None, ContinuousBlend: true);
        var program = SingleSegmentProgram(transition);

        var steps = _compiler.Compile(program);

        Assert.Single(steps);
        Assert.DoesNotContain(steps, s => ((GCodeLineCommand)s.Command).Line.StartsWith("G4", StringComparison.Ordinal));
    }

    [Fact]
    public void Compile_MultipleSegments_AssignsCorrectSegmentIndexToEachStep()
    {
        var program = new JibProgram();
        program.Waypoints.Add(new Waypoint(Guid.NewGuid(), "A", MachinePose.Zero));
        program.Waypoints.Add(new Waypoint(Guid.NewGuid(), "B", new MachinePose(10, 0, 0, 0)));
        program.Waypoints.Add(new Waypoint(Guid.NewGuid(), "C", new MachinePose(20, 0, 0, 0)));
        var transition = new TransitionSettings(FeedRateUnitsPerMin: 500, DwellSeconds: 0, EaseMode.None, ContinuousBlend: false);
        program.Transitions.Add(transition);
        program.Transitions.Add(transition);

        var steps = _compiler.Compile(program);

        Assert.Equal(4, steps.Count); // 2 segments x (1 G1 + 1 G4, since ContinuousBlend=false)
        Assert.All(steps.Take(2), s => Assert.Equal(0, s.SegmentIndex));
        Assert.All(steps.Skip(2), s => Assert.Equal(1, s.SegmentIndex));
    }
}
