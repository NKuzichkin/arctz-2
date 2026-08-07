# Program Progress Time Interpolation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the program-execution progress bar move continuously in real time (estimated from distance/feed, self-calibrating against actual ack timing) instead of jumping only when the controller acknowledges a G-code line.

**Architecture:** `TrajectoryCompiler` gains a per-step `EstimatedDurationSeconds` (distance/feed, or exact dwell time). `ProgramViewModel` drives a new `DisplayProgress` property from a periodic timer that interpolates linearly toward each step's known target over that estimated duration, snapping to the ack-confirmed truth (`OverallProgress`) whenever a real ack arrives, and refining a cumulative calibration factor (actual-time / estimated-time so far) applied to every subsequent step's estimate. The XAML `ProgressBar` binds to `DisplayProgress` instead of `OverallProgress`.

**Tech Stack:** Avalonia UI (.NET 10), CommunityToolkit.Mvvm (`[ObservableProperty]`), xUnit tests, existing `IPeriodicTimer`/`ManualPeriodicTimer` test-double infrastructure.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-08-07-program-progress-time-interpolation-design.md`.
- `OverallProgress`/`SegmentProgress`/`CurrentSegmentIndex` (ack-confirmed truth) must not change behavior — existing tests asserting them must keep passing unmodified.
- No wall-clock (`DateTime.UtcNow`/`Stopwatch`) — elapsed time is `ticks * tickInterval`, driven by the existing `IPeriodicTimer` abstraction, so tests stay deterministic via `ManualPeriodicTimer`.
- New `ProgramViewModel` constructor parameters (`IPeriodicTimer`, `TimeSpan`) are wired via an explicit DI factory lambda in `ServiceCollectionExtensions.cs`, matching the existing `MockDeviceTransport` registration pattern — not auto-resolved from the container.
- UI verification for this feature must follow the project's mandatory workflow: build `ArctZ.Desktop`, run it, ask the user to exercise the feature, then confirm each changed behavior individually via `AskUserQuestion` (see CLAUDE.md "Тестирование UI"). Do not claim the UI change works without this.

---

### Task 1: Estimated step duration in `TrajectoryCompiler`

**Files:**
- Modify: `ArctZ/Services/Program/CompiledStep.cs`
- Modify: `ArctZ/Services/Program/TrajectoryCompiler.cs`
- Test: `ArctZ.Tests/Services/Program/TrajectoryCompilerTests.cs`

**Interfaces:**
- Consumes: nothing new — `KeyPoint.FeedRateUnitsPerMin`, `KeyPoint.DwellSeconds`, `MachinePose` (all pre-existing).
- Produces: `CompiledStep.EstimatedDurationSeconds` (double, seconds) — Task 2 reads this field from every step in the compiled list.

- [ ] **Step 1: Write the failing tests**

Add to `ArctZ.Tests/Services/Program/TrajectoryCompilerTests.cs` (inside the existing `TrajectoryCompilerTests` class, after `Compile_NoEase_ProducesSingleG1StepAtFullProgress`):

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~TrajectoryCompilerTests"`
Expected: compile error — `CompiledStep` has no `EstimatedDurationSeconds` member.

- [ ] **Step 3: Add `EstimatedDurationSeconds` to `CompiledStep`**

Replace the full contents of `ArctZ/Services/Program/CompiledStep.cs`:

```csharp
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Program;

public sealed record CompiledStep(int SegmentIndex, IDeviceCommand Command, double SegmentProgress, double EstimatedDurationSeconds);
```

- [ ] **Step 4: Compute the duration at each of the three call sites in `TrajectoryCompiler`**

Replace the full contents of `ArctZ/Services/Program/TrajectoryCompiler.cs`:

