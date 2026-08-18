# G93 Inverse-Time Moves Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ключевые точки программы задают время перехода в секундах, а все перемещения уходят на устройство командой `G93 G1 ... F<60/t>` (inverse time feed) вместо `G1 ... F<ед/мин>`.

**Architecture:** Формат строки движения централизуется в новом статическом классе `InverseTimeMove` (`ArctZ/Services/Program/`), который заодно владеет правилом «непозитивное время = 5 секунд». `KeyPoint.FeedRateUnitsPerMin` заменяется на `KeyPoint.TransitionSeconds`; `TrajectoryCompiler` и обе ручные команды перемещения в `ProgramViewModel` переходят на хелпер. `MockDeviceTransport` получает второй режим движения — линейную интерполяцию по времени, — чтобы Демо-режим соответствовал реальности.

**Tech Stack:** .NET 10, C#, Avalonia UI, CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-18-g93-inverse-time-moves-design.md`

## Global Constraints

- Ветка: `master`, без worktree. Работать прямо в `z:\Jib S\Application\ArctZ`.
- Тесты запускать так: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`. Фильтр по классу: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ИмяКласса"`.
- Сборку solution `ArctZ.slnx` для проверки **не** использовать — она флакает на Android-restore. Проверять `dotnet build ArctZ/ArctZ.csproj` и `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`.
- Все числа в G-code форматируются с `CultureInfo.InvariantCulture`.
- Формат координат: `"0.###"`. Формат `F` в inverse-time режиме: `"0.#######"`.
- `G93` пишется в каждой строке движения; `G94` приложение не отправляет никогда.
- Значение по умолчанию для времени перехода — `5` секунд, константа `InverseTimeMove.DefaultTransitionSeconds`.
- Джог (`$J=`, `FluidNcCommandSerializer`, `JogCommandFactory`) не трогать: он остаётся скоростным.
- Русскоязычные комментарии в коде допустимы там, где они уже есть; новые тексты UI — по-русски.
- Коммиты — с `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>` в конце сообщения.

---

### Task 1: `InverseTimeMove` — формат строки движения

**Files:**
- Create: `ArctZ/Services/Program/InverseTimeMove.cs`
- Test: `ArctZ.Tests/Services/Program/InverseTimeMoveTests.cs`

**Interfaces:**
- Consumes: `ArctZ.Services.Device.MachinePose` (record с `double X, Y, Z, A`).
- Produces:
  - `public const double InverseTimeMove.DefaultTransitionSeconds = 5.0`
  - `public static double InverseTimeMove.EffectiveSeconds(double seconds)`
  - `public static string InverseTimeMove.Line(MachinePose pose, double seconds)`

- [ ] **Step 1: Write the failing test**

Создать `ArctZ.Tests/Services/Program/InverseTimeMoveTests.cs`:

```csharp
using ArctZ.Services.Device;
using ArctZ.Services.Program;

namespace ArctZ.Tests.Services.Program;

public class InverseTimeMoveTests
{
    [Fact]
    public void Line_FifteenSeconds_EmitsG93WithInverseFeedOfFour()
    {
        var line = InverseTimeMove.Line(new MachinePose(60, 0, 0, 0), 15);

        Assert.Equal("G93 G1 X60 Y0 Z0 A0 F4", line);
    }

    [Fact]
    public void Line_FormatsCoordinatesWithThreeDecimalsAndInvariantCulture()
    {
        var line = InverseTimeMove.Line(new MachinePose(12.345, -6.7, 0, 90), 7.5);

        Assert.Equal("G93 G1 X12.345 Y-6.7 Z0 A90 F8", line);
    }

    /// <summary>Час перехода даёт F0.0166667 — формат "0.###" округлил бы до 0.017 (ошибка ~2%).</summary>
    [Fact]
    public void Line_OneHourTransition_KeepsSevenDecimalsOfFeedPrecision()
    {
        var line = InverseTimeMove.Line(new MachinePose(60, 0, 0, 0), 3600);

        Assert.Equal("G93 G1 X60 Y0 Z0 A0 F0.0166667", line);
    }

    /// <summary>Ноль — это старый файл программы или пустое поле ввода, а не «максимально быстро».</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    public void Line_NonPositiveSeconds_FallsBackToTheDefaultFiveSeconds(double seconds)
    {
        var line = InverseTimeMove.Line(new MachinePose(60, 0, 0, 0), seconds);

        Assert.Equal("G93 G1 X60 Y0 Z0 A0 F12", line);
    }

    [Theory]
    [InlineData(0.0, 5.0)]
    [InlineData(-1.0, 5.0)]
    [InlineData(12.5, 12.5)]
    public void EffectiveSeconds_ReplacesNonPositiveValuesOnly(double input, double expected)
    {
        Assert.Equal(expected, InverseTimeMove.EffectiveSeconds(input));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~InverseTimeMoveTests"`
