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
/// The three playback entries at the tail go further and share ONE running
/// PlayCommand between them (see <see cref="ScreenCatalogContext.PlaybackTask"/>):
/// "playback" starts it, "playback-warning" only advances the clock inside it, and
/// "keypoint-messages" reads the warning it produced — a warning message only ever
/// exists as a by-product of a real over-budget transition. "about" then follows
/// them because its "Скопировать лог программы" button is bound to
/// AboutViewModel.HasExecutionLog, which is only true once a run has happened.
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

/// <summary>
/// The fakes a screen's Setup/Teardown may need to drive: the demo transport (to feed status
/// reports and acks), the hand-driven progress timer and the hand-driven clock (to make elapsed
/// playback time deterministic instead of dependent on how long capture happened to take).
/// </summary>
public sealed record ScreenCatalogContext(
    FakeDeviceTransport DemoTransport,
    ManualPeriodicTimer ProgressTimer,
    MutableClock Clock)
{
    /// <summary>
    /// The PlayCommand invocation started by the "playback" screen and left running across the
    /// two screens after it. Deliberately not awaited inside a Teardown: it only completes once
    /// the stopped program's in-flight ack arrives, and awaiting a not-yet-completed task from a
    /// Teardown would need the dispatcher pumped from inside the driver's own await. The driver
    /// drains it once, with a timeout, after the whole loop instead.
    /// </summary>
    public Task? PlaybackTask { get; set; }
}

public static class ScreenCatalog
{
    // The demo program's two key points, as seeded by ScreenshotGalleryTests: point 1 at the
    // origin, point 2 at (120, 45, 80, 15), five seconds of transition time each and a one-second
    // dwell on point 2. TimeProgressTracker decides which key point is physically active by
    // projecting the reported position onto the pass's legs, so the poses below have to sit
    // unambiguously on one leg. That also rules out parking the machine at its usual
    // (120.5, 45.25, 80, 15) before pressing Play: the leg from there to point 1 and the leg from
    // point 1 to point 2 would be the same line walked in opposite directions, and every position
    // on one would be ~0.2 mm from the other — the projection then picks by rounding noise.
    private const string PoseParkedBeforePlay = "60.000,0.000,0.000,0.000";
    private const string PosePartwayToFirstPoint = "30.000,0.000,0.000,0.000";
    private const string PosePartwayToSecondPoint = "48.000,18.000,32.000,6.000";

    public static IReadOnlyList<ScreenDefinition> Build(ScreenCatalogContext context)
    {
        var demoTransport = context.DemoTransport;

        return new[]
        {
            new ScreenDefinition(
                "connection",
                "Модалка подключения",
                Setup: vm => vm.Connection.RefreshEndpointsCommand.Execute().ToTask(),
                Teardown: _ => Task.CompletedTask),

            new ScreenDefinition(
                "auto-connect-splash",
                "Восстановление связи (автоподключение)",
                // Not a startup screen: AutoConnectAsync is only ever kicked off by a live session
                // dropping (see ConnectionViewModel's ConnectionStateChanged subscription), so this
                // is what an operator sees after the link breaks, not on launch.
                // Hides the connection modal on its own: IsConnectionModalVisible is defined as
                // "not the splash and not connected", so no extra teardown of that is needed.
                Setup: vm => { vm.Connection.AutoConnectPhase = AutoConnectPhase.Searching; return Task.CompletedTask; },
                Teardown: vm => { vm.Connection.AutoConnectPhase = AutoConnectPhase.Idle; return Task.CompletedTask; }),

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
                    // Real emitted forms (FluidNcCommandSerializer / InverseTimeMove /
                    // TrajectoryCompiler), not paraphrases: the log is one-directional and
                    // carries no acks, so seeding an "ok" here would misrepresent it.
                    vm.Connection.SentGCodeLines.Add("$J=G91 G21 X1.5 Y0 Z0 A0 F1000");
                    vm.Connection.SentGCodeLines.Add("G93 G1 X120.5 Y45.25 Z80 A15 F12");
                    vm.Connection.SentGCodeLines.Add("G4 P1");
                    return vm.Connection.ToggleGCodeLogCommand.Execute().ToTask();
                },
                Teardown: vm => vm.Connection.ToggleGCodeLogCommand.Execute().ToTask()),

