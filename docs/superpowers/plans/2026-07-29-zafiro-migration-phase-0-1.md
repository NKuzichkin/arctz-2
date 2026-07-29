# Zafiro Migration — Phase 0 + Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire `Zafiro.Avalonia` + `ReactiveUI` into ArctZ (packages, DI bootstrap, `DataTypeViewLocator`) and migrate the smallest ViewModel (`ConnectionViewModel`) end-to-end as the pattern the later phases (`ProgramViewModel`, `KeyPointEditorViewModel`, theming pass) will repeat.

**Architecture:** `CommunityToolkit.Mvvm` and `ReactiveUI`-based ViewModels coexist during the transition (`ViewModelBase : ObservableObject` for not-yet-migrated VMs, new `ReactiveViewModelBase : ReactiveObject, IDisposable` for migrated ones). `ArctZ.Views.ConnectionView` and its `ContentControl` resolution in `MainView.axaml` move onto Zafiro's `DataTypeViewLocator`, which requires an explicit `RegisterGlobal<TViewModel, TView>()` call per pair (verified: unlike our current `ViewLocator.cs`, it has no naming-convention fallback).

**Tech Stack:** .NET 10, Avalonia 12.0.4, Zafiro.Avalonia 53.3.0, ReactiveUI 23.2.28, ReactiveUI.Avalonia 11.4.13, ReactiveUI.SourceGenerators 3.1.0, xUnit.

## Global Constraints

- Central package management is on (`Directory.Packages.props`, `ManagePackageVersionsCentrally=true`) — every package version is set there, never in a `.csproj`.
- "Important: keep version in sync!" — all `Avalonia.*` package versions in `Directory.Packages.props` must match.
- Compiled bindings are on (`AvaloniaUseCompiledBindingsByDefault=true`) — every XAML binding needs `x:DataType` in scope.
- `[Reactive]` (property codegen) is in namespace `ReactiveUI.SourceGenerators`, **not** `ReactiveUI` — a bare `using ReactiveUI;` resolves `Reactive` to an unrelated non-attribute type and fails with CS0616. Always add `using ReactiveUI.SourceGenerators;` explicitly where `[Reactive]` is used.
- `.DisposeWith(...)` is `System.Reactive.Disposables.Fluent.DisposableExtensions.DisposeWith` — requires `using System.Reactive.Disposables.Fluent;`. It is not a ReactiveUI API.
- ReactiveUI 23.2.28 throws `InvalidOperationException: ReactiveUI has not been initialized` from the static constructor of `ReactiveNotifyPropertyChangedMixin` the first time `WhenAnyValue`/`[Reactive]`/`ReactiveCommand` is touched, unless the process already ran `ReactiveUI.Builder.RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().BuildApp();` (production: happens via `.UseReactiveUI(...)` once the `AppBuilder` actually starts/sets up; tests: needs an explicit call, Task 2 below).
- `ReactiveUI.RxSchedulers.MainThreadScheduler`/`.TaskpoolScheduler` replace the old `RxApp.MainThreadScheduler`/`.TaskpoolScheduler` — the `RxApp` static class does not exist in this ReactiveUI version.
- `dotnet test ArctZ.Tests/ArctZ.Tests.csproj` must stay green after every task in this plan.
- Zafiro.Avalonia's controls/markup extensions/view-locator all carry `[XmlnsDefinition("https://github.com/avaloniaui", "Zafiro.Avalonia.*")]` — they're usable directly under the `xmlns="https://github.com/avaloniaui"` every View already declares, no extra `xmlns:zafiro=` prefix needed.

---

### Task 1: Add Zafiro/ReactiveUI packages, bump Avalonia pins to 12.0.4

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `ArctZ/ArctZ.csproj`

**Interfaces:**
- Produces: `Zafiro.Avalonia`, `ReactiveUI`, `ReactiveUI.Avalonia`, `ReactiveUI.SourceGenerators` available as compile-time references in the `ArctZ` project for every later task in this plan.

- [ ] **Step 1: Bump the Avalonia core package versions**

In `Directory.Packages.props`, change every one of these six lines from `12.0.3` to `12.0.4` (verified minimum: `Zafiro.Avalonia 53.3.0` depends on `Avalonia >= 12.0.4`; restoring against `12.0.3` fails with `NU1605` downgrade error):

