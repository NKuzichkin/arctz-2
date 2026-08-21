# Лог исполнения программы Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Вести в памяти текстовый лог исполнения программы (движение к точкам, паузы, рассинхронизация ack/физики и времени точки) с прогрессом на каждое событие, и дать скопировать лог последнего запуска кнопкой в диалоге «О программе».

**Architecture:** Новый чистый класс `ProgramExecutionLog` копит строки текста; `ProgramViewModel` создаёт его на холодном старте `PlayAsync` и наполняет из уже существующих точек наблюдения за состоянием (переходы `PlaybackState`, `TimeProgressTracker.Changed`, `OnSegmentTimeOverage`) — никакой новой инфраструктуры наблюдения не вводится, только новые побочные эффекты в уже существующих обработчиках. `AboutViewModel` получает текст лога как ещё одно готовое поле, копируется тем же Avalonia `Clipboard`-механизмом, что и существующий диагностический отчёт.

**Tech Stack:** C#/.NET 10, CommunityToolkit.Mvvm, Avalonia (XAML), xUnit.

**Spec:** `docs/superpowers/specs/2026-08-21-program-execution-log-design.md`

## Global Constraints

- Лог хранится только в памяти (никакой записи на диск, никакой per-platform абстракции хранилища).
- Один буфер на самый последний холодный старт программы — resume после паузы не создаёт новый буфер; новый холодный старт полностью заменяет предыдущий.
- Триггер событий движения — физически активный сегмент (`TimeProgressTracker`/`PhysicallyExecutingKeyPointId`), не ack.
- Рассинхронизация ack/физика логируется по фронту: разница `ackSegmentIndex - physicalSegmentIndex > 1`, ровно один раз на возникновение, без отдельной строки на исчезновение.
- Весь пользовательский текст (события, кнопки) — на русском, в стиле уже существующих строк `ProgramViewModel`/`AboutViewModel`.
- Обязательный финальный шаг — живой UI-тест по правилам `CLAUDE.md` (раздел «Тестирование UI»): собрать и запустить `ArctZ.Desktop`, попросить пользователя проверить, задать вопросы через `AskUserQuestion` по каждому пункту отдельно.

---

## Task 1: `ProgramExecutionLog` — новый класс лога

**Files:**
- Create: `ArctZ/Services/Program/ProgramExecutionLog.cs`
- Test: `ArctZ.Tests/Services/Program/ProgramExecutionLogTests.cs`

**Interfaces:**
- Produces: `ProgramExecutionLog(string programName, int keyPointCount, DateTimeOffset startedAt)`; `void LogMovementEnded(string? pointLabel, double overallProgress, double stepProgress, DateTimeOffset now)`; `void LogMovementStarted(string? pointLabel, double overallProgress, double stepProgress, DateTimeOffset now)`; `void LogPauseStarted(double overallProgress, double stepProgress, DateTimeOffset now)`; `void LogPauseEnded(double overallProgress, double stepProgress, DateTimeOffset now)`; `void LogAckDesync(int ackSegmentIndex, int physicalSegmentIndex, double overallProgress, double stepProgress, DateTimeOffset now)`; `void LogTimeOverage(string? pointLabel, double actualSeconds, double estimatedSeconds, double overallProgress, double stepProgress, DateTimeOffset now)`; `void LogProgramEnded(string outcomeLabel, double overallProgress, double stepProgress, DateTimeOffset now)`; `string Text { get; }`. Every later task in this plan consumes these exact names.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using ArctZ.Services.Program;
using Xunit;

namespace ArctZ.Tests.Services.Program;