```csharp
using System;
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
                var command = MoveCommand(segment.To.Pose, segment.To.FeedRateUnitsPerMin);
                var duration = EstimateDuration(Distance(segment.From.Pose, segment.To.Pose), segment.To.FeedRateUnitsPerMin);
                steps.Add(new CompiledStep(segment.Index, command, SegmentProgress: 1.0, EstimatedDurationSeconds: duration));
            }

            if (segment.To.StopsAtWaypoint)
            {
                var dwellLine = $"G4 P{Format(segment.To.DwellSeconds)}";
                steps.Add(new CompiledStep(segment.Index, new GCodeLineCommand(dwellLine), SegmentProgress: 1.0, EstimatedDurationSeconds: segment.To.DwellSeconds));
            }
        }

        return steps;
    }

    private static void CompileEased(ProgramSegment segment, List<CompiledStep> steps)
    {
        var previousPose = segment.From.Pose;

        for (var i = 1; i <= EaseSubdivisions; i++)
        {
            var t = (double)i / EaseSubdivisions;
            var pose = Interpolate(segment.From.Pose, segment.To.Pose, t);
            var feed = FeedMultiplier(t) * segment.To.FeedRateUnitsPerMin;
            var duration = EstimateDuration(Distance(previousPose, pose), feed);
            steps.Add(new CompiledStep(segment.Index, MoveCommand(pose, feed), SegmentProgress: t, EstimatedDurationSeconds: duration));
            previousPose = pose;
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

    /// <summary>Euclidean distance across all 4 axes — a UI-facing time estimate, not a controller-accurate one.</summary>
    private static double Distance(MachinePose a, MachinePose b) => Math.Sqrt(
        Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2) + Math.Pow(b.Z - a.Z, 2) + Math.Pow(b.A - a.A, 2));

    private static double EstimateDuration(double distance, double feedUnitsPerMin) =>
        feedUnitsPerMin > 0 ? distance / feedUnitsPerMin * 60 : 0;

    private static GCodeLineCommand MoveCommand(MachinePose pose, double feed) => new(
        $"G1 X{Format(pose.X)} Y{Format(pose.Y)} Z{Format(pose.Z)} A{Format(pose.A)} F{Format(feed)}");

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~TrajectoryCompilerTests"`
Expected: PASS (all `TrajectoryCompilerTests`, including the 4 new ones).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Services/Program/CompiledStep.cs ArctZ/Services/Program/TrajectoryCompiler.cs ArctZ.Tests/Services/Program/TrajectoryCompilerTests.cs
git commit -m "feat: estimate per-step execution duration in TrajectoryCompiler"
```

---

### Task 2: `DisplayProgress` animated property driven by a periodic timer

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs`
- Modify: `ArctZ/Services/Device/ServiceCollectionExtensions.cs`
- Test: `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs`
- Test: `ArctZ.Tests/ViewModels/ProgramViewModelStatusLabelTests.cs` (constructor call site only)
- Test: `ArctZ.Tests/ViewModels/ProgramViewModelAuthoringTests.cs` (constructor call site only)
- Test: `ArctZ.Tests/ViewModels/ProgramViewModelSideMenuTests.cs` (constructor call site only)

**Interfaces:**
- Consumes: `CompiledStep.EstimatedDurationSeconds` (Task 1), `IPeriodicTimer` (`event Action? Elapsed`, `void Start(TimeSpan)`, `void Stop()` — `ArctZ/Services/Device/IPeriodicTimer.cs`), `ManualPeriodicTimer` test double (`ArctZ.Tests/Services/Device/ManualPeriodicTimer.cs`, has `RaiseElapsed()`).
- Produces: `ProgramViewModel.DisplayProgress` (double, `[ObservableProperty]`) — Task 4 binds `ProgressBar.Value` to it. New constructor shape `ProgramViewModel(ConnectionViewModel, IProgramStorage, ITrajectoryCompiler, IPeriodicTimer, TimeSpan)` — Task 3 adds calibration on top of the same constructor/fields, no further signature change.

- [ ] **Step 1: Write the failing test**

In `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs`, replace the existing `CreateViewModel` helper (lines 14-20) with two overloads so all 15 existing call sites keep compiling unchanged:

```csharp
private static ProgramViewModel CreateViewModel(out FakeDeviceTransport transport) =>
    CreateViewModel(out transport, out _);

private static ProgramViewModel CreateViewModel(out FakeDeviceTransport transport, out ManualPeriodicTimer progressTimer)
{
    transport = new FakeDeviceTransport();
    var storage = new FakeProgramStorage();
    var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default));
    progressTimer = new ManualPeriodicTimer();
    return new ProgramViewModel(connection, storage, new TrajectoryCompiler(), progressTimer, TimeSpan.FromMilliseconds(100));
}
```

Add a new test method after `PlayAsync_DispatchesAllStepsBeforeAwaitingAcks_ThenTracksProgress`:

```csharp
[Fact]
public async Task DisplayProgress_AnimatesTowardStepTarget_BetweenDispatchAndAck_ThenSnapsOnAck()
{
    var vm = CreateViewModel(out var transport, out var progressTimer);
    await vm.Connection.ConnectCommand.Execute();
    SeedTwoSegmentProgram(vm, transport);

    var playTask = vm.PlayCommand.ExecuteAsync(null);
    Assert.Equal(0, vm.DisplayProgress);

    // Segment distance 10 @ feed 500 => EstimatedDurationSeconds = 1.2s = 12 ticks @ 100ms.
    progressTimer.RaiseElapsed();
    progressTimer.RaiseElapsed();
    progressTimer.RaiseElapsed();

    // 3 of 12 ticks toward the first segment's target (0.5), starting from 0.
    Assert.Equal(0.125, vm.DisplayProgress, 3);
    Assert.True(vm.DisplayProgress < 0.5);

    transport.SimulateReceivedLine("ok");
    await WaitUntilAsync(() => vm.CurrentSegmentIndex == 0, TimeSpan.FromSeconds(1));

    Assert.Equal(0.5, vm.DisplayProgress);

    transport.SimulateReceivedLine("ok");
    await playTask;

    Assert.Equal(1.0, vm.DisplayProgress);
}
```

