# Program library: persistent "loaded" highlight + save-overwrite warnings

Date: 2026-07-28

## Problem

`ProgramViewModel`'s library list (`Library: ObservableCollection<ProgramSummary>`) has no lasting visual indication of which program is currently loaded, and `SaveProgramAsync` silently overwrites files with no confirmation.

Two concrete bugs drive this:

1. The library `ListBox` in `MainView.axaml` only wires `SelectionChanged` (no bound `SelectedItem`), and `RefreshLibraryAsync` rebuilds `Library` from scratch after every save. `ProgramSummary` is a record whose equality includes `ModifiedAt`, which changes on every save — so even if selection were bound, it would not survive a save.
2. `SaveProgramAsync` (`ArctZ/ViewModels/ProgramViewModel.cs`) calls `_storage.SaveAsync(program)` unconditionally, silently overwriting the file for `ProgramId` if one already exists, with no warning about name collisions with other saved programs either.

The app runs on Desktop (classic `Window`) and Android/iOS/Browser (`ISingleViewApplicationLifetime`/`IActivityApplicationLifetime`, no OS windows) via the same shared `MainView` — see `ArctZ/App.axaml.cs`. Any dialog mechanism must work identically across all four heads.

## Design

### 1. Persistent "loaded" highlight

Add `ArctZ/ViewModels/ProgramLibraryItem.cs`:

```csharp
public partial class ProgramLibraryItem : ObservableObject
{
    public Guid Id { get; }
    public string Name { get; }
    public DateTimeOffset ModifiedAt { get; }

    [ObservableProperty]
    private bool _isLoaded;

    public ProgramLibraryItem(ProgramSummary summary, bool isLoaded)
    {
        Id = summary.Id;
        Name = summary.Name;
        ModifiedAt = summary.ModifiedAt;
        _isLoaded = isLoaded;
    }
}
```

`ProgramViewModel` changes:

- `Library` becomes `ObservableCollection<ProgramLibraryItem>`.
- `RefreshLibraryAsync` builds items via `new ProgramLibraryItem(summary, summary.Id == ProgramId)`.
- Add `partial void OnProgramIdChanged(Guid? value)` (MVVM Toolkit codegen hook) that loops `Library` and sets `item.IsLoaded = item.Id == value` for each entry — no re-fetch from disk. This single hook covers `NewProgram` (clears all flags), `LoadProgramAsync`, and `SaveProgramAsync` (all of which set `ProgramId`).
- `LoadProgramCommand` parameter type changes from `ProgramSummary` to `ProgramLibraryItem` (uses `.Id` to call `_storage.LoadAsync`).
- `MainView.axaml.cs`'s `OnLibrarySelectionChanged` pattern-matches `ProgramLibraryItem` instead of `ProgramSummary`.

`MainView.axaml`: the library `DataTemplate`'s `x:DataType` changes to `vm:ProgramLibraryItem`. Wrap its content in a `Border` with `Classes.loaded-entry="{Binding IsLoaded}"`; add a `.loaded-entry` style to `UserControl.Styles` (accent left border + tinted background, using the existing `HudPanelBrush`/`HudBorderBrush`/accent resources so it matches the HUD theme).

### 2. In-view confirmation overlay

No `Window`, no new DI-registered service — a small piece of `ProgramViewModel` state drives an overlay control that already lives in the shared `MainView`.

Add to `ProgramViewModel`:

```csharp
public sealed class PendingConfirmation
{
    public required string Message { get; init; }
    internal required TaskCompletionSource<bool> Completion { get; init; }
}

[ObservableProperty]
private PendingConfirmation? _pendingConfirmation;

private Task<bool> ConfirmAsync(string message)
{
    var tcs = new TaskCompletionSource<bool>();
    PendingConfirmation = new PendingConfirmation { Message = message, Completion = tcs };
    return tcs.Task;
}

[RelayCommand]
private void ConfirmYes()
{
    PendingConfirmation?.Completion.TrySetResult(true);
    PendingConfirmation = null;
}

[RelayCommand]
private void ConfirmNo()
{
    PendingConfirmation?.Completion.TrySetResult(false);
    PendingConfirmation = null;
}
```

`MainView.axaml`: wrap the existing root `DockPanel` in an outer `Panel`. Add a scrim `Border` (semi-transparent, fills the panel) plus a centered dialog `Border` on top, both with `IsVisible="{Binding PendingConfirmation, Converter={x:Static ObjectConverters.IsNotNull}}"`. The dialog contains a `TextBlock` bound to `PendingConfirmation.Message` and two buttons bound to `ConfirmYesCommand` ("Да") / `ConfirmNoCommand` ("Нет"). Styled with the existing HUD panel/border brushes.

This is generic enough to reuse for any future yes/no confirmation in this ViewModel — just call `ConfirmAsync(message)` from any command.

### 3. Save flow validation

`SaveProgramAsync` runs two sequential checks before calling `_storage.SaveAsync`. Either check answering "no" aborts the save with no side effects (no `_storage.SaveAsync` call, no `ProgramId`/library changes):

1. **Self-overwrite** — if `ProgramId is not null` (true whenever a file already exists for this program, whether loaded from disk or saved earlier this session):
   > «Сохранить поверх ранее сохранённой программы «{ProgramName}»? Текущие данные на диске будут перезаписаны.»
2. **Name collision** — if `Library` contains an entry with the same `Name` (trimmed, ordinal case-insensitive) but a different `Id` than `ProgramId`:
   > «В библиотеке уже есть программа с именем «{ProgramName}». Сохранить ещё одну с таким же именем?»

Both checks can fire in the same save (sequentially) if both conditions hold; either alone can fire independently (e.g. a brand-new program named the same as an existing one triggers only check 2; editing a loaded program without renaming triggers only check 1).

### 4. Testing

No fakes needed for the confirmation mechanism — it's plain ViewModel state. Tests:

- Start `SaveProgramCommand.ExecuteAsync()` without awaiting to completion (or inspect state right after triggering it), assert `PendingConfirmation.Message` content, then invoke `ConfirmYesCommand`/`ConfirmNoCommand` to unblock it, and assert the resulting `_storage` state / `ProgramId` / `Library[].IsLoaded`.
- Cover `IsLoaded` transitions across `NewProgram`, `LoadProgramAsync`, `SaveProgramAsync`, and `RefreshLibraryAsync`.
- Cover: saving a never-before-saved program with a colliding name (only check 2 fires); saving a loaded program without renaming (only check 1 fires); saying "no" to either check leaves storage/state untouched.

## Out of scope

- Warning on `NewProgram`/switching library selection when there are unsaved edits (not requested; separate concern).
- Any rename-in-dialog affordance — canceling a confirmation just aborts the save; the user edits the name manually and saves again.