```xml
<PackageVersion Include="Avalonia" Version="12.0.4" />
<PackageVersion Include="Avalonia.Themes.Fluent" Version="12.0.4" />
<PackageVersion Include="Avalonia.Fonts.Inter" Version="12.0.4" />
<PackageVersion Include="AvaloniaUI.DiagnosticsSupport" Version="2.2.1" />
<PackageVersion Include="Avalonia.Desktop" Version="12.0.4" />
<PackageVersion Include="Avalonia.iOS" Version="12.0.4" />
<PackageVersion Include="Avalonia.Browser" Version="12.0.4" />
<PackageVersion Include="Avalonia.Android" Version="12.0.4" />
```

(`AvaloniaUI.DiagnosticsSupport` keeps its own independent version scheme — leave it at `2.2.1`.)

- [ ] **Step 2: Add the new package versions**

Immediately after the `CommunityToolkit.Mvvm` line in `Directory.Packages.props`, add:

```xml
<PackageVersion Include="Zafiro.Avalonia" Version="53.3.0" />
<PackageVersion Include="ReactiveUI" Version="23.2.28" />
<PackageVersion Include="ReactiveUI.Avalonia" Version="11.4.13" />
<PackageVersion Include="ReactiveUI.SourceGenerators" Version="3.1.0" />
```

- [ ] **Step 3: Reference the new packages from `ArctZ.csproj`**

In `ArctZ/ArctZ.csproj`, add to the existing `<ItemGroup>` that has `CommunityToolkit.Mvvm`:

```xml
<PackageReference Include="Zafiro.Avalonia" />
<PackageReference Include="ReactiveUI" />
<PackageReference Include="ReactiveUI.Avalonia" />
<PackageReference Include="ReactiveUI.SourceGenerators" />
```

- [ ] **Step 4: Restore and build**

Run: `dotnet restore ArctZ.slnx`
Expected: succeeds, no `NU1605` errors.

Run: `dotnet build ArctZ.slnx`
Expected: succeeds (nothing references the new packages yet, so this just proves the dependency graph resolves cleanly).

- [ ] **Step 5: Commit**

```bash
git add Directory.Packages.props ArctZ/ArctZ.csproj
git commit -m "build: add Zafiro.Avalonia/ReactiveUI packages, bump Avalonia to 12.0.4"
```

---

### Task 2: Initialize ReactiveUI in all 4 app heads and in ArctZ.Tests

**Files:**
- Modify: `ArctZ.Desktop/Program.cs:29-36`
- Modify: `ArctZ.Android/Application.cs:28-29`
- Modify: `ArctZ.iOS/AppDelegate.cs:32-33`
- Modify: `ArctZ.Browser/Program.cs:21-26`
- Create: `ArctZ.Tests/ReactiveUIBootstrap.cs`

**Interfaces:**
- Consumes: `ReactiveUI.Avalonia.AppBuilderExtensions.UseReactiveUI(this AppBuilder, Action<IReactiveUIBuilder>)`, `ReactiveUI.Avalonia.AppBuilderExtensions.WithAvalonia(this IReactiveUIBuilder)` (both confirmed present via reflection on the installed package).
- Produces: every process that runs ArctZ code (4 heads + test process) can construct a `ReactiveObject`-derived ViewModel without throwing `InvalidOperationException`. All later tasks depend on this.

- [ ] **Step 1: Wire `UseReactiveUI` into the Desktop head**

In `ArctZ.Desktop/Program.cs`, add `using ReactiveUI.Avalonia;` to the usings, and change:

```csharp
public static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>()
        .UsePlatformDetect()
#if DEBUG
        .WithDeveloperTools()
#endif
        .WithInterFont()
        .LogToTrace();
```

to:

```csharp
public static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseReactiveUI(b => b.WithAvalonia())
#if DEBUG
        .WithDeveloperTools()
#endif
        .WithInterFont()
        .LogToTrace();
```

- [ ] **Step 2: Wire `UseReactiveUI` into Android**

In `ArctZ.Android/Application.cs`, add `using ReactiveUI.Avalonia;`, and change:

```csharp
return base.CustomizeAppBuilder(builder)
    .WithInterFont();
```

to:

```csharp
return base.CustomizeAppBuilder(builder)
    .UseReactiveUI(b => b.WithAvalonia())
    .WithInterFont();
```

- [ ] **Step 3: Wire `UseReactiveUI` into iOS**

In `ArctZ.iOS/AppDelegate.cs`, add `using ReactiveUI.Avalonia;`, and change:

```csharp
return base.CustomizeAppBuilder(builder)
    .WithInterFont();
```

to:

```csharp
return base.CustomizeAppBuilder(builder)
    .UseReactiveUI(b => b.WithAvalonia())
    .WithInterFont();
```

- [ ] **Step 4: Wire `UseReactiveUI` into Browser**

