# Program Menu and Rename Dialog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the `Новая`/`Сохранить`/`Библиотека` buttons into a "⋮" overflow menu next to the program name, make the program name read-only in the panel, and add a dedicated rename dialog that's used both for explicit renaming and as a mandatory first step when saving a never-saved program.

**Architecture:** Add a new lightweight overlay-request type `RenameProgramRequest` (mirrors the existing `ConfirmationRequest` pattern but carries an editable `Name` instead of yes/no) plus `PendingRename`/`RequestNameAsync`/`ConfirmRenameCommand`/`CancelRenameCommand`/`RenameProgramCommand` on `ProgramViewModel`. Rewire `SaveProgramAsync` so a `ProgramId == null` save routes through the rename dialog before the existing collision-check/save logic; an already-saved program's save flow is unchanged. Rewrite the name/button block in `MainView.axaml` to a `TextBlock` + "⋮" `MenuFlyout`, and add a new overlay `Border` for the rename dialog next to the existing `KeyPointEditor`/`PendingConfirmation`/library overlays.

**Tech Stack:** Avalonia UI (compiled bindings, `x:DataType` required), CommunityToolkit.Mvvm 8.4.0 source-gen (`[ObservableProperty]`, `[RelayCommand]`), xUnit (`ArctZ.Tests`).

## Global Constraints

- Menu item order and exact Russian labels: `Переименовать`, `Новая`, `Сохранить`, `Библиотека`.
- Rename dialog copy: heading `ИМЯ ПРОГРАММЫ`, `TextBox` placeholder `Имя программы`, buttons `Отмена` / `Сохранить` (matches the existing `KeyPointEditor` cancel/save button copy).
- An empty name (after `.Trim()`) silently blocks confirmation — the dialog just stays open. No validation message in this scope.
- Compiled bindings are on by default — the new rename-dialog `DataTemplate`/nested-`DataContext` block needs `x:DataType="vm:RenameProgramRequest"`. Preserve the existing `((vm:ProgramViewModel)DataContext).XyzCommand, ElementName=RootPanel` pattern already used by the `PendingConfirmation` overlay's Да/Нет buttons for routing dialog button commands back to `ProgramViewModel`.
- Verification: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj` must pass after each ViewModel task; `dotnet build ArctZ/ArctZ.csproj` must succeed after the XAML task.
- Do not touch `NewProgramCommand`, `OpenLibraryCommand`, `CaptureKeyPointCommand`, `KeyPointEditorViewModel`, `ConfirmationRequest`, the library overlay, or `MainView.axaml.cs` — none of them change in this plan.
- `Захватить точку` stays a standalone visible `Button` — it does not move into the menu.

---

## Task 1: `RenameProgramRequest` + rename plumbing on `ProgramViewModel`

**Files:**
- Create: `ArctZ/ViewModels/RenameProgramRequest.cs`
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs` (insert after the `ConfirmNo()` command, currently `ArctZ/ViewModels/ProgramViewModel.cs:132-137`)
- Test: `ArctZ.Tests/ViewModels/ProgramViewModelAuthoringTests.cs`

**Interfaces:**
- Consumes: nothing new — uses only `System.Threading.Tasks.TaskCompletionSource<T>` (already used by `ConfirmAsync` in the same file) and `CommunityToolkit.Mvvm.ComponentModel.ObservableObject`.
- Produces: `ProgramViewModel.PendingRename` (`RenameProgramRequest?`), `ProgramViewModel.RenameProgramCommand` (`IAsyncRelayCommand`), `ProgramViewModel.ConfirmRenameCommand` / `ProgramViewModel.CancelRenameCommand` (`IRelayCommand`), and `ProgramViewModel.RequestNameAsync(string initialName)` (private `Task<string?>` helper) — Task 2's `SaveProgramAsync` change consumes `RequestNameAsync`; Task 3's XAML consumes `PendingRename`, `RenameProgramCommand`, `ConfirmRenameCommand`, `CancelRenameCommand`, and `RenameProgramRequest.Name`.

- [ ] **Step 1: Write the failing tests**

Add these two test methods inside `ArctZ.Tests/ViewModels/ProgramViewModelAuthoringTests.cs`, right after the `LeftAndRightJoystick_EndJogOnlyAfterBothSticksReleased` test (the last one in the class, before the closing `}`):