public class ProgramExecutionLogTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_WritesTheProgramStartedHeaderLine()
    {
        var log = new ProgramExecutionLog("Тест", keyPointCount: 5, startedAt: T0);

        Assert.Equal("[00:00.000] Программа запущена: «Тест», 5 точек", log.Text);
    }

    [Fact]
    public void LogMovementStarted_FormatsPointLabelAndBothProgressValues()
    {
        var log = new ProgramExecutionLog("Тест", 3, T0);

        log.LogMovementStarted("Точка 1", overallProgress: 0.18, stepProgress: 0.0, T0.AddSeconds(4.32));

        Assert.Contains("[00:04.320] Начало движения к точке «Точка 1» — общий 18%, шаг 0%", log.Text);
    }

    [Fact]
    public void LogMovementEnded_FormatsPointLabelAndBothProgressValues()
    {
        var log = new ProgramExecutionLog("Тест", 3, T0);

        log.LogMovementEnded("Точка 1", overallProgress: 0.18, stepProgress: 1.0, T0.AddSeconds(4.32));

        Assert.Contains("Окончание движения к точке «Точка 1» — общий 18%, шаг 100%", log.Text);
    }

    [Fact]
    public void LogPauseStarted_AndLogPauseEnded_AppendInOrder()
    {
        var log = new ProgramExecutionLog("Тест", 1, T0);

        log.LogPauseStarted(0.34, 0.55, T0.AddSeconds(7.1));
        log.LogPauseEnded(0.34, 0.55, T0.AddSeconds(12.4));

        var lines = log.Text.Split(Environment.NewLine);
        var pauseIndex = Array.IndexOf(lines, "[00:07.100] Пауза — общий 34%, шаг 55%");
        var resumeIndex = Array.IndexOf(lines, "[00:12.400] Возобновление — общий 34%, шаг 55%");
        Assert.True(pauseIndex >= 0);
        Assert.True(resumeIndex > pauseIndex);
    }

    [Fact]
    public void LogAckDesync_ReportsTheGapBetweenAckAndPhysicalSegments()
    {
        var log = new ProgramExecutionLog("Тест", 3, T0);

        log.LogAckDesync(ackSegmentIndex: 2, physicalSegmentIndex: 0, overallProgress: 0.61, stepProgress: 0.2, T0.AddSeconds(15));

        Assert.Contains(
            "Рассинхронизация: буфер контроллера опережает факт на 2 точки (ack: сегмент 2, факт: сегмент 0) — общий 61%, шаг 20%",
            log.Text);
    }

    [Fact]
    public void LogTimeOverage_ReportsActualVsEstimatedSeconds()
    {
        var log = new ProgramExecutionLog("Тест", 3, T0);

        log.LogTimeOverage("Точка 3", actualSeconds: 14.2, estimatedSeconds: 8.0, overallProgress: 0.75, stepProgress: 1.0, T0.AddSeconds(20));

        Assert.Contains(
            "Рассинхронизация: превышение расчётного времени точки «Точка 3» (14.2с факт / 8.0с расчёт) — общий 75%, шаг 100%",
            log.Text);
    }

    [Fact]
    public void LogProgramEnded_ReportsTheOutcomeLabel()
    {
        var log = new ProgramExecutionLog("Тест", 3, T0);

        log.LogProgramEnded("Завершено", overallProgress: 1.0, stepProgress: 1.0, T0.AddSeconds(41.22));

        Assert.Contains("[00:41.220] Программа завершена: Завершено — общий 100%, шаг 100%", log.Text);
    }

    [Fact]
    public void FormatElapsed_MinutesRollOverPastFiftyNineSeconds()
    {
        var log = new ProgramExecutionLog("Тест", 1, T0);

        log.LogPauseStarted(0, 0, T0.AddSeconds(65));

        Assert.Contains("[01:05.000] Пауза", log.Text);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramExecutionLogTests"`
Expected: FAIL (compile error — `ProgramExecutionLog` doesn't exist yet).

- [ ] **Step 3: Implement `ProgramExecutionLog`**

```csharp
using System;
using System.Collections.Generic;

namespace ArctZ.Services.Program;

/// <summary>
/// Text log of one program run — see
/// docs/superpowers/specs/2026-08-21-program-execution-log-design.md. Plain, UI-agnostic: time is
/// passed by the caller, never read from the system clock, for testability. Lives entirely in
/// memory for the duration of the app session; ProgramViewModel owns the instance and exposes its
/// Text through the "О программе" dialog.
/// </summary>
public sealed class ProgramExecutionLog
{
    private readonly DateTimeOffset _startedAt;
    private readonly List<string> _lines = new();

    public ProgramExecutionLog(string programName, int keyPointCount, DateTimeOffset startedAt)
    {
        _startedAt = startedAt;
        _lines.Add(FormatLine(startedAt, $"Программа запущена: «{programName}», {keyPointCount} точек"));
    }

    public void LogMovementEnded(string? pointLabel, double overallProgress, double stepProgress, DateTimeOffset now) =>
        Append($"Окончание движения к точке «{pointLabel}»", overallProgress, stepProgress, now);

    public void LogMovementStarted(string? pointLabel, double overallProgress, double stepProgress, DateTimeOffset now) =>
        Append($"Начало движения к точке «{pointLabel}»", overallProgress, stepProgress, now);

    public void LogPauseStarted(double overallProgress, double stepProgress, DateTimeOffset now) =>
        Append("Пауза", overallProgress, stepProgress, now);

    public void LogPauseEnded(double overallProgress, double stepProgress, DateTimeOffset now) =>
        Append("Возобновление", overallProgress, stepProgress, now);

    public void LogAckDesync(int ackSegmentIndex, int physicalSegmentIndex, double overallProgress, double stepProgress, DateTimeOffset now) =>
        Append(
            $"Рассинхронизация: буфер контроллера опережает факт на {ackSegmentIndex - physicalSegmentIndex} точки (ack: сегмент {ackSegmentIndex}, факт: сегмент {physicalSegmentIndex})",
            overallProgress, stepProgress, now);

    public void LogTimeOverage(string? pointLabel, double actualSeconds, double estimatedSeconds, double overallProgress, double stepProgress, DateTimeOffset now) =>
        Append(
            $"Рассинхронизация: превышение расчётного времени точки «{pointLabel}» ({actualSeconds:F1}с факт / {estimatedSeconds:F1}с расчёт)",
            overallProgress, stepProgress, now);

    public void LogProgramEnded(string outcomeLabel, double overallProgress, double stepProgress, DateTimeOffset now) =>
        Append($"Программа завершена: {outcomeLabel}", overallProgress, stepProgress, now);

    public string Text => string.Join(Environment.NewLine, _lines);

    private void Append(string eventText, double overallProgress, double stepProgress, DateTimeOffset now) =>
        _lines.Add(FormatLine(now, $"{eventText} — общий {FormatPercent(overallProgress)}, шаг {FormatPercent(stepProgress)}"));

    private string FormatLine(DateTimeOffset now, string text) => $"[{FormatElapsed(now)}] {text}";

    private string FormatElapsed(DateTimeOffset now)
    {
        var elapsed = now - _startedAt;
        return $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}.{elapsed.Milliseconds:D3}";
    }

    private static string FormatPercent(double fraction) => $"{fraction * 100:F0}%";
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramExecutionLogTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Services/Program/ProgramExecutionLog.cs ArctZ.Tests/Services/Program/ProgramExecutionLogTests.cs
git commit -m "feat: add ProgramExecutionLog for program-run event logging"
```

---

## Task 2: Лог создаётся на холодном старте, пишет паузы и финал — с фиксом захвата прогресса

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs`
- Test: Create `ArctZ.Tests/ViewModels/ProgramViewModelExecutionLogTests.cs`

**Interfaces:**
- Consumes: `ProgramExecutionLog` from Task 1 (all methods).
- Produces: `ProgramViewModel.ExecutionLogText` (`string?`, `get`-only) — Task 6 (About wiring) consumes this. Private field `_executionLog` (`ProgramExecutionLog?`) — Tasks 3, 4, 5 write to it via `_executionLog?.Log...(...)`.

- [ ] **Step 1: Write the failing tests**

Create `ArctZ.Tests/ViewModels/ProgramViewModelExecutionLogTests.cs`:

```csharp
using System;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.App;
using ArctZ.Tests.Services.Device;
using ArctZ.Tests.Services.Program;
using ArctZ.ViewModels;
using static ArctZ.Tests.TestSupport.AsyncAssert;

namespace ArctZ.Tests.ViewModels;

public class ProgramViewModelExecutionLogTests
{
    private static ProgramViewModel CreateViewModel(out FakeDeviceTransport transport, out ManualPeriodicTimer progressTimer, Func<DateTimeOffset>? now = null)
    {
        transport = new FakeDeviceTransport();
        var storage = new FakeProgramStorage();
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default), new SingleRealDeviceEndpointProvider());
        progressTimer = new ManualPeriodicTimer();
        return new ProgramViewModel(connection, storage, new TrajectoryCompiler(), new FakeAppExitService(), now, progressTimer: progressTimer);
    }

    private static void SeedTwoSegmentProgram(ProgramViewModel vm, FakeDeviceTransport transport)
    {
        foreach (var pose in new[] { "0,0,0,0", "10,0,0,0", "20,0,0,0" })
        {
            transport.SimulateReceivedLine($"<Idle|WPos:{pose}|FS:0,0>");
            vm.CaptureKeyPointCommand.Execute(null);
        }

        for (var i = 0; i < vm.KeyPoints.Count; i++)
        {
            vm.KeyPoints[i] = vm.KeyPoints[i] with { TransitionSeconds = 5, DwellSeconds = 0, Ease = EaseMode.None, ContinuousBlend = true };
        }

        vm.ProgramId = Guid.NewGuid();
        vm.IsDirty = false;
    }

    [Fact]
    public async Task PlayAsync_ColdStart_CreatesTheLogWithAHeaderLine()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        vm.ProgramName = "Тестовая программа";

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        Assert.Contains("Программа запущена: «Тестовая программа», 3 точек", vm.ExecutionLogText);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    [Fact]
    public async Task PlayAsync_ResumeAfterPause_DoesNotStartANewLog()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        var textAtStart = vm.ExecutionLogText!;

        await vm.PauseCommand.ExecuteAsync(null);
        await vm.PlayCommand.ExecuteAsync(null); // resume

        Assert.StartsWith(textAtStart, vm.ExecutionLogText);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    [Fact]
    public async Task PlayAsync_Completed_AppendsAProgramEndedLineWithFinalProgress()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        Assert.Contains("Программа завершена: Завершено — общий 100%, шаг 100%", vm.ExecutionLogText);
    }

    [Fact]
    public async Task StopAsync_DuringMotion_LogsTheActualProgressAtTheMomentOfStopping_NotZero()
    {
        var currentTime = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        currentTime = currentTime.AddSeconds(5); // 5 of 15s total estimate (3 points x 5s) = 33%
        progressTimer.RaiseElapsed();
        Assert.True(vm.PhysicalOverallProgress > 0);

        await vm.StopCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok"); // resolves the command already in flight
        await playTask;

        // Regression guard: without capturing progress before ClearProgressTracker() runs (which
        // this Stopped/Faulted branch calls), this would read "общий 0%" instead of the real value.
        Assert.Contains("Программа завершена: Остановлено — общий 33%", vm.ExecutionLogText);
    }

    [Fact]
    public async Task Pause_ThenResume_LogsBothEventsWithProgressAtTheMomentOfEach()
    {
        var currentTime = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        currentTime = currentTime.AddSeconds(3); // 3 of 15s = 20%
        progressTimer.RaiseElapsed();

        await vm.PauseCommand.ExecuteAsync(null);
        Assert.Contains("Пауза — общий 20%", vm.ExecutionLogText);

        currentTime = currentTime.AddSeconds(100); // a long pause
        await vm.PlayCommand.ExecuteAsync(null); // resume
        Assert.Contains("Возобновление — общий 20%", vm.ExecutionLogText);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelExecutionLogTests"`
Expected: FAIL (`ExecutionLogText` doesn't exist yet; no log lines are written).

- [ ] **Step 3: Add the field, the property, and cold-start creation**

In `ArctZ/ViewModels/ProgramViewModel.cs`, add a field next to the existing `_progressTracker` field (around line 30):

```csharp
    private TimeProgressTracker? _progressTracker;
    private ProgramExecutionLog? _executionLog;
```

Add a public property near `OverallProgress`/`PhysicalOverallProgress` (around line 1066):

```csharp
    /// <summary>Text of the most recently STARTED run's log — survives that run's own completion,
    /// replaced only by the next cold Play start. Null until the first Play of the session.</summary>
    public string? ExecutionLogText => _executionLog?.Text;
```

In `PlayAsync`, right after the existing line `TotalSegments = KeyPoints.Count + (ReturnToStartOnFinish ? 1 : 0);` (this is the cold-start branch — the Paused-resume branch returns earlier in the method and never reaches this line):

```csharp
        TotalSegments = KeyPoints.Count + (ReturnToStartOnFinish ? 1 : 0);
        _executionLog = new ProgramExecutionLog(ProgramName, KeyPoints.Count, _now());
```

- [ ] **Step 4: Capture progress before `ClearProgressTracker()`, log pause events and the program-ended bookend**

In `ArctZ/ViewModels/ProgramViewModel.cs`, replace the start of `OnPlaybackStateChanged`:

```csharp
    partial void OnPlaybackStateChanged(PlaybackState value)
    {
        _pauseResumeSignal?.TrySetResult(true);

        // Stop/Faulted abandon the run outright, so nothing is left to wait for. Paused is
        // deliberately absent: a held machine reports Hold rather than Idle, so the same wait
        // simply continues once the operator resumes — no resume-side bookkeeping needed.
        if (value is PlaybackState.Stopped or PlaybackState.Faulted)
        {
            _motionIdleSignal?.TrySetResult(false);
            ClearProgressTracker();
        }

        if (value == PlaybackState.Paused)
        {
            _pausedAt = _now();
        }
        else if (value == PlaybackState.Running && _pausedAt is { } pausedAt)
        {
            _progressTracker?.ShiftForPause(_now() - pausedAt);
            _pausedAt = null;
        }
        else if (value is PlaybackState.Stopped or PlaybackState.Faulted)
        {
            _pausedAt = null;
        }
```

with:

```csharp
    partial void OnPlaybackStateChanged(PlaybackState value)
    {
        // Captured before ClearProgressTracker() (below) can null out _progressTracker — logging
        // after that point would record 0% for a run stopped/faulted mid-motion instead of its
        // actual progress at that instant. See
        // docs/superpowers/specs/2026-08-21-program-execution-log-design.md.
        var loggedOverallProgress = PhysicalOverallProgress;
        var loggedStepProgress = 1.0 - PhysicalPointRemainingFraction;

        _pauseResumeSignal?.TrySetResult(true);

        // Stop/Faulted abandon the run outright, so nothing is left to wait for. Paused is
        // deliberately absent: a held machine reports Hold rather than Idle, so the same wait
        // simply continues once the operator resumes — no resume-side bookkeeping needed.
        if (value is PlaybackState.Stopped or PlaybackState.Faulted)
        {
            _motionIdleSignal?.TrySetResult(false);
            ClearProgressTracker();
        }

        if (value == PlaybackState.Paused)
        {
            _pausedAt = _now();
            _executionLog?.LogPauseStarted(loggedOverallProgress, loggedStepProgress, _now());
        }
        else if (value == PlaybackState.Running && _pausedAt is { } pausedAt)
        {
            _progressTracker?.ShiftForPause(_now() - pausedAt);
            _pausedAt = null;
            _executionLog?.LogPauseEnded(loggedOverallProgress, loggedStepProgress, _now());
        }
        else if (value is PlaybackState.Stopped or PlaybackState.Faulted)
        {
            _pausedAt = null;
        }
```

Then, in the same method, find the existing terminal-state block near the end:

```csharp
        _terminalStatusResetCts?.Cancel();
        _terminalStatusResetCts = null;

        if (value is PlaybackState.Completed or PlaybackState.Stopped or PlaybackState.Faulted)
        {
            var cts = new CancellationTokenSource();
            _terminalStatusResetCts = cts;
            _ = ResetToIdleAfterDelayAsync(value, cts.Token);
        }
    }
```

and add the bookend as the first line inside that `if`:

```csharp
        _terminalStatusResetCts?.Cancel();
        _terminalStatusResetCts = null;

        if (value is PlaybackState.Completed or PlaybackState.Stopped or PlaybackState.Faulted)
        {
            _executionLog?.LogProgramEnded(StatusLabel, loggedOverallProgress, loggedStepProgress, _now());

            var cts = new CancellationTokenSource();
            _terminalStatusResetCts = cts;
            _ = ResetToIdleAfterDelayAsync(value, cts.Token);
        }
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelExecutionLogTests"`
Expected: PASS (5 tests).

- [ ] **Step 6: Run the full playback test class to check for regressions**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: PASS (no change in behavior for existing tests — only new side effects were added).

- [ ] **Step 7: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/ViewModels/ProgramViewModelExecutionLogTests.cs
git commit -m "feat: create execution log on Play, log pause/resume and program-ended events"
```

---

## Task 3: Логирование начала/окончания физического движения к точке

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs`
- Modify: `ArctZ.Tests/ViewModels/ProgramViewModelExecutionLogTests.cs`

**Interfaces:**
- Consumes: `_executionLog` field and `ExecutionLogText` from Task 2; `ProgramExecutionLog.LogMovementStarted`/`LogMovementEnded` from Task 1; existing `PhysicallyExecutingKeyPointId`, `PhysicalOverallProgress`, `PhysicalPointRemainingFraction`.
- Produces: private field `_lastLoggedPhysicalKeyPointId` (`Guid?`), reset by Task 4's edit to `RunPassAsync` (both tasks touch that reset line — Task 4 adds its own reset right next to this one).

- [ ] **Step 1: Write the failing tests**

Append to `ArctZ.Tests/ViewModels/ProgramViewModelExecutionLogTests.cs`:

```csharp
    [Fact]
    public async Task PlayAsync_FirstPhysicalSegment_LogsMovementStartedForTheFirstKeyPoint()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");

        await WaitUntilAsync(() => vm.PhysicallyExecutingKeyPointId == vm.KeyPoints[0].Id, TimeSpan.FromSeconds(1));
        Assert.Contains($"Начало движения к точке «{vm.KeyPoints[0].Label}»", vm.ExecutionLogText);

        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    [Fact]
    public async Task PlayAsync_PhysicalMovementBetweenPoints_LogsEndThenStartPairInOrder()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.PhysicallyExecutingKeyPointId == vm.KeyPoints[0].Id, TimeSpan.FromSeconds(1));

        // Real motion crossing well into the second key point's territory (10 units past it).
        transport.SimulateReceivedLine("<Run|WPos:15.000,0.000,0.000,0.000|FS:500,0>");
        await WaitUntilAsync(() => vm.PhysicallyExecutingKeyPointId == vm.KeyPoints[1].Id, TimeSpan.FromSeconds(1));

        var lines = vm.ExecutionLogText!.Split(Environment.NewLine);
        var endedIndex = Array.FindIndex(lines, l => l.Contains($"Окончание движения к точке «{vm.KeyPoints[0].Label}»"));
        var startedIndex = Array.FindIndex(lines, l => l.Contains($"Начало движения к точке «{vm.KeyPoints[1].Label}»"));
        Assert.True(endedIndex >= 0, "expected an 'ended' line for the first key point");
        Assert.True(startedIndex > endedIndex, "expected the 'started' line for the second key point right after it");

        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelExecutionLogTests"`
Expected: FAIL (the two new tests — no movement lines are written yet).

- [ ] **Step 3: Implement**

In `ArctZ/ViewModels/ProgramViewModel.cs`, add a field near `_lastSegmentIndex`/`_currentPassBackward` (around line 848):

```csharp
    private Guid? _lastLoggedPhysicalKeyPointId;
```

Extend `OnProgressTrackerChanged` (around line 1080):

```csharp
    private void OnProgressTrackerChanged()
    {
        OnPropertyChanged(nameof(PhysicalOverallProgress));
        OnPropertyChanged(nameof(PhysicalPointRemainingFraction));
        OnPropertyChanged(nameof(PhysicalPointHasTimeWarning));
        OnPropertyChanged(nameof(PhysicallyExecutingKeyPointId));

        LogPhysicalMovementTransitionIfChanged();
    }

    /// <summary>Movement start/end events are driven by the physically active key point (not
    /// ack) — see docs/superpowers/specs/2026-08-21-program-execution-log-design.md. A transition
    /// produces an "ended" line for the point being left (if any) immediately followed by a
    /// "started" line for the point being entered (if any), both stamped with the same instant.</summary>
    private void LogPhysicalMovementTransitionIfChanged()
    {
        var current = PhysicallyExecutingKeyPointId;
        if (current == _lastLoggedPhysicalKeyPointId)
        {
            return;
        }

        var now = _now();
        var overallProgress = PhysicalOverallProgress;
        var stepProgress = 1.0 - PhysicalPointRemainingFraction;

        if (_lastLoggedPhysicalKeyPointId is { } previousId
            && KeyPoints.FirstOrDefault(k => k.Id == previousId) is { } previousPoint)
        {
            _executionLog?.LogMovementEnded(previousPoint.Label, overallProgress, stepProgress, now);
        }

        if (current is { } currentId && KeyPoints.FirstOrDefault(k => k.Id == currentId) is { } currentPoint)
        {
            _executionLog?.LogMovementStarted(currentPoint.Label, overallProgress, stepProgress, now);
        }

        _lastLoggedPhysicalKeyPointId = current;
    }
```

In `RunPassAsync`, right before the existing `OnProgressTrackerChanged();` call at the end of the tracker setup (around line 1484):

```csharp
        _progressTracker = new TimeProgressTracker(steps, startingPose, _now());
        _progressTracker.Changed += OnProgressTrackerChanged;
        _progressTracker.SegmentTimeOverage += OnSegmentTimeOverage;
        _lastLoggedPhysicalKeyPointId = null; // a fresh pass starts its own movement-transition tracking, even if its first point is the same KeyPoint the previous pass ended on (PingPong)
        OnProgressTrackerChanged();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelExecutionLogTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Run the full playback test class to check for regressions**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/ViewModels/ProgramViewModelExecutionLogTests.cs
git commit -m "feat: log physical movement start/end transitions between key points"
```

---

## Task 4: Логирование рассинхронизации ack/физика

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs`
- Modify: `ArctZ.Tests/ViewModels/ProgramViewModelExecutionLogTests.cs`

**Interfaces:**
- Consumes: `ProgramExecutionLog.LogAckDesync` from Task 1; existing `CurrentSegmentIndex` (ack) and `_progressTracker.CurrentSegmentIndex` (physical, `TimeProgressTracker.CurrentSegmentIndex`, public `int?`).
- Produces: private field `_ackDesyncLogged` (`bool`), reset alongside `_lastLoggedPhysicalKeyPointId` in `RunPassAsync`.

- [ ] **Step 1: Write the failing test**

Append to `ArctZ.Tests/ViewModels/ProgramViewModelExecutionLogTests.cs`:

```csharp
    [Fact]
    public async Task PlayAsync_AckOutrunsPhysicalPositionByMoreThanOnePoint_LogsAckDesyncOnce()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        // All three acks land before any position update — ack jumps to segment 2 while the
        // physical tracker (position never moved) stays on segment 0. Gap of 2 exceeds the
        // >1-point desync threshold. Same scenario as
        // PlayAsync_PhysicallyExecutingKeyPointId_CanLagTheAckBasedHighlightWhenTheBufferRunsAhead
        // in ProgramViewModelPlaybackTests.
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.CurrentlyExecutingKeyPointId == vm.KeyPoints[2].Id, TimeSpan.FromSeconds(1));

        Assert.Equal(1, CountOccurrences(vm.ExecutionLogText!, "Рассинхронизация: буфер контроллера опережает факт"));

        // A further recompute while the position still hasn't moved must not duplicate the line.
        transport.SimulateReceivedLine("<Run|WPos:0.000,0.000,0.000,0.000|FS:500,0>");
        Assert.Equal(1, CountOccurrences(vm.ExecutionLogText!, "Рассинхронизация: буфер контроллера опережает факт"));

        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }

    private static int CountOccurrences(string text, string substring)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(substring, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += substring.Length;
        }

        return count;
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~PlayAsync_AckOutrunsPhysicalPositionByMoreThanOnePoint_LogsAckDesyncOnce"`
Expected: FAIL (0 occurrences instead of 1 — no desync detection yet).