Expected: сборка падает — `CS0103: The name 'InverseTimeMove' does not exist`.

- [ ] **Step 3: Write minimal implementation**

Создать `ArctZ/Services/Program/InverseTimeMove.cs`:

```csharp
using System.Globalization;
using ArctZ.Services.Device;

namespace ArctZ.Services.Program;

/// <summary>
/// Единственное место, где формируется строка перемещения. Все движения идут
/// в режиме inverse time (G93): F = 1 / t, где t — время блока в минутах.
/// G93 повторяется в каждой строке намеренно — иначе любой путь останова
/// (Stop, StopAndDrain, ошибка, обрыв связи) был бы обязан вернуть G94, и
/// один пропущенный возврат оставил бы машину в режиме, где команда без F
/// даёт error:2.
/// </summary>
public static class InverseTimeMove
{
    /// <summary>Время перехода по умолчанию: подставляется вместо непозитивного значения.</summary>
    public const double DefaultTransitionSeconds = 5.0;

    /// <summary>
    /// Непозитивное время нельзя клампить к «почти нулю»: F стремился бы к
    /// бесконечности, и переход превратился бы в бросок на максимальной
    /// скорости оси. Это худший ответ и на пустое поле ввода, и на старый файл
    /// программы, где TransitionSeconds десериализуется в 0.
    /// </summary>
    public static double EffectiveSeconds(double seconds) =>
        seconds > 0 ? seconds : DefaultTransitionSeconds;

    public static string Line(MachinePose pose, double seconds) =>
        $"G93 G1 X{Axis(pose.X)} Y{Axis(pose.Y)} Z{Axis(pose.Z)} A{Axis(pose.A)} F{Feed(seconds)}";

    private static string Feed(double seconds) =>
        (60.0 / EffectiveSeconds(seconds)).ToString("0.#######", CultureInfo.InvariantCulture);

    private static string Axis(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~InverseTimeMoveTests"`
Expected: PASS, 8 тестов.

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Services/Program/InverseTimeMove.cs ArctZ.Tests/Services/Program/InverseTimeMoveTests.cs
git commit -m "feat: add InverseTimeMove G93 line factory

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: `KeyPoint.TransitionSeconds` и перевод компилятора на G93

Это атомарная задача: переименование поля записи ломает компиляцию во всех
потребителях сразу, поэтому они правятся одним коммитом.

**Files:**
- Modify: `ArctZ/Services/Program/KeyPoint.cs:19` (поле записи)
- Modify: `ArctZ/Services/Program/TrajectoryCompiler.cs` (весь файл)
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs:516`, `:650`, `:1228`
- Test: `ArctZ.Tests/Services/Program/TrajectoryCompilerTests.cs` (переписывается)
- Test (механические правки конструкторов/ассертов): `ArctZ.Tests/Services/Program/JibProgramTests.cs:11`, `ArctZ.Tests/Services/Program/JsonFileProgramStorageTests.cs:45-46`, `ArctZ.Tests/Services/Program/KeyPointTests.cs:10`, `ArctZ.Tests/ViewModels/ProgramViewModelAboutTests.cs:28`, `ArctZ.Tests/ViewModels/ProgramViewModelAuthoringTests.cs:341-351`, `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs`, `ArctZ.Tests/ViewModels/ProgramViewModelStatusLabelTests.cs:33`, `ArctZ.Tests.Screenshots/ScreenshotGalleryTests.cs:35,38`

**Interfaces:**
- Consumes: `InverseTimeMove.Line(MachinePose, double)`, `InverseTimeMove.EffectiveSeconds(double)`, `InverseTimeMove.DefaultTransitionSeconds` (Task 1).
- Produces: `KeyPoint.TransitionSeconds` (double, 6-й позиционный параметр записи, на месте бывшего `FeedRateUnitsPerMin`); `CompiledStep.EstimatedDurationSeconds` для шага движения теперь равен командованному времени.

- [ ] **Step 1: Write the failing test**

Полностью заменить содержимое `ArctZ.Tests/Services/Program/TrajectoryCompilerTests.cs` на:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~TrajectoryCompilerTests"`
Expected: сборка падает — `CS1739`/`CS7036` на именованном аргументе `transitionSeconds` в `Point(...)`, потому что запись `KeyPoint` всё ещё имеет `FeedRateUnitsPerMin`.

