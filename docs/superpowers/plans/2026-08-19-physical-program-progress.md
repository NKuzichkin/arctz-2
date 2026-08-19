# Physical Program Progress Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three progress displays driven by real machine position (not G-code ack timing) —
a shrinking circle on the currently-approached key-point tile, a re-added overall progress bar
above the key-points list, and a percentage in the Android foreground notification (5% steps).

**Architecture:** A new UI-agnostic `PhysicalProgressTracker` class projects the live `WPos`
telemetry (already arriving every ~100ms via `IDeviceSession.DeviceStatusChanged`) onto the
piecewise-linear path of the current playback pass, using a monotonic-maximum cumulative-distance
clamp so it can never jump backward. `ProgramViewModel` owns one tracker instance per pass (fresh
on every Loop/PingPong repeat) and exposes three read-only properties UI binds to. The existing
ack-based `OverallProgress`/`CurrentSegmentIndex`/`CurrentlyExecutingKeyPointId` are untouched.

**Tech Stack:** .NET 10, Avalonia (compiled bindings), CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-19-physical-program-progress-design.md`

## Global Constraints

- Existing ack-based progress properties (`OverallProgress`, `CurrentSegmentIndex`,
  `SegmentProgress`, `CurrentlyExecutingKeyPointId`, `FaultedMessage`) must not change behavior —
  every existing test in `ArctZ.Tests/ViewModels/ProgramViewModel*Tests.cs` must keep passing
  unmodified except where a task explicitly says to touch one.
- The overall bar resets to 0% on every new pass (including each Loop/PingPong repeat) —
  confirmed with the user, not "spans the whole run."
- Android notification progress updates only on crossing a 5%-multiple boundary — this falls out
  for free from `BackgroundSessionCoordinator`'s existing `_lastSent == state` dedup once
  `BackgroundSessionState.ProgressPercent` is added and rounded to the nearest 5.
- No time-based estimation anywhere in the new code path — only real position (`MachinePose`) and,
  for the dwell countdown only, real elapsed wall-clock time bounded by two real events (position
  arrival, position departure) per the spec's §5.
- `RunReturnToStartMoveAsync` (Loop's return-to-start hop and `ReturnToStartOnFinish`) is
  explicitly out of scope for the new tracker — the bar/circle simply hold their last pass's final
  state during that move and reset at the start of the next full pass. Do not build tracking for it.
- Follow existing code style: file-scoped namespaces, `sealed` classes, XML-doc comments only where
  the *why* is non-obvious (see existing files for the level of terseness expected).

---

## File Structure

- `ArctZ/Services/Program/CompiledStep.cs` — **modify**: add `Pose` and `IsDwellStep` fields.
- `ArctZ/Services/Program/TrajectoryCompiler.cs` — **modify**: populate the two new fields at all
  three `CompiledStep` construction sites.
- `ArctZ/Services/Program/JibProgram.cs` — **modify**: add static `TargetKeyPoint` helper.
- `ArctZ/ViewModels/ProgramViewModel.cs` — **modify**: use the new helper in
  `CurrentlyExecutingKeyPointId`; own a `PhysicalProgressTracker` per pass; expose
  `PhysicalOverallProgress`, `PhysicalPointRemainingFraction`, `PhysicallyExecutingKeyPointId`
  (new name, distinct from the existing ack-based one — see Task 6 naming note).
- `ArctZ/Services/Program/PhysicalProgressTracker.cs` — **new**: the position-projection engine.
- `ArctZ/Services/Device/ServiceCollectionExtensions.cs` — **modify**: pass the progress timer pair
  to `ProgramViewModel`'s registration.
- `ArctZ/Services/App/BackgroundSessionState.cs` — **modify**: add `ProgressPercent`.
- `ArctZ/Services/App/BackgroundSessionProjector.cs` — **modify**: compute `ProgressPercent`.
- `ArctZ/Services/App/BackgroundSessionCoordinator.cs` — **modify**: pass the new fraction into
  `Project(...)`.
- `ArctZ.Android/MachineSessionService.cs` — **modify**: `SetProgress` in `BuildNotification`.
- `ArctZ/Converters/FractionToPieSliceConverter.cs` — **new**: pie-slice `Geometry` for the circle.
- `ArctZ/Views/MainView.axaml` — **modify**: re-add the overall `ProgressBar`; add the circle
  `Path` to the key-point tile `DataTemplate`.
- Tests mirror each new/changed production file under `ArctZ.Tests/...` with the same relative path.

---

## Task 1: `CompiledStep` gets a target pose and a dwell flag

**Files:**
- Modify: `ArctZ/Services/Program/CompiledStep.cs`
- Modify: `ArctZ/Services/Program/TrajectoryCompiler.cs:26-34,58-63`
- Test: `ArctZ.Tests/Services/Program/TrajectoryCompilerTests.cs`

**Interfaces:**
- Produces: `CompiledStep(int SegmentIndex, IDeviceCommand Command, double SegmentProgress, double EstimatedDurationSeconds, MachinePose Pose, bool IsDwellStep)` — every later task that reads a `CompiledStep` uses these two new members.

- [ ] **Step 1: Write the failing tests**

Add to `ArctZ.Tests/Services/Program/TrajectoryCompilerTests.cs` (adjust `using`s/namespace to match
the existing file; these three cases cover the three `CompiledStep` construction sites in
`TrajectoryCompiler`):

```csharp
[Fact]
public void Compile_StraightMove_StepCarriesTargetPoseAndIsNotADwellStep()
{
    var program = new JibProgram();
    program.KeyPoints.Add(new KeyPoint(Guid.NewGuid(), 1, "A", MachinePose.Zero, DwellSeconds: 0, TransitionSeconds: 5, EaseMode.None, ContinuousBlend: true));
    program.KeyPoints.Add(new KeyPoint(Guid.NewGuid(), 2, "B", new MachinePose(10, 0, 0, 0), DwellSeconds: 0, TransitionSeconds: 5, EaseMode.None, ContinuousBlend: true));

    var steps = new TrajectoryCompiler().Compile(program);
    var moveStep = steps.Single(s => s.SegmentIndex == 1);

    Assert.Equal(new MachinePose(10, 0, 0, 0), moveStep.Pose);
    Assert.False(moveStep.IsDwellStep);
}

[Fact]
public void Compile_DwellStep_StepCarriesTheSameTargetPoseAsThePrecedingMoveAndIsADwellStep()
{
    var program = new JibProgram();
    program.KeyPoints.Add(new KeyPoint(Guid.NewGuid(), 1, "A", MachinePose.Zero, DwellSeconds: 2, TransitionSeconds: 5, EaseMode.None, ContinuousBlend: false));

    var steps = new TrajectoryCompiler().Compile(program);
    var dwellStep = steps.Single(s => s.SegmentIndex == 0 && s.IsDwellStep);

    Assert.Equal(MachinePose.Zero, dwellStep.Pose);
    Assert.Equal(2, dwellStep.EstimatedDurationSeconds);
}