Add `using ArctZ.Tests.Services.Device;` if not already present (it already is — `ManualPeriodicTimer` lives in that namespace, same as `FakeDeviceTransport`).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: compile error — no `ProgramViewModel` constructor takes `(ConnectionViewModel, IProgramStorage, ITrajectoryCompiler, IPeriodicTimer, TimeSpan)`, and no `DisplayProgress` member.

- [ ] **Step 3: Add the animation fields, `DisplayProgress` property, and constructor parameters**

In `ArctZ/ViewModels/ProgramViewModel.cs`, replace the field block and constructor (current lines 19-67):

```csharp
    private readonly IProgramStorage _storage;
    private readonly ITrajectoryCompiler _compiler;
    private readonly IPeriodicTimer _progressTimer;
    private readonly TimeSpan _progressTickInterval;
    private JoystickAxisInput _leftInput;
    private JoystickAxisInput _rightInput;
    private bool _leftActive;
    private bool _rightActive;

    private double _animStartProgress;
    private double _animTargetProgress;
    private double _animDurationSeconds;
    private double _animElapsedSeconds;

    public ConnectionViewModel Connection { get; }

    [ObservableProperty]
    private Guid? _programId;

    [ObservableProperty]
    private string _programName = "Новая программа";

    [ObservableProperty]
    private KeyPoint? _selectedKeyPoint;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingKeyPoint))]
    private KeyPointEditorViewModel? _keyPointEditor;

    public bool IsEditingKeyPoint => KeyPointEditor is not null;

    public ObservableCollection<KeyPoint> KeyPoints { get; } = new();

    public ObservableCollection<ProgramLibraryItem> Library { get; } = new();

    [ObservableProperty]
    private bool _isLibraryOpen;

    [ObservableProperty]
    private bool _isSideMenuOpen;

    /// <summary>Time-interpolated view of OverallProgress — moves continuously between
    /// ack-confirmed checkpoints instead of jumping only when a G-code line is acknowledged.</summary>
    [ObservableProperty]
    private double _displayProgress;

    public ProgramViewModel(ConnectionViewModel connection, IProgramStorage storage, ITrajectoryCompiler compiler, IPeriodicTimer progressTimer, TimeSpan progressTickInterval)
    {
        Connection = connection;
        _storage = storage;
        _compiler = compiler;
        _progressTimer = progressTimer;
        _progressTickInterval = progressTickInterval;
        _progressTimer.Elapsed += OnProgressTick;
        Connection.PropertyChanged += OnConnectionPropertyChanged;

        // Add/Remove/Move/Reset all need to re-evaluate whether a given point
        // is still first/last, which MoveKeyPointUp/Down's CanExecute depends on.
        KeyPoints.CollectionChanged += (_, _) =>
        {
            MoveKeyPointUpCommand.NotifyCanExecuteChanged();
            MoveKeyPointDownCommand.NotifyCanExecuteChanged();
        };
    }

    private void OnProgressTick()
    {
        _animElapsedSeconds += _progressTickInterval.TotalSeconds;
        var frac = _animDurationSeconds <= 0 ? 1.0 : Math.Clamp(_animElapsedSeconds / _animDurationSeconds, 0, 1);
        DisplayProgress = _animStartProgress + (_animTargetProgress - _animStartProgress) * frac;
    }

    private void BeginStepAnimation(double startProgress, double targetProgress, double durationSeconds)
    {
        _animStartProgress = startProgress;
        _animTargetProgress = targetProgress;
        _animDurationSeconds = durationSeconds;
        _animElapsedSeconds = 0;
    }
```

This requires `using ArctZ.Services.Device;` to already be present for `IPeriodicTimer` — it is (line 9 already imports that namespace for other types).

- [ ] **Step 4: Start/stop the timer centrally in `OnPlaybackStateChanged`, reset `DisplayProgress`, and drive the animation in `PlayAsync`/`StopAsync`**

In `ArctZ/ViewModels/ProgramViewModel.cs`, find `partial void OnPlaybackStateChanged(PlaybackState value)` (current line 521) and insert at the very top of the method body, before `Connection.IsPlaybackLocked = IsProgramLocked;`:

```csharp
        if (value == PlaybackState.Running)
        {
            _progressTimer.Start(_progressTickInterval);
        }
        else
        {
            _progressTimer.Stop();
        }

```

In `PlayAsync` (current lines 718-722), replace:

```csharp
        PlaybackState = PlaybackState.Running;
        CurrentSegmentIndex = null;
        SegmentProgress = 0;
        FaultedAtSegmentIndex = null;
        TotalSegments = Math.Max(0, KeyPoints.Count - 1);
```

with:

