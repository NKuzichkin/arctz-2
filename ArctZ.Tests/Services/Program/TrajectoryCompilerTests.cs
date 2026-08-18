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
        double transitionSeconds = 5,
        double dwellSeconds = 0,
        EaseMode ease = EaseMode.None,
        bool continuousBlend = false) =>
        new(Guid.NewGuid(), number, Label: null, pose, dwellSeconds, transitionSeconds, ease, continuousBlend);

    private static JibProgram SingleSegmentProgram(KeyPoint to)
    {
        var program = new JibProgram();
        program.KeyPoints.Add(Point(1, MachinePose.Zero));
        program.KeyPoints.Add(to);
        return program;
    }

    private static string[] MotionLines(System.Collections.Generic.IReadOnlyList<CompiledStep> steps) =>
        steps.Select(s => ((GCodeLineCommand)s.Command).Line)
             .Where(l => l.StartsWith("G93", StringComparison.Ordinal))
             .ToArray();

    private static CompiledStep[] MotionSteps(System.Collections.Generic.IReadOnlyList<CompiledStep> steps) =>
        steps.Where(s => ((GCodeLineCommand)s.Command).Line.StartsWith("G93", StringComparison.Ordinal)).ToArray();

    private static double ParseFeed(string line)
    {
        var token = line.Split(' ').Single(t => t.StartsWith("F", StringComparison.Ordinal));
        return double.Parse(token[1..], CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Compile_NoEase_ProducesSingleG93StepAtFullProgress()
    {
        var to = Point(2, new MachinePose(60, 0, 0, 0), transitionSeconds: 15);
        var program = SingleSegmentProgram(to);

        var steps = _compiler.Compile(program);

        var motionSteps = MotionSteps(steps);
        Assert.Single(motionSteps);
        Assert.Equal("G93 G1 X60 Y0 Z0 A0 F4", ((GCodeLineCommand)motionSteps[0].Command).Line);
        Assert.Equal(1.0, motionSteps[0].SegmentProgress);
    }

    [Fact]
    public void Compile_NoEase_ReportsTheCommandedTimeAsTheEstimatedDuration()
    {
        var to = Point(2, new MachinePose(60, 0, 0, 0), transitionSeconds: 15);
        var program = SingleSegmentProgram(to);

        var motionSteps = MotionSteps(_compiler.Compile(program));

        Assert.Equal(15.0, motionSteps[0].EstimatedDurationSeconds);
    }

    /// <summary>Ноль приходит из старых файлов программ; бросок на максимальной скорости недопустим.</summary>
    [Fact]
    public void Compile_NonPositiveTransitionSeconds_FallsBackToTheDefault()
    {
        var to = Point(2, new MachinePose(60, 0, 0, 0), transitionSeconds: 0);
        var program = SingleSegmentProgram(to);

        var motionSteps = MotionSteps(_compiler.Compile(program));

        Assert.Equal("G93 G1 X60 Y0 Z0 A0 F12", ((GCodeLineCommand)motionSteps[0].Command).Line);
        Assert.Equal(5.0, motionSteps[0].EstimatedDurationSeconds);
    }

    /// <summary>
    /// Профиль скорости прежний (0.3x -> 1.0x -> 0.3x), но распределяется время:
    /// подшаги равны по расстоянию, поэтому время i-го обратно пропорционально
    /// множителю скорости, а сумма нормируется к заданной длительности.
    /// </summary>
    [Fact]
    public void Compile_EaseInOut_SplitsTheTransitionTimeAcrossSubstepsByTheSpeedProfile()
    {
        var to = Point(2, new MachinePose(60, 0, 0, 0), transitionSeconds: 12, ease: EaseMode.EaseInOut);
        var program = SingleSegmentProgram(to);

        var motionSteps = MotionSteps(_compiler.Compile(program));

        var rounded = motionSteps.Select(s => Math.Round(s.EstimatedDurationSeconds, 3)).ToArray();
        Assert.Equal(new[] { 1.962, 1.275, 1.275, 1.275, 1.962, 4.251 }, rounded);
    }

    /// <summary>Новая гарантия, которой не было при G94: ease не растягивает сегмент.</summary>
    [Fact]
    public void Compile_EaseInOut_SubstepDurationsSumToExactlyTheTransitionTime()
    {
        var to = Point(2, new MachinePose(60, 0, 0, 0), transitionSeconds: 12, ease: EaseMode.EaseInOut);
        var program = SingleSegmentProgram(to);

        var motionSteps = MotionSteps(_compiler.Compile(program));

        Assert.Equal(12.0, motionSteps.Sum(s => s.EstimatedDurationSeconds), 9);
    }

    [Fact]
    public void Compile_EaseInOut_EmitsSixSubstepsWhoseFeedMatchesTheirOwnDuration()
    {
        var to = Point(2, new MachinePose(60, 0, 0, 0), transitionSeconds: 12, ease: EaseMode.EaseInOut);
        var program = SingleSegmentProgram(to);

        var motionSteps = MotionSteps(_compiler.Compile(program));

        Assert.Equal(6, motionSteps.Length);
        foreach (var step in motionSteps)
        {
            var feed = ParseFeed(((GCodeLineCommand)step.Command).Line);
            Assert.Equal(60.0 / step.EstimatedDurationSeconds, feed, 4);
        }

        Assert.Equal("G93 G1 X60 Y0 Z0 A0 F14.1153846", ((GCodeLineCommand)motionSteps[5].Command).Line);
    }

    [Fact]
    public void Compile_EaseInOut_KeepsProgressLinearInDistance()
    {
        var to = Point(2, new MachinePose(60, 0, 0, 0), transitionSeconds: 12, ease: EaseMode.EaseInOut);
        var program = SingleSegmentProgram(to);

        var motionSteps = MotionSteps(_compiler.Compile(program));

        var roundedProgress = motionSteps.Select(s => Math.Round(s.SegmentProgress, 3)).ToArray();
        Assert.Equal(new[] { 0.167, 0.333, 0.5, 0.667, 0.833, 1.0 }, roundedProgress);
    }

    [Fact]
    public void Compile_DwellPositive_EstimatesExactDwellDuration()
    {
        var to = Point(2, new MachinePose(60, 0, 0, 0), dwellSeconds: 2.5, continuousBlend: true);
        var program = SingleSegmentProgram(to);

        var steps = _compiler.Compile(program);
        var dwellStep = steps[1];

        Assert.Equal(2.5, dwellStep.EstimatedDurationSeconds);
    }

    [Fact]
    public void Compile_DwellPositive_AppendsG4AfterMotionAtFullProgress()
    {
        var to = Point(2, new MachinePose(60, 0, 0, 0), dwellSeconds: 2.5, continuousBlend: true);
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
        var to = Point(2, new MachinePose(60, 0, 0, 0), continuousBlend: true);
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

        Assert.Equal(4, steps.Count); // 2 segments x (1 move + 1 G4, since ContinuousBlend=false)
        Assert.All(steps.Take(2), s => Assert.Equal(0, s.SegmentIndex));
        Assert.All(steps.Skip(2), s => Assert.Equal(1, s.SegmentIndex));
        Assert.Equal(2, MotionLines(steps).Length);
    }
}