- [ ] **Step 3: Implement**

In `ArctZ/ViewModels/ProgramViewModel.cs`, add a field next to `_lastLoggedPhysicalKeyPointId` (Task 3):

```csharp
    private Guid? _lastLoggedPhysicalKeyPointId;
    private bool _ackDesyncLogged;
```

Extend `OnProgressTrackerChanged`:

```csharp
    private void OnProgressTrackerChanged()
    {
        OnPropertyChanged(nameof(PhysicalOverallProgress));
        OnPropertyChanged(nameof(PhysicalPointRemainingFraction));
        OnPropertyChanged(nameof(PhysicalPointHasTimeWarning));
        OnPropertyChanged(nameof(PhysicallyExecutingKeyPointId));

        LogPhysicalMovementTransitionIfChanged();
        LogAckDesyncIfNewlyDetected();
    }
```

Add the new method:

```csharp
    /// <summary>Edge-triggered: logs once when the ack-confirmed segment gets more than one point
    /// ahead of the physically active one, then stays silent until the gap closes back to ≤1 and
    /// widens past the threshold again. No "recovered" line is logged — see the design doc.</summary>
    private void LogAckDesyncIfNewlyDetected()
    {
        if (_progressTracker is not { } tracker
            || CurrentSegmentIndex is not { } ackIndex
            || tracker.CurrentSegmentIndex is not { } physicalIndex)
        {
            return;
        }

        if (ackIndex - physicalIndex > 1)
        {
            if (!_ackDesyncLogged)
            {
                _executionLog?.LogAckDesync(
                    ackIndex, physicalIndex, PhysicalOverallProgress, 1.0 - PhysicalPointRemainingFraction, _now());
                _ackDesyncLogged = true;
            }
        }
        else
        {
            _ackDesyncLogged = false;
        }
    }
```

