using System;
using System.Collections.Generic;
using ArctZ.Services.Device;

namespace ArctZ.Services.Program;

/// <summary>
/// Tracks playback progress from the machine's real position (WPos), not G-code ack timing — see
/// docs/superpowers/specs/2026-08-19-physical-program-progress-design.md for why: ack confirms a
/// line reached the controller's buffer, not that the move finished, and two earlier attempts to
/// animate progress off ack timing were reverted for exactly that reason.
///
/// Built fresh for each playback pass (forward or backward/PingPong). Projects each reported
/// position onto the pass's piecewise-linear path (one edge per <see cref="CompiledStep"/>) and
/// keeps a monotonic-maximum cumulative distance so cornering/reporting noise can never move
/// progress backward.
/// </summary>
public sealed class PhysicalProgressTracker
{
    private readonly struct Edge
    {
        public required MachinePose From { get; init; }
        public required MachinePose To { get; init; }
        public required int SegmentIndex { get; init; }
        public required double Length { get; init; }
        public required double CumulativeBefore { get; init; }
    }

    private readonly Edge[] _edges;
    private readonly double _totalLength;
    private readonly Dictionary<int, (double Start, double Length)> _segmentSpans = new();

    private double _farthestCumulativeDistance;

    public event Action? Changed;

    public PhysicalProgressTracker(IReadOnlyList<CompiledStep> steps, MachinePose startingPose)
    {
        _edges = new Edge[steps.Count];
        var previousPose = startingPose;
        var cumulative = 0.0;

        for (var i = 0; i < steps.Count; i++)
        {
            var length = Distance(previousPose, steps[i].Pose);
            _edges[i] = new Edge
            {
                From = previousPose,
                To = steps[i].Pose,
                SegmentIndex = steps[i].SegmentIndex,
                Length = length,
                CumulativeBefore = cumulative,
            };

            _segmentSpans[steps[i].SegmentIndex] = _segmentSpans.TryGetValue(steps[i].SegmentIndex, out var span)
                ? (span.Start, span.Length + length)
                : (cumulative, length);

            cumulative += length;
            previousPose = steps[i].Pose;
        }

        _totalLength = cumulative;
    }

    public double OverallFraction => _totalLength <= 0 ? 1.0 : Math.Clamp(_farthestCumulativeDistance / _totalLength, 0, 1);

    public double ApproachFraction
    {
        get
        {
            if (CurrentSegmentIndex is not { } index || !_segmentSpans.TryGetValue(index, out var span))
            {
                return 0;
            }

            return span.Length <= 0 ? 1.0 : Math.Clamp((_farthestCumulativeDistance - span.Start) / span.Length, 0, 1);
        }
    }

    public int? CurrentSegmentIndex => _edges.Length == 0 ? null : _edges[FindEdgeIndexAt(_farthestCumulativeDistance)].SegmentIndex;

    public void OnPositionUpdated(MachinePose position)
    {
        if (_edges.Length > 0)
        {
            var bestCumulative = _farthestCumulativeDistance;
            var bestDistance = double.MaxValue;

            foreach (var edge in _edges)
            {
                if (edge.Length <= 0)
                {
                    continue; // zero-length edges (dwell steps, or a coincident target) aren't projectable
                }

                var t = Math.Clamp(Dot(Subtract(position, edge.From), Subtract(edge.To, edge.From)) / (edge.Length * edge.Length), 0, 1);
                var projected = Lerp(edge.From, edge.To, t);
                var distanceToProjected = Distance(position, projected);

                if (distanceToProjected < bestDistance)
                {
                    bestDistance = distanceToProjected;
                    bestCumulative = edge.CumulativeBefore + t * edge.Length;
                }
            }

            _farthestCumulativeDistance = Math.Max(_farthestCumulativeDistance, bestCumulative);
        }

        Changed?.Invoke();
    }

    private int FindEdgeIndexAt(double cumulative)
    {
        for (var i = 0; i < _edges.Length - 1; i++)
        {
            if (cumulative <= _edges[i].CumulativeBefore + _edges[i].Length)
            {
                return i;
            }
        }

        return _edges.Length - 1;
    }

    private static double Distance(MachinePose a, MachinePose b) => Math.Sqrt(
        Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2) + Math.Pow(b.Z - a.Z, 2) + Math.Pow(b.A - a.A, 2));

    private static MachinePose Subtract(MachinePose a, MachinePose b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.A - b.A);

    private static double Dot(MachinePose a, MachinePose b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.A * b.A;

    private static MachinePose Lerp(MachinePose from, MachinePose to, double t) => new(
        from.X + (to.X - from.X) * t,
        from.Y + (to.Y - from.Y) * t,
        from.Z + (to.Z - from.Z) * t,
        from.A + (to.A - from.A) * t);
}
