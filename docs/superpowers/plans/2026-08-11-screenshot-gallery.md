# Screenshot Gallery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A new isolated xUnit project (`ArctZ.Tests.Screenshots`) that boots the real `ArctZ.App` headless with the existing `--theme=print` palette, drives `ProgramViewModel`/`ConnectionViewModel` directly (no UI clicks) through 11 named screens, and saves one PNG per screen plus a generated `screenshots/SCREENS.md` index to the repo root.

**Architecture:** `ArctZ.App` can only be `AppBuilder.Configure<T>().Setup()`'d once per process, and `ArctZ.Tests` already commits its process to a stripped-down `TestApp`. A separate test project gets its own process, so it can configure the real `App` (full `FluentTheme`/`MaterialIconStyles`/`DataTypeViewLocator`/print palette) with zero risk to the 9 existing headless test classes. A single `Window` hosts `MainView`; a data-driven catalog of `(id, title, Setup, Teardown)` entries — the single source of truth for both the screenshot loop and the generated Markdown — puts the view into each screen's state via direct `ProgramViewModel`/`ConnectionViewModel` calls and `Window.CaptureRenderedFrame()` grabs the pixels.

**Tech Stack:** .NET 10, Avalonia 12.0.4 + Avalonia.Headless 12.0.4, xUnit 2.9.2, ReactiveUI 23.2.28, CommunityToolkit.Mvvm 8.4.0.

## Global Constraints

- Design doc: `docs/superpowers/specs/2026-08-11-screenshot-gallery-design.md` — read it if anything below is ambiguous.
- Screenshot theme = the existing `--theme=print` (`App.PrintMode = true` before `AppBuilder...Setup`), not a new theme.
- Frame size: 390×844 (mobile), fixed — no other sizes.
- Output: `screenshots/SCREENS.md` (generated) + `screenshots/NN-<id>.png` for all 11 screens, in the repo root (found by walking up from `AppContext.BaseDirectory` to the directory containing `ArctZ.slnx`).
- New project is **not** wired into the `dotnet test ArctZ.Tests/ArctZ.Tests.csproj` command documented in `CLAUDE.md` — it's run on its own via `dotnet test ArctZ.Tests.Screenshots/ArctZ.Tests.Screenshots.csproj`.
- No pixel-diffing/regression checks — the test only (re)generates screenshots on every run.
- Package versions come from the central `Directory.Packages.props` — do not add per-project `Version=` attributes.

---

### Task 1: Project scaffold + real-App headless capture pipeline (1 screen)

Proves the hard part end-to-end — the real `ArctZ.App`, print theme, headless rendering, and PNG capture — on the simplest possible screen (`connection`, the state the app is in before anything else happens, so it needs no demo data).

**Files:**
- Create: `ArctZ.Tests.Screenshots/ArctZ.Tests.Screenshots.csproj`
- Modify: `ArctZ.slnx`
- Create: `ArctZ.Tests.Screenshots/GlobalUsings.cs`
- Create: `ArctZ.Tests.Screenshots/ReactiveUIBootstrap.cs`
- Create: `ArctZ.Tests.Screenshots/HeadlessAppBootstrap.cs`
- Create: `ArctZ.Tests.Screenshots/Support/FakeDeviceTransport.cs`
- Create: `ArctZ.Tests.Screenshots/Support/FakeProgramStorage.cs`
- Create: `ArctZ.Tests.Screenshots/Support/VisualTreeAnimationStripper.cs`
- Create: `ArctZ.Tests.Screenshots/Support/RepoRoot.cs`
- Create: `ArctZ.Tests.Screenshots/ScreenCatalog.cs`
- Create: `ArctZ.Tests.Screenshots/ScreenshotGalleryTests.cs`