- [ ] **Step 3: Переименовать поле записи `KeyPoint`**

В `ArctZ/Services/Program/KeyPoint.cs` заменить строку

```csharp
    double FeedRateUnitsPerMin,
```

на

```csharp
    double TransitionSeconds,
```

и дополнить XML-комментарий записи: после «how long it stays» добавить
предложение

```
/// TransitionSeconds — время перехода В эту точку (секунды); у первой точки
/// оно используется только для возврата в начальную позицию по завершении.
```

- [ ] **Step 4: Переписать `TrajectoryCompiler`**

Полностью заменить содержимое `ArctZ/Services/Program/TrajectoryCompiler.cs` на:

```csharp
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
```

`using System;` из этого файла уходит вместе с `Distance()`/`EstimateDuration()`
(`Math` больше не используется), а `using ArctZ.Services.Device;` остаётся —
`MachinePose` живёт там.

- [ ] **Step 5: Перевести обе ручные команды `ProgramViewModel` на хелпер**

В `ArctZ/ViewModels/ProgramViewModel.cs`:

1. Строка ~516, в `CaptureKeyPoint`, заменить

```csharp
            FeedRateUnitsPerMin: 500,
```

на

```csharp
            TransitionSeconds: InverseTimeMove.DefaultTransitionSeconds,
```

2. Строка ~650, в `MoveMachineToKeyPointAsync`, заменить

```csharp
        var line = $"G1 X{FormatAxis(pose.X)} Y{FormatAxis(pose.Y)} Z{FormatAxis(pose.Z)} A{FormatAxis(pose.A)} F{FormatAxis(keyPoint.FeedRateUnitsPerMin)}";
```

на

```csharp
        var line = InverseTimeMove.Line(pose, keyPoint.TransitionSeconds);
```

3. Строка ~1228, в `RunReturnToStartMoveAsync`, заменить

```csharp
        var line = $"G1 X{FormatAxis(start.Pose.X)} Y{FormatAxis(start.Pose.Y)} Z{FormatAxis(start.Pose.Z)} A{FormatAxis(start.Pose.A)} F{FormatAxis(start.FeedRateUnitsPerMin)}";
```

на

```csharp
        var line = InverseTimeMove.Line(start.Pose, start.TransitionSeconds);
```

4. Если после этого `FormatAxis` больше нигде не используется (проверить
   `grep -n "FormatAxis" ArctZ/ViewModels/ProgramViewModel.cs`), удалить его
   определение вместе с `using System.Globalization;`, если тот стал лишним.

- [ ] **Step 6: Обновить остальные тесты, конструирующие `KeyPoint`**

Механические замены (позиционный аргумент остался шестым, меняется только имя
и смысл значения):

```bash
cd "z:/Jib S/Application/ArctZ"
sed -i 's/FeedRateUnitsPerMin: 500/TransitionSeconds: 5/g' \
  ArctZ.Tests/Services/Program/JibProgramTests.cs \
  ArctZ.Tests/Services/Program/JsonFileProgramStorageTests.cs \
  ArctZ.Tests/Services/Program/KeyPointTests.cs \
  ArctZ.Tests/ViewModels/ProgramViewModelAboutTests.cs \
  ArctZ.Tests.Screenshots/ScreenshotGalleryTests.cs
sed -i 's/FeedRateUnitsPerMin = 500/TransitionSeconds = 5/g' \
  ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs \
  ArctZ.Tests/ViewModels/ProgramViewModelStatusLabelTests.cs
```

