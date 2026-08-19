# Прогресс выполнения программы: временная метрика (Ревизия 2) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Заменить дистанционную метрику прогресса программы (`PhysicalProgressTracker`,
реализован и смёржен ранее тем же днём) на временную: прогресс на точке — доля прошедшего времени
против расчётного (переход + стоянка), общий прогресс — доля прошедшего астрономического времени
прохода против суммы расчётного времени всех точек, плюс живой warning-индикатор при отставании
более чем на 15%.

**Architecture:** `PhysicalProgressTracker` переименовывается в `TimeProgressTracker` и переписывается:
геометрическая проекция реальной позиции на ломаную остаётся (единственный источник истины для
«какая точка сейчас физически активна»), но все публичные метрики прогресса становятся функцией
переданного времени (`DateTimeOffset`), а не пройденной дистанции. `EstimatedDurationSeconds` —
уже существующее поле `CompiledStep` — становится источником расчётного времени, новых полей на
`CompiledStep`/`KeyPoint` не требуется. `ProgramViewModel` пробрасывает те же имена свойств, что уже
привязаны в `MainView.axaml` (минимизация изменений XAML), плюс одно новое —
`PhysicalPointHasTimeWarning`. Единый таймер (переиспользуется существующий `_progressTimer`,
интервал меняется со 100мс на 200мс) обслуживает и throttled push в UI, и рост времени во время
dwell, когда позиция не меняется.