**Interfaces:**
- Consumes: `ArctZ.App` (public static `PrintMode` bool, public parameterless ctor), `ArctZ.Views.MainView` (public, `DataContext` settable), `ArctZ.ViewModels.ConnectionViewModel(IDeviceTransport realTransport, Func<IDeviceTransport> createDemoTransport, IDeviceSessionFactory sessionFactory)`, `ArctZ.ViewModels.ProgramViewModel(ConnectionViewModel connection, IProgramStorage storage, ITrajectoryCompiler compiler)`, `ArctZ.Services.Device.DeviceSessionFactory(MachineLimits limits)`, `ArctZ.Services.Device.MachineLimits.Default`, `ArctZ.Services.Program.TrajectoryCompiler()`, `ArctZ.Services.Device.IDeviceTransport`, `ArctZ.Services.Program.IProgramStorage`.
- Produces: `ArctZ.Tests.Screenshots.ScreenDefinition` record `(string Id, string Title, Func<ProgramViewModel, Task> Setup, Func<ProgramViewModel, Task> Teardown)`; `ArctZ.Tests.Screenshots.ScreenCatalog.Build()` returning `IReadOnlyList<ScreenDefinition>`; `ArctZ.Tests.Screenshots.Support.RepoRoot.Find()` returning `string`; `ArctZ.Tests.Screenshots.Support.VisualTreeAnimationStripper.StripRevealAnimations(Control root)`; `ArctZ.Tests.Screenshots.HeadlessAppBootstrap.EnsureInitialized()`; `ArctZ.Tests.Screenshots.Support.FakeDeviceTransport` (implements `IDeviceTransport`, has `SimulateReceivedLine(string)`); `ArctZ.Tests.Screenshots.Support.FakeProgramStorage` (implements `IProgramStorage`). Task 2 extends `ScreenCatalog.Build` to take a `FakeDeviceTransport demoTransport` parameter and grows the returned list from 1 to 11 entries — later code should not assume the list stays at 1 entry.

- [ ] **Step 1: Create the project file**

`ArctZ.Tests.Screenshots/ArctZ.Tests.Screenshots.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Avalonia.Headless" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ArctZ\ArctZ.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Register the project in the solution**

In `ArctZ.slnx`, add a new `<Project Path="..." />` line right after the `ArctZ.Tests` entry:

```xml
    <Project Path="ArctZ.Tests/ArctZ.Tests.csproj" />
    <Project Path="ArctZ.Tests.Screenshots/ArctZ.Tests.Screenshots.csproj" />
```

- [ ] **Step 3: Verify the empty project builds**

Run: `dotnet build ArctZ.Tests.Screenshots/ArctZ.Tests.Screenshots.csproj`
Expected: build succeeds (no `.cs` files yet, so nothing to compile besides implicit references — this only validates the csproj/slnx wiring and package restore).

- [ ] **Step 4: Add GlobalUsings.cs**

`ArctZ.Tests.Screenshots/GlobalUsings.cs`:

```csharp
global using Xunit;
global using System.Reactive.Linq;
```

- [ ] **Step 5: Add the ReactiveUI bootstrap**

`ArctZ.Tests.Screenshots/ReactiveUIBootstrap.cs` (copy of `ArctZ.Tests/ReactiveUIBootstrap.cs`, same reasoning: `ConnectionViewModel` derives from `ReactiveViewModelBase` and throws without ReactiveUI initialized, and forcing `ImmediateScheduler` keeps every `WhenAnyValue`/`ObserveOn` chain synchronous so the test never has to pump a real dispatcher loop to observe a completion):

```csharp
using System.Reactive.Concurrency;
using System.Runtime.CompilerServices;
using ReactiveUI;
using ReactiveUI.Builder;

namespace ArctZ.Tests.Screenshots;

internal static class ReactiveUIBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();

        RxSchedulers.MainThreadScheduler = ImmediateScheduler.Instance;
    }
}
```

- [ ] **Step 6: Add the fake device transport**

`ArctZ.Tests.Screenshots/Support/FakeDeviceTransport.cs` (copy of `ArctZ.Tests/Services/Device/FakeDeviceTransport.cs`, namespace changed — duplicated instead of referenced so this project doesn't depend on `ArctZ.Tests`, which would pull its whole xunit suite in alongside it):

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Screenshots.Support;

public sealed class FakeDeviceTransport : IDeviceTransport
{
    public List<string> SentLines { get; } = new();
    public List<byte> SentRawBytes { get; } = new();
    public bool IsConnected { get; private set; }

    public int ConnectFailuresRemaining { get; set; }

    public event Action<string>? LineReceived;
    public event Action? Disconnected;

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (ConnectFailuresRemaining > 0)
        {
            ConnectFailuresRemaining--;
            throw new InvalidOperationException("Simulated connect failure");
        }

        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        SentLines.Add(line);
        return Task.CompletedTask;
    }

    public Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default)
    {
        SentRawBytes.Add(value);
        return Task.CompletedTask;
    }

    public void SimulateReceivedLine(string line) => LineReceived?.Invoke(line);

    public void SimulateDisconnect()
    {
        IsConnected = false;
        Disconnected?.Invoke();
    }
}
```