            new ScreenDefinition(
                "mock-settings",
                "Настройки мока",
                Setup: vm => vm.Connection.ToggleMockSettingsCommand.Execute().ToTask(),
                Teardown: vm => vm.Connection.ToggleMockSettingsCommand.Execute().ToTask()),

            new ScreenDefinition(
                "playback",
                "Выполнение программы (прогресс)",
                Setup: vm =>
                {
                    // Guards against an earlier screen having dirtied the program: PlayAsync would
                    // then open the "save before starting?" confirm dialog, whose TaskCompletionSource
                    // nothing here answers, and the whole run would hang instead of failing.
                    vm.ProgramId ??= Guid.NewGuid();
                    vm.IsDirty = false;

                    demoTransport.SimulateReceivedLine($"<Idle|WPos:{PoseParkedBeforePlay}|FS:0,0>");
                    context.PlaybackTask = vm.PlayCommand.ExecuteAsync(null);
                    demoTransport.SimulateReceivedLine("ok"); // the move to point 1 reached the controller

                    // Three of point 1's five transition seconds spent, machine halfway there:
                    // 27% on the overall bar, a ring with 40% of its circle left on point 1's tile.
                    context.Clock.Advance(TimeSpan.FromSeconds(3));
                    demoTransport.SimulateReceivedLine($"<Run|WPos:{PosePartwayToFirstPoint}|FS:500,0>");
                    context.ProgressTimer.RaiseElapsed();
                    return Task.CompletedTask;
                },
                Teardown: _ => Task.CompletedTask),

            new ScreenDefinition(
                "playback-warning",
                "Перерасход времени на точке",
                // Same run, three seconds later: six seconds spent on a five-second transition is
                // past the tracker's 15% threshold, so the tile swaps its ring for the ⚠ badge.
                // (The two never show together by design — the ring is empty from the moment the
                // step's own budget runs out, which is strictly before the warning threshold.)
                Setup: vm =>
                {
                    context.Clock.Advance(TimeSpan.FromSeconds(3));
                    context.ProgressTimer.RaiseElapsed();
                    return Task.CompletedTask;
                },
                Teardown: _ => Task.CompletedTask),

            new ScreenDefinition(
                "keypoint-messages",
                "Сообщения ключевой точки",
                Setup: vm =>
                {
                    // The overage message is emitted when the machine physically LEAVES the
                    // over-budget segment, so the run has to reach the leg toward point 2 first.
                    demoTransport.SimulateReceivedLine($"<Run|WPos:{PosePartwayToSecondPoint}|FS:500,0>");
                    vm.ShowKeyPointMessagesCommand.Execute(vm.KeyPoints[0]);
                    return Task.CompletedTask;
                },
                Teardown: async vm =>
                {
                    vm.CloseKeyPointMessagesCommand.Execute(null);
                    await vm.StopCommand.ExecuteAsync(null);
                    // Resolves whatever the stopped run still had in flight so PlaybackTask can finish.
                    demoTransport.SimulateReceivedLine("ok");
                    demoTransport.SimulateReceivedLine("ok");
                    demoTransport.SimulateReceivedLine("ok");
                    // Without this the machine is still reporting Run, and the header of every
                    // later screen keeps claiming "Выполнение" for a program that has stopped.
                    demoTransport.SimulateReceivedLine($"<Idle|WPos:{PosePartwayToSecondPoint}|FS:0,0>");
                }),

            new ScreenDefinition(
                "about",
                "О программе (диагностика)",
                Setup: vm =>
                {
                    // Seeded so the report's log sections show real entries rather than "(пусто)":
                    // the demo transport's own traffic is filtered out as status-poll noise.
                    demoTransport.SimulateReceivedLine("Grbl 3.7 [FluidNC v3.7.0 (wifi) '$' for help]");
                    demoTransport.SimulateReceivedLine("error:9");
                    vm.OpenAboutCommand.Execute(null);
                    return Task.CompletedTask;
                },
                Teardown: vm => { vm.CloseAboutCommand.Execute(null); return Task.CompletedTask; }),
        };
    }
}