- [ ] **Step 7: Обновить ассерты на отправленные строки**

`SeedTwoSegmentProgram` задаёт `TransitionSeconds = 5`, поэтому строка
возврата в начало становится `G93 G1 X0 Y0 Z0 A0 F12`, а строки движения
начинаются с `G93`, а не с `G1`:

```bash
cd "z:/Jib S/Application/ArctZ"
sed -i 's/"G1 X0 Y0 Z0 A0 F500"/"G93 G1 X0 Y0 Z0 A0 F12"/g' \
  ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs
sed -i 's/StartsWith("G1", StringComparison.Ordinal)/StartsWith("G93", StringComparison.Ordinal)/g' \
  ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs
```

В `ArctZ.Tests/ViewModels/ProgramViewModelAuthoringTests.cs` заменить тест
`MoveMachineToKeyPoint_SendsG1MoveToThePointsPoseAndFeed` (строки ~340-351)
целиком на:

```csharp
    [Fact]
    public async Task MoveMachineToKeyPoint_SendsInverseTimeMoveToThePointsPoseAndTime()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);

        _ = vm.MoveMachineToKeyPointCommand.ExecuteAsync(vm.KeyPoints[0]);

        Assert.Contains(transport.SentLines, l => l == "G93 G1 X0 Y0 Z0 A0 F12");
    }
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS, весь прогон зелёный. Если какой-то класс `ProgramViewModel*Tests`
не завершается за десятки секунд — это зависание на неотвеченном диалоге, а не
медленный тест (см. `CLAUDE.md`, раздел про асинхронные диалоги-«ворота»);
искать неучтённый ассерт, а не увеличивать таймаут.

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: Build succeeded.

Run: `dotnet build ArctZ.Tests.Screenshots/ArctZ.Tests.Screenshots.csproj`
Expected: Build succeeded (этот проект тоже конструирует `KeyPoint`, но в
обычный прогон тестов не входит).

- [ ] **Step 9: Commit**

```bash
git add -A ArctZ ArctZ.Tests ArctZ.Tests.Screenshots
git commit -m "feat: key points store transition time, moves go out as G93

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Режим inverse-time в мок-устройстве

Без этого Демо-режим на строке `G93 G1 X60 ... F4` поехал бы со скоростью
4 ед/мин вместо 60 единиц за 15 секунд — на порядки медленнее заданного.

**Files:**
- Modify: `ArctZ/Services/Device/Simulation/MockDeviceTransport.cs` (поля ~36-40, `SendRawByteAsync` ~124-138, `ProcessLine` ~233-253, `AdvanceMotion` ~255-286, `FormatStatusLine` ~303)
- Test: `ArctZ.Tests/Services/Device/MockDeviceTransportTests.cs`

**Interfaces:**
- Consumes: строки вида `G93 G1 X.. Y.. Z.. A.. F<1/t_min>` из Task 2.
- Produces: ничего для последующих задач (внутреннее поведение симулятора).

- [ ] **Step 1: Write the failing test**

Добавить в конец класса `MockDeviceTransportTests` (перед закрывающей скобкой):