In `ArctZ.Browser/Program.cs`, add `using ReactiveUI.Avalonia;`, and change:

```csharp
return BuildAvaloniaApp()
    .WithInterFont()
#if DEBUG
    .WithDeveloperTools()
#endif
    .StartBrowserAppAsync("out");
```

to:

```csharp
return BuildAvaloniaApp()
    .WithInterFont()
    .UseReactiveUI(b => b.WithAvalonia())
#if DEBUG
    .WithDeveloperTools()
#endif
    .StartBrowserAppAsync("out");
```

- [ ] **Step 5: Bootstrap ReactiveUI (and a deterministic scheduler) for the test process**

Create `ArctZ.Tests/ReactiveUIBootstrap.cs`:

```csharp
using System.Reactive.Concurrency;
using System.Runtime.CompilerServices;
using ReactiveUI;
using ReactiveUI.Builder;

namespace ArctZ.Tests;

/// <summary>
/// Runs once when the test assembly loads (before any test executes). Plain xUnit
/// tests never touch Avalonia's AppBuilder/UseReactiveUI, so without this every
/// ReactiveObject-derived ViewModel's constructor throws
/// InvalidOperationException("ReactiveUI has not been initialized"). Also forces
/// RxSchedulers.MainThreadScheduler to ImmediateScheduler for the whole process —
/// simpler and safer than a per-test save/restore guard, since it's global mutable
/// state and xUnit can run different test classes in parallel.
/// </summary>
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

- [ ] **Step 6: Build and run the existing suite**

Run: `dotnet build ArctZ.slnx`
Expected: succeeds for all 4 heads.

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: all existing tests still pass (nothing uses ReactiveUI yet — this just proves `ReactiveUIBootstrap` doesn't break anything, e.g. doesn't collide with `AvaloniaHeadlessBootstrap`'s own `AppBuilder.Configure<TestApp>().UseHeadless(...).SetupWithoutStarting()` used by `VirtualJoystickTests`).

- [ ] **Step 7: Commit**

```bash
git add ArctZ.Desktop/Program.cs ArctZ.Android/Application.cs ArctZ.iOS/AppDelegate.cs ArctZ.Browser/Program.cs ArctZ.Tests/ReactiveUIBootstrap.cs
git commit -m "feat: initialize ReactiveUI across all app heads and the test process"
```

---

### Task 3: Swap the hand-rolled `ViewLocator` for Zafiro's `DataTypeViewLocator`

**Files:**
- Modify: `ArctZ/App.axaml:9-11`
- Modify: `ArctZ/App.axaml.cs`
- Delete: `ArctZ/ViewLocator.cs`
- Create: `ArctZ.Tests/DataTypeViewLocatorTests.cs`

**Interfaces:**
- Consumes: `ArctZ.ViewModels.ConnectionViewModel` (existing 4-arg constructor — **not yet migrated**, that's Task 4), `ArctZ.Views.ConnectionView`.
- Produces: `Zafiro.Avalonia.ViewLocators.DataTypeViewLocator` registered as the app's only `IDataTemplate`, with `ConnectionViewModel → ConnectionView` registered in its global registry. `MainView.axaml:81`'s `<ContentControl Content="{Binding Connection}" />` keeps resolving to `ConnectionView` exactly as before.

- [ ] **Step 1: Write the failing test**

`DataTypeViewLocator.Match`/`.Build` only recognize types explicitly registered via `RegisterGlobal<TViewModel, TView>()` — verified by reflection: there is no naming-convention fallback (unlike our current `ViewLocator.cs`), so this test fails until Step 3 registers the pair.

Create `ArctZ.Tests/DataTypeViewLocatorTests.cs`:

```csharp
using ArctZ.Services.Device;
using ArctZ.Tests.Services;
using ArctZ.Tests.Services.Device;
using ArctZ.ViewModels;
using ArctZ.Views;
using Avalonia.Controls.Templates;
using Zafiro.Avalonia.ViewLocators;

namespace ArctZ.Tests;