```csharp
    [Fact]
    public void RenameProgramAsync_Confirmed_UpdatesProgramName()
    {
        var vm = CreateViewModel(out _, out _);
        vm.ProgramName = "Старое имя";

        var renameTask = vm.RenameProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingRename);
        Assert.Equal("Старое имя", vm.PendingRename!.Name);

        vm.PendingRename.Name = "Новое имя";
        vm.ConfirmRenameCommand.Execute(null);

        Assert.Null(vm.PendingRename);
        Assert.Equal("Новое имя", vm.ProgramName);
        Assert.True(renameTask.IsCompleted);
    }

    [Fact]
    public void RenameProgramAsync_Cancelled_LeavesProgramNameUnchanged()
    {
        var vm = CreateViewModel(out _, out _);
        vm.ProgramName = "Старое имя";

        var renameTask = vm.RenameProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingRename);

        vm.CancelRenameCommand.Execute(null);

        Assert.Null(vm.PendingRename);
        Assert.Equal("Старое имя", vm.ProgramName);
        Assert.True(renameTask.IsCompleted);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "RenameProgramAsync_Confirmed_UpdatesProgramName|RenameProgramAsync_Cancelled_LeavesProgramNameUnchanged"`
Expected: FAIL with a compile error — `ProgramViewModel` does not contain definitions for `RenameProgramCommand`, `PendingRename`, or `ConfirmRenameCommand`/`CancelRenameCommand`.

- [ ] **Step 3: Create `RenameProgramRequest.cs`**

```csharp
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArctZ.ViewModels;

public partial class RenameProgramRequest : ObservableObject
{
    internal RenameProgramRequest(string initialName, TaskCompletionSource<string?> completion)
    {
        _name = initialName;
        Completion = completion;
    }

    [ObservableProperty]
    private string _name;

    internal TaskCompletionSource<string?> Completion { get; }
}
```

- [ ] **Step 4: Add the rename plumbing to `ProgramViewModel`**

In `ArctZ/ViewModels/ProgramViewModel.cs`, this block currently reads:

```csharp
    [RelayCommand]
    private void ConfirmNo()
    {
        PendingConfirmation?.Completion.TrySetResult(false);
        PendingConfirmation = null;
    }

    [RelayCommand]
    private async Task SaveProgramAsync()
```

Insert the new members between `ConfirmNo()` and `SaveProgramAsync()`:

```csharp
    [RelayCommand]
    private void ConfirmNo()
    {
        PendingConfirmation?.Completion.TrySetResult(false);
        PendingConfirmation = null;
    }

    [ObservableProperty]
    private RenameProgramRequest? _pendingRename;

    private Task<string?> RequestNameAsync(string initialName)
    {
        var completion = new TaskCompletionSource<string?>();
        PendingRename = new RenameProgramRequest(initialName, completion);
        return completion.Task;
    }

    [RelayCommand]
    private void ConfirmRename()
    {
        var name = PendingRename?.Name.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        PendingRename?.Completion.TrySetResult(name);
        PendingRename = null;
    }

    [RelayCommand]
    private void CancelRename()
    {
        PendingRename?.Completion.TrySetResult(null);
        PendingRename = null;
    }

    [RelayCommand]
    private async Task RenameProgramAsync()
    {
        var name = await RequestNameAsync(ProgramName);
        if (name is not null)
        {
            ProgramName = name;
        }
    }

    [RelayCommand]
    private async Task SaveProgramAsync()
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "RenameProgramAsync_Confirmed_UpdatesProgramName|RenameProgramAsync_Cancelled_LeavesProgramNameUnchanged"`
Expected: PASS.

- [ ] **Step 6: Run the full test suite to check for regressions**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: all tests pass, including the pre-existing `ProgramViewModelAuthoringTests` and `ProgramViewModelPlaybackTests` suites.

- [ ] **Step 7: Commit**

```bash
git add ArctZ/ViewModels/RenameProgramRequest.cs ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/ViewModels/ProgramViewModelAuthoringTests.cs
git commit -m "feat: add rename dialog plumbing to ProgramViewModel"
```

---