```csharp
    /// <summary>
    /// G93: F — это 1/t в минутах, а не единиц в минуту. F12 = 5 секунд;
    /// при тике 100 мс это ровно 50 тиков до цели, независимо от дистанции.
    /// </summary>
    [Fact]
    public async Task SendLineAsync_InverseTimeMove_ArrivesAfterTheCommandedTime()
    {
        await _mock.ConnectAsync("demo");

        await _mock.SendLineAsync("G93 G1 X60 Y0 Z0 A0 F12");
        _ticker.RaiseElapsed(); // dequeues + acks

        // Тик, снявший строку с очереди, уже двигает: до цели ровно 50 тиков по 100 мс.
        for (var i = 0; i < 48; i++)
        {
            _ticker.RaiseElapsed();
        }

        var beforeArrival = QueryStatus();
        Assert.NotEqual(new MachinePose(60, 0, 0, 0), beforeArrival.WPos);

        _ticker.RaiseElapsed();

        var status = QueryStatus();
        Assert.Equal(new MachinePose(60, 0, 0, 0), status.WPos);
        Assert.Equal(MachineState.Idle, status.State);
    }

    /// <summary>Координированное движение: обе оси приходят одновременно, а не по очереди.</summary>
    [Fact]
    public async Task SendLineAsync_InverseTimeMove_MovesAllAxesProportionally()
    {
        await _mock.ConnectAsync("demo");

        await _mock.SendLineAsync("G93 G1 X60 Y6 Z0 A0 F12");
        _ticker.RaiseElapsed(); // ack + первый шаг
        for (var i = 0; i < 24; i++)
        {
            _ticker.RaiseElapsed();
        }

        var halfway = QueryStatus(); // 25 тиков = 2.5 с из 5 с

        Assert.Equal(30.0, halfway.WPos!.Value.X, 3);
        Assert.Equal(3.0, halfway.WPos!.Value.Y, 3);
    }

    /// <summary>Джог остаётся скоростным: F600 = 600 ед/мин = 1 единица за тик 100 мс.</summary>
    [Fact]
    public async Task SendLineAsync_JogAfterInverseTimeMove_StillUsesFeedRateSemantics()
    {
        await _mock.ConnectAsync("demo");
        await _mock.SendLineAsync("G93 G1 X60 Y0 Z0 A0 F12");
        _ticker.RaiseElapsed();
        await _mock.SendRawByteAsync(0x18); // soft reset drops the inverse-time move

        await _mock.SendLineAsync("$J=G91 G21 X10 Y0 Z0 A0 F600");
        _ticker.RaiseElapsed(); // ack + first 1-unit step
        for (var i = 0; i < 20; i++)
        {
            _ticker.RaiseElapsed();
        }

        var status = QueryStatus();
        Assert.Equal(new MachinePose(10, 0, 0, 0), status.WPos);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~MockDeviceTransportTests"`
Expected: FAIL — `SendLineAsync_InverseTimeMove_ArrivesAfterTheCommandedTime` не доезжает (мок трактует F12 как 12 ед/мин, то есть 0.02 единицы за тик).

- [ ] **Step 3: Добавить состояние inverse-time движения**

В `ArctZ/Services/Device/Simulation/MockDeviceTransport.cs`, в блок полей
(после `private double _feedUnitsPerMin = 1;`) добавить:

```csharp
    // Второй режим движения: для G93 задано время, а не скорость, поэтому поза
    // интерполируется от стартовой к целевой по накопленному времени, и все оси
    // приходят одновременно. _moveTotalSeconds > 0 означает «идёт G93-движение».
    private MachinePose _moveStartPose = MachinePose.Zero;
    private double _moveTotalSeconds;
    private double _moveElapsedSeconds;
```

- [ ] **Step 4: Разбирать G93 в `ProcessLine`**

Заменить хвост ветки `$J=`/`G0`/`G1` (нынешние строки ~247-252):

```csharp
            if (tokens.TryGetValue('F', out var feed) && feed > 0)
            {
                _feedUnitsPerMin = feed;
            }
```

на:

```csharp
            tokens.TryGetValue('F', out var feed);

            if (trimmed.Contains("G93", StringComparison.OrdinalIgnoreCase) && feed > 0)
            {
                _moveStartPose = _currentPose;
                _moveTotalSeconds = 60.0 / feed;
                _moveElapsedSeconds = 0;
                // FS: должен показывать эффективную подачу, а не 1/t.
                _feedUnitsPerMin = Distance(_currentPose, _targetPose.Value) / _moveTotalSeconds * 60.0;
            }
            else
            {
                _moveTotalSeconds = 0;
                if (feed > 0)
                {
                    _feedUnitsPerMin = feed;
                }
            }
```

И добавить рядом с `ParseAxisTokens` (в конце класса) хелпер:

```csharp
    private static double Distance(MachinePose a, MachinePose b) => Math.Sqrt(
        Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2) + Math.Pow(b.Z - a.Z, 2) + Math.Pow(b.A - a.A, 2));
```