public class DataTypeViewLocatorTests
{
    [Fact]
    public void Build_ConnectionViewModel_ResolvesToConnectionView()
    {
        AvaloniaHeadlessBootstrap.EnsureInitialized();
        IDataTemplate locator = new DataTypeViewLocator();
        var vm = new ConnectionViewModel(
            new FakeDeviceTransport(),
            () => new FakeDeviceTransport(),
            new DeviceSessionFactory(MachineLimits.Default),
            new InlineUiDispatcher());

        Assert.True(locator.Match(vm));
        Assert.IsType<ConnectionView>(locator.Build(vm));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter DataTypeViewLocatorTests`
Expected: FAIL — `Assert.True(locator.Match(vm))` fails because nothing is registered yet.

- [ ] **Step 3: Register the pair and swap the DataTemplate in `App.axaml.cs`/`App.axaml`**

In `ArctZ/App.axaml.cs`, add the registration to `Initialize()` (must run before any DataTemplate lookup) and the `using`:

```csharp
using ArctZ.ViewModels;
using ArctZ.Views;
using Zafiro.Avalonia.ViewLocators;
// ...existing usings...

public override void Initialize()
{
    DataTypeViewLocator.RegisterGlobal<ConnectionViewModel, ConnectionView>();
    AvaloniaXamlLoader.Load(this);
}
```

In `ArctZ/App.axaml`, replace:

```xml
<Application.DataTemplates>
    <local:ViewLocator/>
</Application.DataTemplates>
```

with:

```xml
<Application.DataTemplates>
    <DataTypeViewLocator />
</Application.DataTemplates>
```

(No new `xmlns` needed — `DataTypeViewLocator` is `[XmlnsDefinition]`-mapped into the default `https://github.com/avaloniaui` namespace already declared on `<Application>`. The `xmlns:local="using:ArctZ"` declaration can stay; nothing else in `App.axaml` uses it, but removing it is out of scope for this task.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter DataTypeViewLocatorTests`
Expected: PASS.

- [ ] **Step 5: Delete the old `ViewLocator` and confirm nothing else references it**

Run: `grep -rn "ViewLocator" ArctZ ArctZ.Tests --include=*.cs --include=*.axaml` and confirm the only remaining hits are `DataTypeViewLocator`/`DataTypeViewLocatorTests` (i.e. no other file still says `local:ViewLocator` or `ArctZ.ViewLocator`).

Delete `ArctZ/ViewLocator.cs`.

- [ ] **Step 6: Full build, full test run, manual smoke check**

Run: `dotnet build ArctZ.slnx`
Expected: succeeds.

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: all tests pass.

Run: `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj`, switch to demo mode and connect.
Expected: the connection status/Homing/Сброс аварии/Отключить panel (rendered via the `ContentControl` at `MainView.axaml:81`) still appears and behaves exactly as before — this is the one spot in the app that goes through a `DataTemplate`, so it's the only thing this task could have silently broken.

- [ ] **Step 7: Commit**

```bash
git add ArctZ/App.axaml ArctZ/App.axaml.cs ArctZ.Tests/DataTypeViewLocatorTests.cs
git rm ArctZ/ViewLocator.cs
git commit -m "feat: replace hand-rolled ViewLocator with Zafiro's DataTypeViewLocator"
```

---

### Task 4: Migrate `ConnectionViewModel` to `ReactiveObject`

**Files:**
- Create: `ArctZ/ViewModels/ReactiveViewModelBase.cs`
- Modify: `ArctZ/ViewModels/ConnectionViewModel.cs`
- Modify: `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs`

**Interfaces:**
- Produces: `ArctZ.ViewModels.ReactiveViewModelBase` — `protected CompositeDisposable Disposables { get; }`, `public void Dispose()`. Later phases (`ProgramViewModel`, `KeyPointEditorViewModel`) derive from this too.
- Produces: `ConnectionViewModel` public surface unchanged in shape (`Session`, `ConnectionState`, `SelectedEndpoint`, `IsConnectionModalVisible`, `AvailableEndpoints`, `ConnectCommand`/`DisconnectCommand`/`HomeCommand`/`ResetAlarmCommand`) plus a new `ConnectionStateLabel` computed property (replaces the label converter — used by Task 5), but the constructor drops its 4th `IUiDispatcher` parameter (now 3-arg) and the four commands change type from CommunityToolkit's generated `IAsyncRelayCommand` to `Zafiro.UI.Commands.IEnhancedCommand<System.Reactive.Unit, System.Reactive.Unit>`.

- [ ] **Step 1: Rewrite the test file for the new constructor and command shape**

This is a refactor of passing tests, not new behavior — TDD's usual "write a failing test for new behavior" doesn't apply verbatim. Instead: rewrite every test to the *new* API shape first (they'll fail to *compile* until Step 3 changes `ConnectionViewModel` itself), which pins down exactly what Step 3 needs to produce.

Replace `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs` in full:

```csharp
using System.Linq;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Tests.Services.Device;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class ConnectionViewModelTests
{
    private static ConnectionViewModel CreateVm(IDeviceTransport realTransport, IDeviceTransport? demoTransport = null) =>
        new(realTransport, () => demoTransport ?? new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default));

    [Fact]
    public void Constructor_DefaultsToFirstEndpointAndListsRealAndDemo()
    {
        var vm = CreateVm(new FakeDeviceTransport());

        Assert.Equal(2, vm.AvailableEndpoints.Count);
        Assert.Contains(vm.AvailableEndpoints, e => e.Kind == ConnectionEndpointKind.RealDevice);
        Assert.Contains(vm.AvailableEndpoints, e => e.Kind == ConnectionEndpointKind.Demo);
        Assert.Equal(ConnectionEndpointKind.RealDevice, vm.SelectedEndpoint!.Kind);
    }

    [Fact]
    public async Task ConnectCommand_DemoSelected_ConnectsUsingDemoTransportNotRealTransport()
    {
        var realTransport = new FakeDeviceTransport();
        var demoTransport = new FakeDeviceTransport();
        var vm = CreateVm(realTransport, demoTransport);
        vm.SelectedEndpoint = vm.AvailableEndpoints.Single(e => e.Kind == ConnectionEndpointKind.Demo);

        await vm.ConnectCommand.Execute();

        Assert.True(demoTransport.IsConnected);
        Assert.False(realTransport.IsConnected);
        Assert.Equal(ConnectionState.Connected, vm.Session!.ConnectionState);
    }

    [Fact]
    public async Task ConnectCommand_RealDeviceSelected_ConnectsUsingRealTransport()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = CreateVm(realTransport);

        await vm.ConnectCommand.Execute();

        Assert.True(realTransport.IsConnected);
    }

    [Fact]
    public async Task DisconnectCommand_DisconnectsActiveSessionAndClearsIt()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = CreateVm(realTransport);
        await vm.ConnectCommand.Execute();

        await vm.DisconnectCommand.Execute();

        Assert.False(realTransport.IsConnected);
        Assert.Null(vm.Session);
    }

    [Fact]
    public async Task ConnectCommand_WhileAlreadyConnected_DisconnectsPreviousSessionBeforeCreatingNewOne()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = CreateVm(realTransport);
        await vm.ConnectCommand.Execute();
        var firstSession = vm.Session;

        await vm.ConnectCommand.Execute();

        Assert.NotNull(firstSession);
        Assert.NotSame(firstSession, vm.Session);
        Assert.Equal(ConnectionState.Disconnected, firstSession!.ConnectionState);
        Assert.Equal(ConnectionState.Connected, vm.Session!.ConnectionState);
        Assert.True(realTransport.IsConnected);
    }

    [Fact]
    public async Task ConnectCommand_SwitchingEndpointWhileConnected_TearsDownTheRealTransportSession()
    {
        var realTransport = new FakeDeviceTransport();
        var demoTransport = new FakeDeviceTransport();
        var vm = CreateVm(realTransport, demoTransport);
        await vm.ConnectCommand.Execute();

        vm.SelectedEndpoint = vm.AvailableEndpoints.Single(e => e.Kind == ConnectionEndpointKind.Demo);
        await vm.ConnectCommand.Execute();

        Assert.False(realTransport.IsConnected);
        Assert.True(demoTransport.IsConnected);
        Assert.Equal(ConnectionState.Connected, vm.Session!.ConnectionState);
    }

    [Fact]
    public async Task IsConnectionModalVisible_TracksSessionLifecycle()
    {
        var vm = CreateVm(new FakeDeviceTransport());

        Assert.True(vm.IsConnectionModalVisible);

        await vm.ConnectCommand.Execute();
        Assert.False(vm.IsConnectionModalVisible);

        await vm.DisconnectCommand.Execute();
        Assert.True(vm.IsConnectionModalVisible);
    }

    [Fact]
    public async Task ConnectCommand_TransportThrows_ResetsSessionAndReenablesRetry()
    {
        var realTransport = new FakeDeviceTransport { ConnectFailuresRemaining = 1 };
        var vm = CreateVm(realTransport);

        await vm.ConnectCommand.Execute();

        Assert.Null(vm.Session);
        Assert.True(vm.IsConnectionModalVisible);
        Assert.True(vm.ConnectCommand.CanExecute(null));

        // Retry succeeds now that ConnectFailuresRemaining is exhausted.
        await vm.ConnectCommand.Execute();
        Assert.NotNull(vm.Session);
        Assert.False(vm.IsConnectionModalVisible);
        Assert.Equal(ConnectionState.Connected, vm.Session!.ConnectionState);
    }
}
```

Note what changed vs. the CommunityToolkit-era test file: no `InlineUiDispatcher` argument anywhere (dropped from the constructor); `.ExecuteAsync(null)` → `.Execute()` (awaits `ReactiveCommand`'s `IObservable<Unit>` directly via Rx's built-in `GetAwaiter`).

- [ ] **Step 2: Confirm it fails to compile against the current `ConnectionViewModel`**

Run: `dotnet build ArctZ.Tests/ArctZ.Tests.csproj`
Expected: FAIL — `CS1729`/`CS7036` (no 3-arg constructor overload yet) and `CS1061` (`IAsyncRelayCommand` has no `Execute()`).

- [ ] **Step 3: Add `ReactiveViewModelBase`**

Create `ArctZ/ViewModels/ReactiveViewModelBase.cs`:

```csharp
using System;
using System.Reactive.Disposables;
using ReactiveUI;

namespace ArctZ.ViewModels;

public abstract class ReactiveViewModelBase : ReactiveObject, IDisposable
{
    protected CompositeDisposable Disposables { get; } = new();

    public void Dispose() => Disposables.Dispose();
}
```

- [ ] **Step 4: Rewrite `ConnectionViewModel`**

Replace `ArctZ/ViewModels/ConnectionViewModel.cs` in full:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Zafiro.UI.Commands;

namespace ArctZ.ViewModels;

public partial class ConnectionViewModel : ReactiveViewModelBase
{
    private readonly IDeviceTransport _realTransport;
    private readonly Func<IDeviceTransport> _createDemoTransport;
    private readonly IDeviceSessionFactory _sessionFactory;

    [Reactive] private IDeviceSession? session;

    // Mirrors Session.ConnectionState. IDeviceSession does not implement
    // INotifyPropertyChanged, so a direct "Session.ConnectionState" binding
    // only ever reads the value once (when Session itself changes) and never
    // updates when the same session's state transitions later. This property
    // is kept current via the ConnectionStateChanged event subscription set up
    // in the constructor below, so bindings on THIS view model update live.
    [Reactive] private ConnectionState connectionState = ConnectionState.Disconnected;

    [Reactive] private ConnectionEndpoint? selectedEndpoint;

    public bool IsConnectionModalVisible => Session is null || ConnectionState != ConnectionState.Connected;

    public string ConnectionStateLabel => ConnectionState switch
    {
        ConnectionState.Disconnected => "Не подключено",
        ConnectionState.Connecting => "Подключение…",
        ConnectionState.Connected => "Подключено",
        ConnectionState.Reconnecting => "Переподключение…",
        _ => "—",
    };

    public ObservableCollection<ConnectionEndpoint> AvailableEndpoints { get; } = new()
    {
        new ConnectionEndpoint("real", "Устройство", ConnectionEndpointKind.RealDevice),
        new ConnectionEndpoint("demo", "Демо", ConnectionEndpointKind.Demo),
    };

    public IEnhancedCommand<Unit, Unit> ConnectCommand { get; }
    public IEnhancedCommand<Unit, Unit> DisconnectCommand { get; }
    public IEnhancedCommand<Unit, Unit> HomeCommand { get; }
    public IEnhancedCommand<Unit, Unit> ResetAlarmCommand { get; }

    public ConnectionViewModel(
        IDeviceTransport realTransport,
        Func<IDeviceTransport> createDemoTransport,
        IDeviceSessionFactory sessionFactory)
    {
        _realTransport = realTransport;
        _createDemoTransport = createDemoTransport;
        _sessionFactory = sessionFactory;
        SelectedEndpoint = AvailableEndpoints[0];

        var canConnect = this.WhenAnyValue(
            x => x.SelectedEndpoint,
            x => x.ConnectionState,
            (endpoint, state) => endpoint is not null &&
                state is not (ConnectionState.Connecting or ConnectionState.Reconnecting));

        ConnectCommand = ReactiveCommand.CreateFromTask(ConnectAsync, canConnect)
            .Enhance(text: "Подключить", name: "ConnectCommand");
        DisconnectCommand = ReactiveCommand.CreateFromTask(DisconnectAsync)
            .Enhance(text: "Отключить", name: "DisconnectCommand");
        HomeCommand = ReactiveCommand.CreateFromTask(HomeAsync)
            .Enhance(text: "Homing", name: "HomeCommand");
        ResetAlarmCommand = ReactiveCommand.CreateFromTask(ResetAlarmAsync)
            .Enhance(text: "Сброс аварии", name: "ResetAlarmCommand");

        ((IDisposable)ConnectCommand).DisposeWith(Disposables);
        ((IDisposable)DisconnectCommand).DisposeWith(Disposables);
        ((IDisposable)HomeCommand).DisposeWith(Disposables);
        ((IDisposable)ResetAlarmCommand).DisposeWith(Disposables);

        // Immediately mirror a newly-assigned session's state, then keep mirroring it
        // as ConnectionStateChanged fires later (on a background thread for the
        // real-device path — ObserveOn marshals back before the property is set).
        // .Switch() drops the previous session's event subscription the moment
        // Session changes to a new value or null, replacing the old
        // OnSessionChanged-based subscribe/unsubscribe dance.
        this.WhenAnyValue(x => x.Session)
            .Do(s => ConnectionState = s?.ConnectionState ?? ConnectionState.Disconnected)
            .Select(s => s is null
                ? Observable.Empty<Unit>()
                : Observable.FromEvent(h => s.ConnectionStateChanged += h, h => s.ConnectionStateChanged -= h)
                    .ObserveOn(RxSchedulers.MainThreadScheduler))
            .Switch()
            .Subscribe(_ => ConnectionState = Session?.ConnectionState ?? ConnectionState.Disconnected)
            .DisposeWith(Disposables);

        // IsConnectionModalVisible/ConnectionStateLabel are plain computed
        // properties (no ObservableAsPropertyHelper) — re-raise their
        // INotifyPropertyChanged notifications whenever a dependency changes,
        // same intent as CommunityToolkit's [NotifyPropertyChangedFor] before.
        this.WhenAnyValue(x => x.Session, x => x.ConnectionState)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(IsConnectionModalVisible));
                this.RaisePropertyChanged(nameof(ConnectionStateLabel));
            })
            .DisposeWith(Disposables);
    }