In `RunPassAsync`, next to the reset added in Task 3:

```csharp
        _lastLoggedPhysicalKeyPointId = null; // a fresh pass starts its own movement-transition tracking, even if its first point is the same KeyPoint the previous pass ended on (PingPong)
        _ackDesyncLogged = false;
        OnProgressTrackerChanged();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelExecutionLogTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Run the full playback test class to check for regressions**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/ViewModels/ProgramViewModelExecutionLogTests.cs
git commit -m "feat: log ack-vs-physical desync when the controller buffer outruns real motion"
```

---

## Task 5: Логирование рассинхронизации по превышению расчётного времени точки

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs`
- Modify: `ArctZ.Tests/ViewModels/ProgramViewModelExecutionLogTests.cs`

**Interfaces:**
- Consumes: `ProgramExecutionLog.LogTimeOverage` from Task 1; existing `OnSegmentTimeOverage(int segmentIndex, double actualSeconds, double estimatedSeconds)` handler and its existing `JibProgram.TargetKeyPoint` resolution.

- [ ] **Step 1: Write the failing test**

Append to `ArctZ.Tests/ViewModels/ProgramViewModelExecutionLogTests.cs`:

```csharp
    [Fact]
    public async Task PlayAsync_SegmentEndsOverTime_LogsATimeOverageDesyncLine()
    {
        var currentTime = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out var transport, out var progressTimer, () => currentTime);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport); // each key point's TransitionSeconds is 5
        transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        currentTime = currentTime.AddSeconds(6); // 20% over the 5s estimate for segment 0
        progressTimer.RaiseElapsed();

        transport.SimulateReceivedLine("<Run|WPos:5.000,0.000,0.000,0.000|FS:0,0>"); // leaves segment 0 while over time

        Assert.Contains(
            $"Рассинхронизация: превышение расчётного времени точки «{vm.KeyPoints[0].Label}» (6.0с факт / 5.0с расчёт)",
            vm.ExecutionLogText);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~PlayAsync_SegmentEndsOverTime_LogsATimeOverageDesyncLine"`