- [ ] **Step 5: Интерполировать позу в `AdvanceMotion`**

В `AdvanceMotion`, сразу после существующей проверки

```csharp
        if (_targetPose is not { } target || target == _currentPose)
        {
            return;
        }
```

вставить:

```csharp
        if (_moveTotalSeconds > 0)
        {
            _moveElapsedSeconds += elapsedSeconds;
            var progress = Math.Min(1.0, _moveElapsedSeconds / _moveTotalSeconds);

            _currentPose = progress >= 1.0
                ? target
                : new MachinePose(
                    X: _moveStartPose.X + (target.X - _moveStartPose.X) * progress,
                    Y: _moveStartPose.Y + (target.Y - _moveStartPose.Y) * progress,
                    Z: _moveStartPose.Z + (target.Z - _moveStartPose.Z) * progress,
                    A: _moveStartPose.A + (target.A - _moveStartPose.A) * progress);

            if (progress >= 1.0)
            {
                _targetPose = null;
                _moveTotalSeconds = 0;
            }

            return;
        }
```

- [ ] **Step 6: Сбрасывать режим на jog-cancel и soft reset**

В `SendRawByteAsync` добавить `_moveTotalSeconds = 0;` рядом с
`_targetPose = null;` в обеих ветках — `case 0x85:` и `case 0x18:`.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~MockDeviceTransportTests"`
Expected: PASS, включая все ранее существовавшие тесты джога, feed hold и alarm.

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add ArctZ/Services/Device/Simulation/MockDeviceTransport.cs ArctZ.Tests/Services/Device/MockDeviceTransportTests.cs
git commit -m "feat: simulate inverse-time moves in the mock transport

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: Поле «Время перехода, с» в редакторе точки

**Files:**
- Modify: `ArctZ/ViewModels/KeyPointEditorViewModel.cs`
- Modify: `ArctZ/Views/MainView.axaml:330-341` (сетка оверлея редактора точки)
- Test: `ArctZ.Tests/ViewModels/ProgramViewModelAuthoringTests.cs`

**Interfaces:**
- Consumes: `KeyPoint.TransitionSeconds` (Task 2).
- Produces: `KeyPointEditorViewModel.TransitionSeconds` (double, `[ObservableProperty]`).

- [ ] **Step 1: Write the failing test**

Добавить в `ArctZ.Tests/ViewModels/ProgramViewModelAuthoringTests.cs` после
теста `EditKeyPoint_Save_UpdatesThePointInPlaceAndClosesEditor`:

```csharp
    [Fact]
    public async Task EditKeyPoint_OpensEditorPrefilledWithTheTransitionTime()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);

        vm.EditKeyPointCommand.Execute(vm.KeyPoints[0]);

        Assert.Equal(5, vm.KeyPointEditor!.TransitionSeconds);
    }

    [Fact]
    public async Task EditKeyPoint_Save_UpdatesTheTransitionTime()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);

        vm.EditKeyPointCommand.Execute(vm.KeyPoints[0]);
        vm.KeyPointEditor!.TransitionSeconds = 12.5;
        vm.KeyPointEditor.SaveCommand.Execute(null);

        Assert.Equal(12.5, vm.KeyPoints[0].TransitionSeconds);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelAuthoringTests"`
Expected: сборка падает — `CS1061: 'KeyPointEditorViewModel' does not contain a definition for 'TransitionSeconds'`.

- [ ] **Step 3: Добавить свойство в `KeyPointEditorViewModel`**

В `ArctZ/ViewModels/KeyPointEditorViewModel.cs`:

1. Добавить поле рядом с `_dwellSeconds`:

```csharp
    [ObservableProperty]
    private double _transitionSeconds;
```

2. В конструкторе, рядом с `DwellSeconds = source.DwellSeconds;`, добавить:

```csharp
        TransitionSeconds = source.TransitionSeconds;
```

3. Заменить `Save()` на:

```csharp
    [RelayCommand]
    private void Save() => _onSave(_source with
    {
        Label = Label,
        Pose = new MachinePose(X, Y, Z, A),
        TransitionSeconds = TransitionSeconds,
        DwellSeconds = DwellSeconds
    });
```