    private async Task ConnectAsync()
    {
        if (SelectedEndpoint is null)
        {
            return;
        }

        // All platform heads register IDeviceTransport as a singleton, so a second
        // session would wrap the same transport as the first: two LineReceived
        // subscribers, two status pollers, two racing reconnect loops. Tear the
        // previous session down first — this covers both reconnecting and
        // switching endpoints while connected.
        if (Session is not null)
        {
            await Session.DisconnectAsync();
            Session = null;
        }

        var transport = SelectedEndpoint.Kind == ConnectionEndpointKind.Demo
            ? _createDemoTransport()
            : _realTransport;

        var session = _sessionFactory.Create(transport);
        Session = session;

        try
        {
            await session.ConnectAsync(SelectedEndpoint.Id);
        }
        catch
        {
            // A failed connect leaves the transport's LineReceived/Disconnected handlers
            // subscribed (DeviceSession.ConnectAsync wires them before attempting the
            // transport-level connect). session.DisconnectAsync() unwinds that — critical
            // for the real-device transport, which is a singleton reused by the next
            // attempt; leaked handlers there would double-fire on every subsequent connect.
            await session.DisconnectAsync();
            Session = null;
        }
    }

    private async Task DisconnectAsync()
    {
        if (Session is not null)
        {
            await Session.DisconnectAsync();
            Session = null;
        }
    }