- [ ] **Step 7: Add the fake program storage**

`ArctZ.Tests.Screenshots/Support/FakeProgramStorage.cs` (copy of `ArctZ.Tests/Services/Program/FakeProgramStorage.cs`, namespace changed):

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Program;

namespace ArctZ.Tests.Screenshots.Support;

public sealed class FakeProgramStorage : IProgramStorage
{
    private readonly Dictionary<Guid, JibProgram> _programs = new();
    private readonly Dictionary<Guid, DateTimeOffset> _createdAt = new();
    private readonly Dictionary<Guid, DateTimeOffset> _modifiedAt = new();

    public Task<IReadOnlyList<ProgramSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProgramSummary>>(
            _programs.Values.Select(p => new ProgramSummary(p.Id, p.Name, _createdAt[p.Id], _modifiedAt[p.Id])).ToList());

    public Task<JibProgram> LoadAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_programs[id]);

    public Task SaveAsync(JibProgram program, CancellationToken cancellationToken = default)
    {
        _programs[program.Id] = program;
        var now = DateTimeOffset.UtcNow;
        if (!_createdAt.ContainsKey(program.Id))
        {
            _createdAt[program.Id] = now;
        }

        _modifiedAt[program.Id] = now;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _programs.Remove(id);
        _createdAt.Remove(id);
        _modifiedAt.Remove(id);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 8: Add the reveal-animation stripper**

`ArctZ.Tests.Screenshots/Support/VisualTreeAnimationStripper.cs`:

```csharp
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace ArctZ.Tests.Screenshots.Support;

/// <summary>
/// MainView.axaml fades in its header/content panels via "reveal-1"/"reveal-3"
/// classes (Opacity 0→1 over real time, FillMode=Forward). Headless capture
/// doesn't advance that animation clock, so a frame taken without this would
/// risk landing on Opacity 0. Removing the classes before the first render
/// stops the animation selectors from ever matching, so the panels render at
/// their default (fully opaque) state deterministically.
/// </summary>
public static class VisualTreeAnimationStripper
{
    private static readonly string[] RevealClassNames = { "reveal-1", "reveal-2", "reveal-3" };

    public static void StripRevealAnimations(Control root)
    {
        RemoveRevealClasses(root);
        foreach (var descendant in root.GetVisualDescendants().OfType<StyledElement>())
        {
            RemoveRevealClasses(descendant);
        }
    }

    private static void RemoveRevealClasses(StyledElement element)
    {
        foreach (var name in RevealClassNames)
        {
            element.Classes.Remove(name);
        }
    }
}
```

- [ ] **Step 9: Add the repo-root resolver**

`ArctZ.Tests.Screenshots/Support/RepoRoot.cs`:

```csharp
using System;
using System.IO;

namespace ArctZ.Tests.Screenshots.Support;

public static class RepoRoot
{
    public static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ArctZ.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"Could not locate repo root (ArctZ.slnx) above '{AppContext.BaseDirectory}'.");
        }

        return dir.FullName;
    }
}
```

- [ ] **Step 10: Add the headless App bootstrap**

`ArctZ.Tests.Screenshots/HeadlessAppBootstrap.cs`:

```csharp
using System;
using System.Threading;
using Avalonia;
using Avalonia.Headless;

namespace ArctZ.Tests.Screenshots;

/// <summary>
/// Boots the real ArctZ.App (not a stand-in) headless, with App.PrintMode
/// set beforehand so App.Initialize() applies the existing --theme=print
/// palette exactly as ArctZ.Desktop.exe --theme=print would. AppBuilder.Setup
/// can only run once per process — this project exists specifically so that
/// "once" can be spent on the real App instead of ArctZ.Tests' stripped-down
/// TestApp — so this is guarded the same way AvaloniaHeadlessBootstrap
/// guards ArctZ.Tests' single Setup call.
/// </summary>
public static class HeadlessAppBootstrap
{
    private static readonly Lazy<bool> Init = new(() =>
    {
        App.PrintMode = true;
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        return true;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void EnsureInitialized() => _ = Init.Value;
}
```

- [ ] **Step 11: Add the screen catalog (one entry)**

`ArctZ.Tests.Screenshots/ScreenCatalog.cs`:

```csharp
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
```

- [ ] **Step 12: Write the capture-loop test**

`ArctZ.Tests.Screenshots/ScreenshotGalleryTests.cs`:

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Screenshots.Support;
using ArctZ.ViewModels;
using ArctZ.Views;

namespace ArctZ.Tests.Screenshots;

public class ScreenshotGalleryTests
{
    public ScreenshotGalleryTests() => HeadlessAppBootstrap.EnsureInitialized();

    [Fact]
    public async Task GeneratesScreenshotsForAllScreens()
    {
        var screenshotsDir = Path.Combine(RepoRoot.Find(), "screenshots");
        Directory.CreateDirectory(screenshotsDir);

        var realTransport = new FakeDeviceTransport();
        var demoTransport = new FakeDeviceTransport();
        var storage = new FakeProgramStorage();
        var connection = new ConnectionViewModel(
            realTransport,
            () => demoTransport,
            new DeviceSessionFactory(MachineLimits.Default));
        var programViewModel = new ProgramViewModel(connection, storage, new TrajectoryCompiler());

        var mainView = new MainView { DataContext = programViewModel };
        VisualTreeAnimationStripper.StripRevealAnimations(mainView);

        var window = new Window { Width = 390, Height = 844, Content = mainView };
        window.Classes.Add("print");
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var screens = ScreenCatalog.Build();
        for (var i = 0; i < screens.Count; i++)
        {
            var screen = screens[i];
            var setupTask = screen.Setup(programViewModel);
            Dispatcher.UIThread.RunJobs();

            var frame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException($"No frame captured for screen '{screen.Id}'.");
            frame.Save(Path.Combine(screenshotsDir, $"{i + 1:D2}-{screen.Id}.png"));

            var teardownTask = screen.Teardown(programViewModel);
            await setupTask;
            await teardownTask;
            Dispatcher.UIThread.RunJobs();
        }

        window.Close();

        Assert.True(File.Exists(Path.Combine(screenshotsDir, "01-connection.png")));
    }
}
```

- [ ] **Step 13: Run the test**

Run: `dotnet test ArctZ.Tests.Screenshots/ArctZ.Tests.Screenshots.csproj`
Expected: PASS (1 test). `screenshots/01-connection.png` now exists at the repo root and is a non-trivial PNG (open it — it should show the "ПОДКЛЮЧЕНИЕ" modal in the print palette: black text/borders on white, over a scrim).

- [ ] **Step 14: Commit**

```bash
git add ArctZ.Tests.Screenshots ArctZ.slnx
git commit -m "$(cat <<'EOF'
test: scaffold headless screenshot gallery project

Boots the real ArctZ.App (not a test stand-in) in its own process so
--theme=print, FluentTheme, and MaterialIconStyles all apply exactly as
in production, and proves the capture pipeline on the connection screen.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Full 11-screen catalog + generated SCREENS.md

Extends Task 1's proven pipeline with demo data (so `main`/`library`/`keypoint-editor` have something to show), the remaining 10 screen definitions, and the Markdown index generated from the same catalog.

**Files:**
- Modify: `ArctZ.Tests.Screenshots/ScreenCatalog.cs`
- Modify: `ArctZ.Tests.Screenshots/ScreenshotGalleryTests.cs`

**Interfaces:**
- Consumes (from Task 1): `ScreenDefinition`, `ScreenCatalog.Build()`, `FakeDeviceTransport`, `FakeProgramStorage`, `RepoRoot.Find()`, `VisualTreeAnimationStripper.StripRevealAnimations(Control)`, `HeadlessAppBootstrap.EnsureInitialized()`.
- Consumes (from `ArctZ` core): `ProgramViewModel.Connection` (`ConnectionViewModel`), `.KeyPoints` (`ObservableCollection<KeyPoint>`), `.Library` (`ObservableCollection<ProgramLibraryItem>`), `.OpenLibraryCommand`/`.CloseLibraryCommand`/`.EditKeyPointCommand`/`.KeyPointEditor`/`.EditCompletionSettingsCommand`/`.CompletionSettingsEditor`/`.RenameProgramCommand`/`.CancelRenameCommand`/`.RemoveKeyPointCommand`/`.ConfirmNoCommand`/`.ToggleSideMenuCommand`/`.CloseSideMenuCommand`/`.RefreshLibraryCommand`/`.LoadProgramCommand` (CommunityToolkit-generated `IRelayCommand`/`IRelayCommand<T>`/`IAsyncRelayCommand`/`IAsyncRelayCommand<T>`); `ConnectionViewModel.SelectedEndpoint`/`.AvailableEndpoints`/`.ConnectCommand`/`.LastAlarmCode`/`.ToggleGCodeLogCommand`/`.ToggleMockSettingsCommand` (`IEnhancedCommand<Unit>`, `.Execute()` → `IObservable<Unit>`); `ConnectionEndpointKind.Demo`; `JibProgram` (`Name` settable, `KeyPoints` a `List<KeyPoint>`); `KeyPoint(Guid Id, int Number, string? Label, MachinePose Pose, double DwellSeconds, double FeedRateUnitsPerMin, EaseMode Ease, bool ContinuousBlend)`; `MachinePose(double X, double Y, double Z, double A)`; `EaseMode.None`.
- Produces: `ScreenCatalog.Build(FakeDeviceTransport demoTransport)` (signature change from Task 1 — drops the parameterless overload) returning all 11 entries in capture order; `ScreenshotGalleryTests` additionally writes `screenshots/SCREENS.md`.

- [ ] **Step 1: Grow the catalog to all 11 screens**

Replace the whole body of `ArctZ.Tests.Screenshots/ScreenCatalog.cs` with:

```csharp
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
            Setup: _ => Task.CompletedTask,
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
            Setup: vm => vm.Connection.ToggleGCodeLogCommand.Execute().ToTask(),
            Teardown: vm => vm.Connection.ToggleGCodeLogCommand.Execute().ToTask()),

        new ScreenDefinition(
            "mock-settings",
            "Настройки мока",
            Setup: vm => vm.Connection.ToggleMockSettingsCommand.Execute().ToTask(),
            Teardown: vm => vm.Connection.ToggleMockSettingsCommand.Execute().ToTask()),
    };
}
```

`System.Reactive.Threading.Tasks` (for `.ToTask()` on the `IObservable<Unit>` that `IEnhancedCommand<Unit>.Execute()` returns) is already in the usings above alongside `System`, `System.Collections.Generic`, `System.Linq`, `System.Threading.Tasks`, `ArctZ.Tests.Screenshots.Support`, `ArctZ.ViewModels`.

- [ ] **Step 2: Seed demo data, wire the new catalog signature, and generate SCREENS.md**

Replace `ArctZ.Tests.Screenshots/ScreenshotGalleryTests.cs` with:

```csharp
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Screenshots.Support;
using ArctZ.ViewModels;
using ArctZ.Views;

namespace ArctZ.Tests.Screenshots;

public class ScreenshotGalleryTests
{
    public ScreenshotGalleryTests() => HeadlessAppBootstrap.EnsureInitialized();

    [Fact]
    public async Task GeneratesScreenshotsForAllScreens()
    {
        var screenshotsDir = Path.Combine(RepoRoot.Find(), "screenshots");
        Directory.CreateDirectory(screenshotsDir);

        var realTransport = new FakeDeviceTransport();
        var demoTransport = new FakeDeviceTransport();
        var storage = new FakeProgramStorage();

        var demoProgram = new JibProgram { Name = "Демо программа" };
        demoProgram.KeyPoints.Add(new KeyPoint(
            Guid.NewGuid(), 1, "Точка 1", new MachinePose(0, 0, 0, 0),
            DwellSeconds: 0, FeedRateUnitsPerMin: 500, EaseMode.None, ContinuousBlend: false));
        demoProgram.KeyPoints.Add(new KeyPoint(
            Guid.NewGuid(), 2, "Точка 2", new MachinePose(120, 45, 80, 15),
            DwellSeconds: 1, FeedRateUnitsPerMin: 500, EaseMode.None, ContinuousBlend: false));
        await storage.SaveAsync(demoProgram);

        var connection = new ConnectionViewModel(
            realTransport,
            () => demoTransport,
            new DeviceSessionFactory(MachineLimits.Default));
        var programViewModel = new ProgramViewModel(connection, storage, new TrajectoryCompiler());

        var mainView = new MainView { DataContext = programViewModel };
        VisualTreeAnimationStripper.StripRevealAnimations(mainView);

        var window = new Window { Width = 390, Height = 844, Content = mainView };
        window.Classes.Add("print");
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var screens = ScreenCatalog.Build(demoTransport);
        WriteScreensMarkdown(screenshotsDir, screens);

        for (var i = 0; i < screens.Count; i++)
        {
            var screen = screens[i];
            var setupTask = screen.Setup(programViewModel);
            Dispatcher.UIThread.RunJobs();

            var frame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException($"No frame captured for screen '{screen.Id}'.");
            frame.Save(Path.Combine(screenshotsDir, $"{i + 1:D2}-{screen.Id}.png"));

            var teardownTask = screen.Teardown(programViewModel);
            await setupTask;
            await teardownTask;
            Dispatcher.UIThread.RunJobs();
        }

        window.Close();

        Assert.True(File.Exists(Path.Combine(screenshotsDir, "SCREENS.md")));
        for (var i = 0; i < screens.Count; i++)
        {
            Assert.True(File.Exists(Path.Combine(screenshotsDir, $"{i + 1:D2}-{screens[i].Id}.png")));
        }
    }

    private static void WriteScreensMarkdown(string screenshotsDir, System.Collections.Generic.IReadOnlyList<ScreenDefinition> screens)
    {
        var md = new StringBuilder();
        md.AppendLine("# Экраны ArctZ — галерея скриншотов");
        md.AppendLine();
        md.AppendLine("Сгенерировано автоматически тестом `ScreenshotGalleryTests` (`ArctZ.Tests.Screenshots`). Не редактировать вручную — при следующем запуске тест перезапишет файл.");
        md.AppendLine();
        md.AppendLine("| # | Экран | Файл |");
        md.AppendLine("|---|---|---|");
        for (var i = 0; i < screens.Count; i++)
        {
            var fileName = $"{i + 1:D2}-{screens[i].Id}.png";
            md.AppendLine($"| {i + 1} | {screens[i].Title} (`{screens[i].Id}`) | [{fileName}]({fileName}) |");
        }

        File.WriteAllText(Path.Combine(screenshotsDir, "SCREENS.md"), md.ToString());
    }
}
```

- [ ] **Step 3: Run the test**

Run: `dotnet test ArctZ.Tests.Screenshots/ArctZ.Tests.Screenshots.csproj`
Expected: PASS (1 test). `screenshots/` now contains `SCREENS.md` and `01-connection.png` through `11-mock-settings.png`.

- [ ] **Step 4: Visually spot-check a few screenshots**

Open `screenshots/02-main.png`, `screenshots/04-library.png`, and `screenshots/07-rename.png`. Expected: print palette (white background, black text/borders), 390×844 frame, `main` shows the seeded "Демо программа" with 2 key points and no leftover fade-in transparency, `library` shows that same program listed, `rename` shows the rename dialog with "Демо программа" pre-filled.

- [ ] **Step 5: Commit**

```bash
git add ArctZ.Tests.Screenshots screenshots
git commit -m "$(cat <<'EOF'
test: capture all 11 ArctZ screens into screenshots/

ScreenCatalog is the single source of truth for both the PNG capture
loop and the generated screenshots/SCREENS.md index, so the two can't
drift out of sync.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```