## Task 2: Route new-program `SaveProgramAsync` through the rename dialog

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs:139-169` (the `SaveProgramAsync` method, using Task 1's line numbers before insertion — locate by method name, not line number, since Task 1 shifted it)
- Test: `ArctZ.Tests/ViewModels/ProgramViewModelAuthoringTests.cs`

**Interfaces:**
- Consumes: `ProgramViewModel.RequestNameAsync(string)` and `ProgramViewModel.PendingRename`/`ConfirmRenameCommand`/`CancelRenameCommand` from Task 1.
- Produces: nothing new — `SaveProgramCommand`'s public shape (`IAsyncRelayCommand`) is unchanged; only its internal sequencing changes. Task 3's XAML binds to it exactly as before.

- [ ] **Step 1: Update the six existing save tests and add one new test**

In `ArctZ.Tests/ViewModels/ProgramViewModelAuthoringTests.cs`, replace these seven items (six modified, one new) exactly as shown.

Replace `SaveProgramAsync_ThenRefreshLibrary_ListsSavedProgram`:

```csharp
    [Fact]
    public async Task SaveProgramAsync_ThenRefreshLibrary_ListsSavedProgram()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);
        vm.ProgramName = "Тест";

        var saveTask = vm.SaveProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingRename);
        vm.ConfirmRenameCommand.Execute(null);
        await saveTask;

        await vm.RefreshLibraryCommand.ExecuteAsync(null);

        Assert.Contains(vm.Library, s => s.Name == "Тест");
    }
```

Replace `SaveProgramAsync_ThenRefreshLibrary_MarksSavedProgramAsLoaded`:

```csharp
    [Fact]
    public async Task SaveProgramAsync_ThenRefreshLibrary_MarksSavedProgramAsLoaded()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);
        vm.ProgramName = "Тест";

        var saveTask = vm.SaveProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingRename);
        vm.ConfirmRenameCommand.Execute(null);
        await saveTask;

        await vm.RefreshLibraryCommand.ExecuteAsync(null);

        Assert.True(vm.Library.Single(s => s.Name == "Тест").IsLoaded);
    }
```

Replace `SaveProgramAsync_OverwritingLoadedProgram_AsksForConfirmation`:

```csharp
    [Fact]
    public async Task SaveProgramAsync_OverwritingLoadedProgram_AsksForConfirmation()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);
        vm.ProgramName = "Тест";

        var firstSaveTask = vm.SaveProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingRename);
        vm.ConfirmRenameCommand.Execute(null);
        await firstSaveTask;

        var saveTask = vm.SaveProgramCommand.ExecuteAsync(null);

        Assert.NotNull(vm.PendingConfirmation);
        vm.ConfirmYesCommand.Execute(null);
        await saveTask;

        Assert.Null(vm.PendingConfirmation);
    }
```

Replace `SaveProgramAsync_DecliningOverwriteConfirmation_DoesNotSave`:

```csharp
    [Fact]
    public async Task SaveProgramAsync_DecliningOverwriteConfirmation_DoesNotSave()
    {
        var vm = CreateViewModel(out var transport, out var storage);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);
        vm.ProgramName = "Тест";

        var firstSaveTask = vm.SaveProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingRename);
        vm.ConfirmRenameCommand.Execute(null);
        await firstSaveTask;
        var savedId = vm.ProgramId!.Value;

        transport.SimulateReceivedLine("<Idle|WPos:10,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);

        var saveTask = vm.SaveProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingConfirmation);
        vm.ConfirmNoCommand.Execute(null);
        await saveTask;

        var stored = await storage.LoadAsync(savedId);
        Assert.Single(stored.KeyPoints);
    }
```

Replace `SaveProgramAsync_NameCollidesWithDifferentProgram_AsksForConfirmation`:

```csharp
    [Fact]
    public async Task SaveProgramAsync_NameCollidesWithDifferentProgram_AsksForConfirmation()
    {
        var vm = CreateViewModel(out _, out var storage);
        await storage.SaveAsync(new JibProgram { Id = Guid.NewGuid(), Name = "Existing" });
        await vm.RefreshLibraryCommand.ExecuteAsync(null);
        vm.ProgramName = "Existing";

        var saveTask = vm.SaveProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingRename);
        vm.ConfirmRenameCommand.Execute(null);

        Assert.NotNull(vm.PendingConfirmation);
        vm.ConfirmYesCommand.Execute(null);
        await saveTask;

        Assert.NotNull(vm.ProgramId);
    }
