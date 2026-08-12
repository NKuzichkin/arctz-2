using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using ArctZ.Tests.Screenshots.Support;
using ArctZ.ViewModels;

namespace ArctZ.Tests.Screenshots;

/// <summary>
/// One entry per screen. Setup puts ProgramViewModel/ConnectionViewModel
/// into that screen's state; Teardown reverts it before the next entry runs.
/// Entries run in order and are not independently reorderable: in particular
/// "main"'s Setup connects and loads the demo program, and its Teardown is
/// deliberately a no-op so that connected/loaded state stays live for every
/// later entry (e.g. "keypoint-editor", "rename", "confirm-delete") to build on.
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
    public static IReadOnlyList<ScreenDefinition> Build(FakeDeviceTransport demoTransport) => new[]
    {
        new ScreenDefinition(
            "connection",
            "Модалка подключения",
            Setup: vm => vm.Connection.RefreshEndpointsCommand.Execute().ToTask(),
            Teardown: _ => Task.CompletedTask),

        new ScreenDefinition(
            "main",
            "Главный экран (программа, точки, джойстики)",
            Setup: async vm =>
            {
                vm.Connection.SelectedEndpoint = vm.Connection.AvailableEndpoints
                    .Single(e => e.Kind == ConnectionEndpointKind.Demo);
                await vm.Connection.ConnectCommand.Execute();
                demoTransport.SimulateReceivedLine("<Idle|WPos:120.500,45.250,80.000,15.000|FS:0,0>");
                await vm.RefreshLibraryCommand.ExecuteAsync(null);
                await vm.LoadProgramCommand.ExecuteAsync(vm.Library[0]);
            },
            Teardown: _ => Task.CompletedTask),

        new ScreenDefinition(
            "alarm",
            "Модалка аварии",
            Setup: vm => { vm.Connection.LastAlarmCode = 1; return Task.CompletedTask; },
            Teardown: vm => { vm.Connection.LastAlarmCode = null; return Task.CompletedTask; }),

        new ScreenDefinition(
            "library",
            "Библиотека программ",
            Setup: vm => vm.OpenLibraryCommand.ExecuteAsync(null),
            Teardown: vm => { vm.CloseLibraryCommand.Execute(null); return Task.CompletedTask; }),

        new ScreenDefinition(
            "keypoint-editor",
            "Редактор ключевой точки",
            Setup: vm => { vm.EditKeyPointCommand.Execute(vm.KeyPoints[0]); return Task.CompletedTask; },
            Teardown: vm => { vm.KeyPointEditor = null; return Task.CompletedTask; }),

        new ScreenDefinition(
            "completion-settings",
            "Настройки завершения программы",
            Setup: vm => { vm.EditCompletionSettingsCommand.Execute(null); return Task.CompletedTask; },
            Teardown: vm => { vm.CompletionSettingsEditor = null; return Task.CompletedTask; }),

        new ScreenDefinition(
            "rename",
            "Переименование программы",
            Setup: vm => vm.RenameProgramCommand.ExecuteAsync(null),
            Teardown: vm => { vm.CancelRenameCommand.Execute(null); return Task.CompletedTask; }),

        new ScreenDefinition(
            "confirm-delete",
            "Подтверждение удаления точки",
            Setup: vm => vm.RemoveKeyPointCommand.ExecuteAsync(vm.KeyPoints[0]),
            Teardown: vm => { vm.ConfirmNoCommand.Execute(null); return Task.CompletedTask; }),

        new ScreenDefinition(
            "side-menu",
            "Боковое меню",
            Setup: vm => { vm.ToggleSideMenuCommand.Execute(null); return Task.CompletedTask; },
            Teardown: vm => { vm.CloseSideMenuCommand.Execute(null); return Task.CompletedTask; }),

        new ScreenDefinition(
            "gcode-log",
            "Лог G-code",
            Setup: vm =>
            {
                vm.Connection.SentGCodeLines.Add("$H");
                vm.Connection.SentGCodeLines.Add("G1 X120.500 Y45.250 Z80.000 A15.000 F500");
                vm.Connection.SentGCodeLines.Add("ok");
                return vm.Connection.ToggleGCodeLogCommand.Execute().ToTask();
            },
            Teardown: vm => vm.Connection.ToggleGCodeLogCommand.Execute().ToTask()),

        new ScreenDefinition(
            "mock-settings",
            "Настройки мока",
            Setup: vm => vm.Connection.ToggleMockSettingsCommand.Execute().ToTask(),
            Teardown: vm => vm.Connection.ToggleMockSettingsCommand.Execute().ToTask()),
    };
}