```csharp
        PlaybackState = PlaybackState.Running;
        CurrentSegmentIndex = null;
        SegmentProgress = 0;
        DisplayProgress = 0;
        FaultedAtSegmentIndex = null;
        TotalSegments = Math.Max(0, KeyPoints.Count - 1);
```

Then replace the dispatch loop (current lines 731-749):

```csharp
        foreach (var (step, completion) in dispatched)
        {
            var result = await completion;

            if (PlaybackState == PlaybackState.Stopped)
            {
                return;
            }

            if (result.Outcome != CommandOutcome.Acknowledged)
            {
                PlaybackState = PlaybackState.Faulted;
                FaultedAtSegmentIndex = step.SegmentIndex;
                return;
            }

            CurrentSegmentIndex = step.SegmentIndex;
            SegmentProgress = step.SegmentProgress;
        }
```

with:

```csharp
        var previousDisplayProgress = 0.0;

        foreach (var (step, completion) in dispatched)
        {
            var targetProgress = TotalSegments > 0
                ? Math.Clamp((step.SegmentIndex + step.SegmentProgress) / TotalSegments, 0, 1)
                : 0;
            BeginStepAnimation(previousDisplayProgress, targetProgress, step.EstimatedDurationSeconds);

            var result = await completion;

            if (PlaybackState == PlaybackState.Stopped)
            {
                return;
            }

            if (result.Outcome != CommandOutcome.Acknowledged)
            {
                PlaybackState = PlaybackState.Faulted;
                FaultedAtSegmentIndex = step.SegmentIndex;
                return;
            }

            CurrentSegmentIndex = step.SegmentIndex;
            SegmentProgress = step.SegmentProgress;
            DisplayProgress = targetProgress;
            previousDisplayProgress = targetProgress;
        }
```

(Task 3 will insert the calibration bookkeeping between the `Acknowledged` check and `CurrentSegmentIndex = step.SegmentIndex;` — no need to leave a placeholder, Task 3 edits this same block again.)

In `StopAsync` (current lines 769-782), replace:

```csharp
        PlaybackState = PlaybackState.Stopped;
        CurrentSegmentIndex = null;
        SegmentProgress = 0;
        Connection.Session?.AbortPendingCommands();
        return Connection.Session?.FeedHoldAsync() ?? Task.CompletedTask;
```

with:

```csharp
        PlaybackState = PlaybackState.Stopped;
        CurrentSegmentIndex = null;
        SegmentProgress = 0;
        DisplayProgress = 0;
        Connection.Session?.AbortPendingCommands();
        return Connection.Session?.FeedHoldAsync() ?? Task.CompletedTask;
```

- [ ] **Step 5: Fix the other three test files that also construct `ProgramViewModel` directly**

Three more test files call the old 3-argument constructor and must be updated so the build stays green — none of them need to control the timer themselves, so just append a fresh `ManualPeriodicTimer` and the same 100ms interval:

In `ArctZ.Tests/ViewModels/ProgramViewModelStatusLabelTests.cs` (line 18), replace:

```csharp
        return new ProgramViewModel(connection, storage, new TrajectoryCompiler());
```

with:

```csharp
        return new ProgramViewModel(connection, storage, new TrajectoryCompiler(), new ManualPeriodicTimer(), TimeSpan.FromMilliseconds(100));
```

In `ArctZ.Tests/ViewModels/ProgramViewModelAuthoringTests.cs` (line 20), make the identical replacement.

In `ArctZ.Tests/ViewModels/ProgramViewModelSideMenuTests.cs` (line 16), make the identical replacement, and also add `using System;` at the top of the file (it currently has no `using System;`, and `TimeSpan` needs it) — insert as the new first line, before `using ArctZ.Services.Device;`.

- [ ] **Step 6: Update the DI registration**

In `ArctZ/Services/Device/ServiceCollectionExtensions.cs`, replace:

```csharp
        services.AddSingleton<ProgramViewModel>();
```

with:

```csharp
        services.AddSingleton<ProgramViewModel>(sp => new ProgramViewModel(
            sp.GetRequiredService<ConnectionViewModel>(),
            sp.GetRequiredService<IProgramStorage>(),
            sp.GetRequiredService<ITrajectoryCompiler>(),
            new SystemPeriodicTimer(),
            TimeSpan.FromMilliseconds(100)));
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: PASS (all existing tests still pass unmodified via the single-out-parameter `CreateViewModel` overload; the new `DisplayProgress_...` test passes).

Then run the full suite once more:

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS — this also exercises the three files fixed in Step 5.

- [ ] **Step 8: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ/Services/Device/ServiceCollectionExtensions.cs ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs ArctZ.Tests/ViewModels/ProgramViewModelStatusLabelTests.cs ArctZ.Tests/ViewModels/ProgramViewModelAuthoringTests.cs ArctZ.Tests/ViewModels/ProgramViewModelSideMenuTests.cs
git commit -m "feat: animate program progress toward each step's estimated duration"
```

---