**Tech Stack:** .NET 10, Avalonia UI, CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-19-physical-program-progress-design.md` (Ревизия 2 —
файл был переписан поверх Ревизии 1 после живого UI-тестирования и отказа пользователя от
дистанционной метрики; коммит `466304e`).

## Global Constraints

- Единый таймер тика — **200мс** (`TimeSpan.FromMilliseconds(200)`), заменяет прежний 100мс
  dwell-таймер. Один и тот же `IPeriodicTimer`/`_progressTimer`, уже существующий в
  `ProgramViewModel` — интервал меняется, второй таймер не заводится.
- Warning-threshold — **±15%** (`elapsedInSegment > estimatedForSegment * 1.15`), зафиксирован в
  коде, не выносится в настройки в этой итерации.
- Время передаётся везде явным параметром (`DateTimeOffset`, тот же тип, что уже использует
  существующее поле `ProgramViewModel._now` — `Func<DateTimeOffset>`) — никогда не читается из
  `DateTimeOffset.Now`/`DateTime.UtcNow` внутри `TimeProgressTracker`, только через параметры
  публичных методов. Это единственная причина, по которой юнит-тесты трекера и VM-тесты вообще
  могут детерминированно проверять временные метрики.
- Имена уже привязанных в `MainView.axaml` свойств ViewModel не меняются:
  `PhysicalOverallProgress`, `PhysicalPointRemainingFraction`, `PhysicallyExecutingKeyPointId`.
  Меняется только то, откуда берутся их значения (время вместо дистанции).
- Ack-based слой (`OverallProgress`, `CurrentSegmentIndex`, `SegmentProgress`,
  `CurrentlyExecutingKeyPointId`, `FaultedMessage`) не трогается — ни поведение, ни тесты.
- `BackgroundSessionState`/`BackgroundSessionProjector`/`BackgroundSessionCoordinator`/
  `MachineSessionService` (Android-уведомление) не трогаются — контракт (`double` 0..1,
  проброшенный как `PhysicalOverallProgress`) не изменился.

## File Structure

- `ArctZ/Services/Program/PhysicalProgressTracker.cs` → переименовывается и полностью
  переписывается в `ArctZ/Services/Program/TimeProgressTracker.cs` (Task 1).
- `ArctZ.Tests/Services/Program/PhysicalProgressTrackerTests.cs` → переименовывается и полностью
  переписывается в `ArctZ.Tests/Services/Program/TimeProgressTrackerTests.cs` (Task 1).
- `ArctZ/ViewModels/ProgramViewModel.cs` — точечные правки: тип поля `_progressTracker`, интервал
  таймера по умолчанию, тело `OnProgressTimerElapsed`, конструирование трекера в `RunPassAsync`,
  вызов из `OnSessionDeviceStatusChanged`, блок публичных свойств прогресса,
  `OnProgressTrackerChanged` (Task 2).
- `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs` — обновляется хелпер `CreateViewModel`
  (новый опциональный параметр `now`) и переписываются тесты, завязанные на старую (дистанционную)
  семантику `PhysicalOverallProgress`; добавляется новый тест на
  `PhysicalPointHasTimeWarning` (Task 2, тот же коммит — оба файла меняются вместе, иначе тесты
  временно красные).
- Create: `ArctZ/Converters/KeyPointHasTimeWarningConverter.cs` (Task 3).
- Create: `ArctZ.Tests/Converters/KeyPointHasTimeWarningConverterTests.cs` (Task 3).
- Modify: `ArctZ/Views/MainView.axaml` — регистрация нового конвертера, новый элемент
  `MaterialIcon` внутри тайла точки (Task 3).

---

### Task 1: `TimeProgressTracker` — переписать из `PhysicalProgressTracker`, вместе с юнит-тестами

Самодостаточная задача: чистый класс без зависимостей от `ProgramViewModel`/Avalonia. Это не
классический greenfield-TDD (класс уже существует и покрыт тестами Ревизии 1) — задача заменяет
и реализацию, и тесты одним связным изменением, поскольку старые тесты проверяют дистанционную
семантику (`ApproachFraction`, `IsDwelling`, `DwellFraction`), которая в этой ревизии удаляется
целиком.

**Files:**
- Modify (переименовать + переписать): `ArctZ/Services/Program/PhysicalProgressTracker.cs` →
  `ArctZ/Services/Program/TimeProgressTracker.cs`
- Modify (переименовать + переписать): `ArctZ.Tests/Services/Program/PhysicalProgressTrackerTests.cs`
  → `ArctZ.Tests/Services/Program/TimeProgressTrackerTests.cs`

**Interfaces:**
- Consumes: `CompiledStep` (`SegmentIndex`, `Pose`, `EstimatedDurationSeconds`, `IsDwellStep` —
  все уже существуют, без изменений), `MachinePose` (уже существует).
- Produces (для Task 2): конструктор
  `TimeProgressTracker(IReadOnlyList<CompiledStep> steps, MachinePose startingPose, DateTimeOffset passStartedAt)`;
  методы `void OnPositionUpdated(MachinePose position, DateTimeOffset now)`,
  `void OnClockTick(DateTimeOffset now)`; свойства `double OverallFraction`,
  `double CurrentStepFraction`, `bool CurrentPointHasWarning`, `int? CurrentSegmentIndex`;
  `event Action? Changed`.

- [ ] **Step 1: Переименовать файл реализации**

```bash
git mv "ArctZ/Services/Program/PhysicalProgressTracker.cs" "ArctZ/Services/Program/TimeProgressTracker.cs"
```

- [ ] **Step 2: Заменить содержимое `TimeProgressTracker.cs` целиком**

```csharp
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
    private readonly DateTimeOffset _passStartedAt;

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
```

- [ ] **Step 3: Переименовать файл тестов**

```bash
git mv "ArctZ.Tests/Services/Program/PhysicalProgressTrackerTests.cs" "ArctZ.Tests/Services/Program/TimeProgressTrackerTests.cs"
```

- [ ] **Step 4: Заменить содержимое `TimeProgressTrackerTests.cs` целиком**

```csharp
using System;
using System.Collections.Generic;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;
using ArctZ.Services.Program;
using Xunit;

namespace ArctZ.Tests.Services.Program;