    private Task HomeAsync() => Session?.HomeAsync() ?? Task.CompletedTask;

    private Task ResetAlarmAsync() => Session?.ResetAlarmAsync() ?? Task.CompletedTask;
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter ConnectionViewModelTests`
Expected: PASS — all 8 tests green.

- [ ] **Step 6: Fix up `DataTypeViewLocatorTests` for the new constructor**

Task 3's test still constructs `ConnectionViewModel` with the old 4-arg (`+ InlineUiDispatcher`) signature, which no longer compiles. In `ArctZ.Tests/DataTypeViewLocatorTests.cs`, change:

```csharp
var vm = new ConnectionViewModel(
    new FakeDeviceTransport(),
    () => new FakeDeviceTransport(),
    new DeviceSessionFactory(MachineLimits.Default),
    new InlineUiDispatcher());
```

to:

```csharp
var vm = new ConnectionViewModel(
    new FakeDeviceTransport(),
    () => new FakeDeviceTransport(),
    new DeviceSessionFactory(MachineLimits.Default));
```

and drop the now-unused `using ArctZ.Tests.Services;` line if nothing else in the file needs it.

- [ ] **Step 7: Full suite**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: all tests pass, including `DataTypeViewLocatorTests` and every `ConnectionViewModelTests` case.

- [ ] **Step 8: Commit**

```bash
git add ArctZ/ViewModels/ReactiveViewModelBase.cs ArctZ/ViewModels/ConnectionViewModel.cs ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs ArctZ.Tests/DataTypeViewLocatorTests.cs
git commit -m "feat: migrate ConnectionViewModel to ReactiveObject/ReactiveUI"
```

---

### Task 5: Update `ConnectionView.axaml` and the connection modal in `MainView.axaml`

**Files:**
- Modify: `ArctZ/Views/ConnectionView.axaml`
- Modify: `ArctZ/Views/MainView.axaml:15-18,266-287`
- Modify: `ArctZ/Converters/ConnectionStateConverters.cs`

**Interfaces:**
- Consumes: `ConnectionViewModel.ConnectionStateLabel` (new in Task 4), `ConnectionViewModel.ConnectionState` (still bound for the brush converter).

- [ ] **Step 1: Drop the label converter, keep the brush converter**

In `ArctZ/Converters/ConnectionStateConverters.cs`, delete the `ConnectionStateToLabelConverter` class. Keep `ConnectionStateToBrushConverter` as-is — state→color is exactly the "purely visual, highly reusable" case the layout skill's own guidance carves out as a legitimate converter use, so there's no win in replacing it with Classes/Styles here.

- [ ] **Step 2: Update `ConnectionView.axaml`**

Replace `ArctZ/Views/ConnectionView.axaml` in full:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:ArctZ.ViewModels"
             xmlns:conv="using:ArctZ.Converters"
             x:Class="ArctZ.Views.ConnectionView"
             x:DataType="vm:ConnectionViewModel">

    <UserControl.Resources>
        <conv:ConnectionStateToBrushConverter x:Key="StateToBrush" />
    </UserControl.Resources>

    <StackPanel Orientation="Horizontal" Spacing="10" VerticalAlignment="Center">
        <HeaderedContainer Padding="10,6">
            <EdgePanel VerticalAlignment="Center">
                <EdgePanel.StartContent>
                    <Ellipse Width="8" Height="8" VerticalAlignment="Center"
                             Fill="{Binding ConnectionState, Converter={StaticResource StateToBrush}}" />
                </EdgePanel.StartContent>
                <TextBlock Text="{Binding ConnectionStateLabel}" Margin="8,0,0,0" VerticalAlignment="Center" />
            </EdgePanel>
        </HeaderedContainer>

        <Button Content="Homing" Command="{Binding HomeCommand}" />
        <Button Classes="danger" Content="Сброс аварии" Command="{Binding ResetAlarmCommand}" />
        <Button Content="Отключить" Command="{Binding DisconnectCommand}" />
    </StackPanel>
</UserControl>
```

(`HeaderedContainer`/`EdgePanel` need no `Header`/`EndContent` here — `HeaderedContainer` is used purely for its themed padding/border in place of the old literal `Border` with `HudPanelElevatedBrush`/`HudBorderStrongBrush`; if its default chrome doesn't visually match, apply the existing `HudPanelElevatedBrush`/`HudBorderStrongBrush` via a `Classes` style in `Themes/HudControls.axaml` rather than hardcoding properties again — check visually in Step 4 before deciding whether that follow-up is needed.)

- [ ] **Step 3: Update the connection modal block in `MainView.axaml`**

In `ArctZ/Views/MainView.axaml`, remove the now-unused `<conv:ConnectionStateToLabelConverter x:Key="StateToLabel" />` line from `<UserControl.Resources>` (keep `StateToBrush`), and change the modal block:

```xml
<TextBlock VerticalAlignment="Center"
           Text="{Binding ConnectionState, Converter={StaticResource StateToLabel}}" />
```

to:

```xml
<TextBlock VerticalAlignment="Center"
           Text="{Binding ConnectionStateLabel}" />
```

(inside the `<Border x:DataType="vm:ConnectionViewModel" DataContext="{Binding Connection}">` block, i.e. `ConnectionStateLabel` resolves against `ConnectionViewModel`, not `ProgramViewModel` — matches the existing `DataContext` scoping there.)

- [ ] **Step 4: Build and manually verify**

Run: `dotnet build ArctZ.slnx`
Expected: succeeds.

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: all tests pass (this task only touches XAML/converters, no ViewModel test should be affected).

Run: `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj`. Check:
- The connection modal on startup shows the same label text it did before (Cyrillic labels: "Не подключено"/"Подключение…"/"Подключено"/"Переподключение…").
- Selecting "Демо" and clicking "Подключить" transitions the modal away and the top status panel (now `HeaderedContainer`/`EdgePanel`) shows "Подключено" with the accent-colored dot.
- Homing / Сброс аварии / Отключить buttons in the status panel still work and Отключить brings the modal back.

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Views/ConnectionView.axaml ArctZ/Views/MainView.axaml ArctZ/Converters/ConnectionStateConverters.cs
git commit -m "feat: move ConnectionView onto Zafiro layout containers, drop label converter"
```
