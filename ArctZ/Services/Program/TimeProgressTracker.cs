using System;
using System.Collections.Generic;
using ArctZ.Services.Device;

namespace ArctZ.Services.Program;

/// <summary>
/// Tracks playback progress in TIME, not distance — see
/// docs/superpowers/specs/2026-08-19-physical-program-progress-design.md (Revision 2) for why: a
/// distance-based bar (Revision 1, same file, formerly PhysicalProgressTracker) didn't read the
/// way the operator wanted after live testing. This revision keeps Revision 1's one solid idea —
/// the machine's real position, not G-code ack timing, decides which key point is physically
/// active, because ack confirms a line reached the controller's buffer, not that the move
/// finished — while changing what the progress VALUE measures: elapsed wall-clock time against
/// each step's <see cref="CompiledStep.EstimatedDurationSeconds"/>, not distance covered.
///
/// Built fresh for each playback pass (forward or backward/PingPong). Position updates still
/// project onto the pass's piecewise-linear path (one edge per <see cref="CompiledStep"/>, using a
/// monotonic-maximum cumulative distance so cornering/reporting noise can never move the active
/// segment backward) purely to know which SegmentIndex is physically current — that geometry never
/// leaves the class. Time (passed in by the caller, never read from the system clock internally,
/// for testability) is what every public property reports.
///
/// There is deliberately no separate "dwelling" state (Revision 1 had one, driven by a 100ms
/// timer): once a segment's real motion arrives, position stops changing but CurrentSegmentIndex
/// naturally stays put (the zero-length dwell edge shares its SegmentIndex), so the segment
/// boundary is already strictly physical — no extra state machine needed to track it.
/// </summary>
public sealed class TimeProgressTracker
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
    private readonly Dictionary<int, double> _segmentEstimatedSeconds = new();
    private readonly double _totalEstimatedSeconds;
    private DateTimeOffset _passStartedAt;

    private double _farthestCumulativeDistance;
    private int? _lastSegmentIndex;
    private DateTimeOffset _currentSegmentEnteredAt;

    public event Action? Changed;

    public TimeProgressTracker(IReadOnlyList<CompiledStep> steps, MachinePose startingPose, DateTimeOffset passStartedAt)
    {
        _passStartedAt = passStartedAt;
        _currentSegmentEnteredAt = passStartedAt;

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

            _segmentEstimatedSeconds[steps[i].SegmentIndex] =
                _segmentEstimatedSeconds.GetValueOrDefault(steps[i].SegmentIndex) + steps[i].EstimatedDurationSeconds;
            _totalEstimatedSeconds += steps[i].EstimatedDurationSeconds;

            cumulative += length;
            previousPose = steps[i].Pose;
        }

        // Captured here, not left to default(int?)/null, so the FIRST OnPositionUpdated/OnClockTick
        // call after construction doesn't mistake "no prior recorded segment" for "just entered
        // segment 0" — that would reset _currentSegmentEnteredAt to whenever that first call
        // happens instead of passStartedAt, undercounting segment 0's elapsed time by however long
        // the caller waited before the first tick.
        _lastSegmentIndex = CurrentSegmentIndex;
    }

    public double OverallFraction { get; private set; }

    public double CurrentStepFraction { get; private set; }

    public bool CurrentPointHasWarning { get; private set; }

    public int? CurrentSegmentIndex => _edges.Length == 0 ? null : _edges[FindEdgeIndexAt(_farthestCumulativeDistance)].SegmentIndex;

    public void OnPositionUpdated(MachinePose position, DateTimeOffset now)
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

        Recompute(now);
    }

    public void OnClockTick(DateTimeOffset now) => Recompute(now);

    /// <summary>
    /// Shifts the pass-start and current-segment-entry clocks forward by a paused interval, so
    /// time spent paused — including an arbitrarily long link-loss reconnect (see
    /// ProgramViewModel.ApplySessionConnectionState) — never counts as elapsed progress or
    /// triggers a false time-overage warning.
    /// </summary>
    public void ShiftForPause(TimeSpan pauseDuration)
    {
        _passStartedAt += pauseDuration;
        _currentSegmentEnteredAt += pauseDuration;
    }

    private void Recompute(DateTimeOffset now)
    {
        var segmentIndex = CurrentSegmentIndex;
        if (segmentIndex != _lastSegmentIndex)
        {
            _lastSegmentIndex = segmentIndex;
            _currentSegmentEnteredAt = now;
        }

        var estimatedForSegment = segmentIndex is { } index ? _segmentEstimatedSeconds.GetValueOrDefault(index) : 0;
        var elapsedInSegment = (now - _currentSegmentEnteredAt).TotalSeconds;

        CurrentStepFraction = estimatedForSegment <= 0 ? 1.0 : elapsedInSegment / estimatedForSegment;
        CurrentPointHasWarning = estimatedForSegment > 0 && elapsedInSegment > estimatedForSegment * 1.15;
        OverallFraction = _totalEstimatedSeconds <= 0 ? 1.0 : Math.Clamp((now - _passStartedAt).TotalSeconds / _totalEstimatedSeconds, 0, 1);

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
