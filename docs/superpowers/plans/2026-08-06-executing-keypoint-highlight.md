# Executing-Keypoint Tile Highlight Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** During program playback (`Running`/`Paused`), highlight the tile of the KeyPoint the machine is currently moving toward, in `MainView`'s keypoint list, and clear the highlight when playback stops.

**Architecture:** `ProgramViewModel` gains a computed `Guid? CurrentlyExecutingKeyPointId` derived from the existing `PlaybackState`/`CurrentSegmentIndex` signals (no new device-feedback plumbing). `MainView.axaml`'s keypoint `DataTemplate` compares each tile's own `Id` against that VM property via a `MultiBinding` + a new `IMultiValueConverter`, and toggles the visibility of a translucent accent-colored overlay `Border` layered on top of the tile (not a `Classes.x` toggle — Avalonia's `Classes.name="{Binding}"` attribute shorthand only accepts a single `Binding`, not a `MultiBinding`, so the two-value comparison needs a real property, and `IsVisible` on a plain overlay `Border` is a normal bindable property that supports `MultiBinding` via element syntax).

**Tech Stack:** Avalonia UI (compiled bindings, `x:DataType`), CommunityToolkit.Mvvm (`[ObservableProperty]`, `[NotifyPropertyChangedFor]`), xUnit.

## Global Constraints

- Compiled bindings are enabled by default in this project — any new `x:DataType`-scoped binding must resolve to a real, spelled-correctly property or the build fails (this is a feature: it's the regression check for Task 3's XAML).
- UI/behavioral changes may only be considered verified after the CLAUDE.md-mandated sequence: build the relevant platform head, run it (not just build), ask the user to exercise the feature, then ask one `AskUserQuestion` per changed behavior — never fewer, never a single "looks good?" catch-all.
- New ViewModel code must not call `Dispatcher.UIThread` directly (project convention — not needed here since this task adds no new async/threading code, only a computed property over existing observable state).

---

### Task 1: `ProgramViewModel.CurrentlyExecutingKeyPointId`

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs:475-481` (add `[NotifyPropertyChangedFor]` to `_playbackState`), `:565-567` (same for `_currentSegmentIndex`), and after `:591` (add the new property).
- Test: `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs` (append new `[Fact]`s to the existing class).

**Interfaces:**
- Produces: `ProgramViewModel.CurrentlyExecutingKeyPointId` — `public Guid? CurrentlyExecutingKeyPointId { get; }`, recomputed from `PlaybackState` and `CurrentSegmentIndex`, raises `PropertyChanged` whenever either of those changes.

- [ ] **Step 1: Write the failing tests**

Append to `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs`, inside the `ProgramViewModelPlaybackTests` class (this file already has `CreateViewModel`, `SeedTwoSegmentProgram`, and `WaitUntilAsync` helpers — reuse them, don't redefine):

```csharp
[Fact]
public void CurrentlyExecutingKeyPointId_IsNull_WhileIdle()
{
    var vm = CreateViewModel(out var transport);
    SeedTwoSegmentProgram(vm, transport);

    Assert.Null(vm.CurrentlyExecutingKeyPointId);
}

[Fact]
public async Task CurrentlyExecutingKeyPointId_TargetsFirstDestination_AsSoonAsPlayStarts_BeforeAnyAck()
{
    var vm = CreateViewModel(out var transport);
    await vm.Connection.ConnectCommand.Execute();
    SeedTwoSegmentProgram(vm, transport);

    var playTask = vm.PlayCommand.ExecuteAsync(null);

    Assert.Equal(vm.KeyPoints[1].Id, vm.CurrentlyExecutingKeyPointId);

    transport.SimulateReceivedLine("ok");
    transport.SimulateReceivedLine("ok");
    await playTask;
}

[Fact]
public async Task CurrentlyExecutingKeyPointId_AdvancesWithEachSegmentAck_ThenClearsOnCompletion()
{
    var vm = CreateViewModel(out var transport);
    await vm.Connection.ConnectCommand.Execute();
    SeedTwoSegmentProgram(vm, transport);

    var playTask = vm.PlayCommand.ExecuteAsync(null);
    Assert.Equal(vm.KeyPoints[1].Id, vm.CurrentlyExecutingKeyPointId);

    transport.SimulateReceivedLine("ok");
    await WaitUntilAsync(() => vm.CurrentSegmentIndex == 0, TimeSpan.FromSeconds(1));
    Assert.Equal(vm.KeyPoints[2].Id, vm.CurrentlyExecutingKeyPointId);

    transport.SimulateReceivedLine("ok");
    await playTask;

    Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
    Assert.Null(vm.CurrentlyExecutingKeyPointId);
}

[Fact]
public async Task CurrentlyExecutingKeyPointId_StaysOnTarget_WhilePaused()
{
    var vm = CreateViewModel(out var transport);
    await vm.Connection.ConnectCommand.Execute();
    SeedTwoSegmentProgram(vm, transport);

    var playTask = vm.PlayCommand.ExecuteAsync(null);
    await vm.PauseCommand.ExecuteAsync(null);

    Assert.Equal(PlaybackState.Paused, vm.PlaybackState);
    Assert.Equal(vm.KeyPoints[1].Id, vm.CurrentlyExecutingKeyPointId);

    await vm.PlayCommand.ExecuteAsync(null);
    transport.SimulateReceivedLine("ok");
    transport.SimulateReceivedLine("ok");
    await playTask;
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~CurrentlyExecutingKeyPointId"`
Expected: build error / FAIL — `CurrentlyExecutingKeyPointId` does not exist on `ProgramViewModel` yet.

- [ ] **Step 3: Implement the property**

In `ArctZ/ViewModels/ProgramViewModel.cs`, add `[NotifyPropertyChangedFor(nameof(CurrentlyExecutingKeyPointId))]` to the existing attribute stacks on both backing fields:

```csharp
[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(PlayCommand))]
[NotifyCanExecuteChangedFor(nameof(PauseCommand))]
[NotifyCanExecuteChangedFor(nameof(StopCommand))]
[NotifyPropertyChangedFor(nameof(IsProgramLocked))]
[NotifyPropertyChangedFor(nameof(StatusLabel))]
[NotifyPropertyChangedFor(nameof(CurrentlyExecutingKeyPointId))]
private PlaybackState _playbackState = PlaybackState.Idle;
```

```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(SegmentProgressLabel))]
[NotifyPropertyChangedFor(nameof(CurrentlyExecutingKeyPointId))]
private int? _currentSegmentIndex;
```

Then add the new property right after `SegmentProgressLabel` (currently ending at line 591):

```csharp
public Guid? CurrentlyExecutingKeyPointId
{
    get
    {
        if (PlaybackState is not (PlaybackState.Running or PlaybackState.Paused))
        {
            return null;
        }

        var targetIndex = (CurrentSegmentIndex ?? -1) + 1;
        return targetIndex >= 0 && targetIndex < KeyPoints.Count
            ? KeyPoints[targetIndex].Id
            : null;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: PASS (all tests in the file, including the pre-existing ones — confirms the new `[NotifyPropertyChangedFor]` attributes didn't break anything).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs
git commit -m "feat: add CurrentlyExecutingKeyPointId to ProgramViewModel"
```

---

### Task 2: `KeyPointIsExecutingConverter`

**Files:**
- Create: `ArctZ/Converters/KeyPointIsExecutingConverter.cs`
- Test: Create `ArctZ.Tests/Converters/KeyPointIsExecutingConverterTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1 directly (pure converter, works on `Guid?` values regardless of where they come from).
- Produces: `KeyPointIsExecutingConverter : IMultiValueConverter` — `Convert(IList<object?> values, ...)` where `values[0]` is expected to be a `Guid` (a tile's own KeyPoint `Id`) and `values[1]` a `Guid?` (`ProgramViewModel.CurrentlyExecutingKeyPointId`); returns `bool`.

- [ ] **Step 1: Write the failing tests**

Create `ArctZ.Tests/Converters/KeyPointIsExecutingConverterTests.cs`:

```csharp
using System;
using System.Globalization;
using ArctZ.Converters;

namespace ArctZ.Tests.Converters;

public class KeyPointIsExecutingConverterTests
{
    [Fact]
    public void Convert_ReturnsTrue_WhenTileIdMatchesExecutingId()
    {
        var id = Guid.NewGuid();
        var converter = new KeyPointIsExecutingConverter();

        var result = converter.Convert(new object?[] { id, (Guid?)id }, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(true, result);
    }

    [Fact]
    public void Convert_ReturnsFalse_WhenIdsDiffer()
    {
        var converter = new KeyPointIsExecutingConverter();

        var result = converter.Convert(new object?[] { Guid.NewGuid(), (Guid?)Guid.NewGuid() }, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }

    [Fact]
    public void Convert_ReturnsFalse_WhenExecutingIdIsNull()
    {
        var id = Guid.NewGuid();
        var converter = new KeyPointIsExecutingConverter();

        var result = converter.Convert(new object?[] { id, null }, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~KeyPointIsExecutingConverterTests"`
Expected: build error — `ArctZ.Converters.KeyPointIsExecutingConverter` does not exist yet.

- [ ] **Step 3: Implement the converter**

Create `ArctZ/Converters/KeyPointIsExecutingConverter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ArctZ.Converters;

public class KeyPointIsExecutingConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count != 2 || values[0] is not Guid tileId || values[1] is not Guid executingId)
        {
            return false;
        }

        return tileId == executingId;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~KeyPointIsExecutingConverterTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Converters/KeyPointIsExecutingConverter.cs ArctZ.Tests/Converters/KeyPointIsExecutingConverterTests.cs
git commit -m "feat: add KeyPointIsExecutingConverter"
```

---

### Task 3: Wire the highlight into `MainView.axaml`

**Files:**
- Modify: `ArctZ/Views/MainView.axaml:15-19` (register the converter resource), `:77-81` (add the highlight style next to `Border.loaded-entry`), `:161-186` (wrap the tile `Button` in a `Panel` and add the overlay `Border`).

**Interfaces:**
- Consumes: `ProgramViewModel.CurrentlyExecutingKeyPointId` (Task 1), `KeyPointIsExecutingConverter` (Task 2).

- [ ] **Step 1: Register the converter as a resource**

In `ArctZ/Views/MainView.axaml`, `UserControl.Resources` (lines 15-19) currently reads:

```xml
<UserControl.Resources>

    <conv:LabelLengthToFontSizeConverter x:Key="LabelLengthToFontSize" />

</UserControl.Resources>
```

Add the new converter alongside it:

```xml
<UserControl.Resources>

    <conv:LabelLengthToFontSizeConverter x:Key="LabelLengthToFontSize" />
    <conv:KeyPointIsExecutingConverter x:Key="KeyPointIsExecuting" />

</UserControl.Resources>
```

- [ ] **Step 2: Add the highlight style**

In `UserControl.Styles` (lines 77-81), add a new style next to `Border.loaded-entry`:

```xml
<Style Selector="Border.loaded-entry">
    <Setter Property="Background" Value="{DynamicResource HudAccentDimBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource HudAccentBrush}" />
    <Setter Property="BorderThickness" Value="3,0,0,0" />
</Style>

<Style Selector="Border.executing-indicator">
    <Setter Property="Background" Value="{DynamicResource HudAccentDimBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource HudAccentBrush}" />
    <Setter Property="BorderThickness" Value="2" />
</Style>
```

- [ ] **Step 3: Wrap the tile in a `Panel` and add the overlay**

The current tile template (lines 161-186) is:

```xml
<DataTemplate x:DataType="program:KeyPoint">
    <Button Width="120" Height="60"
            Background="{StaticResource HudPanelElevatedBrush}"
            BorderBrush="{StaticResource HudBorderBrush}" BorderThickness="1"
            Padding="16,14" HorizontalContentAlignment="Left">
        <Button.Flyout>
            <MenuFlyout>
                <!-- ... unchanged MenuItems ... -->
            </MenuFlyout>
        </Button.Flyout>
        <TextBlock Classes="telemetry" TextWrapping="Wrap" MaxLines="2" TextTrimming="CharacterEllipsis"
                   FontSize="{Binding Label, Converter={StaticResource LabelLengthToFontSize}}"
                   Text="{Binding Label}" />
    </Button>
</DataTemplate>
```

Wrap the `Button` in a `Panel` and add the overlay `Border` as its second child, after the `Button` (so it paints on top). Keep the `Button` and everything inside it — its `Width`/`Height`/`Background`/`BorderBrush`/`BorderThickness`/`Padding`/`Flyout`/`TextBlock` — exactly as-is; only the wrapping changes:

```xml
<DataTemplate x:DataType="program:KeyPoint">
    <Panel>
        <Button Width="120" Height="60"
                Background="{StaticResource HudPanelElevatedBrush}"
                BorderBrush="{StaticResource HudBorderBrush}" BorderThickness="1"
                Padding="16,14" HorizontalContentAlignment="Left">
            <Button.Flyout>
                <MenuFlyout>
                    <!-- ... unchanged MenuItems ... -->
                </MenuFlyout>
            </Button.Flyout>
            <TextBlock Classes="telemetry" TextWrapping="Wrap" MaxLines="2" TextTrimming="CharacterEllipsis"
                       FontSize="{Binding Label, Converter={StaticResource LabelLengthToFontSize}}"
                       Text="{Binding Label}" />
        </Button>
        <Border Classes="executing-indicator" IsHitTestVisible="False">
            <Border.IsVisible>
                <MultiBinding Converter="{StaticResource KeyPointIsExecuting}">
                    <Binding Path="Id" />
                    <Binding Path="((vm:ProgramViewModel)DataContext).CurrentlyExecutingKeyPointId" ElementName="KeyPointsList" />
                </MultiBinding>
            </Border.IsVisible>
        </Border>
    </Panel>
</DataTemplate>
```

`Panel` sizes itself to its largest child (the 120x60 `Button`), and a plain `Border` with no explicit `Width`/`Height` stretches to fill its parent `Panel` by default, so the overlay automatically matches the tile's footprint without repeating the `120,60` literals. `IsHitTestVisible="False"` keeps the tile's click/right-click flyout working through the overlay.

- [ ] **Step 4: Build to verify the compiled bindings resolve**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: build succeeds. If `CurrentlyExecutingKeyPointId` or `Id` is misspelled, compiled bindings make this a build error, not a runtime silent failure — read the error message if it fails, it will name the exact missing member.

- [ ] **Step 5: Run the tests one more time (regression check)**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS (no test touches XAML directly, but this confirms Tasks 1-2 are still green after the XAML change).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Views/MainView.axaml
git commit -m "feat: highlight the currently executing keypoint tile during playback"
```

- [ ] **Step 7: Mandatory manual UI verification (per CLAUDE.md)**

This is the only acceptable way to confirm the feature works — do not skip it or substitute a self-taken screenshot.

1. Run: `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj` — the app must actually be running, not just built.
2. Ask the user to load or capture a program with at least 3 key points, connect to a device (real or mock — see the side-menu mock settings dialog if no hardware is available), and press "Пуск".
3. Ask the user to observe: does the **first** key point's tile light up (border + fill) the moment playback starts (before any ack), does the highlight advance to the next tile after each segment's ack, does it stay visible (not washed out) while the rest of the list is dimmed/disabled during playback, and does it disappear when the program finishes, is stopped, or faults.
4. Ask via `AskUserQuestion`, one question per behavior observed in step 3 (at minimum: initial highlight on Пуск, highlight advancing per segment, highlight visible under the disabled/dimmed list, highlight clearing on Завершено/Стоп/Ошибка) — never a single combined "does it look right?" question.
5. If any answer is negative, treat it as a bug against this task's own code (most likely the `:disabled` dimming assumption in the Architecture note above, or a Z-order/converter issue) and fix before considering Task 3 done.

---

### Task 4: Fix highlight target — off-by-one found during manual UI verification

**Context:** Tasks 1-3 shipped and passed code review, but Task 3's Step 7 manual UI check (run live against the real app, with the user watching) surfaced a genuine behavior bug: on "Пуск", the tile that lit up was the **second** key point, not the first. The correct behavior, confirmed with the user: the **first** key point's tile lights up immediately on "Пуск" (before any ack — the machine is either already there or that's where the program's first move is headed), and only after each segment's `ok` does the highlight advance to the next tile. Task 1's implementer used the formula `(CurrentSegmentIndex ?? -1) + 2`, reasoning it was required to satisfy the tests in the Task 1 brief — but those tests themselves encoded the wrong target (`KeyPoints[1]` instead of `KeyPoints[0]` as the initial highlight). The fix corrects both the formula and the tests together, back to `(CurrentSegmentIndex ?? -1) + 1` — the plan's original formula, which was right all along.

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs` (the `CurrentlyExecutingKeyPointId` getter added in Task 1).
- Modify: `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs` (the three `CurrentlyExecutingKeyPointId_*` tests added in Task 1 — `..._TargetsFirstDestination_AsSoonAsPlayStarts_BeforeAnyAck`, `..._AdvancesWithEachSegmentAck_ThenClearsOnCompletion`, `..._StaysOnTarget_WhilePaused`; the fourth, `..._IsNull_WhileIdle`, is unaffected and stays as-is).

**Interfaces:**
- No change to the property's signature (`Guid? CurrentlyExecutingKeyPointId`) or to what Tasks 2-3 consume from it — only which `KeyPoints` index it resolves to changes.

- [ ] **Step 1: Update the tests to the corrected expectations (still red against the current code until Step 3)**

In `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs`, replace the three affected tests (currently at lines 235-289) with:

```csharp
[Fact]
public async Task CurrentlyExecutingKeyPointId_TargetsFirstKeyPoint_AsSoonAsPlayStarts_BeforeAnyAck()
{
    var vm = CreateViewModel(out var transport);
    await vm.Connection.ConnectCommand.Execute();
    SeedTwoSegmentProgram(vm, transport);

    var playTask = vm.PlayCommand.ExecuteAsync(null);

    Assert.Equal(vm.KeyPoints[0].Id, vm.CurrentlyExecutingKeyPointId);

    transport.SimulateReceivedLine("ok");
    transport.SimulateReceivedLine("ok");
    await playTask;
}

[Fact]
public async Task CurrentlyExecutingKeyPointId_AdvancesWithEachSegmentAck_ThenClearsOnCompletion()
{
    var vm = CreateViewModel(out var transport);
    await vm.Connection.ConnectCommand.Execute();
    SeedTwoSegmentProgram(vm, transport);

    var playTask = vm.PlayCommand.ExecuteAsync(null);
    Assert.Equal(vm.KeyPoints[0].Id, vm.CurrentlyExecutingKeyPointId);

    transport.SimulateReceivedLine("ok");
    await WaitUntilAsync(() => vm.CurrentSegmentIndex == 0, TimeSpan.FromSeconds(1));
    Assert.Equal(vm.KeyPoints[1].Id, vm.CurrentlyExecutingKeyPointId);

    transport.SimulateReceivedLine("ok");
    await playTask;

    Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
    Assert.Null(vm.CurrentlyExecutingKeyPointId);
}

[Fact]
public async Task CurrentlyExecutingKeyPointId_StaysOnTarget_WhilePaused()
{
    var vm = CreateViewModel(out var transport);
    await vm.Connection.ConnectCommand.Execute();
    SeedTwoSegmentProgram(vm, transport);

    var playTask = vm.PlayCommand.ExecuteAsync(null);
    await vm.PauseCommand.ExecuteAsync(null);

    Assert.Equal(PlaybackState.Paused, vm.PlaybackState);
    Assert.Equal(vm.KeyPoints[0].Id, vm.CurrentlyExecutingKeyPointId);

    await vm.PlayCommand.ExecuteAsync(null);
    transport.SimulateReceivedLine("ok");
    transport.SimulateReceivedLine("ok");
    await playTask;
}
```

(Only the asserted `KeyPoints[...]` indices and the first test's name changed from what Task 1 wrote — `KeyPoints[1]`→`KeyPoints[0]` for the pre-first-ack state, `KeyPoints[2]`→`KeyPoints[1]` for the post-first-ack state. Everything else — setup, helper calls, ack sequencing — is identical to what's already in the file.)

- [ ] **Step 2: Run the tests to verify they now fail against the current (wrong) implementation**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~CurrentlyExecutingKeyPointId"`
Expected: FAIL — the current code returns `KeyPoints[1].Id`/`KeyPoints[2].Id` where the updated tests now expect `KeyPoints[0].Id`/`KeyPoints[1].Id`.

- [ ] **Step 3: Fix the property**

In `ArctZ/ViewModels/ProgramViewModel.cs`, in the `CurrentlyExecutingKeyPointId` getter added by Task 1, change:

```csharp
var targetIndex = (CurrentSegmentIndex ?? -1) + 2;
```

to:

```csharp
var targetIndex = (CurrentSegmentIndex ?? -1) + 1;
```

No other line in the getter changes.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: PASS (all tests in the file).

- [ ] **Step 5: Run the full suite as a regression check**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS (241/241 or more, matching Task 3's reported baseline — Tasks 2-3 don't touch this formula, so nothing else should move).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs
git commit -m "fix: highlight the first keypoint (not the second) before the first playback ack"
```

- [ ] **Step 7: Re-run the mandatory manual UI verification (per CLAUDE.md)**

Same procedure as Task 3 Step 7, scoped to just the corrected behavior:

1. Build and run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj` then `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj`.
2. Ask the user to load/capture a program with at least 3 key points, connect (real or mock device), press "Пуск".
3. Ask via `AskUserQuestion`: does the **first** key point's tile light up immediately on "Пуск" (before any ack), and does it advance to the second tile only after the first segment's `ok`?
4. If the answer is negative, treat it as a bug in this task's own fix and resolve before considering Task 4 done.
