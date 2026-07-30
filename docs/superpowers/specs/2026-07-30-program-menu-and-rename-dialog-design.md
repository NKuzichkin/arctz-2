# Program Menu and Rename Dialog Design

**Goal:** Move the library/new/save buttons out of the always-visible program panel and into a "⋮" overflow menu next to the program name. Make the program name read-only in the panel; renaming happens only through a dedicated dialog, which is also the required first step when saving a brand-new (never-saved) program.

## Context

`ArctZ/Views/MainView.axaml` currently renders the program name as an editable `TextBox` (`{Binding ProgramName}`) directly above a row of four buttons: `Захватить точку`, `Новая`, `Сохранить`, `Библиотека`. `ProgramViewModel` (`ArctZ/ViewModels/ProgramViewModel.cs`) already has `NewProgramCommand`, `SaveProgramCommand`, `OpenLibraryCommand`, and an existing `ConfirmationRequest`/`PendingConfirmation` overlay pattern used for save-collision and overwrite confirmations (`ConfirmAsync`, `ConfirmYesCommand`, `ConfirmNoCommand`).

## Layout Change

Replace the name `TextBox` + button row with:

- A `TextBlock` (read-only) bound to `ProgramName`, with `TextTrimming="CharacterEllipsis"` so long names don't overflow.
- A "⋮" `Button` (same visual treatment as the existing per-key-point "⋮" button) to its right, opening a `MenuFlyout` with four `MenuItem`s in this order: **Переименовать**, **Новая**, **Сохранить**, **Библиотека**, bound to `RenameProgramCommand` (new), `NewProgramCommand`, `SaveProgramCommand`, `OpenLibraryCommand` respectively.

`Захватить точку` stays as its own visible `Button` below this row — it is unrelated to library/save/load and was explicitly excluded from the menu.

## Rename Dialog

New lightweight request object `RenameProgramRequest : ObservableObject` (mirrors `ConfirmationRequest` but carries an editable name instead of a yes/no):

```csharp
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

`ProgramViewModel` additions:

- `PendingRename` (`ObservableProperty<RenameProgramRequest?>`).
- `RequestNameAsync(string initialName)`: creates the request, sets `PendingRename`, returns `completion.Task` — mirrors the existing `ConfirmAsync` helper.
- `ConfirmRenameCommand`: reads `PendingRename.Name.Trim()`; if empty, does nothing (dialog stays open — no validation message needed for this scope); otherwise resolves the completion with the trimmed name and clears `PendingRename`.
- `CancelRenameCommand`: resolves the completion with `null` and clears `PendingRename`.
- `RenameProgramCommand` (new `[RelayCommand]`, bound to the "Переименовать" menu item): `await RequestNameAsync(ProgramName)`; if the result is non-null, assign it to `ProgramName`.

XAML: a new overlay `Border` in the same stacked-overlay `Grid` (`RootPanel`) as `KeyPointEditor`/`PendingConfirmation`/library, visible when `PendingRename` is not null, `x:DataType="vm:RenameProgramRequest"` with `DataContext="{Binding PendingRename}"`. Contents: a `TextBlock` heading, a `TextBox` bound to `Name`, and "Отмена"/"Сохранить" buttons routed back to `ProgramViewModel` via the existing `ElementName=RootPanel` cast pattern (same trick used by the confirmation overlay's Да/Нет buttons).

## Save Flow Change

`SaveProgramAsync` currently starts with an overwrite-confirmation step when `ProgramId is not null`. New logic:

```csharp
if (ProgramId is null)
{
    var name = await RequestNameAsync(ProgramName);
    if (name is null)
    {
        return; // cancelled
    }
    ProgramName = name;
}
else
{
    var confirmed = await ConfirmAsync(...); // unchanged
    if (!confirmed) return;
}

// unchanged: name-collision check, BuildProgram, storage.SaveAsync, ProgramId assignment, RefreshLibraryAsync
```

For a new program, the rename dialog is mandatory and precedes the existing name-collision check (which still runs after, using whatever name the user confirmed). For an already-saved program (`ProgramId != null`), behavior is unchanged — no rename dialog, straight to the overwrite confirmation.

## Test Impact

`ArctZ.Tests/ViewModels/ProgramViewModelAuthoringTests.cs` has six tests that call `SaveProgramCommand.ExecuteAsync` on a new (never-saved) program and await it directly:

- `SaveProgramAsync_ThenRefreshLibrary_ListsSavedProgram`
- `SaveProgramAsync_ThenRefreshLibrary_MarksSavedProgramAsLoaded`
- `SaveProgramAsync_OverwritingLoadedProgram_AsksForConfirmation` (its first save call)
- `SaveProgramAsync_DecliningOverwriteConfirmation_DoesNotSave` (its first save call)
- `SaveProgramAsync_NameCollidesWithDifferentProgram_AsksForConfirmation`
- `SaveProgramAsync_DecliningNameCollisionConfirmation_DoesNotSave`

Each of these must be updated to drive the new rename dialog before the save can proceed, e.g.:

```csharp
vm.ProgramName = "Тест";
var saveTask = vm.SaveProgramCommand.ExecuteAsync(null);
Assert.NotNull(vm.PendingRename);
vm.ConfirmRenameCommand.Execute(null);
await saveTask;
```

New tests to add:

- `RenameProgramAsync_Confirmed_UpdatesProgramName`
- `RenameProgramAsync_Cancelled_LeavesProgramNameUnchanged`
- `SaveProgramAsync_NewProgram_CancellingRenameDialog_DoesNotSave`

## Out of Scope

- No validation UI/message for empty names beyond silently blocking confirmation.
- No changes to `NewProgramCommand`, `OpenLibraryCommand`, `CaptureKeyPointCommand`, or the library overlay itself.
- No changes to `KeyPointEditorViewModel`/`ConfirmationRequest` — `RenameProgramRequest` is a new, separate type.