```

Replace `SaveProgramAsync_DecliningNameCollisionConfirmation_DoesNotSave`:

```csharp
    [Fact]
    public async Task SaveProgramAsync_DecliningNameCollisionConfirmation_DoesNotSave()
    {
        var vm = CreateViewModel(out _, out var storage);
        await storage.SaveAsync(new JibProgram { Id = Guid.NewGuid(), Name = "Existing" });
        await vm.RefreshLibraryCommand.ExecuteAsync(null);
        vm.ProgramName = "Existing";

        var saveTask = vm.SaveProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingRename);
        vm.ConfirmRenameCommand.Execute(null);

        Assert.NotNull(vm.PendingConfirmation);
        vm.ConfirmNoCommand.Execute(null);
        await saveTask;

        Assert.Null(vm.ProgramId);
    }
```

Add a new test right after `SaveProgramAsync_DecliningNameCollisionConfirmation_DoesNotSave`:

```csharp
    [Fact]
    public async Task SaveProgramAsync_NewProgram_CancellingRenameDialog_DoesNotSave()
    {
        var vm = CreateViewModel(out _, out var storage);
        vm.ProgramName = "Тест";

        var saveTask = vm.SaveProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingRename);
        vm.CancelRenameCommand.Execute(null);
        await saveTask;

        Assert.Null(vm.ProgramId);
        Assert.Empty(await storage.ListAsync());
    }
```

- [ ] **Step 2: Run the updated tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "SaveProgramAsync"`
Expected: FAIL — the seven tests above time out or fail their `Assert.NotNull(vm.PendingRename)` assertion, because `SaveProgramAsync` doesn't yet open the rename dialog for a new program.

- [ ] **Step 3: Update `SaveProgramAsync`**

In `ArctZ/ViewModels/ProgramViewModel.cs`, the method currently reads:

```csharp
    [RelayCommand]
    private async Task SaveProgramAsync()
    {
        if (ProgramId is not null)
        {
            var confirmed = await ConfirmAsync(
                $"Сохранить поверх ранее сохранённой программы «{ProgramName}»? Текущие данные на диске будут перезаписаны.");
            if (!confirmed)
            {
                return;
            }
        }

        var hasNameCollision = Library.Any(item =>
            item.Id != ProgramId && string.Equals(item.Name.Trim(), ProgramName.Trim(), StringComparison.OrdinalIgnoreCase));

        if (hasNameCollision)
        {
            var confirmed = await ConfirmAsync(
                $"В библиотеке уже есть программа с именем «{ProgramName}». Сохранить ещё одну с таким же именем?");
            if (!confirmed)
            {
                return;
            }
        }

        var program = BuildProgram();
        await _storage.SaveAsync(program);
        ProgramId = program.Id;
        await RefreshLibraryAsync();
    }
```

Replace it with:

```csharp
    [RelayCommand]
    private async Task SaveProgramAsync()
    {
        if (ProgramId is null)
        {
            var name = await RequestNameAsync(ProgramName);
            if (name is null)
            {
                return;
            }

            ProgramName = name;
        }
        else
        {
            var confirmed = await ConfirmAsync(
                $"Сохранить поверх ранее сохранённой программы «{ProgramName}»? Текущие данные на диске будут перезаписаны.");
            if (!confirmed)
            {
                return;
            }
        }

        var hasNameCollision = Library.Any(item =>
            item.Id != ProgramId && string.Equals(item.Name.Trim(), ProgramName.Trim(), StringComparison.OrdinalIgnoreCase));

        if (hasNameCollision)
        {
            var confirmed = await ConfirmAsync(
                $"В библиотеке уже есть программа с именем «{ProgramName}». Сохранить ещё одну с таким же именем?");
            if (!confirmed)
            {
                return;
            }
        }

        var program = BuildProgram();
        await _storage.SaveAsync(program);
        ProgramId = program.Id;
        await RefreshLibraryAsync();
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "SaveProgramAsync"`
Expected: PASS for all `SaveProgramAsync*` tests.