public class TimeProgressTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static CompiledStep Move(int segmentIndex, double x, double estimatedSeconds = 5, bool isDwell = false) =>
        new(segmentIndex, new GCodeLineCommand("G93 G1 X" + x), SegmentProgress: 1.0, EstimatedDurationSeconds: estimatedSeconds, Pose: new MachinePose(x, 0, 0, 0), IsDwellStep: isDwell);

    [Fact]
    public void OnClockTick_HalfwayThroughTheEstimatedTimeOfTheOnlySegment_ReportsHalfOverallAndHalfStep()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnClockTick(T0.AddSeconds(5));

        Assert.Equal(0.5, tracker.OverallFraction);
        Assert.Equal(0.5, tracker.CurrentStepFraction);
        Assert.Equal(0, tracker.CurrentSegmentIndex);
    }

    [Fact]
    public void OnClockTick_TimeDoesNotDependOnPosition_KeepsGrowingWhileTheMachineStandsStill()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        // Position never changes (as if the machine were dwelling) — OnPositionUpdated isn't even called.
        tracker.OnClockTick(T0.AddSeconds(3));
        var afterThree = tracker.OverallFraction;
        tracker.OnClockTick(T0.AddSeconds(6));

        Assert.True(tracker.OverallFraction > afterThree);
    }

    [Fact]
    public void OnPositionUpdated_MovingIntoTheNextSegment_ResetsCurrentStepFractionForThatSegment()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10), Move(1, x: 20, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnPositionUpdated(new MachinePose(15, 0, 0, 0), T0.AddSeconds(12)); // crosses into segment 1's territory
        Assert.Equal(1, tracker.CurrentSegmentIndex);
        Assert.Equal(0.0, tracker.CurrentStepFraction); // just entered, no time elapsed in segment 1 yet

        tracker.OnClockTick(T0.AddSeconds(14)); // 2s later, still in segment 1

        Assert.Equal(0.2, tracker.CurrentStepFraction); // 2 of 10s estimate for segment 1
    }

    [Fact]
    public void OnPositionUpdated_NoisyPositionThatLooksLikeItWentBackward_NeverDecreasesTheActiveSegment()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10), Move(1, x: 20, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnPositionUpdated(new MachinePose(15, 0, 0, 0), T0.AddSeconds(12));
        Assert.Equal(1, tracker.CurrentSegmentIndex);

        tracker.OnPositionUpdated(new MachinePose(12, 0, 0, 0), T0.AddSeconds(13)); // controller cornering smoothing noise

        Assert.Equal(1, tracker.CurrentSegmentIndex);
    }

    [Fact]
    public void OnPositionUpdated_ZeroLengthFirstSegment_CurrentSegmentIndexIsZeroFromConstruction()
    {
        // Real segment 0 is From == To == KeyPoints[0] in the model; here the compiled step's own
        // pose equals the starting pose, producing a zero-length edge — but its estimated time
        // still applies, since EstimatedDurationSeconds is a time estimate, not a distance one.
        var steps = new List<CompiledStep> { Move(0, x: 0, estimatedSeconds: 5) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        Assert.Equal(0, tracker.CurrentSegmentIndex);

        tracker.OnClockTick(T0.AddSeconds(2.5));

        Assert.Equal(0.5, tracker.CurrentStepFraction);
    }

    [Fact]
    public void Construction_DoesNotResetTheEntryClockOnTheFirstTick()
    {
        // If the first Recompute call mistook "no prior recorded segment" for "just entered this
        // segment", it would reset the entry clock to whenever that first call happens instead of
        // passStartedAt — undercounting elapsed time for segment 0 by however long the caller
        // waited before the first tick/position update.
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 5) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnClockTick(T0.AddSeconds(2));

        Assert.Equal(0.4, tracker.CurrentStepFraction); // 2 of 5, measured from passStartedAt, not from this first tick
    }

    [Fact]
    public void Changed_FiresOnPositionUpdateAndOnClockTick()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);
        var raiseCount = 0;
        tracker.Changed += () => raiseCount++;

        tracker.OnPositionUpdated(new MachinePose(5, 0, 0, 0), T0.AddSeconds(1));
        tracker.OnClockTick(T0.AddSeconds(2));

        Assert.Equal(2, raiseCount);
    }

    [Fact]
    public void CurrentPointHasWarning_ElapsedTwentyPercentOverEstimate_IsTrue()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnClockTick(T0.AddSeconds(12)); // 12 of 10 estimated = 20% over

        Assert.True(tracker.CurrentPointHasWarning);
    }

    [Fact]
    public void CurrentPointHasWarning_ElapsedTenPercentOverEstimate_IsFalse()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnClockTick(T0.AddSeconds(11)); // 11 of 10 estimated = 10% over, under the 15% threshold

        Assert.False(tracker.CurrentPointHasWarning);
    }

    [Fact]
    public void CurrentPointHasWarning_ClearsImmediatelyOnMovingToTheNextSegment()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10), Move(1, x: 20, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnClockTick(T0.AddSeconds(15)); // segment 0, 50% over its 10s estimate
        Assert.True(tracker.CurrentPointHasWarning);

        tracker.OnPositionUpdated(new MachinePose(11, 0, 0, 0), T0.AddSeconds(15)); // real motion into segment 1

        Assert.False(tracker.CurrentPointHasWarning);
    }

    [Fact]
    public void CurrentStepFraction_ZeroEstimatedSecondsForTheSegment_IsOneAndNeverWarns()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 0) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnClockTick(T0.AddSeconds(5));

        Assert.Equal(1.0, tracker.CurrentStepFraction);
        Assert.False(tracker.CurrentPointHasWarning);
    }

    [Fact]
    public void OverallFraction_ZeroTotalEstimatedSeconds_IsOne()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 0) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnClockTick(T0.AddSeconds(5));

        Assert.Equal(1.0, tracker.OverallFraction);
    }

    [Fact]
    public void OverallFraction_TimeBeyondTheWholePassEstimate_ClampsAtOne()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnClockTick(T0.AddSeconds(50));

        Assert.Equal(1.0, tracker.OverallFraction);
    }

    [Fact]
    public void OverallFraction_SumsEstimatedSecondsAcrossAllStepsIncludingDwell()
    {
        var steps = new List<CompiledStep>
        {
            Move(0, x: 10, estimatedSeconds: 10),
            new(0, new GCodeLineCommand("G4 P5"), SegmentProgress: 1.0, EstimatedDurationSeconds: 5, Pose: new MachinePose(10, 0, 0, 0), IsDwellStep: true),
        };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        // Total estimated for the pass is 10 (transition) + 5 (dwell) = 15; halfway through that is 7.5s.
        tracker.OnClockTick(T0.AddSeconds(7.5));

        Assert.Equal(0.5, tracker.OverallFraction);
    }
}
```

- [ ] **Step 5: Прогнать тесты трекера в изоляции**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~TimeProgressTrackerTests"`
Expected: PASS, 14/14.

