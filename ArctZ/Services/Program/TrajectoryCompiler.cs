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
            if (segment.Transition.Ease == EaseMode.EaseInOut)
            {
                CompileEased(segment, steps);
            }
            else
            {
                var command = MoveCommand(segment.To.Pose, segment.Transition.FeedRateUnitsPerMin);
                steps.Add(new CompiledStep(segment.Index, command, SegmentProgress: 1.0));
            }

            if (segment.Transition.StopsAtWaypoint)
            {
                var dwellLine = $"G4 P{Format(segment.Transition.DwellSeconds)}";
                steps.Add(new CompiledStep(segment.Index, new GCodeLineCommand(dwellLine), SegmentProgress: 1.0));
            }
        }

        return steps;
    }

    private static void CompileEased(ProgramSegment segment, List<CompiledStep> steps)
    {
        for (var i = 1; i <= EaseSubdivisions; i++)
        {
            var t = (double)i / EaseSubdivisions;
            var pose = Interpolate(segment.From.Pose, segment.To.Pose, t);
            var feed = FeedMultiplier(t) * segment.Transition.FeedRateUnitsPerMin;
            steps.Add(new CompiledStep(segment.Index, MoveCommand(pose, feed), SegmentProgress: t));
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

    private static GCodeLineCommand MoveCommand(MachinePose pose, double feed) => new(
        $"G1 X{Format(pose.X)} Y{Format(pose.Y)} Z{Format(pose.Z)} A{Format(pose.A)} F{Format(feed)}");

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