### Task 3: Self-calibrating duration estimate from actual ack timing

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs`
- Test: `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs`

**Interfaces:**
- Consumes: `_animElapsedSeconds` (Task 2, the raw unclamped tick accumulator for the step currently animating), `BeginStepAnimation` (Task 2).
- Produces: nothing new consumed elsewhere — purely refines the `durationSeconds` argument already passed into `BeginStepAnimation` inside `PlayAsync`.

- [ ] **Step 1: Write the failing test**

Add to `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs`, after `DisplayProgress_AnimatesTowardStepTarget_BetweenDispatchAndAck_ThenSnapsOnAck`:

```csharp
[Fact]
public async Task DisplayProgress_CalibratesFutureEstimates_FromActualFirstStepDuration()
{
    var vm = CreateViewModel(out var transport, out var progressTimer);
    await vm.Connection.ConnectCommand.Execute();
    SeedTwoSegmentProgram(vm, transport);

    var playTask = vm.PlayCommand.ExecuteAsync(null);

    // First segment's estimate is 12 ticks (1.2s @ 100ms). Let it actually take 24 ticks
    // (2x slower) before the ack arrives.
    for (var i = 0; i < 24; i++)
    {
        progressTimer.RaiseElapsed();
    }

    transport.SimulateReceivedLine("ok");
    await WaitUntilAsync(() => vm.CurrentSegmentIndex == 0, TimeSpan.FromSeconds(1));
    Assert.Equal(0.5, vm.DisplayProgress);

    // Second segment's raw estimate is also 12 ticks, but the calibration factor from the
    // first step (actual 2.4s / estimated 1.2s = 2.0) doubles it to 24 ticks. After 12 ticks
    // it should be roughly halfway from 0.5 to 1.0, not already there.
    for (var i = 0; i < 12; i++)
    {
        progressTimer.RaiseElapsed();
    }

    Assert.Equal(0.75, vm.DisplayProgress, 3);

    transport.SimulateReceivedLine("ok");
    await playTask;

    Assert.Equal(1.0, vm.DisplayProgress);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~DisplayProgress_CalibratesFutureEstimates"`
Expected: FAIL — without calibration, the second segment's estimate stays at 12 ticks, so after 12 ticks `DisplayProgress` is already `1.0`, not `0.75`.

- [ ] **Step 3: Add the calibration fields and wire them into the dispatch loop**

In `ArctZ/ViewModels/ProgramViewModel.cs`, find the animation fields added in Task 2:

```csharp
    private double _animStartProgress;
    private double _animTargetProgress;
    private double _animDurationSeconds;
    private double _animElapsedSeconds;
```

and add three more fields directly below `_animElapsedSeconds;`:

```csharp
    private double _cumulativeEstimatedSeconds;
    private double _cumulativeActualSeconds;
    private double _durationCalibrationFactor = 1.0;
```

In `PlayAsync`, extend the reset block (the one that now also sets `DisplayProgress = 0;` from Task 2) to also reset the calibration state:

```csharp
        PlaybackState = PlaybackState.Running;
        CurrentSegmentIndex = null;
        SegmentProgress = 0;
        DisplayProgress = 0;
        FaultedAtSegmentIndex = null;
        TotalSegments = Math.Max(0, KeyPoints.Count - 1);
        _cumulativeEstimatedSeconds = 0;
        _cumulativeActualSeconds = 0;
        _durationCalibrationFactor = 1.0;
```

Then update the dispatch loop to apply the calibration factor before starting each step's animation, and refine it after each ack:

```csharp
        var previousDisplayProgress = 0.0;

        foreach (var (step, completion) in dispatched)
        {
            var targetProgress = TotalSegments > 0
                ? Math.Clamp((step.SegmentIndex + step.SegmentProgress) / TotalSegments, 0, 1)
                : 0;
            var correctedDuration = step.EstimatedDurationSeconds * _durationCalibrationFactor;
            BeginStepAnimation(previousDisplayProgress, targetProgress, correctedDuration);

            var result = await completion;

            if (PlaybackState == PlaybackState.Stopped)
            {
                return;
            }

            if (result.Outcome != CommandOutcome.Acknowledged)
            {
                PlaybackState = PlaybackState.Faulted;
                FaultedAtSegmentIndex = step.SegmentIndex;
                return;
            }

            _cumulativeEstimatedSeconds += step.EstimatedDurationSeconds;
            _cumulativeActualSeconds += _animElapsedSeconds;
            _durationCalibrationFactor = _cumulativeEstimatedSeconds > 0
                ? _cumulativeActualSeconds / _cumulativeEstimatedSeconds
                : 1.0;

            CurrentSegmentIndex = step.SegmentIndex;
            SegmentProgress = step.SegmentProgress;
            DisplayProgress = targetProgress;
            previousDisplayProgress = targetProgress;
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: PASS (all tests, including both new `DisplayProgress_...` tests).

Run the full suite once more:

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs
git commit -m "feat: calibrate progress-bar duration estimates against actual ack timing"
```

---

### Task 4: Bind the progress bar to `DisplayProgress` and verify in the running app

**Files:**
- Modify: `ArctZ/Views/MainView.axaml`

**Interfaces:**
- Consumes: `ProgramViewModel.DisplayProgress` (Task 2).
- Produces: nothing consumed by later tasks — this is the last task.

- [ ] **Step 1: Switch the binding**

In `ArctZ/Views/MainView.axaml`, find (current lines 208-209):

```xml
                            <ProgressBar Margin="0,12,0,0" IsVisible="{Binding IsProgramLocked}"
                                         HorizontalAlignment="Stretch" Minimum="0" Maximum="1" Value="{Binding OverallProgress}">
```

Replace with:

```xml
                            <ProgressBar Margin="0,12,0,0" IsVisible="{Binding IsProgramLocked}"
                                         HorizontalAlignment="Stretch" Minimum="0" Maximum="1" Value="{Binding DisplayProgress}">
```

Leave the `ProgressBar.Transitions` block (the existing `DoubleTransition Duration="0:0:0.3"`) untouched — it keeps smoothing each `DisplayProgress` tick update on top of the timer-driven interpolation.

- [ ] **Step 2: Build the desktop head**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: build succeeds, no errors.

- [ ] **Step 3: Run the desktop app**

Run: `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj`

This must be a real running process (not just a build) per the project's UI-testing rule — the app needs to actually run so the user can create/play a program and watch the bar.

- [ ] **Step 4: Ask the user to exercise the feature, then confirm via `AskUserQuestion`**

Ask the user to: connect (or use the mock/simulated device if that's the configured transport), create a program with at least 2-3 key points (mix of `EaseInOut` and plain moves if convenient), and press Play.

Then ask, one question per changed behavior (per CLAUDE.md's "Тестирование UI" — one question per behavior, not a single "looks fine?"):
1. Does the progress bar move continuously while a segment is executing, instead of sitting still and then jumping?
2. Does the bar ever visibly jump backward?
3. Does the bar reach 100% by the time the program reports Completed?

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Views/MainView.axaml
git commit -m "feat: bind progress bar to the time-interpolated DisplayProgress"
```

> **Revision note (added after the first live-UI pass):** Steps 1-4 above ran
> once and Step 4 failed both checks — the bar still jumped, and it visibly
> jumped backward at the point1→point2 transition. Root cause traced to
> Tasks 2-3's core premise (see the spec's "Ревизия" section, 2026-08-07):
> `MockDeviceTransport`/real FluidNC ack a G-code line on buffer-dequeue, not
> on physical motion completion, so "time to ack" does not approximate motion
> time — animating/calibrating against it cannot produce smooth motion, and
> the calibration factor actively converges toward 0. **Task 5 below replaces
> the ack-driven animation from Tasks 2-3 with a fully ack-independent visual
> timeline, and must land before repeating Steps 2-4 of this task.** The XAML
> binding above (`Value="{Binding DisplayProgress}"`) does not change — only
> how `DisplayProgress` is computed changes.

---

### Task 5: Decouple `DisplayProgress` from ack timing; remove calibration; fix the timer/UI-thread race

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs`
- Modify: `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs`

**Interfaces:**
- Consumes: `CompiledStep.EstimatedDurationSeconds`/`.SegmentIndex`/`.SegmentProgress` (Task 1). `IPeriodicTimer`/`ManualPeriodicTimer` (Task 2 — constructor shape `ProgramViewModel(ConnectionViewModel, IProgramStorage, ITrajectoryCompiler, IPeriodicTimer, TimeSpan)` is unchanged, no test file outside `ProgramViewModelPlaybackTests.cs` needs touching).
- Produces: `DisplayProgress` keeps its name and 0..1 meaning — Task 4's XAML binding does not change. `BeginStepAnimation` (Task 2) and `_cumulativeEstimatedSeconds`/`_cumulativeActualSeconds`/`_durationCalibrationFactor` (Task 3) are deleted — nothing else in the codebase references them (grep confirms `BeginStepAnimation` and `_durationCalibrationFactor` are private to `ProgramViewModel.cs`).

- [ ] **Step 1: Write the failing tests**

In `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs`, delete these two existing test methods entirely — they assert the ack-driven snapping and calibration behavior this task removes:
- `DisplayProgress_AnimatesTowardStepTarget_BetweenDispatchAndAck_ThenSnapsOnAck`
- `DisplayProgress_CalibratesFutureEstimates_FromActualFirstStepDuration`

Add these two in their place:

```csharp
[Fact]
public async Task DisplayProgress_ForcesTo100Percent_WhenCompleted_EvenIfAcksArrivedBeforeAnyTick()
{
    var vm = CreateViewModel(out var transport, out var progressTimer);
    await vm.Connection.ConnectCommand.Execute();
    SeedTwoSegmentProgram(vm, transport);

    var playTask = vm.PlayCommand.ExecuteAsync(null);
    Assert.Equal(0, vm.DisplayProgress);

    // Ack both segments immediately, before any progressTimer tick — real ack timing
    // reflects buffer-drain speed, not motion time, so this is the common case, not an
    // edge case. DisplayProgress must not have anywhere else to get a value from except
    // the explicit Completed-forced snap.
    transport.SimulateReceivedLine("ok");
    transport.SimulateReceivedLine("ok");
    await playTask;

    Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
    Assert.Equal(1.0, vm.DisplayProgress);
}

[Fact]
public async Task DisplayProgress_FollowsItsOwnTimeline_UnaffectedByAnEarlyAck()
{
    var vm = CreateViewModel(out var transport, out var progressTimer);
    await vm.Connection.ConnectCommand.Execute();
    SeedTwoSegmentProgram(vm, transport);

    var playTask = vm.PlayCommand.ExecuteAsync(null);

    // First segment's estimate is 1.2s = 12 ticks @ 100ms. Ack it after only 3 ticks.
    progressTimer.RaiseElapsed();
    progressTimer.RaiseElapsed();
    progressTimer.RaiseElapsed();

    transport.SimulateReceivedLine("ok");
    await WaitUntilAsync(() => vm.CurrentSegmentIndex == 0, TimeSpan.FromSeconds(1));

    // OverallProgress (ack-confirmed truth) jumps to 0.5 immediately — DisplayProgress must
    // NOT snap to it, and must keep following the same first-segment animation.
    Assert.Equal(0.5, vm.OverallProgress);
    Assert.Equal(0.125, vm.DisplayProgress, 3); // 3/12 ticks toward the first segment's 0.5 target
    Assert.True(vm.DisplayProgress < 0.5);

    // A 4th tick continues the SAME first-segment animation — the ack that already arrived
    // changes nothing about its pace.
    progressTimer.RaiseElapsed();
    Assert.Equal(4.0 / 12 * 0.5, vm.DisplayProgress, 3);

    transport.SimulateReceivedLine("ok");
    await playTask;

    Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
    Assert.Equal(1.0, vm.DisplayProgress);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: FAIL — with the current (Task 2/3) implementation, `DisplayProgress` snaps to `0.5` the instant the ack arrives (3 ticks in), so `Assert.Equal(0.125, vm.DisplayProgress, 3)` fails, and the calibration factor changes the second segment's pacing, so the `4.0/12*0.5` assertion also fails.

- [ ] **Step 3: Replace the field block, constructor body, and `OnProgressTick`/`BeginStepAnimation`**

In `ArctZ/ViewModels/ProgramViewModel.cs`, add `using System.Collections.Generic;` to the top of the file (needed for the `IReadOnlyList<CompiledStep>` field below) — insert it after `using System;` (currently line 1), before `using System.ComponentModel;`.

Replace the field block (currently lines 19-34, from `private readonly IProgramStorage _storage;` through `private double _durationCalibrationFactor = 1.0;`):

```csharp
    private readonly IProgramStorage _storage;
    private readonly ITrajectoryCompiler _compiler;
    private readonly IPeriodicTimer _progressTimer;
    private readonly TimeSpan _progressTickInterval;
    private JoystickAxisInput _leftInput;
    private JoystickAxisInput _rightInput;
    private bool _leftActive;
    private bool _rightActive;

    // Guards the fields below AND DisplayProgress: OnProgressTick fires on a raw
    // System.Threading.Timer callback (a ThreadPool thread, unsynchronized with the UI
    // thread), while OnPlaybackStateChanged's Completed-forced snap runs on whatever thread
    // PlayAsync's `await` resumes on. Without this lock a stale tick — already queued to the
    // ThreadPool before Completed fired, computed from now-superseded field values — can
    // execute after the Completed snap and overwrite DisplayProgress with a lower value: a
    // visible backward jump. The lock removes the race outright rather than narrowing it.
    private readonly object _animLock = new();
    private IReadOnlyList<CompiledStep> _visualSteps = Array.Empty<CompiledStep>();
    private int _visualStepIndex;
    private double _animStartProgress;
    private double _animTargetProgress;
    private double _animDurationSeconds;
    private double _animElapsedSeconds;
```

Replace the constructor body's timer subscription line and the `OnProgressTick`/`BeginStepAnimation` methods that follow the constructor (currently lines 68-100, from `public ProgramViewModel(...)` through the closing `}` of `BeginStepAnimation`):

```csharp
    public ProgramViewModel(ConnectionViewModel connection, IProgramStorage storage, ITrajectoryCompiler compiler, IPeriodicTimer progressTimer, TimeSpan progressTickInterval)
    {
        Connection = connection;
        _storage = storage;
        _compiler = compiler;
        _progressTimer = progressTimer;
        _progressTickInterval = progressTickInterval;
        _progressTimer.Elapsed += OnProgressTick;
        Connection.PropertyChanged += OnConnectionPropertyChanged;

        // Add/Remove/Move/Reset all need to re-evaluate whether a given point
        // is still first/last, which MoveKeyPointUp/Down's CanExecute depends on.
        KeyPoints.CollectionChanged += (_, _) =>
        {
            MoveKeyPointUpCommand.NotifyCanExecuteChanged();
            MoveKeyPointDownCommand.NotifyCanExecuteChanged();
        };
    }

    /// <summary>Advances DisplayProgress along the precomputed visual timeline by however much
    /// real time has passed — entirely independent of ack arrival (see the class-level note on
    /// _animLock for why acks cannot drive this: ack timing reflects buffer-drain speed, not
    /// physical motion time).</summary>
    private void OnProgressTick()
    {
        lock (_animLock)
        {
            _animElapsedSeconds += _progressTickInterval.TotalSeconds;

            while (_animElapsedSeconds >= _animDurationSeconds && _visualStepIndex < _visualSteps.Count - 1)
            {
                _animElapsedSeconds -= _animDurationSeconds;
                _visualStepIndex++;
                _animStartProgress = _animTargetProgress;
                _animTargetProgress = StepOverallProgress(_visualSteps[_visualStepIndex]);
                _animDurationSeconds = _visualSteps[_visualStepIndex].EstimatedDurationSeconds;
            }

            var frac = _animDurationSeconds <= 0 ? 1.0 : Math.Clamp(_animElapsedSeconds / _animDurationSeconds, 0, 1);
            DisplayProgress = _animStartProgress + (_animTargetProgress - _animStartProgress) * frac;
        }
    }

    private double StepOverallProgress(CompiledStep step) => TotalSegments > 0
        ? Math.Clamp((step.SegmentIndex + step.SegmentProgress) / TotalSegments, 0, 1)
        : 0;
```

- [ ] **Step 4: Force `DisplayProgress` to 1.0 on Completed, seed the visual timeline in `PlayAsync`, revert the dispatch loop, and stop touching animation state in `StopAsync`**

In `ArctZ/ViewModels/ProgramViewModel.cs`, find `partial void OnPlaybackStateChanged(PlaybackState value)` and insert this immediately after the existing `if (value == PlaybackState.Running) { _progressTimer.Start(...); } else { _progressTimer.Stop(); }` block, before `Connection.IsPlaybackLocked = IsProgramLocked;`:

```csharp

        if (value == PlaybackState.Completed)
        {
            lock (_animLock)
            {
                DisplayProgress = 1.0;
            }
        }
```

In `PlayAsync`, replace the reset block (currently the lines from `PlaybackState = PlaybackState.Running;` through `_durationCalibrationFactor = 1.0;`):

```csharp
        PlaybackState = PlaybackState.Running;
        CurrentSegmentIndex = null;
        SegmentProgress = 0;
        DisplayProgress = 0;
        FaultedAtSegmentIndex = null;
        TotalSegments = Math.Max(0, KeyPoints.Count - 1);

        lock (_animLock)
        {
            _visualSteps = steps;
            _visualStepIndex = 0;
            _animStartProgress = 0;
            _animTargetProgress = StepOverallProgress(steps[0]);
            _animDurationSeconds = steps[0].EstimatedDurationSeconds;
            _animElapsedSeconds = 0;
        }
```

Then replace the dispatch loop (currently from `var previousDisplayProgress = 0.0;` through the loop's closing `}`) with the ack loop reverted to touching only ack-confirmed state:

```csharp
        foreach (var (step, completion) in dispatched)
        {
            var result = await completion;

            if (PlaybackState == PlaybackState.Stopped)
            {
                return;
            }

            if (result.Outcome != CommandOutcome.Acknowledged)
            {
                PlaybackState = PlaybackState.Faulted;
                FaultedAtSegmentIndex = step.SegmentIndex;
                return;
            }

            CurrentSegmentIndex = step.SegmentIndex;
            SegmentProgress = step.SegmentProgress;
        }
```

In `StopAsync`, replace `DisplayProgress = 0;` with:

```csharp
            lock (_animLock)
            {
                DisplayProgress = 0;
            }
```

(keep it positioned exactly where the current unguarded `DisplayProgress = 0;` line sits, between `SegmentProgress = 0;` and `Connection.Session?.AbortPendingCommands();`).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: PASS — all tests in the file, including the two new ones.

Then run the full suite:

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs
git commit -m "fix: decouple progress-bar animation from ack timing, drop calibration, fix timer race"
```

---

### Task 4 (resume): re-run the live UI verification

After Task 5 lands, repeat Task 4's Steps 2-5 (build `ArctZ.Desktop`, run it, ask the user the same three questions, commit the XAML binding change alongside Task 5's fix if not already committed). Do not mark Task 4 complete until both "moves continuously" and "never jumps backward" get a yes.
