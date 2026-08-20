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

    private readonly List<Edge> _edges = new();
    private readonly Dictionary<int, double> _segmentEstimatedSeconds = new();
    private double _totalEstimatedSeconds;
    private DateTimeOffset _passStartedAt;

    private double _farthestCumulativeDistance;
    private int? _lastSegmentIndex;
    private DateTimeOffset _currentSegmentEnteredAt;

    public event Action? Changed;

    /// <summary>
    /// Fires once when the machine leaves a segment that was over its 15% warning threshold at
    /// the moment it left (int segmentIndex, double actualSeconds, double estimatedSeconds). The
    /// last segment of a pass never triggers this naturally — call <see cref="FlushCurrentSegment"/>
    /// when the pass ends to cover it.
    /// </summary>
    public event Action<int, double, double>? SegmentTimeOverage;

    public TimeProgressTracker(IReadOnlyList<CompiledStep> steps, MachinePose startingPose, DateTimeOffset passStartedAt)
    {
        _passStartedAt = passStartedAt;
        _currentSegmentEnteredAt = passStartedAt;

        AppendEdges(steps, startingPose);

        // Captured here, not left to default(int?)/null, so the FIRST OnPositionUpdated/OnClockTick
        // call after construction doesn't mistake "no prior recorded segment" for "just entered
        // segment 0" — that would reset _currentSegmentEnteredAt to whenever that first call
        // happens instead of passStartedAt, undercounting segment 0's elapsed time by however long
        // the caller waited before the first tick.
        _lastSegmentIndex = CurrentSegmentIndex;
    }

    /// <summary>
    /// Appends more steps to the tail of the currently in-flight pass instead of starting a fresh
    /// one — used for the ReturnToStartOnFinish move, which is the same pass's own extra (N+1)th
    /// step, not a new pass: OverallFraction must keep growing against the bigger total estimate
    /// instead of resetting to 0%. <paramref name="now"/> immediately recomputes the public
    /// properties against the new total so callers observe the (lower, but non-zero) fraction
    /// right away rather than waiting for the next clock tick.
    /// </summary>
    public void Extend(IReadOnlyList<CompiledStep> steps, DateTimeOffset now)
    {
        AppendEdges(steps, _edges.Count > 0 ? _edges[^1].To : MachinePose.Zero);
        Recompute(now);
    }

    private void AppendEdges(IReadOnlyList<CompiledStep> steps, MachinePose startingPose)
    {
        var previousPose = startingPose;
        var cumulative = _edges.Count > 0 ? _edges[^1].CumulativeBefore + _edges[^1].Length : 0.0;

        foreach (var step in steps)
        {
            var length = Distance(previousPose, step.Pose);
            _edges.Add(new Edge
            {
                From = previousPose,
                To = step.Pose,
                SegmentIndex = step.SegmentIndex,
                Length = length,
                CumulativeBefore = cumulative,
            });

            _segmentEstimatedSeconds[step.SegmentIndex] =
                _segmentEstimatedSeconds.GetValueOrDefault(step.SegmentIndex) + step.EstimatedDurationSeconds;
            _totalEstimatedSeconds += step.EstimatedDurationSeconds;

            cumulative += length;
            previousPose = step.Pose;
        }
    }

    public double OverallFraction { get; private set; }

    public double CurrentStepFraction { get; private set; }

    public bool CurrentPointHasWarning { get; private set; }

    public int? CurrentSegmentIndex => _edges.Count == 0 ? null : _edges[FindEdgeIndexAt(_farthestCumulativeDistance)].SegmentIndex;

    public void OnPositionUpdated(MachinePose position, DateTimeOffset now)
    {
        if (_edges.Count > 0)
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
    /// Reports the currently active segment's overage, if any, without waiting for a transition
    /// into a next segment — the last point of a pass has no next segment to move into, so the
    /// caller must flush it explicitly (on pass end/stop) to still get its message.
    /// </summary>
    public void FlushCurrentSegment(DateTimeOffset now)
    {
        if (_lastSegmentIndex is { } index)
        {
            EmitOverageIfNeeded(index, now);
        }
    }

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
            if (_lastSegmentIndex is { } finishedIndex)
            {
                EmitOverageIfNeeded(finishedIndex, now);
            }

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

    private void EmitOverageIfNeeded(int segmentIndex, DateTimeOffset now)
    {
        var estimatedForSegment = _segmentEstimatedSeconds.GetValueOrDefault(segmentIndex);
        var elapsedInSegment = (now - _currentSegmentEnteredAt).TotalSeconds;

        if (estimatedForSegment > 0 && elapsedInSegment > estimatedForSegment * 1.15)
        {
            SegmentTimeOverage?.Invoke(segmentIndex, elapsedInSegment, estimatedForSegment);
        }
    }

    private int FindEdgeIndexAt(double cumulative)
    {
        for (var i = 0; i < _edges.Count - 1; i++)
        {
            if (cumulative <= _edges[i].CumulativeBefore + _edges[i].Length)
            {
                return i;
            }
        }

        return _edges.Count - 1;
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