- [ ] **Step 5: Run the full test suite to check for regressions**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/ViewModels/ProgramViewModelAuthoringTests.cs
git commit -m "feat: require rename dialog before saving a new program"
```

---

## Task 3: `MainView.axaml` — overflow menu, read-only name, rename overlay

**Files:**
- Modify: `ArctZ/Views/MainView.axaml:161-171` (name/button block) and `ArctZ/Views/MainView.axaml:237-263` (overlay stack, insert after the `IsEditingKeyPoint` overlay)

**Interfaces:**
- Consumes: `ProgramViewModel.ProgramName`, `RenameProgramCommand`, `NewProgramCommand`, `SaveProgramCommand`, `OpenLibraryCommand`, `PendingRename`, `ConfirmRenameCommand`, `CancelRenameCommand` (all from Task 1/2), plus the pre-existing `RootPanel`-cast binding pattern used by `PendingConfirmation`.
- Produces: nothing new consumed elsewhere — this is a leaf view.

- [ ] **Step 1: Replace the name/button block**

In `ArctZ/Views/MainView.axaml`, this block currently reads (inside `ScrollViewer x:Name="ProgramPanel"`):

```xml
                            <StackPanel Spacing="10" IsEnabled="{Binding !IsProgramLocked}">
                                <TextBlock Classes="section-heading" Text="ПРОГРАММА" />
                                <TextBox Text="{Binding ProgramName}" PlaceholderText="Имя программы" />
                                <WrapPanel ItemSpacing="8" LineSpacing="8">
                                    <Button Classes="primary" Content="Захватить точку" Command="{Binding CaptureKeyPointCommand}" />
                                    <Button Content="Новая" Command="{Binding NewProgramCommand}" />
                                    <Button Content="Сохранить" Command="{Binding SaveProgramCommand}" />
                                    <Button Content="Библиотека" Command="{Binding OpenLibraryCommand}" />
                                </WrapPanel>
                                <TextBlock Classes="section-heading" Text="ТОЧКИ" Margin="0,8,0,0" />
```

Replace it with:

```xml
                            <StackPanel Spacing="10" IsEnabled="{Binding !IsProgramLocked}">
                                <TextBlock Classes="section-heading" Text="ПРОГРАММА" />
                                <Grid ColumnDefinitions="*,Auto">
                                    <TextBlock Grid.Column="0" Classes="telemetry" FontSize="16" VerticalAlignment="Center"
                                               TextTrimming="CharacterEllipsis" Text="{Binding ProgramName}" />
                                    <Button Grid.Column="1" Content="⋮" Padding="8,2" VerticalAlignment="Center">
                                        <Button.Flyout>
                                            <MenuFlyout>
                                                <MenuItem Header="Переименовать" Command="{Binding RenameProgramCommand}" />
                                                <MenuItem Header="Новая" Command="{Binding NewProgramCommand}" />
                                                <MenuItem Header="Сохранить" Command="{Binding SaveProgramCommand}" />
                                                <MenuItem Header="Библиотека" Command="{Binding OpenLibraryCommand}" />
                                            </MenuFlyout>
                                        </Button.Flyout>
                                    </Button>
                                </Grid>
                                <Button Classes="primary" Content="Захватить точку" Command="{Binding CaptureKeyPointCommand}" HorizontalAlignment="Left" />
                                <TextBlock Classes="section-heading" Text="ТОЧКИ" Margin="0,8,0,0" />
```

- [ ] **Step 2: Add the rename dialog overlay**

In the same file, this overlay currently ends the `IsEditingKeyPoint` block and is immediately followed by the `PendingConfirmation` overlay:

```xml
                        </StackPanel>
                    </Border>
                </Border>

                <Border IsVisible="{Binding PendingConfirmation, Converter={x:Static ObjectConverters.IsNotNull}}" Background="#CC0A0E12">