- [ ] **Step 6: Убедиться что весь тестовый проект собирается**

`ProgramViewModel.cs` ещё ссылается на старое имя `PhysicalProgressTracker` (Task 2 это исправит)
— полный `dotnet test` красный на этом шаге ожидаемо. Ограничиться сборкой самого тестового
проекта, чтобы убедиться, что *новый* файл (`TimeProgressTracker.cs`/`TimeProgressTrackerTests.cs`)
не имеет опечаток, отдельно от последующих правок:

Run: `dotnet build ArctZ.Tests/ArctZ.Tests.csproj`
Expected: FAIL — единственная ошибка `CS0246`/аналог: `PhysicalProgressTracker` не найден, в
`ArctZ/ViewModels/ProgramViewModel.cs`. Любая ДРУГАЯ ошибка компиляции (в частности внутри самого
`TimeProgressTracker.cs`/`TimeProgressTrackerTests.cs`) — сигнал реальной опечатки в Step 2/4,
исправить перед коммитом.

- [ ] **Step 7: Commit**

```bash
git add "ArctZ/Services/Program/PhysicalProgressTracker.cs" "ArctZ/Services/Program/TimeProgressTracker.cs" \
        "ArctZ.Tests/Services/Program/PhysicalProgressTrackerTests.cs" "ArctZ.Tests/Services/Program/TimeProgressTrackerTests.cs"
git commit -m "feat: switch program-progress tracker from distance to time"
```

---

### Task 2: Подключить `TimeProgressTracker` к `ProgramViewModel`, обновить тесты плейбека

Task 1's класс не используется до этой задачи — здесь `ProgramViewModel` перестаёт ссылаться на
удалённый `PhysicalProgressTracker` и полностью переходит на новый трекер. Часть существующих
тестов `ProgramViewModelPlaybackTests` проверяет СТАРУЮ (дистанционную) семантику
`PhysicalOverallProgress` — без их переписывания сборка тестового проекта останется зелёной, но
конкретно эти проверки будут падать (значения не совпадут), поэтому оба файла меняются в одном
задании.

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs`
- Modify: `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs`

**Interfaces:**
- Consumes: `TimeProgressTracker` из Task 1 (конструктор с `passStartedAt`, `OnPositionUpdated(pose, now)`,
  `OnClockTick(now)`, `OverallFraction`, `CurrentStepFraction`, `CurrentPointHasWarning`,
  `CurrentSegmentIndex`).
- Produces (для Task 3): `ProgramViewModel.PhysicalOverallProgress` (`double`, без изменений имени),
  `PhysicalPointRemainingFraction` (`double`, без изменений имени, новая формула),
  `PhysicallyExecutingKeyPointId` (`Guid?`, без изменений), новое
  `PhysicalPointHasTimeWarning` (`bool`).

- [ ] **Step 1: Прочитать текущее состояние `ProgramViewModel.cs`**

Файл менялся Ревизией 1 незадолго до этого — прочитать целиком перед правкой (номера строк ниже
ориентировочные, искать по содержимому).

- [ ] **Step 2: Тип поля трекера и интервал таймера по умолчанию**

Найти (около строки 30):
```csharp
    private PhysicalProgressTracker? _progressTracker;
```
Заменить на:
```csharp
    private TimeProgressTracker? _progressTracker;
