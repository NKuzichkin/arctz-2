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

    private static KeyPoint Point(
        int number,
        MachinePose pose,
        double feedRateUnitsPerMin = 500,
        double dwellSeconds = 0,
        EaseMode ease = EaseMode.None,
        bool continuousBlend = false) =>
        new(Guid.NewGuid(), number, Label: null, pose, dwellSeconds, feedRateUnitsPerMin, ease, continuousBlend);

    private static JibProgram SingleSegmentProgram(KeyPoint to)
    {
        var program = new JibProgram();
        program.KeyPoints.Add(Point(1, MachinePose.Zero));
        program.KeyPoints.Add(to);
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
        var to = Point(2, new MachinePose(60, 0, 0, 0), feedRateUnitsPerMin: 1000);
        var program = SingleSegmentProgram(to);

        var steps = _compiler.Compile(program);

        var motionSteps = steps.Where(s => ((GCodeLineCommand)s.Command).Line.StartsWith("G1", StringComparison.Ordinal)).ToList();
        Assert.Single(motionSteps);
        Assert.Equal("G1 X60 Y0 Z0 A0 F1000", ((GCodeLineCommand)motionSteps[0].Command).Line);
        Assert.Equal(1.0, motionSteps[0].SegmentProgress);
    }

    [Fact]
    public void Compile_NoEase_EstimatesDurationFromDistanceAndFeed()
    {
        var to = Point(2, new MachinePose(60, 0, 0, 0), feedRateUnitsPerMin: 1000);
        var program = SingleSegmentProgram(to);

        var steps = _compiler.Compile(program);
        var motionSteps = steps.Where(s => ((GCodeLineCommand)s.Command).Line.StartsWith("G1", StringComparison.Ordinal)).ToList();

        // distance 60 (only X moves) / feed 1000 units-per-min * 60 seconds-per-min = 3.6s.
        Assert.Equal(3.6, motionSteps[0].EstimatedDurationSeconds, 3);
    }

    [Fact]
    public void Compile_ZeroFeed_EstimatesZeroDuration_NoDivideByZero()
    {
        var to = Point(2, new MachinePose(60, 0, 0, 0), feedRateUnitsPerMin: 0);
        var program = SingleSegmentProgram(to);

        var steps = _compiler.Compile(program);
        var motionSteps = steps.Where(s => ((GCodeLineCommand)s.Command).Line.StartsWith("G1", StringComparison.Ordinal)).ToList();

        Assert.Equal(0.0, motionSteps[0].EstimatedDurationSeconds);
    }

    [Fact]
    public void Compile_EaseInOut_EstimatesPerSubstepDurationFromRampedFeed()
    {
        var to = Point(2, new MachinePose(60, 0, 0, 0), feedRateUnitsPerMin: 1000, ease: EaseMode.EaseInOut);
        var program = SingleSegmentProgram(to);

        var steps = _compiler.Compile(program);
        var motionSteps = steps.Where(s => ((GCodeLineCommand)s.Command).Line.StartsWith("G1", StringComparison.Ordinal)).ToList();

        // Each substep covers an equal 10-unit slice (60 / 6 subdivisions); feed ramps
        // 650 -> 1000 -> 1000 -> 1000 -> 650 -> 300 (asserted by feed in the existing test).
        // duration_i = 10 / feed_i * 60.
        var roundedDurations = motionSteps.Select(s => Math.Round(s.EstimatedDurationSeconds, 3)).ToArray();
        Assert.Equal(new[] { 0.923, 0.6, 0.6, 0.6, 0.923, 2.0 }, roundedDurations);
    }

    [Fact]
    public void Compile_DwellPositive_EstimatesExactDwellDuration()
    {
        var to = Point(2, new MachinePose(60, 0, 0, 0), feedRateUnitsPerMin: 1000, dwellSeconds: 2.5, continuousBlend: true);
        var program = SingleSegmentProgram(to);

        var steps = _compiler.Compile(program);
        var dwellStep = steps[1];

        Assert.Equal(2.5, dwellStep.EstimatedDurationSeconds);
    }

    [Fact]
    public void Compile_EaseInOut_ProducesSixSubstepsWithRampedFeedAndIncreasingProgress()
    {
        var to = Point(2, new MachinePose(60, 0, 0, 0), feedRateUnitsPerMin: 1000, ease: EaseMode.EaseInOut);
        var program = SingleSegmentProgram(to);

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
        var to = Point(2, new MachinePose(60, 0, 0, 0), feedRateUnitsPerMin: 1000, dwellSeconds: 2.5, continuousBlend: true);
        var program = SingleSegmentProgram(to);

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
        var to = Point(2, new MachinePose(60, 0, 0, 0), feedRateUnitsPerMin: 1000, continuousBlend: true);
        var program = SingleSegmentProgram(to);

        var steps = _compiler.Compile(program);

        Assert.Single(steps);
        Assert.DoesNotContain(steps, s => ((GCodeLineCommand)s.Command).Line.StartsWith("G4", StringComparison.Ordinal));
    }

    [Fact]
    public void Compile_MultipleSegments_AssignsCorrectSegmentIndexToEachStep()
    {
        var program = new JibProgram();
        program.KeyPoints.Add(Point(1, MachinePose.Zero));
        program.KeyPoints.Add(Point(2, new MachinePose(10, 0, 0, 0)));
        program.KeyPoints.Add(Point(3, new MachinePose(20, 0, 0, 0)));

        var steps = _compiler.Compile(program);

        Assert.Equal(4, steps.Count); // 2 segments x (1 G1 + 1 G4, since ContinuousBlend=false)
        Assert.All(steps.Take(2), s => Assert.Equal(0, s.SegmentIndex));
        Assert.All(steps.Skip(2), s => Assert.Equal(1, s.SegmentIndex));
    }
}