```

Insert a new overlay `Border` between them:

```xml
                        </StackPanel>
                    </Border>
                </Border>

                <Border IsVisible="{Binding PendingRename, Converter={x:Static ObjectConverters.IsNotNull}}" Background="#CC0A0E12">
                    <Border x:DataType="vm:RenameProgramRequest" DataContext="{Binding PendingRename}"
                            Width="320" Background="{StaticResource HudPanelElevatedBrush}"
                            BorderBrush="{StaticResource HudBorderStrongBrush}" BorderThickness="1"
                            Padding="20" HorizontalAlignment="Center" VerticalAlignment="Center">
                        <StackPanel Spacing="14">
                            <TextBlock Classes="section-heading" Text="ИМЯ ПРОГРАММЫ" />
                            <TextBox Text="{Binding Name}" PlaceholderText="Имя программы" />
                            <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
                                <Button Content="Отмена" Command="{Binding ((vm:ProgramViewModel)DataContext).CancelRenameCommand, ElementName=RootPanel}" />
                                <Button Classes="primary" Content="Сохранить" Command="{Binding ((vm:ProgramViewModel)DataContext).ConfirmRenameCommand, ElementName=RootPanel}" />
                            </StackPanel>
                        </StackPanel>
                    </Border>
                </Border>

                <Border IsVisible="{Binding PendingConfirmation, Converter={x:Static ObjectConverters.IsNotNull}}" Background="#CC0A0E12">
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build ArctZ/ArctZ.csproj`
Expected: `Build succeeded.` — validates the new compiled bindings (`RenameProgramCommand`, `PendingRename`, `RenameProgramRequest.Name`, `CancelRenameCommand`/`ConfirmRenameCommand` via the `ElementName=RootPanel` cast) against `x:DataType="vm:ProgramViewModel"` and the new `x:DataType="vm:RenameProgramRequest"` nested context.

- [ ] **Step 4: Run the full test suite to check for regressions**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: all tests still pass (this task is XAML-only, but confirms nothing else broke).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Views/MainView.axaml
git commit -m "feat: move program library/save/new buttons into a program-menu overflow, add rename dialog"
```

---

## Task 4: Manual verification pass

**Files:** none (no code changes — this task only runs the app)

**Interfaces:** N/A

- [ ] **Step 1: Launch the Desktop head**

Run: `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj`

- [ ] **Step 2: Connect via the Демо endpoint**

Use the connection modal to connect with "Демо" selected. Expected: modal closes, the program panel shows "ПРОГРАММА" with a read-only name (`Новая программа`) and a "⋮" button to its right — no editable text box, no `Новая`/`Сохранить`/`Библиотека` buttons visible inline. "Захватить точку" is still its own visible button below.

- [ ] **Step 3: Open the "⋮" menu and verify its contents**

Click the "⋮" button next to the program name. Expected: a flyout menu with exactly four items, in order: `Переименовать`, `Новая`, `Сохранить`, `Библиотека`.

- [ ] **Step 4: Save a new program — rename dialog should appear first**

Capture a key point ("Захватить точку"), then click "⋮" → "Сохранить". Expected: a modal dialog titled "ИМЯ ПРОГРАММЫ" appears with a text box pre-filled `Новая программа`, and "Отмена"/"Сохранить" buttons — the program is not yet saved. Type a name (e.g. "Тестовая"), click "Сохранить". Expected: dialog closes, the program name in the panel now reads "Тестовая", and re-opening "⋮" → "Библиотека" shows it listed.

- [ ] **Step 5: Cancel the rename-on-save dialog**

Click "⋮" → "Новая" to reset, capture a point, click "⋮" → "Сохранить" to open the rename dialog again, then click "Отмена". Expected: dialog closes, nothing is saved (re-open "⋮" → "Библиотека" and confirm no new entry was added), the panel's name display is unchanged.

- [ ] **Step 6: Explicit rename via the menu**

With the "Тестовая" program loaded (or any named program), click "⋮" → "Переименовать". Expected: the same dialog opens, pre-filled with the current name. Change it and click "Сохранить" — the name updates in the panel immediately (this does not touch disk until "Сохранить" from the menu is used separately). Click "⋮" → "Переименовать" again and click "Отмена" this time — name stays as it was.

- [ ] **Step 7: Empty-name guard**

Open the rename dialog (via "Переименовать" or a new-program "Сохранить"), clear the text box completely, and click "Сохранить". Expected: the dialog stays open (nothing happens) until a non-empty name is entered.

- [ ] **Step 8: Re-run the narrow-layout check**

Resize the window below ~700px width (or check on a mobile-sized viewport if available). Expected: the program panel's responsive layout from the prior narrow-screen work is unaffected — the name+"⋮" row and the "Захватить точку" button still stack sensibly above "ТОЧКИ".

- [ ] **Step 9: Close the app**

No commit for this task (verification only). If any step fails, stop and fix the relevant task before proceeding — do not commit further work on top of a failing verification pass.