Expected: FAIL (no such line in `ExecutionLogText`).

- [ ] **Step 3: Implement**

In `ArctZ/ViewModels/ProgramViewModel.cs`, extend the existing `OnSegmentTimeOverage`:

```csharp
    private void OnSegmentTimeOverage(int segmentIndex, double actualSeconds, double estimatedSeconds)
    {
        if (Services.Program.JibProgram.TargetKeyPoint(KeyPoints, segmentIndex, _currentPassBackward) is not { } keyPointId)
        {
            return;
        }

        AddKeyPointMessage(keyPointId, new KeyPointMessage(MessageLevel.Warning,
            $"Превышение фактического времени перемещения ({actualSeconds:F0} сек.) над установленным ({estimatedSeconds:F0} сек)"));

        var pointLabel = KeyPoints.FirstOrDefault(k => k.Id == keyPointId)?.Label;
        _executionLog?.LogTimeOverage(
            pointLabel, actualSeconds, estimatedSeconds, PhysicalOverallProgress, 1.0 - PhysicalPointRemainingFraction, _now());
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelExecutionLogTests"`
Expected: PASS (9 tests).

- [ ] **Step 5: Run the full playback test class to check for regressions**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: PASS (in particular the existing `PlayAsync_SegmentEndsOverTime_RecordsAKeyPointMessageForThatPoint` and `PlayAsync_RepeatedIdenticalOverageAcrossTwoRuns_DoesNotDuplicateTheMessage` — this task adds a side effect to the same handler, must not change their existing assertions).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/ViewModels/ProgramViewModelExecutionLogTests.cs
git commit -m "feat: log time-overage desync alongside the existing key-point warning message"
```

---

## Task 6: Кнопка «Скопировать лог программы» в диалоге «О программе»

**Files:**
- Modify: `ArctZ/ViewModels/AboutViewModel.cs`
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs` (`OpenAbout`)
- Modify: `ArctZ/Views/MainView.axaml`
- Modify: `ArctZ/Views/MainView.axaml.cs`
- Modify: `ArctZ.Tests/ViewModels/ProgramViewModelAboutTests.cs`

