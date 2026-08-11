using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ArctZ.ViewModels;

namespace ArctZ.Tests.Screenshots;

/// <summary>
/// One entry per screen. Setup puts ProgramViewModel/ConnectionViewModel
/// into that screen's state; Teardown reverts it before the next entry runs.
/// Both always return a Task (Task.CompletedTask for synchronous work) so the
/// driver loop in ScreenshotGalleryTests can treat every entry uniformly,
/// including the ones (rename/confirm-delete) whose Setup deliberately stays
/// pending — on a TaskCompletionSource — until Teardown answers the dialog.
/// This same list also drives the generated screenshots/SCREENS.md, so it's
/// the single source of truth for what "all the screens" means.
/// </summary>
public sealed record ScreenDefinition(
    string Id,
    string Title,
    Func<ProgramViewModel, Task> Setup,
    Func<ProgramViewModel, Task> Teardown);

public static class ScreenCatalog
{
    public static IReadOnlyList<ScreenDefinition> Build() => new[]
    {
        new ScreenDefinition(
            "connection",
            "Модалка подключения",
            Setup: _ => Task.CompletedTask,
            Teardown: _ => Task.CompletedTask),
    };
}