4. Обновить XML-комментарий класса: «Editable draft of a KeyPoint's
   coordinates, transition time and dwell time, shown in an overlay while
   editing.»

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelAuthoringTests"`
Expected: PASS.

- [ ] **Step 5: Добавить поле в разметку**

В `ArctZ/Views/MainView.axaml`, в оверлее редактора точки, сетку

```xml
                            <Grid ColumnDefinitions="Auto,*" RowDefinitions="Auto,Auto,Auto,Auto,Auto" Margin="0,4,0,0">
```

заменить на `RowDefinitions="Auto,Auto,Auto,Auto,Auto,Auto"` и вставить перед
строкой со «Стоянка, с» новую строку 4, сдвинув «Стоянка, с» на строку 5:

```xml
                                <TextBlock Grid.Row="4" Grid.Column="0" Text="Время перехода, с" VerticalAlignment="Center" Margin="0,0,10,6" />
                                <TextBox Grid.Row="4" Grid.Column="1" Text="{Binding TransitionSeconds}" Margin="0,0,0,6" />
                                <TextBlock Grid.Row="5" Grid.Column="0" Text="Стоянка, с" VerticalAlignment="Center" Margin="0,0,10,0" />
                                <TextBox Grid.Row="5" Grid.Column="1" Text="{Binding DwellSeconds}" />
```

(старые две строки с `Grid.Row="4"` для «Стоянка, с» при этом удаляются —
итого в блоке остаётся ровно одна пара для каждого поля).

- [ ] **Step 6: Проверить сборку и весь прогон тестов**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: Build succeeded, без предупреждений компилированных биндингов.

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add ArctZ/ViewModels/KeyPointEditorViewModel.cs ArctZ/Views/MainView.axaml ArctZ.Tests/ViewModels/ProgramViewModelAuthoringTests.cs
git commit -m "feat: edit a key point's transition time in the point editor

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: Привести документацию в соответствие

Два документа сейчас прямо предписывают обратное тому, что делает код:
`fluidnc-slow-motion-limits.md` (раздел 6 «`G93` для длительностей переходов
не использовать») и `bluetooth-gcode-control.md` (правило «длительность
перехода не задавать через `G93`»). Их надо переписать, **сохранив** факт о
систематической погрешности тайминга — он никуда не делся, просто принят.

**Files:**
- Modify: `docs/firmware/fluidnc-slow-motion-limits.md` (разделы «2. `TrajectoryCompiler` …» и «6. `G93` для длительностей переходов не использовать»)
- Modify: `docs/protocol/bluetooth-gcode-control.md` (абзац «Отсюда же два правила для генерации программ…», строки ~96-101)

**Interfaces:**
- Consumes: поведение, реализованное в Task 1-4.
- Produces: ничего (документация).

- [ ] **Step 1: Переписать раздел 6 в `fluidnc-slow-motion-limits.md`**

Заменить весь раздел

```
### 6. `G93` для длительностей переходов не использовать
```

вместе с его текстом на:

```markdown
### 6. `G93` — принятый компромисс для длительностей переходов

Программа по ключевым точкам задаётся временем («проезд за 15 секунд»),
поэтому `TrajectoryCompiler` выдаёт переходы как `G93 G1 ... F<60/t_сек>`
(см. `docs/superpowers/specs/2026-08-18-g93-inverse-time-moves-design.md`).
Ограничение из #1372 при этом остаётся в силе: на медленных движениях `G93`
даёт систематическую ошибку времени (в замере автора ~15%), и приложение её
никак не диагностирует — по решению пользователя проверок достижимости
времени нет.

Что это значит на практике: заданное время — это то, что приложение просит
и на чём строит временную интерполяцию прогресса, а не гарантия. Чем ближе
подача к `v_min` оси, тем сильнее реальный проезд заканчивается раньше
полосы прогресса. Признак — та же разница фактической и расчётной
длительности, что и в разделе 4.
```

- [ ] **Step 2: Обновить раздел 2 в `fluidnc-slow-motion-limits.md`**