**Interfaces:**
- Consumes: `ProgramViewModel.ExecutionLogText` from Task 2.
- Produces: `AboutViewModel.ExecutionLogText` (`string`, never null), `AboutViewModel.HasExecutionLog` (`bool`), `AboutViewModel.IsExecutionLogCopied` (`bool`), `AboutViewModel.MarkExecutionLogCopied()`.

- [ ] **Step 1: Write the failing tests**

Add to `ArctZ.Tests/ViewModels/ProgramViewModelAboutTests.cs` (add `using static ArctZ.Tests.TestSupport.AsyncAssert;` to the usings at the top):

```csharp
    [Fact]
    public void OpenAbout_BeforeAnyProgramHasRun_HasExecutionLogIsFalse()
    {
        var vm = CreateViewModel(out _);

        vm.OpenAboutCommand.Execute(null);

        Assert.False(vm.About!.HasExecutionLog);
        Assert.Equal(string.Empty, vm.About.ExecutionLogText);
    }

    [Fact]
    public async Task OpenAbout_AfterACompletedRun_ExposesTheExecutionLog()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();

        foreach (var pose in new[] { "0,0,0,0", "10,0,0,0" })
        {
            transport.SimulateReceivedLine($"<Idle|WPos:{pose}|FS:0,0>");
            vm.CaptureKeyPointCommand.Execute(null);
        }

        for (var i = 0; i < vm.KeyPoints.Count; i++)
        {
            vm.KeyPoints[i] = vm.KeyPoints[i] with { TransitionSeconds = 5, ContinuousBlend = true };
        }

        vm.ProgramId = Guid.NewGuid();
        vm.IsDirty = false;

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:10.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        vm.OpenAboutCommand.Execute(null);

        Assert.True(vm.About!.HasExecutionLog);
        Assert.Contains("Программа запущена", vm.About.ExecutionLogText);
        Assert.Contains("Программа завершена: Завершено", vm.About.ExecutionLogText);
    }

    [Fact]
    public void About_TracksThatTheExecutionLogWasCopied()
    {
        var vm = CreateViewModel(out _);
        vm.OpenAboutCommand.Execute(null);

        Assert.False(vm.About!.IsExecutionLogCopied);
        vm.About.MarkExecutionLogCopied();

        Assert.True(vm.About.IsExecutionLogCopied);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelAboutTests"`
