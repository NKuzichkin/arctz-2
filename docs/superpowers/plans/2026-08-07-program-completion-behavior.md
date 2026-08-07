# Program Completion Behavior Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a saved program choose what happens when it finishes running — stop (optionally returning to the first key point), loop from the start N times (or forever), or ping-pong forward/backward N times (or forever) — configured per-program via a new "Настройки завершения" dialog.

**Architecture:** Three new fields on `JibProgram` (`CompletionMode`, `ReturnToStartOnFinish`, `RepeatCount`), mirrored as `ProgramViewModel` properties and edited through a new `CompletionSettingsViewModel` modal (same pattern as the existing `KeyPointEditorViewModel`). `ProgramViewModel.PlayAsync`'s single dispatch-and-await-acks block is extracted into a reusable `RunPassAsync` helper, then driven by an outer cycle loop that picks forward/backward compiled step lists per pass and repeats according to the configured mode. Only one physical-idle wait (`WaitForMotionToFinishAsync`) happens for the whole multi-pass run, at the very end — matching the existing architecture note that acks reflect buffer-drain speed, not motion completion.

**Tech Stack:** Avalonia UI (compiled bindings), CommunityToolkit.Mvvm (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`), xUnit.

## Global Constraints

- Compiled bindings are on by default — every new `x:DataType` scope in XAML must be set explicitly.
- New `ProgramCompletionMode` enum lives in `ArctZ.Services.Program`, next to `JibProgram`.
- `Loop` repeat count range: 2–50 or unlimited (`null`). `PingPong` repeat count range: 1–50 or unlimited (`null`). `Stop` mode does not use `RepeatCount` (always persisted as `null`).
- `ReturnToStartOnFinish` applies to all three modes, fires exactly once, only on natural completion (never on manual Stop).
- No JSON migration needed: `JsonFileProgramStorage` already uses `PreferredObjectCreationHandling = Populate`, so missing fields in old saved files fall back to the C# property initializers.
- Follow the repo's existing test patterns exactly: `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs` and `ProgramViewModelAuthoringTests.cs` show the `FakeDeviceTransport` / `ManualPeriodicTimer` / `WaitUntilAsync` conventions used throughout — new tests must follow them, not invent new helpers.
- Final task requires the mandatory UI verification loop from `CLAUDE.md`: build the Desktop head, run it, and ask the user (via `AskUserQuestion`, one question per behavior) to confirm each mode actually works.

---

### Task 1: `ProgramCompletionMode` enum + `JibProgram` fields

**Files:**
- Create: `ArctZ/Services/Program/ProgramCompletionMode.cs`
- Modify: `ArctZ/Services/Program/JibProgram.cs`
- Test: `ArctZ.Tests/Services/Program/JibProgramTests.cs`
- Test: `ArctZ.Tests/Services/Program/JsonFileProgramStorageTests.cs`

**Interfaces:**
- Produces: `ProgramCompletionMode { Stop, Loop, PingPong }`; `JibProgram.CompletionMode` (default `Stop`), `JibProgram.ReturnToStartOnFinish` (default `false`), `JibProgram.RepeatCount` (`int?`, default `null`).

- [ ] **Step 1: Write the failing tests**

In `ArctZ.Tests/Services/Program/JibProgramTests.cs`, add:

```csharp
[Fact]
public void NewProgram_DefaultsToStopModeNoReturnNoRepeatLimit()
{
    var program = new JibProgram();

    Assert.Equal(ProgramCompletionMode.Stop, program.CompletionMode);
    Assert.False(program.ReturnToStartOnFinish);
    Assert.Null(program.RepeatCount);
}
```

In `ArctZ.Tests/Services/Program/JsonFileProgramStorageTests.cs`, add (uses `System.Text.Json` directly, so add `using System.Text.Json;` at the top if not already present — check first, it currently is not imported there):

```csharp
[Fact]
public async Task SaveAsync_ThenLoadAsync_RoundTripsCompletionSettings()
{
    var program = SampleProgram("С повторами");
    program.CompletionMode = ProgramCompletionMode.PingPong;
    program.ReturnToStartOnFinish = true;
    program.RepeatCount = 7;

    await _storage.SaveAsync(program);
    var loaded = await _storage.LoadAsync(program.Id);

    Assert.Equal(ProgramCompletionMode.PingPong, loaded.CompletionMode);
    Assert.True(loaded.ReturnToStartOnFinish);
    Assert.Equal(7, loaded.RepeatCount);
}

[Fact]
public async Task LoadAsync_JsonWithoutCompletionFields_DefaultsToStopWithNoReturnAndNoRepeatLimit()
{
    var id = Guid.NewGuid();
    Directory.CreateDirectory(_directory);
    var legacyJson = $$"""
    {
      "Id": "{{id}}",
      "Name": "Старая программа",
      "KeyPoints": []
    }
    """;
    await File.WriteAllTextAsync(Path.Combine(_directory, $"{id}.json"), legacyJson);

    var loaded = await _storage.LoadAsync(id);

    Assert.Equal(ProgramCompletionMode.Stop, loaded.CompletionMode);
    Assert.False(loaded.ReturnToStartOnFinish);
    Assert.Null(loaded.RepeatCount);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~JibProgramTests|FullyQualifiedName~JsonFileProgramStorageTests"`
Expected: compile error (`ProgramCompletionMode`, `CompletionMode`, `ReturnToStartOnFinish`, `RepeatCount` don't exist yet).

- [ ] **Step 3: Create the enum**

`ArctZ/Services/Program/ProgramCompletionMode.cs`:

```csharp
namespace ArctZ.Services.Program;

public enum ProgramCompletionMode
{
    Stop,
    Loop,
    PingPong
}
```

- [ ] **Step 4: Add the fields to `JibProgram`**

In `ArctZ/Services/Program/JibProgram.cs`, replace:

```csharp
    public string Name { get; set; } = "Новая программа";

    public List<KeyPoint> KeyPoints { get; } = new();
```

with:

```csharp
    public string Name { get; set; } = "Новая программа";

    public ProgramCompletionMode CompletionMode { get; set; } = ProgramCompletionMode.Stop;

    public bool ReturnToStartOnFinish { get; set; }

    /// <summary>Repeats for Loop/PingPong; null means unlimited. Unused (always null) in Stop mode.</summary>
    public int? RepeatCount { get; set; }

    public List<KeyPoint> KeyPoints { get; } = new();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~JibProgramTests|FullyQualifiedName~JsonFileProgramStorageTests"`
Expected: PASS (all, including pre-existing tests in both files).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Services/Program/ProgramCompletionMode.cs ArctZ/Services/Program/JibProgram.cs ArctZ.Tests/Services/Program/JibProgramTests.cs ArctZ.Tests/Services/Program/JsonFileProgramStorageTests.cs
git commit -m "feat: add ProgramCompletionMode and completion fields to JibProgram"
```

---

### Task 2: `EnumEqualsConverter`

A small reusable converter so XAML `RadioButton`s can two-way bind `IsChecked` against one enum property (`Mode == ProgramCompletionMode.Loop`-style comparisons). Needed by Task 5's XAML.

**Files:**
- Create: `ArctZ/Converters/EnumEqualsConverter.cs`

**Interfaces:**
- Produces: `EnumEqualsConverter : IValueConverter` — `Convert` returns `true` when `value.Equals(parameter)`; `ConvertBack` returns `parameter` when `value is true`, otherwise `BindingOperations.DoNothing` (so unchecking a `RadioButton` doesn't null out the bound enum).

- [ ] **Step 1: Write the converter**

`ArctZ/Converters/EnumEqualsConverter.cs`:

```csharp
using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace ArctZ.Converters;

public class EnumEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null && value.Equals(parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter : BindingOperations.DoNothing;
}
```

No dedicated test file — this converter is a small utility exercised indirectly through the XAML in Task 5 and via the manual UI verification in Task 10. It follows the exact same shape as `ArctZ/Converters/KeyPointIsExecutingConverter.cs`, which also has no dedicated unit test.

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build ArctZ/ArctZ.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add ArctZ/Converters/EnumEqualsConverter.cs
git commit -m "feat: add EnumEqualsConverter for enum-bound radio buttons"
```

---

### Task 3: `CompletionSettingsViewModel`

The editable draft shown in the new modal — mirrors `KeyPointEditorViewModel`'s save/cancel-callback pattern.

**Files:**
- Create: `ArctZ/ViewModels/CompletionSettingsViewModel.cs`
- Test: `ArctZ.Tests/ViewModels/CompletionSettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `ProgramCompletionMode` (Task 1).
- Produces: `CompletionSettingsViewModel(ProgramCompletionMode mode, int? repeatCount, bool returnToStartOnFinish, Action<ProgramCompletionMode, int?, bool> onSave, Action onCancel)`; observable properties `Mode`, `RepeatCount` (`int`), `IsRepeatUnlimited` (`bool`), `ReturnToStartOnFinish` (`bool`); computed `bool ShowRepeatCount`; `SaveCommand`, `CancelCommand`; constants `LoopMinRepeatCount = 2`, `PingPongMinRepeatCount = 1`, `MaxRepeatCount = 50`.

- [ ] **Step 1: Write the failing tests**

`ArctZ.Tests/ViewModels/CompletionSettingsViewModelTests.cs`:

```csharp
using System.Collections.Generic;
using ArctZ.Services.Program;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class CompletionSettingsViewModelTests
{
    private static (CompletionSettingsViewModel Vm,
        List<(ProgramCompletionMode Mode, int? RepeatCount, bool ReturnToStartOnFinish)> Saved,
        List<int> Cancelled) Create(
        ProgramCompletionMode mode = ProgramCompletionMode.Stop,
        int? repeatCount = null,
        bool returnToStartOnFinish = false)
    {
        var saved = new List<(ProgramCompletionMode, int?, bool)>();
        var cancelled = new List<int>();
        var vm = new CompletionSettingsViewModel(
            mode, repeatCount, returnToStartOnFinish,
            (m, r, rts) => saved.Add((m, r, rts)),
            () => cancelled.Add(1));
        return (vm, saved, cancelled);
    }

    [Fact]
    public void Constructor_FiniteRepeatCount_IsRepeatUnlimitedFalse()
    {
        var (vm, _, _) = Create(ProgramCompletionMode.Loop, repeatCount: 5);

        Assert.False(vm.IsRepeatUnlimited);
        Assert.Equal(5, vm.RepeatCount);
    }

    [Fact]
    public void Constructor_NullRepeatCount_IsRepeatUnlimitedTrue()
    {
        var (vm, _, _) = Create(ProgramCompletionMode.PingPong, repeatCount: null);

        Assert.True(vm.IsRepeatUnlimited);
    }

    [Fact]
    public void ShowRepeatCount_FalseForStop_TrueForLoopAndPingPong()
    {
        var (vm, _, _) = Create(ProgramCompletionMode.Stop);
        Assert.False(vm.ShowRepeatCount);

        vm.Mode = ProgramCompletionMode.Loop;
        Assert.True(vm.ShowRepeatCount);

        vm.Mode = ProgramCompletionMode.PingPong;
        Assert.True(vm.ShowRepeatCount);
    }

    [Fact]
    public void SwitchingToLoop_ClampsRepeatCountUpToLoopMinimum()
    {
        var (vm, _, _) = Create(ProgramCompletionMode.PingPong, repeatCount: 1);

        vm.Mode = ProgramCompletionMode.Loop;

        Assert.Equal(CompletionSettingsViewModel.LoopMinRepeatCount, vm.RepeatCount);
    }

    [Fact]
    public void Save_ClampsRepeatCountAboveMaximum()
    {
        var (vm, saved, _) = Create(ProgramCompletionMode.Loop, repeatCount: 2);
        vm.RepeatCount = 999;

        vm.SaveCommand.Execute(null);

        Assert.Equal((ProgramCompletionMode.Loop, (int?)CompletionSettingsViewModel.MaxRepeatCount, false), saved[0]);
    }

    [Fact]
    public void Save_Unlimited_PassesNullRepeatCount()
    {
        var (vm, saved, _) = Create(ProgramCompletionMode.Loop, repeatCount: 2);
        vm.IsRepeatUnlimited = true;

        vm.SaveCommand.Execute(null);

        Assert.Null(saved[0].RepeatCount);
    }

    [Fact]
    public void Save_StopMode_PassesNullRepeatCountRegardless()
    {
        var (vm, saved, _) = Create(ProgramCompletionMode.Stop);

        vm.SaveCommand.Execute(null);

        Assert.Equal(ProgramCompletionMode.Stop, saved[0].Mode);
        Assert.Null(saved[0].RepeatCount);
    }

    [Fact]
    public void Cancel_InvokesCancelCallback()
    {
        var (vm, _, cancelled) = Create();

        vm.CancelCommand.Execute(null);

        Assert.Single(cancelled);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~CompletionSettingsViewModelTests"`
Expected: compile error (`CompletionSettingsViewModel` doesn't exist yet).

- [ ] **Step 3: Write the implementation**

`ArctZ/ViewModels/CompletionSettingsViewModel.cs`:

```csharp
using System;
using ArctZ.Services.Program;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArctZ.ViewModels;

/// <summary>Editable draft of a program's completion behavior, shown in an overlay while editing.</summary>
public partial class CompletionSettingsViewModel : ViewModelBase
{
    public const int LoopMinRepeatCount = 2;
    public const int PingPongMinRepeatCount = 1;
    public const int MaxRepeatCount = 50;

    private readonly Action<ProgramCompletionMode, int?, bool> _onSave;
    private readonly Action _onCancel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRepeatCount))]
    private ProgramCompletionMode _mode;

    [ObservableProperty]
    private int _repeatCount;

    [ObservableProperty]
    private bool _isRepeatUnlimited;

    [ObservableProperty]
    private bool _returnToStartOnFinish;

    public bool ShowRepeatCount => Mode != ProgramCompletionMode.Stop;

    public CompletionSettingsViewModel(
        ProgramCompletionMode mode,
        int? repeatCount,
        bool returnToStartOnFinish,
        Action<ProgramCompletionMode, int?, bool> onSave,
        Action onCancel)
    {
        _onSave = onSave;
        _onCancel = onCancel;
        Mode = mode;
        IsRepeatUnlimited = repeatCount is null;
        RepeatCount = repeatCount ?? MinRepeatCountFor(mode);
        ReturnToStartOnFinish = returnToStartOnFinish;
    }

    private static int MinRepeatCountFor(ProgramCompletionMode mode) =>
        mode == ProgramCompletionMode.Loop ? LoopMinRepeatCount : PingPongMinRepeatCount;

    partial void OnModeChanged(ProgramCompletionMode value)
    {
        var min = MinRepeatCountFor(value);
        if (RepeatCount < min)
        {
            RepeatCount = min;
        }
    }

    [RelayCommand]
    private void Save()
    {
        var min = MinRepeatCountFor(Mode);
        var clamped = Math.Clamp(RepeatCount, min, MaxRepeatCount);
        int? effectiveCount = Mode == ProgramCompletionMode.Stop || IsRepeatUnlimited ? null : clamped;
        _onSave(Mode, effectiveCount, ReturnToStartOnFinish);
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~CompletionSettingsViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ArctZ/ViewModels/CompletionSettingsViewModel.cs ArctZ.Tests/ViewModels/CompletionSettingsViewModelTests.cs
git commit -m "feat: add CompletionSettingsViewModel"
```

---

### Task 4: Wire completion settings into `ProgramViewModel`

Adds the mirrored properties, the open/apply commands, and persistence (save/load/new).

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs`
- Test: `ArctZ.Tests/ViewModels/ProgramViewModelAuthoringTests.cs`

**Interfaces:**
- Consumes: `CompletionSettingsViewModel` (Task 3), `ProgramCompletionMode`/`JibProgram` fields (Task 1).
- Produces: `ProgramViewModel.CompletionMode`, `.ReturnToStartOnFinish`, `.RepeatCount` (`int?`), `.CompletionSettingsEditor` (`CompletionSettingsViewModel?`), `.IsEditingCompletionSettings` (`bool`), `EditCompletionSettingsCommand`.

- [ ] **Step 1: Write the failing tests**

Add to `ArctZ.Tests/ViewModels/ProgramViewModelAuthoringTests.cs` (add `using ArctZ.Services.Program;` if not already present — it already is, via the existing `JibProgram`/`EaseMode` usage):

```csharp
[Fact]
public void EditCompletionSettings_OpensEditorPrefilledFromCurrentSettings()
{
    var vm = CreateViewModel(out _, out _);
    vm.CompletionMode = ProgramCompletionMode.Loop;
    vm.RepeatCount = 5;
    vm.ReturnToStartOnFinish = true;

    vm.EditCompletionSettingsCommand.Execute(null);

    Assert.True(vm.IsEditingCompletionSettings);
    Assert.NotNull(vm.CompletionSettingsEditor);
    Assert.Equal(ProgramCompletionMode.Loop, vm.CompletionSettingsEditor!.Mode);
    Assert.Equal(5, vm.CompletionSettingsEditor.RepeatCount);
    Assert.False(vm.CompletionSettingsEditor.IsRepeatUnlimited);
    Assert.True(vm.CompletionSettingsEditor.ReturnToStartOnFinish);
}

[Fact]
public void EditCompletionSettings_Save_UpdatesProgramViewModelAndClosesEditor()
{
    var vm = CreateViewModel(out _, out _);

    vm.EditCompletionSettingsCommand.Execute(null);
    vm.CompletionSettingsEditor!.Mode = ProgramCompletionMode.Loop;
    vm.CompletionSettingsEditor.RepeatCount = 10;
    vm.CompletionSettingsEditor.ReturnToStartOnFinish = true;
    vm.CompletionSettingsEditor.SaveCommand.Execute(null);

    Assert.False(vm.IsEditingCompletionSettings);
    Assert.Equal(ProgramCompletionMode.Loop, vm.CompletionMode);
    Assert.Equal(10, vm.RepeatCount);
    Assert.True(vm.ReturnToStartOnFinish);
}

[Fact]
public void EditCompletionSettings_Cancel_LeavesProgramViewModelUnchangedAndClosesEditor()
{
    var vm = CreateViewModel(out _, out _);
    vm.CompletionMode = ProgramCompletionMode.Stop;

    vm.EditCompletionSettingsCommand.Execute(null);
    vm.CompletionSettingsEditor!.Mode = ProgramCompletionMode.Loop;
    vm.CompletionSettingsEditor.CancelCommand.Execute(null);

    Assert.False(vm.IsEditingCompletionSettings);
    Assert.Equal(ProgramCompletionMode.Stop, vm.CompletionMode);
}

[Fact]
public async Task SaveProgramAsync_ThenLoadProgramAsync_RoundTripsCompletionSettings()
{
    var vm = CreateViewModel(out var transport, out _);
    await vm.Connection.ConnectCommand.Execute();
    transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
    vm.CaptureKeyPointCommand.Execute(null);
    vm.ProgramName = "Тест";
    vm.CompletionMode = ProgramCompletionMode.PingPong;
    vm.RepeatCount = 3;
    vm.ReturnToStartOnFinish = true;

    var saveTask = vm.SaveProgramCommand.ExecuteAsync(null);
    Assert.NotNull(vm.PendingRename);
    vm.ConfirmRenameCommand.Execute(null);
    await saveTask;

    vm.NewProgramCommand.Execute(null);
    Assert.Equal(ProgramCompletionMode.Stop, vm.CompletionMode);
    Assert.False(vm.ReturnToStartOnFinish);
    Assert.Null(vm.RepeatCount);

    await vm.RefreshLibraryCommand.ExecuteAsync(null);
    await vm.LoadProgramCommand.ExecuteAsync(vm.Library.Single(p => p.Name == "Тест"));

    Assert.Equal(ProgramCompletionMode.PingPong, vm.CompletionMode);
    Assert.Equal(3, vm.RepeatCount);
    Assert.True(vm.ReturnToStartOnFinish);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelAuthoringTests"`
Expected: compile error (`CompletionMode`, `RepeatCount`, `ReturnToStartOnFinish`, `EditCompletionSettingsCommand`, `CompletionSettingsEditor`, `IsEditingCompletionSettings` don't exist yet on `ProgramViewModel`).

- [ ] **Step 3: Add the mirrored properties and command**

In `ArctZ/ViewModels/ProgramViewModel.cs`, add `using ArctZ.Services.Program;` is already present (line 12). Add new observable properties right after the existing `IsSideMenuOpen` block (after line 74, before the `DisplayProgress` doc comment):

```csharp
    [ObservableProperty]
    private bool _isSideMenuOpen;

    [ObservableProperty]
    private ProgramCompletionMode _completionMode = ProgramCompletionMode.Stop;

    [ObservableProperty]
    private bool _returnToStartOnFinish;

    [ObservableProperty]
    private int? _repeatCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingCompletionSettings))]
    private CompletionSettingsViewModel? _completionSettingsEditor;

    public bool IsEditingCompletionSettings => CompletionSettingsEditor is not null;
```

- [ ] **Step 4: Add the open/apply command**

Add right after the existing `EditKeyPoint`/`ApplyKeyPointEdit` pair (after `ApplyKeyPointEdit`, i.e. after the closing brace that currently follows line 423):

```csharp
    [RelayCommand]
    private void EditCompletionSettings()
    {
        CompletionSettingsEditor = new CompletionSettingsViewModel(
            CompletionMode,
            RepeatCount,
            ReturnToStartOnFinish,
            ApplyCompletionSettingsEdit,
            () => CompletionSettingsEditor = null);
    }

    private void ApplyCompletionSettingsEdit(ProgramCompletionMode mode, int? repeatCount, bool returnToStartOnFinish)
    {
        CompletionMode = mode;
        RepeatCount = repeatCount;
        ReturnToStartOnFinish = returnToStartOnFinish;
        CompletionSettingsEditor = null;
    }
```

- [ ] **Step 5: Wire into `NewProgram`, `LoadProgramAsync`, `BuildProgram`**

Replace:

```csharp
    [RelayCommand]
    private void NewProgram()
    {
        ProgramId = null;
        ProgramName = "Новая программа";
        KeyPoints.Clear();
        SelectedKeyPoint = null;
    }
```

with:

```csharp
    [RelayCommand]
    private void NewProgram()
    {
        ProgramId = null;
        ProgramName = "Новая программа";
        CompletionMode = ProgramCompletionMode.Stop;
        ReturnToStartOnFinish = false;
        RepeatCount = null;
        KeyPoints.Clear();
        SelectedKeyPoint = null;
    }
```

Replace:

```csharp
        var program = await _storage.LoadAsync(summary.Id);

        ProgramId = program.Id;
        ProgramName = program.Name;

        KeyPoints.Clear();
```

with:

```csharp
        var program = await _storage.LoadAsync(summary.Id);

        ProgramId = program.Id;
        ProgramName = program.Name;
        CompletionMode = program.CompletionMode;
        ReturnToStartOnFinish = program.ReturnToStartOnFinish;
        RepeatCount = program.RepeatCount;

        KeyPoints.Clear();
```

Replace:

```csharp
    private JibProgram BuildProgram()
    {
        var program = new JibProgram { Id = ProgramId ?? Guid.NewGuid(), Name = ProgramName };
        program.KeyPoints.AddRange(KeyPoints);
        return program;
    }
```

with:

```csharp
    private JibProgram BuildProgram()
    {
        var program = new JibProgram
        {
            Id = ProgramId ?? Guid.NewGuid(),
            Name = ProgramName,
            CompletionMode = CompletionMode,
            ReturnToStartOnFinish = ReturnToStartOnFinish,
            RepeatCount = RepeatCount
        };
        program.KeyPoints.AddRange(KeyPoints);
        return program;
    }
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelAuthoringTests"`
Expected: PASS (all, including pre-existing tests in this file).

- [ ] **Step 7: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/ViewModels/ProgramViewModelAuthoringTests.cs
git commit -m "feat: wire completion settings into ProgramViewModel persistence"
```

---

### Task 5: XAML — menu item and modal

No unit tests (XAML). Verified visually in Task 10. Build must succeed.

**Files:**
- Modify: `ArctZ/Views/MainView.axaml`

**Interfaces:**
- Consumes: `ProgramViewModel.EditCompletionSettingsCommand`, `.IsEditingCompletionSettings`, `.CompletionSettingsEditor` (Task 4); `CompletionSettingsViewModel.Mode/RepeatCount/IsRepeatUnlimited/ReturnToStartOnFinish/ShowRepeatCount/SaveCommand/CancelCommand` (Task 3); `EnumEqualsConverter` (Task 2).

- [ ] **Step 1: Register the converter**

In `ArctZ/Views/MainView.axaml`, in the `<UserControl.Resources>` block (around line 16-19), add after the existing converter entries:

```xml
        <conv:ConnectionStateToBrushConverter x:Key="StateToBrush" />
        <conv:LabelLengthToFontSizeConverter x:Key="LabelLengthToFontSize" />
        <conv:KeyPointIsExecutingConverter x:Key="KeyPointIsExecuting" />
        <conv:EnumEqualsConverter x:Key="EnumEquals" />
        <js:RadiusToSizeConverter x:Key="RadiusToSize" />
```

- [ ] **Step 2: Add the menu item**

Replace:

```xml
                                                <MenuItem Header="Переименовать" Command="{Binding RenameProgramCommand}" />
                                                <MenuItem Header="Новая" Command="{Binding NewProgramCommand}" />
                                                <MenuItem Header="Сохранить" Command="{Binding SaveProgramCommand}" />
                                                <MenuItem Header="Библиотека" Command="{Binding OpenLibraryCommand}" />
```

with:

```xml
                                                <MenuItem Header="Переименовать" Command="{Binding RenameProgramCommand}" />
                                                <MenuItem Header="Новая" Command="{Binding NewProgramCommand}" />
                                                <MenuItem Header="Сохранить" Command="{Binding SaveProgramCommand}" />
                                                <MenuItem Header="Настройки завершения" Command="{Binding EditCompletionSettingsCommand}" />
                                                <MenuItem Header="Библиотека" Command="{Binding OpenLibraryCommand}" />
```

- [ ] **Step 3: Add the modal overlay**

The `xmlns:program="using:ArctZ.Services.Program"` alias already exists (line 7) and covers `ProgramCompletionMode` too, since it's declared in the same namespace as `KeyPoint`.

Insert a new overlay block right after the closing `</Border>` of the existing `IsEditingKeyPoint` overlay (i.e., immediately after the block that ends around what was originally line 273, before the `PendingRename` overlay block):

```xml
                <Border IsVisible="{Binding IsEditingCompletionSettings}" Background="{StaticResource HudScrimBrush}">
                    <Border x:DataType="vm:CompletionSettingsViewModel" DataContext="{Binding CompletionSettingsEditor}"
                            Width="320" Background="{StaticResource HudPanelElevatedBrush}"
                            BorderBrush="{StaticResource HudBorderStrongBrush}" BorderThickness="1"
                            Padding="20" HorizontalAlignment="Center" VerticalAlignment="Center">
                        <StackPanel Spacing="10">
                            <TextBlock Classes="section-heading" Text="НАСТРОЙКИ ЗАВЕРШЕНИЯ" />
                            <RadioButton GroupName="CompletionMode" Content="Завершение"
                                         IsChecked="{Binding Mode, Converter={StaticResource EnumEquals}, ConverterParameter={x:Static program:ProgramCompletionMode.Stop}}" />
                            <RadioButton GroupName="CompletionMode" Content="Начать с начала (по циклу)"
                                         IsChecked="{Binding Mode, Converter={StaticResource EnumEquals}, ConverterParameter={x:Static program:ProgramCompletionMode.Loop}}" />
                            <RadioButton GroupName="CompletionMode" Content="В обратном порядке"
                                         IsChecked="{Binding Mode, Converter={StaticResource EnumEquals}, ConverterParameter={x:Static program:ProgramCompletionMode.PingPong}}" />

                            <StackPanel Spacing="6" IsVisible="{Binding ShowRepeatCount}" Margin="0,4,0,0">
                                <TextBlock Text="Количество повторов" />
                                <TextBox Text="{Binding RepeatCount}" IsEnabled="{Binding !IsRepeatUnlimited}" />
                                <CheckBox Content="Неограниченно" IsChecked="{Binding IsRepeatUnlimited}" />
                            </StackPanel>

                            <CheckBox Content="Встать в начальную позицию по завершении" Margin="0,4,0,0"
                                      IsChecked="{Binding ReturnToStartOnFinish}" />

                            <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right" Margin="0,6,0,0">
                                <Button Content="Отмена" Command="{Binding CancelCommand}" />
                                <Button Classes="primary" Content="Сохранить" Command="{Binding SaveCommand}" />
                            </StackPanel>
                        </StackPanel>
                    </Border>
                </Border>
```

- [ ] **Step 4: Build to verify XAML compiles**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: Build succeeds (compiled-bindings validation catches any XAML/type mismatch at build time).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Views/MainView.axaml
git commit -m "feat: add completion-settings menu item and modal"
```

---

### Task 6: Extract `RunPassAsync` from `PlayAsync` (pure refactor)

Behavior-preserving: pulls the existing dispatch-and-await-acks block out into a reusable method and makes `CurrentlyExecutingKeyPointId` direction-aware, without changing single-pass (Stop-mode) behavior. All existing tests in `ProgramViewModelPlaybackTests.cs` must keep passing unchanged — that's this task's test coverage.

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs`

**Interfaces:**
- Produces: `private async Task<bool> RunPassAsync(IReadOnlyList<CompiledStep> steps, bool backward)` — dispatches all steps, awaits acks in order, updates `CurrentSegmentIndex`/`SegmentProgress`/animation state per pass; returns `true` if the pass completed with `PlaybackState` still `Running`, `false` otherwise (caller must then return immediately — state is already set).

- [ ] **Step 1: Add the `_currentPassBackward` field**

In `ArctZ/ViewModels/ProgramViewModel.cs`, add next to the other playback-tracking fields (right after `private bool _pausedForLinkLoss;`, around line 533):

```csharp
    private bool _pausedForLinkLoss;
    private bool _currentPassBackward;
```

- [ ] **Step 2: Make `CurrentlyExecutingKeyPointId` direction-aware**

Replace:

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

with:

```csharp
    public Guid? CurrentlyExecutingKeyPointId
    {
        get
        {
            if (PlaybackState is not (PlaybackState.Running or PlaybackState.Paused))
            {
                return null;
            }

            var segmentIndex = CurrentSegmentIndex ?? -1;
            var targetIndex = _currentPassBackward
                ? KeyPoints.Count - 2 - segmentIndex
                : segmentIndex + 1;
            return targetIndex >= 0 && targetIndex < KeyPoints.Count
                ? KeyPoints[targetIndex].Id
                : null;
        }
    }
```

(Forward math is unchanged. Backward: pass `i` moves from `KeyPoints[Count-1-i]` to `KeyPoints[Count-2-i]`, so the point currently being approached is `KeyPoints[Count-2-segmentIndex]` — at pass start, `segmentIndex == -1`, giving `Count-1`, i.e. the last key point, which is where the backward pass starts from, mirroring how the forward pass reports index `0` — its own starting point — before any ack.)

- [ ] **Step 3: Extract `RunPassAsync`**

Replace the body of `PlayAsync` from the `var steps = _compiler.Compile(BuildProgram());` line through the end of the method (everything from where `steps` is compiled down to the closing brace) — i.e. replace:

```csharp
        var steps = _compiler.Compile(BuildProgram());
        if (steps.Count == 0)
        {
            return;
        }

        PlaybackState = PlaybackState.Running;
        CurrentSegmentIndex = null;
        SegmentProgress = 0;
        FaultedAtSegmentIndex = null;
        TotalSegments = Math.Max(0, KeyPoints.Count - 1);

        lock (_animLock)
        {
            DisplayProgress = 0;
            _visualSteps = steps;
            _visualStepIndex = 0;
            _animStartProgress = 0;
            _animTargetProgress = StepOverallProgress(steps[0]);
            _animDurationSeconds = steps[0].EstimatedDurationSeconds;
            _animElapsedSeconds = 0;
            _animActive = true;
        }

        var dispatched = new (CompiledStep Step, Task<CommandResult> Completion)[steps.Count];
        for (var i = 0; i < steps.Count; i++)
        {
            var line = ((GCodeLineCommand)steps[i].Command).Line;
            dispatched[i] = (steps[i], Connection.Session!.SendGCodeAsync(line));
        }

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

        if (PlaybackState != PlaybackState.Running)
        {
            return;
        }

        await WaitForMotionToFinishAsync();

        if (PlaybackState == PlaybackState.Running)
        {
            PlaybackState = PlaybackState.Completed;
        }
    }
```

with:

```csharp
        var steps = _compiler.Compile(BuildProgram());
        if (steps.Count == 0)
        {
            return;
        }

        PlaybackState = PlaybackState.Running;
        FaultedAtSegmentIndex = null;
        TotalSegments = Math.Max(0, KeyPoints.Count - 1);

        if (!await RunPassAsync(steps, backward: false))
        {
            return;
        }

        await WaitForMotionToFinishAsync();

        if (PlaybackState == PlaybackState.Running)
        {
            PlaybackState = PlaybackState.Completed;
        }
    }

    private async Task<bool> RunPassAsync(IReadOnlyList<CompiledStep> steps, bool backward)
    {
        _currentPassBackward = backward;
        CurrentSegmentIndex = null;
        SegmentProgress = 0;

        lock (_animLock)
        {
            DisplayProgress = 0;
            _visualSteps = steps;
            _visualStepIndex = 0;
            _animStartProgress = 0;
            _animTargetProgress = StepOverallProgress(steps[0]);
            _animDurationSeconds = steps[0].EstimatedDurationSeconds;
            _animElapsedSeconds = 0;
            _animActive = true;
        }

        var dispatched = new (CompiledStep Step, Task<CommandResult> Completion)[steps.Count];
        for (var i = 0; i < steps.Count; i++)
        {
            var line = ((GCodeLineCommand)steps[i].Command).Line;
            dispatched[i] = (steps[i], Connection.Session!.SendGCodeAsync(line));
        }

        foreach (var (step, completion) in dispatched)
        {
            var result = await completion;

            if (PlaybackState == PlaybackState.Stopped)
            {
                return false;
            }

            if (result.Outcome != CommandOutcome.Acknowledged)
            {
                PlaybackState = PlaybackState.Faulted;
                FaultedAtSegmentIndex = step.SegmentIndex;
                return false;
            }

            CurrentSegmentIndex = step.SegmentIndex;
            SegmentProgress = step.SegmentProgress;
        }

        return PlaybackState == PlaybackState.Running;
    }
```

- [ ] **Step 4: Run the full playback test suite to verify nothing broke**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: PASS — every pre-existing test in this file (all Stop-mode, single-pass scenarios) must still pass unchanged, since `CompletionMode` defaults to `Stop` and `_currentPassBackward` defaults to `false`.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS (no regressions anywhere else).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs
git commit -m "refactor: extract RunPassAsync from PlayAsync, make executing-keypoint direction-aware"
```

---

### Task 7: PingPong mode

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs`
- Test: `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs`

**Interfaces:**
- Consumes: `RunPassAsync` (Task 6), `CompletionMode`/`RepeatCount` (Task 4).
- Produces: `PlayAsync` now runs a forward pass, then (only in `PingPong` mode) a backward pass compiled from the key points in reverse order, repeating the forward+backward pair up to `RepeatCount` times (or forever if `null`), before the single final `WaitForMotionToFinishAsync`.

- [ ] **Step 1: Write the failing test**

Add to `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs`:

```csharp
[Fact]
public async Task PlayAsync_PingPongMode_RunsForwardThenBackward_HighlightingPointsInReverseOnTheReturnLeg()
{
    var vm = CreateViewModel(out var transport);
    await vm.Connection.ConnectCommand.Execute();
    SeedTwoSegmentProgram(vm, transport);
    vm.CompletionMode = ProgramCompletionMode.PingPong;
    vm.RepeatCount = 1;

    var playTask = vm.PlayCommand.ExecuteAsync(null);
    Assert.Equal(vm.KeyPoints[0].Id, vm.CurrentlyExecutingKeyPointId);

    // Forward leg: 2 acks, no idle wait in between passes.
    transport.SimulateReceivedLine("ok");
    await WaitUntilAsync(() => vm.CurrentSegmentIndex == 0, TimeSpan.FromSeconds(1));
    Assert.Equal(vm.KeyPoints[1].Id, vm.CurrentlyExecutingKeyPointId);

    transport.SimulateReceivedLine("ok");
    await WaitUntilAsync(
        () => transport.SentLines.Count(l => l.StartsWith("G1", StringComparison.Ordinal)) == 4,
        TimeSpan.FromSeconds(1));
    Assert.False(vm.IsAwaitingMotionIdle, "no physical-idle wait should happen between the forward and backward legs");

    // Backward leg: highlight now counts down from the last key point.
    Assert.Equal(vm.KeyPoints[2].Id, vm.CurrentlyExecutingKeyPointId);

    transport.SimulateReceivedLine("ok");
    await WaitUntilAsync(() => vm.CurrentSegmentIndex == 0 && vm.SegmentProgress == 1.0, TimeSpan.FromSeconds(1));
    Assert.Equal(vm.KeyPoints[1].Id, vm.CurrentlyExecutingKeyPointId);

    transport.SimulateReceivedLine("ok");
    await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
    Assert.Equal(vm.KeyPoints[0].Id, vm.CurrentlyExecutingKeyPointId);

    transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");
    await playTask;

    Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
    Assert.Null(vm.CurrentlyExecutingKeyPointId);
}

[Fact]
public async Task PlayAsync_PingPongMode_RepeatsForwardBackwardPairUpToRepeatCount()
{
    var vm = CreateViewModel(out var transport);
    await vm.Connection.ConnectCommand.Execute();
    SeedTwoSegmentProgram(vm, transport);
    vm.CompletionMode = ProgramCompletionMode.PingPong;
    vm.RepeatCount = 2;

    var playTask = vm.PlayCommand.ExecuteAsync(null);

    // First pair: forward (2) + backward (2) = 4 G1 lines.
    for (var i = 0; i < 4; i++)
    {
        transport.SimulateReceivedLine("ok");
    }
    // Second forward+backward pair should be dispatched immediately after the first pair's acks, with no idle wait between pairs.
    await WaitUntilAsync(
        () => transport.SentLines.Count(l => l.StartsWith("G1", StringComparison.Ordinal)) == 8,
        TimeSpan.FromSeconds(1));

    // Second (last) pair: another 4 acks, then the run waits for real motion to finish.
    for (var i = 0; i < 4; i++)
    {
        transport.SimulateReceivedLine("ok");
    }
    await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
    transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");
    await playTask;

    Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
    Assert.Equal(8, transport.SentLines.Count(l => l.StartsWith("G1", StringComparison.Ordinal)));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: FAIL on the two new PingPong tests (only one G1-pass currently ever runs; `CompletionMode`/`RepeatCount` are ignored by `PlayAsync` so far).

- [ ] **Step 3: Add reversed-program compilation and the cycle loop**

In `ArctZ/ViewModels/ProgramViewModel.cs`, replace the `PlayAsync` body written in Task 6:

```csharp
        var steps = _compiler.Compile(BuildProgram());
        if (steps.Count == 0)
        {
            return;
        }

        PlaybackState = PlaybackState.Running;
        FaultedAtSegmentIndex = null;
        TotalSegments = Math.Max(0, KeyPoints.Count - 1);

        if (!await RunPassAsync(steps, backward: false))
        {
            return;
        }

        await WaitForMotionToFinishAsync();

        if (PlaybackState == PlaybackState.Running)
        {
            PlaybackState = PlaybackState.Completed;
        }
    }
```

with:

```csharp
        var forwardProgram = BuildProgram();
        var forwardSteps = _compiler.Compile(forwardProgram);
        if (forwardSteps.Count == 0)
        {
            return;
        }

        var backwardSteps = CompletionMode == ProgramCompletionMode.PingPong
            ? _compiler.Compile(ReversedProgram(forwardProgram))
            : null;

        PlaybackState = PlaybackState.Running;
        FaultedAtSegmentIndex = null;
        TotalSegments = Math.Max(0, KeyPoints.Count - 1);

        var cycle = 0;
        while (true)
        {
            if (!await RunPassAsync(forwardSteps, backward: false))
            {
                return;
            }

            if (backwardSteps is not null)
            {
                if (!await RunPassAsync(backwardSteps, backward: true))
                {
                    return;
                }
            }

            cycle++;

            var isLastCycle = CompletionMode == ProgramCompletionMode.Stop
                || (RepeatCount is int repeatLimit && cycle >= repeatLimit);
            if (isLastCycle)
            {
                break;
            }
        }

        if (PlaybackState != PlaybackState.Running)
        {
            return;
        }

        await WaitForMotionToFinishAsync();

        if (PlaybackState == PlaybackState.Running)
        {
            PlaybackState = PlaybackState.Completed;
        }
    }

    private static JibProgram ReversedProgram(JibProgram source)
    {
        var reversed = new JibProgram { Id = source.Id, Name = source.Name };
        reversed.KeyPoints.AddRange(source.KeyPoints.AsEnumerable().Reverse());
        return reversed;
    }
```

(`System.Linq` is already imported at the top of the file.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: PASS (all, including the two new PingPong tests and every pre-existing Stop-mode test).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs
git commit -m "feat: run PlayAsync in PingPong mode (forward/backward repeats)"
```

---

### Task 8: Loop mode

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs`
- Test: `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs`

**Interfaces:**
- Consumes: cycle loop from Task 7.
- Produces: `RunReturnToStartMoveAsync()` — dispatches one G1 move to `KeyPoints[0]`'s pose/feed, awaits its ack, returns `false` (with `PlaybackState` already set to `Stopped`/`Faulted`) on failure. `Loop` mode calls it between cycles (not after the last one).

- [ ] **Step 1: Write the failing test**

Add to `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs`:

```csharp
[Fact]
public async Task PlayAsync_LoopMode_SendsReturnToStartMoveBetweenCyclesButNotAfterTheLastOne()
{
    var vm = CreateViewModel(out var transport);
    await vm.Connection.ConnectCommand.Execute();
    SeedTwoSegmentProgram(vm, transport);
    vm.CompletionMode = ProgramCompletionMode.Loop;
    vm.RepeatCount = 2;

    var playTask = vm.PlayCommand.ExecuteAsync(null);

    // Cycle 1: 2 forward G1 lines.
    transport.SimulateReceivedLine("ok");
    transport.SimulateReceivedLine("ok");

    // The implicit return-to-start move is dispatched right after cycle 1's acks.
    await WaitUntilAsync(
        () => transport.SentLines.Contains("G1 X0 Y0 Z0 A0 F500"),
        TimeSpan.FromSeconds(1));
    Assert.False(vm.IsAwaitingMotionIdle, "the return-to-start move between cycles must not wait for physical idle");

    transport.SimulateReceivedLine("ok"); // acks the return-to-start move

    // Cycle 2 (the last one, RepeatCount == 2): 2 more forward G1 lines, no further return move.
    await WaitUntilAsync(
        () => transport.SentLines.Count(l => l.StartsWith("G1", StringComparison.Ordinal)) == 5,
        TimeSpan.FromSeconds(1));

    transport.SimulateReceivedLine("ok");
    transport.SimulateReceivedLine("ok");
    await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
    transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");
    await playTask;

    Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
    Assert.Equal(5, transport.SentLines.Count(l => l.StartsWith("G1", StringComparison.Ordinal)));
}

[Fact]
public async Task PlayAsync_LoopMode_UnlimitedRepeatCount_KeepsRunningUntilStopped()
{
    var vm = CreateViewModel(out var transport);
    await vm.Connection.ConnectCommand.Execute();
    SeedTwoSegmentProgram(vm, transport);
    vm.CompletionMode = ProgramCompletionMode.Loop;
    vm.RepeatCount = null;

    var playTask = vm.PlayCommand.ExecuteAsync(null);

    // Run through 3 full cycles (2 forward acks + 1 return-move ack each) without ever completing.
    for (var i = 0; i < 3; i++)
    {
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(
            () => transport.SentLines.Count(l => l == "G1 X0 Y0 Z0 A0 F500") == i + 1,
            TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("ok");
    }

    Assert.False(playTask.IsCompleted);
    Assert.Equal(PlaybackState.Running, vm.PlaybackState);

    await vm.StopCommand.ExecuteAsync(null);
    transport.SimulateReceivedLine("ok"); // resolves the command already in flight
    await playTask;

    Assert.Equal(PlaybackState.Stopped, vm.PlaybackState);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: FAIL on the two new Loop tests (Loop mode currently behaves exactly like Stop — no return-to-start move, no repeats).

- [ ] **Step 3: Add `RunReturnToStartMoveAsync` and call it from the cycle loop**

Add the helper method right after `RunPassAsync` (defined in Task 6):

```csharp
    private async Task<bool> RunReturnToStartMoveAsync()
    {
        var start = KeyPoints[0];
        var line = $"G1 X{FormatAxis(start.Pose.X)} Y{FormatAxis(start.Pose.Y)} Z{FormatAxis(start.Pose.Z)} A{FormatAxis(start.Pose.A)} F{FormatAxis(start.FeedRateUnitsPerMin)}";
        var result = await Connection.Session!.SendGCodeAsync(line);

        if (PlaybackState == PlaybackState.Stopped)
        {
            return false;
        }

        if (result.Outcome != CommandOutcome.Acknowledged)
        {
            PlaybackState = PlaybackState.Faulted;
            return false;
        }

        return PlaybackState == PlaybackState.Running;
    }
```

In the cycle loop added in Task 7, replace:

```csharp
            cycle++;

            var isLastCycle = CompletionMode == ProgramCompletionMode.Stop
                || (RepeatCount is int repeatLimit && cycle >= repeatLimit);
            if (isLastCycle)
            {
                break;
            }
        }
```

with:

```csharp
            cycle++;

            var isLastCycle = CompletionMode == ProgramCompletionMode.Stop
                || (RepeatCount is int repeatLimit && cycle >= repeatLimit);
            if (isLastCycle)
            {
                break;
            }

            if (CompletionMode == ProgramCompletionMode.Loop)
            {
                if (!await RunReturnToStartMoveAsync())
                {
                    return;
                }
            }
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: PASS (all, including the two new Loop tests, the PingPong tests from Task 7, and every pre-existing Stop-mode test).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs
git commit -m "feat: run PlayAsync in Loop mode (repeat with return-to-start between cycles)"
```

---

### Task 9: `ReturnToStartOnFinish` (all modes)

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs`
- Test: `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs`

**Interfaces:**
- Consumes: `RunReturnToStartMoveAsync` (Task 8), `ReturnToStartOnFinish` (Task 4).
- Produces: on natural completion (not manual Stop), if `ReturnToStartOnFinish` is set, one more move to the first key point is dispatched and its physical completion awaited before `PlaybackState` becomes `Completed`.

- [ ] **Step 1: Write the failing tests**

Add to `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs`:

```csharp
[Fact]
public async Task PlayAsync_ReturnToStartOnFinish_MovesToFirstKeyPointAfterNaturalCompletion()
{
    var vm = CreateViewModel(out var transport);
    await vm.Connection.ConnectCommand.Execute();
    SeedTwoSegmentProgram(vm, transport);
    vm.ReturnToStartOnFinish = true;

    var playTask = vm.PlayCommand.ExecuteAsync(null);

    transport.SimulateReceivedLine("ok");
    transport.SimulateReceivedLine("ok");
    await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
    transport.SimulateReceivedLine("<Idle|WPos:20.000,0.000,0.000,0.000|FS:0,0>");

    await WaitUntilAsync(() => transport.SentLines.Contains("G1 X0 Y0 Z0 A0 F500"), TimeSpan.FromSeconds(1));
    Assert.False(playTask.IsCompleted, "must wait for the return-to-start move's own physical completion before finishing");

    transport.SimulateReceivedLine("ok");
    await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
    transport.SimulateReceivedLine("<Idle|WPos:0.000,0.000,0.000,0.000|FS:0,0>");
    await playTask;

    Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
}

[Fact]
public async Task Stop_WithReturnToStartOnFinishEnabled_DoesNotTriggerTheReturnMove()
{
    var vm = CreateViewModel(out var transport);
    await vm.Connection.ConnectCommand.Execute();
    SeedTwoSegmentProgram(vm, transport);
    vm.ReturnToStartOnFinish = true;

    var playTask = vm.PlayCommand.ExecuteAsync(null);
    await vm.StopCommand.ExecuteAsync(null);
    transport.SimulateReceivedLine("ok"); // resolves the command already in flight
    await playTask;

    Assert.Equal(PlaybackState.Stopped, vm.PlaybackState);
    Assert.DoesNotContain("G1 X0 Y0 Z0 A0 F500", transport.SentLines);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: FAIL on `PlayAsync_ReturnToStartOnFinish_MovesToFirstKeyPointAfterNaturalCompletion` (no return move is sent yet; `ReturnToStartOnFinish` is not read anywhere in `PlayAsync`). The Stop test should already pass (nothing triggers the move today), which is fine — it locks in the negative behavior going forward.

- [ ] **Step 3: Wire `ReturnToStartOnFinish` into the end of `PlayAsync`**

Replace:

```csharp
        if (PlaybackState != PlaybackState.Running)
        {
            return;
        }

        await WaitForMotionToFinishAsync();

        if (PlaybackState == PlaybackState.Running)
        {
            PlaybackState = PlaybackState.Completed;
        }
    }
```

with:

```csharp
        if (PlaybackState != PlaybackState.Running)
        {
            return;
        }

        await WaitForMotionToFinishAsync();

        if (PlaybackState != PlaybackState.Running)
        {
            return;
        }

        if (ReturnToStartOnFinish)
        {
            if (!await RunReturnToStartMoveAsync())
            {
                return;
            }

            await WaitForMotionToFinishAsync();

            if (PlaybackState != PlaybackState.Running)
            {
                return;
            }
        }

        PlaybackState = PlaybackState.Completed;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelPlaybackTests"`
Expected: PASS (all tests in the file).

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS everywhere — this is the last logic change, so this is the final regression gate before manual UI verification.

- [ ] **Step 6: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs
git commit -m "feat: move to the first key point on natural completion when ReturnToStartOnFinish is set"
```

---

### Task 10: Manual UI verification

Per `CLAUDE.md`, this is the **only** acceptable way to sign off on a UI-facing change — build, actually run the app, and have the user confirm each behavior through `AskUserQuestion` (one question per behavior, not one blanket "looks fine?").

**Files:** none (verification only).

- [ ] **Step 1: Build the Desktop head**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: Build succeeds.

- [ ] **Step 2: Run the app**

Run: `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj` (or launch the built exe) — must be actually running, not just built.

- [ ] **Step 3: Ask the user to exercise each behavior, then confirm via `AskUserQuestion`**

Have the user, against a real or mock-connected session:
1. Open «⋮» → «Настройки завершения», confirm the dialog shows three modes, the repeat-count field only appears for Loop/PingPong, "Неограниченно" disables the repeat field, and the mode-specific min/max clamps on Save.
2. Create/select a 3+ key point program, set mode = Завершение with "Встать в начальную позицию" checked, run it, confirm it finishes and then returns to the first point.
3. Set mode = Начать с начала with a finite repeat count (e.g. 2), run it, confirm it repeats the whole path that many times (with a visible return-to-start jump between repeats) and then stops.
4. Set mode = Начать с начала with «Неограниченно» checked, run it, confirm it keeps looping until «Стоп» is pressed, and that Stop actually halts it.
5. Set mode = В обратном порядке with a finite repeat count (e.g. 2), run it, confirm the key-point highlight visibly moves backward on the return leg and the whole ping-pong sequence repeats and then stops.

Ask one `AskUserQuestion` per numbered behavior above (5 questions), each with Yes/No-style options plus room for free-text notes on anything broken.

- [ ] **Step 4: Fix and re-verify anything the user flags**

If any answer indicates a problem, fix it, rebuild, re-run, and re-ask only the affected question(s) — don't re-ask ones already confirmed working.