```

Найти в конструкторе (около строки 122):
```csharp
        _progressTimerInterval = progressTimerInterval ?? TimeSpan.FromMilliseconds(100);
```
Заменить на:
```csharp
        _progressTimerInterval = progressTimerInterval ?? TimeSpan.FromMilliseconds(200);
```

- [ ] **Step 3: `OnProgressTimerElapsed` — тик по времени вместо накопленной дельты**

Найти:
```csharp
    private void OnProgressTimerElapsed()
    {
        _progressTracker?.OnTimerElapsed(_progressTimerInterval);
    }
```
Заменить на:
```csharp
    private void OnProgressTimerElapsed()
    {
        _progressTracker?.OnClockTick(_now());
    }
```

- [ ] **Step 4: `RunPassAsync` — конструирование трекера с `passStartedAt`**

Найти:
```csharp
        var startingPose = Connection.Session?.DeviceStatus?.WPos ?? MachinePose.Zero;
        _progressTracker = new PhysicalProgressTracker(steps, startingPose);
        _progressTracker.Changed += OnProgressTrackerChanged;
        OnProgressTrackerChanged();
```
Заменить на:
```csharp
        var startingPose = Connection.Session?.DeviceStatus?.WPos ?? MachinePose.Zero;
        _progressTracker = new TimeProgressTracker(steps, startingPose, passStartedAt: _now());
        _progressTracker.Changed += OnProgressTrackerChanged;
        OnProgressTrackerChanged();
```

- [ ] **Step 5: `OnSessionDeviceStatusChanged` — передавать время в позиционный апдейт**

Найти:
```csharp
        if (status is { } value)
        {
            _progressTracker?.OnPositionUpdated(value.WPos);
        }
```
Заменить на:
```csharp
        if (status is { } value)
        {
            _progressTracker?.OnPositionUpdated(value.WPos, _now());
        }
```

- [ ] **Step 6: Блок публичных свойств прогресса**

Найти:
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
```
Заменить на:
```csharp
    public double PhysicalOverallProgress => _progressTracker?.OverallFraction ?? 0;

    // The converter that consumes this (FractionToPieSliceConverter) already clamps its input to
    // 0..1, so an overrun (CurrentStepFraction > 1, the machine hasn't arrived despite exceeding
    // its estimate) naturally reads as an empty circle rather than a negative remainder — the
    // warning indicator (PhysicalPointHasTimeWarning), not the circle's geometry, is what surfaces
    // the overrun itself.
    public double PhysicalPointRemainingFraction => _progressTracker is null
        ? 1.0
        : 1.0 - _progressTracker.CurrentStepFraction;

    public bool PhysicalPointHasTimeWarning => _progressTracker?.CurrentPointHasWarning ?? false;

    public Guid? PhysicallyExecutingKeyPointId => _progressTracker is null
        ? null
        : Services.Program.JibProgram.TargetKeyPoint(KeyPoints, _progressTracker.CurrentSegmentIndex, _currentPassBackward);

    private void OnProgressTrackerChanged()
    {
        OnPropertyChanged(nameof(PhysicalOverallProgress));
        OnPropertyChanged(nameof(PhysicalPointRemainingFraction));
        OnPropertyChanged(nameof(PhysicalPointHasTimeWarning));
        OnPropertyChanged(nameof(PhysicallyExecutingKeyPointId));
    }
```

- [ ] **Step 7: Собрать core-проект**

Run: `dotnet build ArctZ/ArctZ.csproj`
Expected: PASS (никаких ссылок на `PhysicalProgressTracker`/`ApproachFraction`/`IsDwelling`/
`DwellFraction`/`OnTimerElapsed` больше не остаётся нигде в `ArctZ/`).

- [ ] **Step 8: Обновить хелпер `CreateViewModel` в тестах плейбека**

`ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs`, найти:
```csharp
    private static ProgramViewModel CreateViewModel(out FakeDeviceTransport transport, out ManualPeriodicTimer progressTimer)
    {
        transport = new FakeDeviceTransport();
        var storage = new FakeProgramStorage();
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default), new SingleRealDeviceEndpointProvider());
        progressTimer = new ManualPeriodicTimer();
        return new ProgramViewModel(connection, storage, new TrajectoryCompiler(), new FakeAppExitService(), progressTimer: progressTimer);
    }
```
Заменить на:
```csharp
    private static ProgramViewModel CreateViewModel(out FakeDeviceTransport transport, out ManualPeriodicTimer progressTimer, Func<DateTimeOffset>? now = null)
    {
        transport = new FakeDeviceTransport();
        var storage = new FakeProgramStorage();
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default), new SingleRealDeviceEndpointProvider());
        progressTimer = new ManualPeriodicTimer();
        return new ProgramViewModel(connection, storage, new TrajectoryCompiler(), new FakeAppExitService(), now, progressTimer: progressTimer);
    }
```