Expected: FAIL (compile error — `HasExecutionLog`/`ExecutionLogText`/`IsExecutionLogCopied`/`MarkExecutionLogCopied` don't exist on `AboutViewModel` yet).

- [ ] **Step 3: Extend `AboutViewModel`**

Replace the contents of `ArctZ/ViewModels/AboutViewModel.cs`:

```csharp
using System.Collections.Generic;
using ArctZ.Services.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArctZ.ViewModels;

/// <summary>
/// The "О программе" dialog. Holds a finished report rather than live view models:
/// it is rebuilt on every open, so what the user reads and what they copy are the
/// same snapshot, taken at the same instant.
/// </summary>
public partial class AboutViewModel : ViewModelBase
{
    private readonly DiagnosticsReport _report;

    public AboutViewModel(DiagnosticsReport report, string? executionLogText)
    {
        _report = report;
        ReportText = report.ToText();
        ExecutionLogText = executionLogText ?? string.Empty;
    }

    public string AppName => BuildInfo.AppName;

    public IReadOnlyList<DiagnosticsSection> Sections => _report.Sections;

    /// <summary>The whole report as plain text — exactly what the copy button puts on the clipboard.</summary>
    public string ReportText { get; }

    /// <summary>Set once the report has been copied. Not reset on a timer: the dialog is
    /// rebuilt on every open, so the confirmation naturally lasts exactly as long as this view.</summary>
    [ObservableProperty]
    private bool _isCopied;

    public void MarkCopied() => IsCopied = true;

    /// <summary>Text of the most recently started program run's execution log — empty if no
    /// program has been played yet this session (see ProgramViewModel.ExecutionLogText).</summary>
    public string ExecutionLogText { get; }

    public bool HasExecutionLog => !string.IsNullOrEmpty(ExecutionLogText);

    [ObservableProperty]
    private bool _isExecutionLogCopied;

    public void MarkExecutionLogCopied() => IsExecutionLogCopied = true;
}
```

- [ ] **Step 4: Wire `OpenAbout`**

In `ArctZ/ViewModels/ProgramViewModel.cs`, update:

```csharp
    [RelayCommand]
    private void OpenAbout()
    {
        About = new AboutViewModel(DiagnosticsReportBuilder.Build(CaptureDiagnostics()));
        IsSideMenuOpen = false;
    }
```

to:

```csharp
    [RelayCommand]
    private void OpenAbout()
    {
        About = new AboutViewModel(DiagnosticsReportBuilder.Build(CaptureDiagnostics()), ExecutionLogText);
        IsSideMenuOpen = false;
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelAboutTests"`
Expected: PASS (all tests in the class, including the 3 new ones).

- [ ] **Step 6: Add the copy button to `MainView.axaml`**

In `ArctZ/Views/MainView.axaml`, right after the existing `CopyDiagnosticsButton` block (ends around line 669 with `</Button>`), insert:

```xml
                            <Button DockPanel.Dock="Bottom" x:Name="CopyExecutionLogButton" Classes="primary"
                                    HorizontalAlignment="Stretch" Margin="0,8,0,0"
                                    IsVisible="{Binding About.HasExecutionLog}"
                                    Click="OnCopyExecutionLogClick">
                                <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Center">
                                    <materialIcons:MaterialIcon Kind="ContentCopy" Width="16" Height="16" VerticalAlignment="Center"
                                                               IsVisible="{Binding !About.IsExecutionLogCopied}" />
                                    <materialIcons:MaterialIcon Kind="Check" Width="16" Height="16" VerticalAlignment="Center"
                                                               IsVisible="{Binding About.IsExecutionLogCopied}" />
                                    <TextBlock Text="Скопировать лог программы" VerticalAlignment="Center"
                                               IsVisible="{Binding !About.IsExecutionLogCopied}" />
                                    <TextBlock Text="Скопировано" VerticalAlignment="Center"
                                               IsVisible="{Binding About.IsExecutionLogCopied}" />
                                </StackPanel>
                            </Button>
```

- [ ] **Step 7: Add the click handler to `MainView.axaml.cs`**

In `ArctZ/Views/MainView.axaml.cs`, right after `OnCopyDiagnosticsClick`:

```csharp
        /// <summary>Mirrors OnCopyDiagnosticsClick — same clipboard access pattern, different
        /// source text (the execution log instead of the diagnostics report).</summary>
        private async void OnCopyExecutionLogClick(object? sender, RoutedEventArgs e)
        {
            if (ViewModel?.About is not { } about)
            {
                return;
            }

            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            {
                return;
            }

            await clipboard.SetTextAsync(about.ExecutionLogText);
            about.MarkExecutionLogCopied();
        }
```

- [ ] **Step 8: Build the Desktop head to confirm the XAML compiles**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: Build succeeds.

- [ ] **Step 9: Run the full test suite to check for regressions**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS (all tests, including every class touched by Tasks 1-6).

- [ ] **Step 10: Commit**

```bash
git add ArctZ/ViewModels/AboutViewModel.cs ArctZ/ViewModels/ProgramViewModel.cs ArctZ/Views/MainView.axaml ArctZ/Views/MainView.axaml.cs ArctZ.Tests/ViewModels/ProgramViewModelAboutTests.cs
git commit -m "feat: add a copy-execution-log button to the About dialog"
```

---

## Task 7: Живая проверка UI (обязательна по CLAUDE.md)

**Files:** none (verification only).

- [ ] **Step 1: Build the Desktop head**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: Build succeeds.

- [ ] **Step 2: Run the Desktop app**

Run: `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj`

The app must actually be running (not just built) before the next step.

- [ ] **Step 3: Ask the user to exercise the feature**

Ask the user to, using the running app:
1. Connect (real device or the built-in mock), create/load a program with at least 2-3 key points.
2. Run it (Пуск), pause and resume once mid-run.
3. Open «О программе» (side menu) while or after the run and locate the new «Скопировать лог программы» button.
4. Click it and paste the clipboard contents somewhere to inspect (a text editor).
5. If practical, close the app and reopen «О программе» before ever pressing Play in the new session, to check the button's absence.

- [ ] **Step 4: Ask targeted questions via `AskUserQuestion`, one per changed behavior**

Ask separately (not as one combined question) whether:
- The «Скопировать лог программы» button appeared only after Play had been pressed at least once, and was absent/missing beforehand.
- The copied text contains a start line, movement lines for the points visited, and a pause/resume pair (if the user paused).
- The copied text ends with a "Программа завершена" line matching how the run actually ended.
- The progress percentages in the lines look plausible (roughly matching what the on-screen progress bar showed at those moments).

- [ ] **Step 5: Address any issues found, then re-verify**

If the user reports a problem, fix it and repeat Steps 2-4 for the specific behavior that was wrong.