[Fact]
public void Compile_EasedSubdivisions_EachStepCarriesItsOwnInterpolatedPoseAndIsNotADwellStep()
{
    var program = new JibProgram();
    program.KeyPoints.Add(new KeyPoint(Guid.NewGuid(), 1, "A", MachinePose.Zero, DwellSeconds: 0, TransitionSeconds: 6, EaseMode.None, ContinuousBlend: true));
    program.KeyPoints.Add(new KeyPoint(Guid.NewGuid(), 2, "B", new MachinePose(12, 0, 0, 0), DwellSeconds: 0, TransitionSeconds: 6, EaseMode.EaseInOut, ContinuousBlend: true));

    var steps = new TrajectoryCompiler().Compile(program);
    var easedSteps = steps.Where(s => s.SegmentIndex == 1).ToList();

    Assert.Equal(6, easedSteps.Count);
    Assert.All(easedSteps, s => Assert.False(s.IsDwellStep));
    Assert.Equal(2.0, easedSteps[0].Pose.X, precision: 6); // t = 1/6 of the 0->12 move
    Assert.Equal(12.0, easedSteps[5].Pose.X, precision: 6); // t = 6/6, arrives exactly at target
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~TrajectoryCompilerTests"`
Expected: compile error — `CompiledStep` has no `Pose`/`IsDwellStep` members yet.

- [ ] **Step 3: Add the fields to `CompiledStep`**

```csharp
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Program;

public sealed record CompiledStep(
    int SegmentIndex,
    IDeviceCommand Command,
    double SegmentProgress,
    double EstimatedDurationSeconds,
    MachinePose Pose,
    bool IsDwellStep);
```

- [ ] **Step 4: Populate the fields at all three construction sites in `TrajectoryCompiler.cs`**

Replace lines 26-34:

```csharp
                var seconds = InverseTimeMove.EffectiveSeconds(segment.To.TransitionSeconds);
                var command = new GCodeLineCommand(InverseTimeMove.Line(segment.To.Pose, seconds));
                steps.Add(new CompiledStep(segment.Index, command, SegmentProgress: 1.0, EstimatedDurationSeconds: seconds, Pose: segment.To.Pose, IsDwellStep: false));
            }

            if (segment.To.StopsAtWaypoint)
            {
                var dwellLine = $"G4 P{Format(segment.To.DwellSeconds)}";
                steps.Add(new CompiledStep(segment.Index, new GCodeLineCommand(dwellLine), SegmentProgress: 1.0, EstimatedDurationSeconds: segment.To.DwellSeconds, Pose: segment.To.Pose, IsDwellStep: true));
            }
```

Replace lines 58-63 (inside `CompileEased`):

```csharp
            steps.Add(new CompiledStep(
                segment.Index,
                new GCodeLineCommand(InverseTimeMove.Line(pose, seconds)),
                SegmentProgress: t,
                EstimatedDurationSeconds: seconds,
                Pose: pose,
                IsDwellStep: false));
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~TrajectoryCompilerTests"`
Expected: PASS (all cases, including pre-existing ones — the two new constructor args are
positional-named so no other call site breaks).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Services/Program/CompiledStep.cs ArctZ/Services/Program/TrajectoryCompiler.cs ArctZ.Tests/Services/Program/TrajectoryCompilerTests.cs
git commit -m "feat: CompiledStep carries its target pose and dwell flag"
```

---

## Task 2: Extract `JibProgram.TargetKeyPoint` and reuse it

The forward/backward segment-index-to-key-point mapping currently lives only inline in
`ProgramViewModel.CurrentlyExecutingKeyPointId` (`ProgramViewModel.cs:926-948`). Task 6 needs the
same mapping for the new tracker's `PhysicallyExecutingKeyPointId`, so it's extracted once here,
behavior-preserving, with the existing ack-based property switched to call it (no behavior change
— existing tests are the safety net).

**Files:**
- Modify: `ArctZ/Services/Program/JibProgram.cs`
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs:926-948`
- Test: `ArctZ.Tests/Services/Program/JibProgramTests.cs` (create if it doesn't exist)

**Interfaces:**
- Produces: `static Guid? JibProgram.TargetKeyPoint(IReadOnlyList<KeyPoint> forwardKeyPoints, int? segmentIndex, bool backward)` — used by Task 6.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using Xunit;

namespace ArctZ.Tests.Services.Program;

public class JibProgramTests
{
    private static KeyPoint Point(int number) =>
        new(Guid.NewGuid(), number, $"P{number}", MachinePose.Zero, 0, 1, EaseMode.None, true);

    [Fact]
    public void TargetKeyPoint_Forward_SegmentIndexIndexesDirectlyIntoTheList()
    {
        var points = new List<KeyPoint> { Point(1), Point(2), Point(3) };

        Assert.Equal(points[0].Id, JibProgram.TargetKeyPoint(points, segmentIndex: 0, backward: false));
        Assert.Equal(points[2].Id, JibProgram.TargetKeyPoint(points, segmentIndex: 2, backward: false));
    }

    [Fact]
    public void TargetKeyPoint_Backward_SegmentIndexCountsFromTheEndOfTheList()
    {
        var points = new List<KeyPoint> { Point(1), Point(2), Point(3) };

        Assert.Equal(points[2].Id, JibProgram.TargetKeyPoint(points, segmentIndex: 0, backward: true));
        Assert.Equal(points[0].Id, JibProgram.TargetKeyPoint(points, segmentIndex: 2, backward: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(3)]
    public void TargetKeyPoint_NullOrOutOfRangeIndex_ReturnsNull(int? segmentIndex)
    {
        var points = new List<KeyPoint> { Point(1), Point(2), Point(3) };

        Assert.Null(JibProgram.TargetKeyPoint(points, segmentIndex, backward: false));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~JibProgramTests"`
Expected: FAIL — `JibProgram.TargetKeyPoint` doesn't exist yet.

- [ ] **Step 3: Add the helper to `JibProgram.cs`**

```csharp
    /// <summary>Maps a segment index (as produced by <see cref="Segments"/>, or by compiling a
    /// reversed program for a backward/PingPong pass) to the key point it targets in
    /// <paramref name="forwardKeyPoints"/> — the pass's own original (forward) order. A backward
    /// pass compiles a reversed program, whose own segment index 0 targets the *last* forward
    /// point, hence <c>Count - 1 - segmentIndex</c>.</summary>
    public static Guid? TargetKeyPoint(IReadOnlyList<KeyPoint> forwardKeyPoints, int? segmentIndex, bool backward)
    {
        if (segmentIndex is not { } index)
        {
            return null;
        }

        var targetIndex = backward ? forwardKeyPoints.Count - 1 - index : index;
        return targetIndex >= 0 && targetIndex < forwardKeyPoints.Count
            ? forwardKeyPoints[targetIndex].Id
            : null;
    }
```

- [ ] **Step 4: Run the new tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~JibProgramTests"`
Expected: PASS.

- [ ] **Step 5: Switch `CurrentlyExecutingKeyPointId` to use the helper**

In `ProgramViewModel.cs`, replace lines 926-948:

```csharp
    public Guid? CurrentlyExecutingKeyPointId => PlaybackState is PlaybackState.Running or PlaybackState.Paused
        ? Services.Program.JibProgram.TargetKeyPoint(KeyPoints, CurrentSegmentIndex, _currentPassBackward)
        : null;
```

- [ ] **Step 6: Run the full existing playback test suite to confirm no behavior change**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModel"`
Expected: PASS (all pre-existing tests, unmodified).

- [ ] **Step 7: Commit**

```bash
git add ArctZ/Services/Program/JibProgram.cs ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/Services/Program/JibProgramTests.cs
git commit -m "refactor: extract JibProgram.TargetKeyPoint from the ack-based highlight"
```

---

## Task 3: `PhysicalProgressTracker` — polyline, projection, overall/approach fraction

The core position-projection engine, with no dwell handling yet (Task 4) and no key-point mapping
yet (Task 5 wires that in via the Task 2 helper — the tracker itself only needs segment indices).

**Files:**
- Create: `ArctZ/Services/Program/PhysicalProgressTracker.cs`
- Test: `ArctZ.Tests/Services/Program/PhysicalProgressTrackerTests.cs`

**Interfaces:**
- Consumes: `CompiledStep` (Task 1: `SegmentIndex`, `Pose`, `IsDwellStep`), `MachinePose` (`X,Y,Z,A`, `readonly record struct`).
- Produces (used by Tasks 4 and 6):
  - `PhysicalProgressTracker(IReadOnlyList<CompiledStep> steps, MachinePose startingPose)`
  - `void OnPositionUpdated(MachinePose position)`
  - `double OverallFraction { get; }` (0..1)
  - `double ApproachFraction { get; }` (0..1)
  - `int? CurrentSegmentIndex { get; }`
  - `event Action? Changed;`

- [ ] **Step 1: Write the failing tests**

All test moves are along the X axis only, so distances are simple absolute differences — keeps
expected numbers easy to verify by inspection.

```csharp
using System.Collections.Generic;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;
using ArctZ.Services.Program;
using Xunit;

namespace ArctZ.Tests.Services.Program;

public class PhysicalProgressTrackerTests
{
    private static CompiledStep Move(int segmentIndex, double x, bool isDwell = false) =>
        new(segmentIndex, new GCodeLineCommand("G93 G1 X" + x), SegmentProgress: 1.0, EstimatedDurationSeconds: 1, Pose: new MachinePose(x, 0, 0, 0), IsDwellStep: isDwell);

    [Fact]
    public void OnPositionUpdated_HalfwayThroughTheOnlySegment_ReportsHalfOverallAndHalfApproach()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10) };
        var tracker = new PhysicalProgressTracker(steps, startingPose: MachinePose.Zero);

        tracker.OnPositionUpdated(new MachinePose(5, 0, 0, 0));

        Assert.Equal(0.5, tracker.OverallFraction);
        Assert.Equal(0.5, tracker.ApproachFraction);
        Assert.Equal(0, tracker.CurrentSegmentIndex);
    }

    [Fact]
    public void OnPositionUpdated_ReachingTheEndOfASegment_ApproachFractionSaturatesAtOneForThatSegmentOnly()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10), Move(1, x: 20) };
        var tracker = new PhysicalProgressTracker(steps, startingPose: MachinePose.Zero);

        tracker.OnPositionUpdated(new MachinePose(10, 0, 0, 0));

        Assert.Equal(0.5, tracker.OverallFraction); // 10 of 20 total units travelled
        Assert.Equal(1.0, tracker.ApproachFraction); // segment 0 itself is fully covered
        Assert.Equal(0, tracker.CurrentSegmentIndex);
    }

    [Fact]
    public void OnPositionUpdated_MovingIntoTheNextSegment_ApproachFractionResetsForThatSegment()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10), Move(1, x: 20) };
        var tracker = new PhysicalProgressTracker(steps, startingPose: MachinePose.Zero);

        tracker.OnPositionUpdated(new MachinePose(10, 0, 0, 0));
        tracker.OnPositionUpdated(new MachinePose(15, 0, 0, 0));

        Assert.Equal(0.75, tracker.OverallFraction); // 15 of 20
        Assert.Equal(0.5, tracker.ApproachFraction); // halfway from 10 to 20
        Assert.Equal(1, tracker.CurrentSegmentIndex);
    }

    [Fact]
    public void OnPositionUpdated_NoisyPositionThatLooksLikeItWentBackward_NeverDecreasesProgress()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10), Move(1, x: 20) };
        var tracker = new PhysicalProgressTracker(steps, startingPose: MachinePose.Zero);

        tracker.OnPositionUpdated(new MachinePose(15, 0, 0, 0));
        var overallAfterFifteen = tracker.OverallFraction;
        tracker.OnPositionUpdated(new MachinePose(12, 0, 0, 0)); // controller cornering smoothing noise

        Assert.Equal(overallAfterFifteen, tracker.OverallFraction);
    }

    [Fact]
    public void OnPositionUpdated_ZeroLengthFirstSegment_ApproachFractionIsInstantlyOne()
    {
        // Real segment 0 is From == To == KeyPoints[0] in the model; here the compiled step's own
        // pose equals the starting pose, producing a zero-length edge.
        var steps = new List<CompiledStep> { Move(0, x: 0) };
        var tracker = new PhysicalProgressTracker(steps, startingPose: MachinePose.Zero);

        tracker.OnPositionUpdated(MachinePose.Zero);

        Assert.Equal(1.0, tracker.ApproachFraction);
        Assert.Equal(1.0, tracker.OverallFraction);
    }

    [Fact]
    public void Changed_FiresOnEveryPositionUpdate()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10) };
        var tracker = new PhysicalProgressTracker(steps, startingPose: MachinePose.Zero);
        var raiseCount = 0;
        tracker.Changed += () => raiseCount++;

        tracker.OnPositionUpdated(new MachinePose(5, 0, 0, 0));

        Assert.Equal(1, raiseCount);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~PhysicalProgressTrackerTests"`
Expected: FAIL — `PhysicalProgressTracker` doesn't exist yet.

- [ ] **Step 3: Implement `PhysicalProgressTracker.cs` (overall/approach fraction only)**

```csharp
using System;
using System.Collections.Generic;

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
            if (cumulative < _edges[i].CumulativeBefore + _edges[i].Length)
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
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~PhysicalProgressTrackerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Services/Program/PhysicalProgressTracker.cs ArctZ.Tests/Services/Program/PhysicalProgressTrackerTests.cs
git commit -m "feat: PhysicalProgressTracker projects real position onto the playback path"
```

---

## Task 4: Dwell phase — `IsDwelling`/`DwellFraction`

**Files:**
- Modify: `ArctZ/Services/Program/PhysicalProgressTracker.cs`
- Test: `ArctZ.Tests/Services/Program/PhysicalProgressTrackerTests.cs`

**Interfaces:**
- Consumes: `CompiledStep.IsDwellStep`, `CompiledStep.EstimatedDurationSeconds` (exact `DwellSeconds` for a dwell step, per `TrajectoryCompiler`).
- Produces (used by Task 6): `bool IsDwelling { get; }`, `double DwellFraction { get; }` (1.0 = just arrived, 0.0 = dwell finished), `void OnTimerElapsed(TimeSpan interval)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void OnPositionUpdated_ArrivingAtAPointWithADwell_EntersDwellingWithFullDwellFraction()
{
    var steps = new List<CompiledStep>
    {
        Move(0, x: 10),
        new(0, new GCodeLineCommand("G4 P4"), SegmentProgress: 1.0, EstimatedDurationSeconds: 4, Pose: new MachinePose(10, 0, 0, 0), IsDwellStep: true),
    };
    var tracker = new PhysicalProgressTracker(steps, startingPose: MachinePose.Zero);

    tracker.OnPositionUpdated(new MachinePose(10, 0, 0, 0));

    Assert.True(tracker.IsDwelling);
    Assert.Equal(1.0, tracker.DwellFraction);
}

[Fact]
public void OnTimerElapsed_WhileDwelling_CountsDownDwellFractionOverTheRealDwellSeconds()
{
    var steps = new List<CompiledStep>
    {
        Move(0, x: 10),
        new(0, new GCodeLineCommand("G4 P4"), SegmentProgress: 1.0, EstimatedDurationSeconds: 4, Pose: new MachinePose(10, 0, 0, 0), IsDwellStep: true),
    };
    var tracker = new PhysicalProgressTracker(steps, startingPose: MachinePose.Zero);
    tracker.OnPositionUpdated(new MachinePose(10, 0, 0, 0));

    tracker.OnTimerElapsed(TimeSpan.FromSeconds(1));

    Assert.Equal(0.75, tracker.DwellFraction);
}

[Fact]
public void OnTimerElapsed_WhileNotDwelling_DoesNothing()
{
    var steps = new List<CompiledStep> { Move(0, x: 10) };
    var tracker = new PhysicalProgressTracker(steps, startingPose: MachinePose.Zero);

    tracker.OnTimerElapsed(TimeSpan.FromSeconds(1));

    Assert.False(tracker.IsDwelling);
}

[Fact]
public void OnPositionUpdated_RealMotionAwayFromADwellPoint_EndsDwellingRegardlessOfTheTimer()
{
    var steps = new List<CompiledStep>
    {
        Move(0, x: 10),
        new(0, new GCodeLineCommand("G4 P4"), SegmentProgress: 1.0, EstimatedDurationSeconds: 4, Pose: new MachinePose(10, 0, 0, 0), IsDwellStep: true),
        Move(1, x: 20),
    };
    var tracker = new PhysicalProgressTracker(steps, startingPose: MachinePose.Zero);
    tracker.OnPositionUpdated(new MachinePose(10, 0, 0, 0));
    Assert.True(tracker.IsDwelling);

    tracker.OnPositionUpdated(new MachinePose(11, 0, 0, 0)); // real motion toward the next point, timer never fired

    Assert.False(tracker.IsDwelling);
    Assert.Equal(1, tracker.CurrentSegmentIndex);
}

[Fact]
public void OnPositionUpdated_ArrivingAtAPointWithNoDwell_NeverEntersDwelling()
{
    var steps = new List<CompiledStep> { Move(0, x: 10), Move(1, x: 20) };
    var tracker = new PhysicalProgressTracker(steps, startingPose: MachinePose.Zero);

    tracker.OnPositionUpdated(new MachinePose(10, 0, 0, 0));

    Assert.False(tracker.IsDwelling);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~PhysicalProgressTrackerTests"`
Expected: FAIL — `IsDwelling`/`DwellFraction`/`OnTimerElapsed` don't exist yet.

- [ ] **Step 3: Add dwell tracking to `PhysicalProgressTracker`**

Add a `DwellSeconds` field to the `Edge` struct and populate it in the constructor:

```csharp
        public required double DwellSeconds { get; init; }
```

In the constructor loop, when building each `Edge`:

```csharp
            _edges[i] = new Edge
            {
                From = previousPose,
                To = steps[i].Pose,
                SegmentIndex = steps[i].SegmentIndex,
                Length = length,
                CumulativeBefore = cumulative,
                DwellSeconds = steps[i].IsDwellStep ? steps[i].EstimatedDurationSeconds : 0,
            };
```

Add new fields and members:

```csharp
    private bool _isDwelling;
    private double _dwellElapsedSeconds;
    private double _dwellTotalSeconds;

    public bool IsDwelling => _isDwelling;

    public double DwellFraction => !_isDwelling || _dwellTotalSeconds <= 0
        ? 0
        : Math.Clamp(1 - _dwellElapsedSeconds / _dwellTotalSeconds, 0, 1);

    public void OnTimerElapsed(TimeSpan interval)
    {
        if (!_isDwelling)
        {
            return;
        }

        _dwellElapsedSeconds += interval.TotalSeconds;
        Changed?.Invoke();
    }
```

Extend `OnPositionUpdated` — replace `_farthestCumulativeDistance = Math.Max(...)` and the closing
`Changed?.Invoke();` with:

```csharp
            var previousSegmentIndex = CurrentSegmentIndex;
            _farthestCumulativeDistance = Math.Max(_farthestCumulativeDistance, bestCumulative);

            if (_isDwelling && CurrentSegmentIndex != previousSegmentIndex)
            {
                _isDwelling = false;
            }

            if (!_isDwelling && ApproachFraction >= 1.0)
            {
                var dwellEdge = FindDwellEdgeForSegment(CurrentSegmentIndex);
                if (dwellEdge is { DwellSeconds: > 0 } edge)
                {
                    _isDwelling = true;
                    _dwellElapsedSeconds = 0;
                    _dwellTotalSeconds = edge.DwellSeconds;
                }
            }
        }

        Changed?.Invoke();
    }

    private Edge? FindDwellEdgeForSegment(int? segmentIndex)
    {
        if (segmentIndex is not { } index)
        {
            return null;
        }

        foreach (var edge in _edges)
        {
            if (edge.SegmentIndex == index && edge.DwellSeconds > 0)
            {
                return edge;
            }
        }

        return null;
    }
```

(Note: this replaces the tail of the existing `if (_edges.Length > 0) { ... }` block and the method's
closing brace — read the current method body before editing so the braces line up.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~PhysicalProgressTrackerTests"`
Expected: PASS (all tests from Task 3 and Task 4).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Services/Program/PhysicalProgressTracker.cs ArctZ.Tests/Services/Program/PhysicalProgressTrackerTests.cs
git commit -m "feat: PhysicalProgressTracker dwell-phase countdown"
```

---

## Task 5: DI wiring — `IPeriodicTimer` pair for `ProgramViewModel`

Add the constructor parameters before Task 6 wires them up, so Task 6's own tests can use
`ManualPeriodicTimer` from the start.

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs:104-124` (constructor)
- Modify: `ArctZ/Services/Device/ServiceCollectionExtensions.cs:23-27`

**Interfaces:**
- Produces: `ProgramViewModel(ConnectionViewModel, IProgramStorage, ITrajectoryCompiler, IAppExitService, Func<DateTimeOffset>? now = null, IPeriodicTimer? progressTimer = null, TimeSpan? progressTimerInterval = null)` — existing call sites (`CreateViewModel` test helpers) keep compiling unchanged because the new parameters are optional and trail the existing ones.

- [ ] **Step 1: Add the two optional constructor parameters and store them**

In `ProgramViewModel.cs`, replace the constructor (lines 104-124):

```csharp
    public ProgramViewModel(
        ConnectionViewModel connection,
        IProgramStorage storage,
        ITrajectoryCompiler compiler,
        IAppExitService exitService,
        Func<DateTimeOffset>? now = null,
        IPeriodicTimer? progressTimer = null,
        TimeSpan? progressTimerInterval = null)
    {
        Connection = connection;
        _storage = storage;
        _compiler = compiler;
        _exitService = exitService;
        _now = now ?? (() => DateTimeOffset.Now);
        _startedAt = _now();
        _progressTimer = progressTimer ?? new SystemPeriodicTimer();
        _progressTimerInterval = progressTimerInterval ?? TimeSpan.FromMilliseconds(100);
        _progressTimer.Elapsed += OnProgressTimerElapsed;
        Connection.PropertyChanged += OnConnectionPropertyChanged;
        KeyPoints.CollectionChanged += (_, _) =>
        {
            MoveKeyPointUpCommand.NotifyCanExecuteChanged();
            MoveKeyPointDownCommand.NotifyCanExecuteChanged();
            MarkDirtyIfTracking();
        };
    }
```

Add the backing fields near the other private fields at the top of the class:

```csharp
    private readonly IPeriodicTimer _progressTimer;
    private readonly TimeSpan _progressTimerInterval;
    private PhysicalProgressTracker? _progressTracker;
```

Add a placeholder handler (Task 6 fills in the body) so the class compiles:

```csharp
    private void OnProgressTimerElapsed()
    {
        _progressTracker?.OnTimerElapsed(_progressTimerInterval);
    }
```

- [ ] **Step 2: Build to verify existing call sites still compile**

Run: `dotnet build ArctZ.Tests/ArctZ.Tests.csproj`
Expected: builds clean — every existing `new ProgramViewModel(connection, storage, compiler, exitService)` call across the test project still matches (trailing optional params).

- [ ] **Step 3: Register the timer pair in DI**

In `ArctZ/Services/Device/ServiceCollectionExtensions.cs`, replace lines 23-27:

```csharp
        services.AddSingleton<ProgramViewModel>(sp => new ProgramViewModel(
            sp.GetRequiredService<ConnectionViewModel>(),
            sp.GetRequiredService<IProgramStorage>(),
            sp.GetRequiredService<ITrajectoryCompiler>(),
            sp.GetRequiredService<IAppExitService>(),
            now: null,
            progressTimer: new SystemPeriodicTimer(),
            progressTimerInterval: TimeSpan.FromMilliseconds(100)));
```

- [ ] **Step 4: Run the full test suite to confirm nothing broke**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS (every existing test, unmodified).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ/Services/Device/ServiceCollectionExtensions.cs
git commit -m "feat: ProgramViewModel accepts an injectable progress timer"
```

---

## Task 6: Wire `PhysicalProgressTracker` into `ProgramViewModel`

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs`
- Test: `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs`

**Interfaces:**
- Produces (used by Tasks 8, 9, 11, 12): `double PhysicalOverallProgress { get; }` (0..1, 0 when no tracker), `double PhysicalPointRemainingFraction { get; }` (0..1, 1.0 when no tracker — "not started"), `Guid? PhysicallyExecutingKeyPointId { get; }`.

- [ ] **Step 1: Write the failing tests**

Add to `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs`. These reuse `CreateViewModel`
(Task 5 lets us pass a `ManualPeriodicTimer`) and the existing `FakeDeviceTransport` position/ack
simulation shown in the file already (`transport.SimulateReceivedLine("<Idle|WPos:...|FS:0,0>")`).

```csharp
private static ProgramViewModel CreateViewModel(out FakeDeviceTransport transport, out ManualPeriodicTimer progressTimer)
{
    transport = new FakeDeviceTransport();
    var storage = new FakeProgramStorage();
    var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default), new SingleRealDeviceEndpointProvider());
    progressTimer = new ManualPeriodicTimer();
    return new ProgramViewModel(connection, storage, new TrajectoryCompiler(), new FakeAppExitService(), progressTimer: progressTimer);
}

[Fact]
public async Task PlayAsync_AsThePositionAdvancesTowardTheFirstPoint_PhysicalOverallProgressTracksIt()
{
    var vm = CreateViewModel(out var transport, out var progressTimer);
    await vm.Connection.ConnectCommand.Execute();
    SeedTwoSegmentProgram(vm, transport);
    // SeedTwoSegmentProgram leaves the simulated machine at the last captured pose (20,0,0,0) —
    // reset it to the program's actual starting pose before Play, so the tracker's captured
    // starting vertex matches what these assertions assume (a clean 0->10->20 path, 20 units total).
    transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

    var playTask = vm.PlayCommand.ExecuteAsync(null);
    Assert.Equal(0, vm.PhysicalOverallProgress);

    transport.SimulateReceivedLine("<Run|WPos:5.000,0.000,0.000,0.000|FS:0,0>");

    Assert.Equal(0.25, vm.PhysicalOverallProgress); // 5 of 20 total units across both segments

    transport.SimulateReceivedLine("ok");
    transport.SimulateReceivedLine("ok");
    transport.SimulateReceivedLine("ok");
    await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
    transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
    await playTask;
}

[Fact]
public async Task PlayAsync_PhysicallyExecutingKeyPointId_CanLagTheAckBasedHighlightWhenTheBufferRunsAhead()
{
    var vm = CreateViewModel(out var transport, out var progressTimer);
    await vm.Connection.ConnectCommand.Execute();
    SeedTwoSegmentProgram(vm, transport);
    // SeedTwoSegmentProgram leaves the simulated machine at the last captured pose (20,0,0,0) —
    // reset it to the program's actual starting pose before Play, so the tracker's captured
    // starting vertex matches what these assertions assume (a clean 0->10->20 path, 20 units total).
    transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

    var playTask = vm.PlayCommand.ExecuteAsync(null);

    // Both remaining acks land before any position update — the ack-based highlight jumps to the
    // last point, but the physically-executing point stays at the first (position never moved).
    transport.SimulateReceivedLine("ok");
    transport.SimulateReceivedLine("ok");
    transport.SimulateReceivedLine("ok");
    await WaitUntilAsync(() => vm.CurrentlyExecutingKeyPointId == vm.KeyPoints[2].Id, TimeSpan.FromSeconds(1));

    Assert.Equal(vm.KeyPoints[0].Id, vm.PhysicallyExecutingKeyPointId);

    await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
    transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
    await playTask;
}

[Fact]
public async Task PlayAsync_EachNewPass_ResetsPhysicalOverallProgressToZero()
{
    var vm = CreateViewModel(out var transport, out var progressTimer);
    await vm.Connection.ConnectCommand.Execute();
    SeedTwoSegmentProgram(vm, transport);
    // SeedTwoSegmentProgram leaves the simulated machine at the last captured pose (20,0,0,0) —
    // reset it to the program's actual starting pose before Play, so the tracker's captured
    // starting vertex matches what these assertions assume (a clean 0->10->20 path, 20 units total).
    transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");
    vm.CompletionMode = ProgramCompletionMode.PingPong;
    vm.RepeatCount = 1;

    // Timing between the forward pass's last ack and the backward pass's tracker Reset isn't
    // pinned to a single observable predicate (both happen inside the same async continuation),
    // so this records the whole PhysicalOverallProgress sequence instead of polling one instant:
    // a reset-to-zero occurring only after the value has already gone positive is exactly the
    // "each new pass starts over" behavior this test exists to catch a regression in.
    var sawPositive = false;
    var sawResetAfterPositive = false;
    vm.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName != nameof(vm.PhysicalOverallProgress))
        {
            return;
        }

        if (vm.PhysicalOverallProgress > 0)
        {
            sawPositive = true;
        }
        else if (sawPositive)
        {
            sawResetAfterPositive = true;
        }
    };

    var playTask = vm.PlayCommand.ExecuteAsync(null);
    transport.SimulateReceivedLine("<Run|WPos:15.000,0.000,0.000,0.000|FS:0,0>"); // forward pass, well underway

    transport.SimulateReceivedLine("ok");
    transport.SimulateReceivedLine("ok");
    transport.SimulateReceivedLine("ok"); // forward pass fully acked; backward pass starts and its tracker resets
    await WaitUntilAsync(() => sawResetAfterPositive, TimeSpan.FromSeconds(1));

    transport.SimulateReceivedLine("ok");
    transport.SimulateReceivedLine("ok");
    transport.SimulateReceivedLine("ok");
    await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
    transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");
    await playTask;

    Assert.True(sawResetAfterPositive);
}

[Fact]
public async Task StopAsync_ClearsPhysicalProgress()
{
    var vm = CreateViewModel(out var transport, out var progressTimer);
    await vm.Connection.ConnectCommand.Execute();
    SeedTwoSegmentProgram(vm, transport);
    // SeedTwoSegmentProgram leaves the simulated machine at the last captured pose (20,0,0,0) —
    // reset it to the program's actual starting pose before Play, so the tracker's captured
    // starting vertex matches what these assertions assume (a clean 0->10->20 path, 20 units total).
    transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

    var playTask = vm.PlayCommand.ExecuteAsync(null);
    transport.SimulateReceivedLine("<Run|WPos:5.000,0.000,0.000,0.000|FS:0,0>");
    Assert.True(vm.PhysicalOverallProgress > 0);

    await vm.StopCommand.ExecuteAsync(null);
    await playTask;

    Assert.Equal(0, vm.PhysicalOverallProgress);
    Assert.Null(vm.PhysicallyExecutingKeyPointId);
}

[Fact]
public void PhysicalOverallProgress_WithNoActiveTracker_DefaultsToZero()
{
    var vm = CreateViewModel(out _, out _);

    Assert.Equal(0, vm.PhysicalOverallProgress);
    Assert.Equal(1.0, vm.PhysicalPointRemainingFraction);
    Assert.Null(vm.PhysicallyExecutingKeyPointId);
}
```

(`CreateViewModel` gains a second overload above rather than changing the existing 1-out-param
one used by every other test in the file — keep both, existing tests call the original.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: FAIL — `PhysicalOverallProgress`/`PhysicalPointRemainingFraction`/`PhysicallyExecutingKeyPointId` don't exist yet.

- [ ] **Step 3: Expose the three properties on `ProgramViewModel`**

Add near the existing `OverallProgress`/`CurrentlyExecutingKeyPointId` properties:

```csharp
    public double PhysicalOverallProgress => _progressTracker?.OverallFraction ?? 0;

    public double PhysicalPointRemainingFraction => _progressTracker switch
    {
        null => 1.0,
        { IsDwelling: true } tracker => tracker.DwellFraction,
        var tracker => 1.0 - tracker.ApproachFraction,
    };

    public Guid? PhysicallyExecutingKeyPointId => _progressTracker is null
        ? null
        : Services.Program.JibProgram.TargetKeyPoint(KeyPoints, _progressTracker.CurrentSegmentIndex, _currentPassBackward);

    private void OnProgressTrackerChanged()
    {
        OnPropertyChanged(nameof(PhysicalOverallProgress));
        OnPropertyChanged(nameof(PhysicalPointRemainingFraction));
        OnPropertyChanged(nameof(PhysicallyExecutingKeyPointId));
    }

    private void ClearProgressTracker()
    {
        if (_progressTracker is null)
        {
            return;
        }

        _progressTracker.Changed -= OnProgressTrackerChanged;
        _progressTracker = null;
        OnProgressTrackerChanged();
    }
```

- [ ] **Step 4: Create and reset the tracker at the start of each pass**

In `RunPassAsync` (`ProgramViewModel.cs:1247-1256`), replace the reset lines:

```csharp
        _currentPassBackward = backward;
        CurrentSegmentIndex = null;
        SegmentProgress = 0;

        if (_progressTracker is not null)
        {
            _progressTracker.Changed -= OnProgressTrackerChanged;
        }

        var startingPose = Connection.Session?.DeviceStatus?.WPos ?? MachinePose.Zero;
        _progressTracker = new PhysicalProgressTracker(steps, startingPose);
        _progressTracker.Changed += OnProgressTrackerChanged;
        OnProgressTrackerChanged();
```

- [ ] **Step 5: Feed real positions into the tracker**

Replace `OnSessionDeviceStatusChanged` (`ProgramViewModel.cs:991-997`):

```csharp
    private void OnSessionDeviceStatusChanged()
    {
        var status = Connection.Session?.DeviceStatus;
        if (status is { } value)
        {
            _progressTracker?.OnPositionUpdated(value.WPos);
        }

        if (status?.State == MachineState.Idle)
        {
            _motionIdleSignal?.TrySetResult(true);
        }
    }
```

- [ ] **Step 6: Clear the tracker and manage the dwell timer centrally**

In `OnPlaybackStateChanged` (`ProgramViewModel.cs:809-840`), extend the `Stopped or Faulted` branch
and add timer start/stop:

```csharp
        if (value is PlaybackState.Stopped or PlaybackState.Faulted)
        {
            _motionIdleSignal?.TrySetResult(false);
            ClearProgressTracker();
        }

        if (value == PlaybackState.Running)
        {
            _progressTimer.Start(_progressTimerInterval);
        }
        else
        {
            _progressTimer.Stop();
        }
```

(Insert the `if`/`else` block anywhere after the existing `Stopped or Faulted` block in the method
body — order relative to the other branches in that method doesn't matter, they're independent.)

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: PASS (new tests and every pre-existing test in the file, unmodified).

- [ ] **Step 8: Run the full suite**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs
git commit -m "feat: ProgramViewModel exposes physical (position-based) playback progress"
```

---

## Task 7: `BackgroundSessionState`/`BackgroundSessionProjector` — `ProgressPercent`

**Files:**
- Modify: `ArctZ/Services/App/BackgroundSessionState.cs`
- Modify: `ArctZ/Services/App/BackgroundSessionProjector.cs`
- Test: `ArctZ.Tests/Services/App/BackgroundSessionProjectorTests.cs`

**Interfaces:**
- Produces: `BackgroundSessionState.ProgressPercent` (`int?`), `BackgroundSessionProjector.Project(PlaybackState, string, string?, double? overallFraction)` — Task 8 passes `ProgramViewModel.PhysicalOverallProgress`.

- [ ] **Step 1: Write the failing tests**

Add to `ArctZ.Tests/Services/App/BackgroundSessionProjectorTests.cs`:

```csharp
[Theory]
[InlineData(0.0, 0)]
[InlineData(0.02, 0)]
[InlineData(0.03, 5)]
[InlineData(0.475, 50)]
[InlineData(0.99, 100)]
[InlineData(1.0, 100)]
public void Project_WhileRunning_RoundsOverallFractionToTheNearestFivePercent(double fraction, int expectedPercent)
{
    var state = BackgroundSessionProjector.Project(PlaybackState.Running, "Выполнение", "Панорама цеха", fraction);
    Assert.Equal(expectedPercent, state.ProgressPercent);
}

[Fact]
public void Project_WithoutAnOverallFraction_HasNoProgressPercent()
{
    var state = BackgroundSessionProjector.Project(PlaybackState.Idle, "Ожидание", "Панорама цеха", overallFraction: null);
    Assert.Null(state.ProgressPercent);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~BackgroundSessionProjectorTests"`
Expected: compile error — `Project` doesn't take a 4th argument yet.

- [ ] **Step 3: Add `ProgressPercent` to `BackgroundSessionState`**

```csharp
namespace ArctZ.Services.App;

public readonly record struct BackgroundSessionState(
    string Title,
    string Status,
    bool CanPause,
    bool CanResume,
    bool CanStop,
    int? ProgressPercent);
```

- [ ] **Step 4: Update `BackgroundSessionProjector.Project`**

```csharp
using System;
using ArctZ.ViewModels;

namespace ArctZ.Services.App;

public static class BackgroundSessionProjector
{
    public const string AppName = "ArctZ";

    public static BackgroundSessionState Project(PlaybackState playback, string statusLabel, string? programName, double? overallFraction) =>
        new(
            Title: string.IsNullOrWhiteSpace(programName) ? AppName : programName,
            Status: statusLabel,
            CanPause: playback == PlaybackState.Running,
            CanResume: playback == PlaybackState.Paused,
            CanStop: playback is PlaybackState.Running or PlaybackState.Paused,
            ProgressPercent: overallFraction is { } fraction ? (int)(Math.Round(fraction * 100 / 5.0) * 5) : null);
}
```

- [ ] **Step 5: Fix the pre-existing `Project` calls in the test file to pass the new argument**

Every existing call in `BackgroundSessionProjectorTests.cs` (the ones from before this task) needs
a 4th argument — pass `overallFraction: null` for all of them (they're not testing progress).

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~BackgroundSessionProjectorTests"`
Expected: PASS.

- [ ] **Step 7: Fix `MachineSessionService.CurrentState`'s default (Android head, no test to run — code review only)**

In `ArctZ.Android/MachineSessionService.cs:24-25`, the static default constructs a
`BackgroundSessionState` positionally — add a trailing `null`:

```csharp
    public static BackgroundSessionState CurrentState { get; set; } =
        new(BackgroundSessionProjector.AppName, "Ожидание", false, false, false, null);
```

- [ ] **Step 8: Build the whole solution to catch any other construction site**

Run: `dotnet build ArctZ.slnx`
Expected: builds clean (per `project_slnx_build_flakiness` memory, if this is flaky, verify with
per-project builds instead: `dotnet build ArctZ/ArctZ.csproj`, `dotnet build ArctZ.Android/ArctZ.Android.csproj`).

- [ ] **Step 9: Commit**

```bash
git add ArctZ/Services/App/BackgroundSessionState.cs ArctZ/Services/App/BackgroundSessionProjector.cs ArctZ.Android/MachineSessionService.cs ArctZ.Tests/Services/App/BackgroundSessionProjectorTests.cs
git commit -m "feat: BackgroundSessionState carries a 5%-rounded progress percentage"
```

---

## Task 8: `BackgroundSessionCoordinator` passes the physical fraction through

**Files:**
- Modify: `ArctZ/Services/App/BackgroundSessionCoordinator.cs:79-82`
- Test: `ArctZ.Tests/Services/App/BackgroundSessionCoordinatorTests.cs`

**Interfaces:**
- Consumes: `ProgramViewModel.PhysicalOverallProgress` (Task 6), `BackgroundSessionProjector.Project(..., double?)` (Task 7).

- [ ] **Step 1: Write the failing test**

Add to `ArctZ.Tests/Services/App/BackgroundSessionCoordinatorTests.cs`. This drives a real
`PlayAsync` (using `FakeDeviceTransport`, the same pattern as `ProgramViewModelPlaybackTests`) to
get a live `PhysicalOverallProgress`, and checks the notification only updates on a 5%-crossing.

```csharp
[Fact]
public async Task WhilePositionAdvancesDuringRun_HostIsUpdatedOnlyWhenTheRoundedPercentChanges()
{
    var transport = new FakeDeviceTransport();
    var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default), new SingleRealDeviceEndpointProvider());
    var program = new ProgramViewModel(connection, new FakeProgramStorage(), new TrajectoryCompiler(), new FakeAppExitService());
    using var coordinator = new BackgroundSessionCoordinator(program, _host);

    await program.Connection.ConnectCommand.Execute();
    foreach (var pose in new[] { "0,0,0,0", "100,0,0,0" })
    {
        transport.SimulateReceivedLine($"<Idle|WPos:{pose}|FS:0,0>");
        program.CaptureKeyPointCommand.Execute(null);
    }
    for (var i = 0; i < program.KeyPoints.Count; i++)
    {
        program.KeyPoints[i] = program.KeyPoints[i] with { TransitionSeconds = 5, DwellSeconds = 0, Ease = EaseMode.None, ContinuousBlend = true };
    }
    program.ProgramId = Guid.NewGuid();
    program.IsDirty = false;

    var playTask = program.PlayCommand.ExecuteAsync(null);
    var updatesBeforeMotion = _host.Updates.Count;

    transport.SimulateReceivedLine("<Run|WPos:1.000,0.000,0.000,0.000|FS:0,0>"); // 1 of 100 units: rounds to 0%
    Assert.Equal(updatesBeforeMotion, _host.Updates.Count);

    transport.SimulateReceivedLine("<Run|WPos:5.000,0.000,0.000,0.000|FS:0,0>"); // 5 of 100: rounds to 5%
    Assert.True(_host.Updates.Count > updatesBeforeMotion);
    Assert.Equal(5, _host.LastUpdate!.Value.ProgressPercent);

    transport.SimulateReceivedLine("ok");
    transport.SimulateReceivedLine("ok");
    await program.StopCommand.ExecuteAsync(null);
    await playTask;
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~BackgroundSessionCoordinatorTests"`
Expected: FAIL — `ProgressPercent` stays null throughout (Coordinator doesn't pass the fraction
yet), or the assertion at 5 units fails.

- [ ] **Step 3: Pass `PhysicalOverallProgress` into `Project`**

In `BackgroundSessionCoordinator.cs`, replace lines 79-82:

```csharp
        var state = BackgroundSessionProjector.Project(
            _program.PlaybackState,
            _program.StatusLabel,
            _program.ProgramName,
            _program.PhysicalOverallProgress);
```

- [ ] **Step 4: Run the new test and the full existing Coordinator suite**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~BackgroundSessionCoordinatorTests"`
Expected: PASS, including the pre-existing
`WhilePositionChangesDuringRun_UnchangedProjectionDoesNotReUpdateTheHost` test — that test sets
`PlaybackState.Running` directly without calling `PlayCommand`, so no pass ever starts, no tracker
is ever created, `PhysicalOverallProgress` stays `0` both times it pokes `Connection.DeviceStatus`,
and the projected state stays equal — no new update, exactly as that test already asserts.

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Services/App/BackgroundSessionCoordinator.cs ArctZ.Tests/Services/App/BackgroundSessionCoordinatorTests.cs
git commit -m "feat: Android session notification reflects physical playback progress"
```

---

## Task 9: Android notification — `SetProgress`

No test infra exists for the Android head (per prior work in this repo — code review only, per
Global Constraints).

**Files:**
- Modify: `ArctZ.Android/MachineSessionService.cs:88-104`

- [ ] **Step 1: Add the progress bar to `BuildNotification`**

Replace lines 95-97:

```csharp
        builder.SetContentTitle(state.Title).SetContentText(state.Status)
            .SetSmallIcon(Resource.Drawable.ic_notification).SetOngoing(true)
            .SetContentIntent(OpenAppIntent());

        if (state.ProgressPercent is { } percent)
        {
            builder.SetProgress(100, percent, false);
        }
```

- [ ] **Step 2: Build the Android head**

Run: `dotnet build ArctZ.Android/ArctZ.Android.csproj`
Expected: builds clean (per `mobile-build-setup` skill for JDK/SDK prerequisites already installed
on this machine).

- [ ] **Step 3: Commit**

```bash
git add ArctZ.Android/MachineSessionService.cs
git commit -m "feat: Android session notification shows playback progress"
```

---

## Task 10: Re-add the overall progress bar to `MainView.axaml`

**Files:**
- Modify: `ArctZ/Views/MainView.axaml:203-204`

- [ ] **Step 1: Add the `ProgressBar` above the key-points heading**

In `MainView.axaml`, replace line 203-204:

```xml
                    <TextBlock Classes="section-heading" Text="ТОЧКИ" Margin="0,8,0,0" />
                    <ProgressBar Value="{Binding PhysicalOverallProgress}" Minimum="0" Maximum="1"
                                 Height="4" Margin="0,4,0,0"
                                 Foreground="{DynamicResource HudAccentBrush}"
                                 Background="{DynamicResource HudPanelElevatedBrush}"
                                 IsVisible="{Binding IsProgramLocked}">
                        <ProgressBar.Transitions>
                            <Transitions>
                                <DoubleTransition Property="Value" Duration="0:0:0.3" Easing="CubicEaseOut" />
                            </Transitions>
                        </ProgressBar.Transitions>
                    </ProgressBar>
                    <ItemsControl x:Name="KeyPointsList" ItemsSource="{Binding KeyPoints}"
```

(`IsProgramLocked` already exists on `ProgramViewModel` — `PlaybackState is Running or Paused` —
same visibility rule the removed bar used before the `f3b9d3e` revert.)

- [ ] **Step 2: Build the Desktop head**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: builds clean.

- [ ] **Step 3: Commit**

```bash
git add ArctZ/Views/MainView.axaml
git commit -m "feat: re-add the overall program progress bar, driven by real position"
```

---

## Task 11: Shrinking circle on the key-point tile

**Files:**
- Create: `ArctZ/Converters/FractionToPieSliceConverter.cs`
- Modify: `ArctZ/Views/MainView.axaml` (resources block that declares `KeyPointIsExecuting`, and the tile `DataTemplate` at lines 212-259 from Task 10's shifted line numbers)

- [ ] **Step 1: Implement the pie-slice converter**

```csharp
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ArctZ.Converters;

/// <summary>Converts a 0..1 "remaining" fraction into a pie-slice Geometry, 12 o'clock start,
/// clockwise sweep — the shrinking-circle key-point progress badge. 1.0 = full circle (just
/// arrived at the point, or transition not yet started), 0.0 = empty (dwell finished / no dwell).</summary>
public sealed class FractionToPieSliceConverter : IValueConverter
{
    private const double Diameter = 14;
    private const double Radius = Diameter / 2;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double fraction)
        {
            return null;
        }

        fraction = Math.Clamp(fraction, 0, 1);
        var center = new Point(Radius, Radius);

        if (fraction <= 0)
        {
            return Geometry.Parse($"M{Radius.ToString(CultureInfo.InvariantCulture)},{Radius.ToString(CultureInfo.InvariantCulture)} Z");
        }

        if (fraction >= 1)
        {
            return new EllipseGeometry(new Rect(0, 0, Diameter, Diameter));
        }

        var angle = fraction * 2 * Math.PI;
        var start = new Point(center.X, center.Y - Radius);
        var end = new Point(center.X + Radius * Math.Sin(angle), center.Y - Radius * Math.Cos(angle));

        var figure = new PathFigure { StartPoint = center, IsClosed = true };
        figure.Segments!.Add(new LineSegment { Point = start });
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(Radius, Radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = fraction > 0.5,
        });

        var geometry = new PathGeometry();
        geometry.Figures!.Add(figure);
        return geometry;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

- [ ] **Step 2: Register the converter as a resource, alongside `KeyPointIsExecuting`**

Find where `KeyPointIsExecuting` is declared as a `StaticResource` key (grep
`KeyPointIsExecutingConverter` in `MainView.axaml` — it's in a `<ResourceDictionary>`/
`<UserControl.Resources>` block near the top of the file). Add a sibling entry in the same block:

```xml
<converters:FractionToPieSliceConverter x:Key="FractionToPieSlice" />
```

(Match the existing `xmlns:converters` prefix already used for `KeyPointIsExecutingConverter` in
that same block — do not introduce a second prefix for the same namespace.)

- [ ] **Step 3: Add the circle to the tile `DataTemplate`**

In the `Panel` that already holds the tile `Button` and the `executing-indicator` `Border` (originally
lines 213-258, shifted by Task 10's insertion), add a sibling `Path` after the `Border`:

```xml
                    <Path IsHitTestVisible="False" Width="14" Height="14"
                          HorizontalAlignment="Right" VerticalAlignment="Top" Margin="0,4,4,0"
                          Fill="{DynamicResource HudAccentBrush}"
                          Data="{Binding ((vm:ProgramViewModel)DataContext).PhysicalPointRemainingFraction, ElementName=KeyPointsList, Converter={StaticResource FractionToPieSlice}}">
                        <Path.IsVisible>
                            <MultiBinding Converter="{StaticResource KeyPointIsExecuting}">
                                <Binding Path="Id" />
                                <Binding Path="((vm:ProgramViewModel)DataContext).PhysicallyExecutingKeyPointId" ElementName="KeyPointsList" />
                            </MultiBinding>
                        </Path.IsVisible>
                    </Path>
```

(Reuses the existing `KeyPointIsExecutingConverter` — it just compares two `Guid?` values, so it
works unchanged against `PhysicallyExecutingKeyPointId` instead of the ack-based
`CurrentlyExecutingKeyPointId`.)

- [ ] **Step 4: Build the Desktop head**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: builds clean.

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Converters/FractionToPieSliceConverter.cs ArctZ/Views/MainView.axaml
git commit -m "feat: shrinking circle shows real per-point transition/dwell progress"
```

---

## Task 12: Live UI verification (required — do not skip)

Per `CLAUDE.md`'s UI testing rule, this is the only acceptable way to confirm the feature works —
unit tests alone (Tasks 1-11) verify the ViewModel/converter/notification logic, not what actually
renders and animates on screen. This exact combination (real position telemetry via
`MockDeviceTransport`, Loop/PingPong repeats) is precisely what broke the two prior time-based
attempts, so do not shortcut this step.

- [ ] **Step 1: Build and run Desktop**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`, then `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj`.

- [ ] **Step 2: Ask the user to exercise the feature**

Have them: connect to the simulated/mock device, build a program with at least 3 key points where
at least one has `DwellSeconds > 0` and at least one uses `EaseMode.EaseInOut`, then run it once in
`Stop` completion mode and once in `PingPong` with `RepeatCount = 2`.

- [ ] **Step 3: Ask through `AskUserQuestion`, one question per element, per `CLAUDE.md`**

Separate questions for: (a) the overall progress bar moves smoothly forward without visible
backward jumps and resets to 0% at the start of each PingPong pass, (b) the shrinking circle
appears on the currently-approached tile and empties as the machine nears it, (c) the circle
refills and drains again during the dwell point specifically, (d) the circle's tile can visibly
differ from the existing bold "executing" highlight border when the buffer runs ahead (a real,
expected divergence per the design, not a bug — confirm the user isn't surprised by it).

- [ ] **Step 4: Android — per `CLAUDE.md`'s Android UI-testing rule**

Ask the user (via `AskUserQuestion`) to build the APK, install it on a device, and confirm
readiness — do not build/deploy it in this environment. Once confirmed, ask them to run a program
and check the foreground notification's progress bar advances in 5% steps without visible jitter,
then ask through `AskUserQuestion` whether it behaved as expected.

- [ ] **Step 5: Fix anything the live test surfaces before considering the feature done**

If either check in Step 3 or Step 4 fails, treat it as a new finding — do not mark this task's
checkbox until both pass. This mirrors exactly how the two prior time-based attempts were caught
and fixed (or, ultimately, reverted) — the live test is the real gate, not the unit tests above it.

---

## Self-Review Notes

- **Spec coverage:** §1 (CompiledStep.Pose) → Task 1. §2 (shared helper) → Task 2. §3
  (PhysicalProgressTracker core) → Tasks 3-4. §4 (per-pass lifecycle) → Task 6 Steps 4/6. §5
  (dwell) → Task 4. §6 (position feed reuse) → Task 6 Step 5. §7 (circle UI) → Task 11. §8 (bar
  UI) → Task 10. §9 (Android) → Tasks 7-9. Testing section → every task's own test step plus
  Task 12 for the mandatory live check.
- **Placeholder scan:** no TBD/TODO; every step has real, complete code.
- **Type consistency:** `PhysicalOverallProgress`, `PhysicalPointRemainingFraction`,
  `PhysicallyExecutingKeyPointId` are defined once in Task 6 and referenced with those exact names
  in Tasks 8, 10, 11. `CompiledStep`'s `Pose`/`IsDwellStep` (Task 1) are referenced with those
  names in Tasks 3-4. `PhysicalProgressTracker`'s public surface (`OverallFraction`,
  `ApproachFraction`, `CurrentSegmentIndex`, `IsDwelling`, `DwellFraction`, `OnPositionUpdated`,
  `OnTimerElapsed`, `Changed`) is introduced in Tasks 3-4 and consumed with matching names only in
  Task 6.