- [ ] **Step 9: Переписать `PlayAsync_AsThePositionAdvancesTowardTheFirstPoint_PhysicalOverallProgressTracksIt`**

Старая версия проверяла рост `PhysicalOverallProgress` по позиции без течения времени — в
Ревизии 2 это больше не соответствует реализации (метрика по времени, не по дистанции). Найти
весь метод (сигнатура `PlayAsync_AsThePositionAdvancesTowardTheFirstPoint_PhysicalOverallProgressTracksIt`)
и заменить целиком на:

```csharp
    [Fact]
    public async Task PlayAsync_AsTimeElapsesDuringThePass_PhysicalOverallProgressTracksIt()
    {
        var currentTime = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        // SeedTwoSegmentProgram leaves the simulated machine at the last captured pose (20,0,0,0) —
        // reset it to the program's actual starting pose before Play, so the tracker's captured
        // starting vertex matches what these assertions assume.
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.PhysicalOverallProgress);

        // 3 key points at TransitionSeconds=5 each (SeedTwoSegmentProgram) = 15s total estimate for
        // the whole pass, including segment 0's zero-distance self-move; halfway is 7.5s.
        currentTime = currentTime.AddSeconds(7.5);
        progressTimer.RaiseElapsed();

        Assert.Equal(0.5, vm.PhysicalOverallProgress);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }
```

- [ ] **Step 10: Переписать `PlayAsync_EachNewPass_ResetsPhysicalOverallProgressToZero`**

Найти весь метод и заменить целиком на:

```csharp
    [Fact]
    public async Task PlayAsync_EachNewPass_ResetsPhysicalOverallProgressToZero()
    {
        var currentTime = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");
        // Setting CompletionMode/RepeatCount marks the program dirty again (MarkDirtyIfTracking),
        // which would make PlayAsync's EnsureProgramSavedAsync await an unanswered save-confirmation
        // dialog forever (see CLAUDE.md's async-dialog-gate note) — re-clear IsDirty after.
        vm.CompletionMode = ProgramCompletionMode.PingPong;
        vm.RepeatCount = 1;
        vm.IsDirty = false;

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
        currentTime = currentTime.AddSeconds(7.5); // forward pass, well underway (half its 15s estimate)
        progressTimer.RaiseElapsed();

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
```

- [ ] **Step 11: Переписать `StopAsync_ClearsPhysicalProgress`**

Найти весь метод и заменить целиком на:

```csharp
    [Fact]
    public async Task StopAsync_ClearsPhysicalProgress()
    {
        var currentTime = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        currentTime = currentTime.AddSeconds(5);
        progressTimer.RaiseElapsed();
        Assert.True(vm.PhysicalOverallProgress > 0);

        await vm.StopCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok"); // resolves the command already in flight so playTask completes
        await playTask;

        Assert.Equal(0, vm.PhysicalOverallProgress);
        Assert.Null(vm.PhysicallyExecutingKeyPointId);
    }
```

- [ ] **Step 12: Обновить `PhysicalOverallProgress_WithNoActiveTracker_DefaultsToZero`**

Найти:
```csharp
    [Fact]
    public void PhysicalOverallProgress_WithNoActiveTracker_DefaultsToZero()
    {
        var vm = CreateViewModel(out _, out _);

        Assert.Equal(0, vm.PhysicalOverallProgress);
        Assert.Equal(1.0, vm.PhysicalPointRemainingFraction);
        Assert.Null(vm.PhysicallyExecutingKeyPointId);
    }
```
Заменить на:
```csharp
    [Fact]
    public void PhysicalOverallProgress_WithNoActiveTracker_DefaultsToZero()
    {
        var vm = CreateViewModel(out _, out _);

        Assert.Equal(0, vm.PhysicalOverallProgress);
        Assert.Equal(1.0, vm.PhysicalPointRemainingFraction);
        Assert.False(vm.PhysicalPointHasTimeWarning);
        Assert.Null(vm.PhysicallyExecutingKeyPointId);
    }
```

- [ ] **Step 13: Добавить новый тест на `PhysicalPointHasTimeWarning`**

Сразу после метода из Step 12 (перед закрывающей скобкой класса) добавить:

```csharp

    [Fact]
    public async Task PlayAsync_WhenASegmentTakesTooLong_PhysicalPointHasTimeWarningBecomesTrue()
    {
        var currentTime = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport); // each key point's TransitionSeconds is 5
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        Assert.False(vm.PhysicalPointHasTimeWarning);

        // Segment 0's estimate is 5s; 6s elapsed is 20% over, past the 15% warning threshold.
        currentTime = currentTime.AddSeconds(6);
        progressTimer.RaiseElapsed();

        Assert.True(vm.PhysicalPointHasTimeWarning);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }
```

Note: `PlayAsync_PhysicallyExecutingKeyPointId_CanLagTheAckBasedHighlightWhenTheBufferRunsAhead`
(тот же файл) не трогается — не проверяет `PhysicalOverallProgress`/временную метрику,
`PhysicallyExecutingKeyPointId` в Ревизии 2 без изменений.

- [ ] **Step 14: Прогнать тесты плейбека в изоляции**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: PASS. Если какой-то тест зависает (не укладывается в разумное время) — почти наверняка
неотвеченный async-диалог (`EnsureProgramSavedAsync`), см. правило CLAUDE.md — проверить, что
`IsDirty = false` выставлен после любого изменения `CompletionMode`/`RepeatCount`, а не
таймаут/флаки.

- [ ] **Step 15: Прогнать полный тестовый набор**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS, весь набор (включая `TimeProgressTrackerTests` из Task 1 и все остальные, не
затронутые этой ревизией — ack-based тесты, `BackgroundSessionProjectorTests`, screenshot-тесты
и т.д.).

- [ ] **Step 16: Commit**

```bash
git add "ArctZ/ViewModels/ProgramViewModel.cs" "ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs"
git commit -m "feat: wire TimeProgressTracker into ProgramViewModel"
```

---

### Task 3: UI — warning-индикатор на тайле точки

Круг на тайле и общий прогресс-бар уже привязаны к `PhysicalPointRemainingFraction`/
`PhysicalOverallProgress` в `MainView.axaml` (Ревизия 1) — эти биндинги не меняются (Task 2 уже
поменял то, откуда берутся значения). Эта задача добавляет только новый визуальный элемент.

**Files:**
- Create: `ArctZ/Converters/KeyPointHasTimeWarningConverter.cs`
- Create: `ArctZ.Tests/Converters/KeyPointHasTimeWarningConverterTests.cs`
- Modify: `ArctZ/Views/MainView.axaml`

**Interfaces:**
- Consumes: `ProgramViewModel.PhysicallyExecutingKeyPointId` (`Guid?`, Task 2, без изменений имени),
  `ProgramViewModel.PhysicalPointHasTimeWarning` (`bool`, новое в Task 2), `KeyPoint.Id` (`Guid`,
  существует).

- [ ] **Step 1: Написать падающий тест конвертера**

Create `ArctZ.Tests/Converters/KeyPointHasTimeWarningConverterTests.cs`:

```csharp
using System;
using System.Globalization;
using ArctZ.Converters;

namespace ArctZ.Tests.Converters;

public class KeyPointHasTimeWarningConverterTests
{
    [Fact]
    public void Convert_ReturnsTrue_WhenTileIsExecutingAndHasWarning()
    {
        var id = Guid.NewGuid();
        var converter = new KeyPointHasTimeWarningConverter();

        var result = converter.Convert(new object?[] { id, (Guid?)id, true }, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(true, result);
    }

    [Fact]
    public void Convert_ReturnsFalse_WhenExecutingButNoWarning()
    {
        var id = Guid.NewGuid();
        var converter = new KeyPointHasTimeWarningConverter();

        var result = converter.Convert(new object?[] { id, (Guid?)id, false }, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }

    [Fact]
    public void Convert_ReturnsFalse_WhenWarningButNotExecuting()
    {
        var converter = new KeyPointHasTimeWarningConverter();

        var result = converter.Convert(new object?[] { Guid.NewGuid(), (Guid?)Guid.NewGuid(), true }, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }

    [Fact]
    public void Convert_ReturnsFalse_WhenExecutingIdIsNull()
    {
        var id = Guid.NewGuid();
        var converter = new KeyPointHasTimeWarningConverter();

        var result = converter.Convert(new object?[] { id, null, true }, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }
}
```

- [ ] **Step 2: Прогнать тест, убедиться что падает**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~KeyPointHasTimeWarningConverterTests"`
Expected: FAIL — `KeyPointHasTimeWarningConverter` не существует (ошибка компиляции).

