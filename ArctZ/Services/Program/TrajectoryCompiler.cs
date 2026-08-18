using System.Collections.Generic;
using System.Globalization;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Program;

public sealed class TrajectoryCompiler : ITrajectoryCompiler
{
    private const int EaseSubdivisions = 6;
    private const double MinFeedFraction = 0.3;

    public IReadOnlyList<CompiledStep> Compile(JibProgram program)
    {
        var steps = new List<CompiledStep>();

        foreach (var segment in program.Segments())
        {
            if (segment.To.Ease == EaseMode.EaseInOut)
            {
                CompileEased(segment, steps);
            }
            else
            {
                var seconds = InverseTimeMove.EffectiveSeconds(segment.To.TransitionSeconds);
                var command = new GCodeLineCommand(InverseTimeMove.Line(segment.To.Pose, seconds));
                steps.Add(new CompiledStep(segment.Index, command, SegmentProgress: 1.0, EstimatedDurationSeconds: seconds));
            }

            if (segment.To.StopsAtWaypoint)
            {
                var dwellLine = $"G4 P{Format(segment.To.DwellSeconds)}";
                steps.Add(new CompiledStep(segment.Index, new GCodeLineCommand(dwellLine), SegmentProgress: 1.0, EstimatedDurationSeconds: segment.To.DwellSeconds));
            }
        }

        return steps;
    }

    /// <summary>
    /// Подшаги равны по расстоянию, поэтому профиль скорости превращается в
    /// профиль времени: время i-го подшага обратно пропорционально множителю
    /// скорости. Нормировка по сумме весов даёт точное совпадение суммарной
    /// длительности сегмента с заданной — при G94 этого не было.
    /// </summary>
    private static void CompileEased(ProgramSegment segment, List<CompiledStep> steps)
    {
        var total = InverseTimeMove.EffectiveSeconds(segment.To.TransitionSeconds);

        var weights = new double[EaseSubdivisions];
        var weightSum = 0.0;
        for (var i = 1; i <= EaseSubdivisions; i++)
        {
            var weight = 1.0 / FeedMultiplier((double)i / EaseSubdivisions);
            weights[i - 1] = weight;
            weightSum += weight;
        }

        for (var i = 1; i <= EaseSubdivisions; i++)
        {
            var t = (double)i / EaseSubdivisions;
            var pose = Interpolate(segment.From.Pose, segment.To.Pose, t);
            var seconds = total * weights[i - 1] / weightSum;
            steps.Add(new CompiledStep(
                segment.Index,
                new GCodeLineCommand(InverseTimeMove.Line(pose, seconds)),
                SegmentProgress: t,
                EstimatedDurationSeconds: seconds));
        }
    }

    /// <summary>Piecewise-linear ramp: 0.3x -> 1.0x over the first third, cruise at 1.0x, 1.0x -> 0.3x over the last third.</summary>
    private static double FeedMultiplier(double t)
    {
        if (t <= 1.0 / 3)
        {
            return MinFeedFraction + (1 - MinFeedFraction) * (t / (1.0 / 3));
        }

        if (t <= 2.0 / 3)
        {
            return 1.0;
        }

        var local = (t - 2.0 / 3) / (1.0 / 3);
        return 1.0 - (1 - MinFeedFraction) * local;
    }

    private static MachinePose Interpolate(MachinePose from, MachinePose to, double t) => new(
        X: from.X + (to.X - from.X) * t,
        Y: from.Y + (to.Y - from.Y) * t,
        Z: from.Z + (to.Z - from.Z) * t,
        A: from.A + (to.A - from.A) * t);

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