В разделе «### 2. `TrajectoryCompiler` — ease-in/out подходит к порогу втрое
ближе» заменить абзац, начинающийся «Кроме того, `EstimateDuration()` считает
время как…», на:

```markdown
Оценка длительности при этом больше не вычисляется: `EstimatedDurationSeconds`
шага равен времени, которое приложение само и заказало в `G93`. Ошибка теперь
целиком на стороне прошивки — если та поехала быстрее заказанного, реальная
длительность окажется **короче** оценки, прогресс будет отставать, а конец
программы придёт раньше полосы (см. `project_progress_bar_time_interpolation_complete`).
```

Также в первом абзаце этого раздела заменить `F = 500` по умолчанию из
`ProgramViewModel` на: «5 секунд на переход по умолчанию из
`InverseTimeMove.DefaultTransitionSeconds`».

- [ ] **Step 3: Обновить `bluetooth-gcode-control.md`**

Заменить абзац

```
Отсюда же два правила для генерации программ: паузу выражать только через
`G4 P<сек>` (движение в ту же точку даёт ноль шагов и исполняется мгновенно),
а длительность перехода не задавать через `G93` — на медленных подачах его
тайминг систематически врёт. Подробный разбор с исходниками прошивки и
следствиями для `TrajectoryCompiler`/`JogCommandFactory` —
```

на

```
Отсюда правило для генерации программ: паузу выражать только через
`G4 P<сек>` — движение в ту же точку даёт ноль шагов и исполняется мгновенно.
Переходы между точками, наоборот, задаются временем через `G93`
(`G93 G1 ... F<60/t_сек>`): на медленных подачах его тайминг систематически
врёт, но это принятый компромисс, потому что программа по ключевым точкам
мыслится временем, а не подачей. Подробный разбор с исходниками прошивки и
следствиями для `TrajectoryCompiler`/`JogCommandFactory` —
```

Кроме того, в таблице команд (строка ~47) заменить

```
| `G1` | линейное перемещение с текущей подачей |
```

на

```
| `G1` | линейное перемещение; в программах всегда в паре с `G93` |
```

- [ ] **Step 4: Проверить, что не осталось противоречий**

Run:
```bash
cd "z:/Jib S/Application/ArctZ"
grep -rn "не использовать\|не задавать через .G93\|FeedRateUnitsPerMin"   docs/firmware docs/protocol docs/software AI_AGENT_README.md
```
Expected: ни одного попадания про запрет `G93` и ни одного упоминания
`FeedRateUnitsPerMin`.

`docs/superpowers/` в проверку намеренно не входит: спеки и планы прошлых
фич — исторический документ, они законно называют старое поле, и
переписывать их нельзя.

- [ ] **Step 5: Commit**

```bash
git add docs/firmware/fluidnc-slow-motion-limits.md docs/protocol/bluetooth-gcode-control.md
git commit -m "docs: G93 is now how transitions are timed

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: Проверка на живом приложении

По `CLAUDE.md` это единственный допустимый способ подтвердить UI/поведенческие
изменения. Шаги выполняет ведущий агент, а не субагент.

- [ ] **Step 1: Собрать и запустить Desktop-голову**

```bash
cd "z:/Jib S/Application/ArctZ"
dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj
dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj
```

- [ ] **Step 2: Попросить пользователя проверить**

Через `AskUserQuestion` попросить подключиться в Демо-режиме, захватить
2-3 точки, задать разное время перехода и прогнать программу.

- [ ] **Step 3: Задать поточечные вопросы через `AskUserQuestion`**

Отдельным вопросом на каждое изменённое поведение:
1. Поле «Время перехода, с» в редакторе точки — появилось, сохраняется?
2. Фактическая длительность проезда между двумя точками соответствует
   заданной?
3. Переход с `EaseMode.EaseInOut` — плавный вход/выход, суммарное время то же?
4. «Перейти к точке» — едет за время точки?
5. «Встать в начальную позицию по завершении» — едет за время первой точки?

- [ ] **Step 4: Открыть старую программу**

Попросить пользователя открыть программу, сохранённую до этого изменения
(если такая есть), и подтвердить, что она проигрывается спокойно (по 5 секунд
на переход), а не бросками.