- [ ] **Step 3: Реализовать конвертер**

Create `ArctZ/Converters/KeyPointHasTimeWarningConverter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ArctZ.Converters;

public class KeyPointHasTimeWarningConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count != 3 || values[0] is not Guid tileId || values[1] is not Guid executingId || values[2] is not bool hasWarning)
        {
            return false;
        }

        return tileId == executingId && hasWarning;
    }
}
```

- [ ] **Step 4: Прогнать тест, убедиться что проходит**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~KeyPointHasTimeWarningConverterTests"`
Expected: PASS, 4/4.

- [ ] **Step 5: Зарегистрировать конвертер в `MainView.axaml`**

Найти (`UserControl.Resources`, рядом с `FractionToPieSlice`):
```xml
        <conv:FractionToPieSliceConverter x:Key="FractionToPieSlice" />
```
Добавить сразу после:
```xml
        <conv:KeyPointHasTimeWarningConverter x:Key="KeyPointHasTimeWarning" />
```

- [ ] **Step 6: Добавить иконку в тайл точки**

Найти в `DataTemplate x:DataType="program:KeyPoint"` существующий `<Path>` (круг прогресса,
привязан к `PhysicalPointRemainingFraction`/`PhysicallyExecutingKeyPointId`) — он последний
дочерний элемент `<Panel>` тайла:
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
                                            </Panel>
```
Добавить новый элемент между закрывающим `</Path>` и `</Panel>` (иконка в противоположном,
левом верхнем углу тайла — круг остаётся справа):
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
                                                <materialIcons:MaterialIcon Kind="Alert" IsHitTestVisible="False" Width="14" Height="14"
                                                                             HorizontalAlignment="Left" VerticalAlignment="Top" Margin="4,4,0,0"
                                                                             Foreground="{DynamicResource HudWarningBrush}">
                                                    <materialIcons:MaterialIcon.IsVisible>
                                                        <MultiBinding Converter="{StaticResource KeyPointHasTimeWarning}">
                                                            <Binding Path="Id" />
                                                            <Binding Path="((vm:ProgramViewModel)DataContext).PhysicallyExecutingKeyPointId" ElementName="KeyPointsList" />
                                                            <Binding Path="((vm:ProgramViewModel)DataContext).PhysicalPointHasTimeWarning" ElementName="KeyPointsList" />
                                                        </MultiBinding>
                                                    </materialIcons:MaterialIcon.IsVisible>
                                                </materialIcons:MaterialIcon>
                                            </Panel>
```

- [ ] **Step 7: Собрать Desktop-голову**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: PASS — компилируемые биндинги (`x:DataType`) поймают опечатку в именах свойств; если
`MaterialIconKind.Alert` не существует под этим именем — заменить на ближайший валидный (например
`AlertCircleOutline`) и указать это отклонение при отчёте по задаче.

- [ ] **Step 8: Commit**

```bash
git add "ArctZ/Converters/KeyPointHasTimeWarningConverter.cs" "ArctZ.Tests/Converters/KeyPointHasTimeWarningConverterTests.cs" "ArctZ/Views/MainView.axaml"
git commit -m "feat: add time-overage warning indicator to key-point tiles"
```

---

### Task 4: Живой UI-тест (выполняется контроллером напрямую, не делегируется субагенту)

Единственный способ подтвердить UI-изменения по правилам проекта (`CLAUDE.md`, «Тестирование
UI») — реально собрать и запустить приложение и спросить пользователя. Не создаёт новых файлов,
не имеет отдельного коммита.

- [ ] Собрать и запустить `ArctZ.Desktop`.
- [ ] Прогнать программу с несколькими точками, включая: dwell (`DwellSeconds > 0`), `EaseInOut`,
  повтор Loop/PingPong, и одну точку с намеренно заниженным `TransitionSeconds` (например,
  движение на большое расстояние с `TransitionSeconds = 0.5`) — станок физически не успеет,
  должен сработать warning-индикатор.
- [ ] Через `AskUserQuestion` подтвердить по каждому слою отдельно (не один общий вопрос):
  1. Круг на текущей точке растёт по времени плавно (не скачками, не отстаёт от прогресс-бара).
  2. Общий прогресс-бар над списком точек движется по времени и сбрасывается в 0% на каждом новом
     проходе (Loop/PingPong).
  3. Warning-индикатор (восклицательный знак) появляется на точке с заниженным
     `TransitionSeconds` и исчезает при переходе к следующей точке.
