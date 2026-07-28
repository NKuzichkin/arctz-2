# Connection Modal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Block the main screen behind a modal overlay whenever the device isn't `Connected`, and reduce the always-visible header to status + Homing + Reset Alarm + Disconnect (endpoint picker and Connect button move into the modal).

**Architecture:** Add a mirrored, live-updating `ConnectionState` property and a computed `IsConnectionModalVisible` bool to `ConnectionViewModel`. Bind a new full-screen overlay in `MainView.axaml` to that bool. Strip the endpoint picker/Connect button out of the always-visible `ConnectionView.axaml` header control and into the new overlay.

**Tech Stack:** Avalonia UI (compiled bindings), CommunityToolkit.Mvvm 8.4.0 source-gen (`[ObservableProperty]`, `[RelayCommand]`, `[NotifyPropertyChangedFor]`, `[NotifyCanExecuteChangedFor]`).

## Global Constraints

- No test projects exist in this solution (see CLAUDE.md) — verification is `dotnet build ArctZ.slnx` after each task, plus one manual run-through at the end. Do not add a test project as part of this plan.
- Keep Russian-language UI strings consistent with existing copy (`Не подключено`, `Подключение…`, `Подключено`, `Переподключение…`, `Homing`, `Сброс аварии`, `Отключить`, `Подключить`).
- Compiled bindings are on by default — every `DataTemplate`/nested-`DataContext` block needs an explicit `x:DataType`.

---

## Task 1: `ConnectionViewModel` — live `ConnectionState` + `IsConnectionModalVisible`

**Files:**
- Modify: `ArctZ/ViewModels/ConnectionViewModel.cs` (full file, currently 82 lines)

**Interfaces:**
- Consumes: `IDeviceSession.ConnectionState` (get), `IDeviceSession.ConnectionStateChanged` (event) — both already defined in `ArctZ/Services/Device/IDeviceSession.cs`. `Services.Device.ConnectionState` enum (`Disconnected`, `Connecting`, `Connected`, `Reconnecting`).
- Produces: `ConnectionViewModel.ConnectionState` (public, `Services.Device.ConnectionState`, default `Disconnected`) and `ConnectionViewModel.IsConnectionModalVisible` (public bool) — both consumed by Task 2 and Task 3's XAML bindings. `ConnectCommand` gains a `CanExecute` guard; its generated `ConnectCommand` name/signature is unchanged.

- [ ] **Step 1: Replace the file contents**

Replace the entire contents of `ArctZ/ViewModels/ConnectionViewModel.cs` with:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArctZ.ViewModels;

public partial class ConnectionViewModel : ViewModelBase
{
    private readonly IDeviceTransport _realTransport;
    private readonly Func<IDeviceTransport> _createDemoTransport;
    private readonly IDeviceSessionFactory _sessionFactory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnectionModalVisible))]
    private IDeviceSession? _session;

    // Mirrors Session.ConnectionState. IDeviceSession does not implement
    // INotifyPropertyChanged, so a direct "Session.ConnectionState" binding
    // only ever reads the value once (when Session itself changes) and never
    // updates when the same session's state transitions later. This property
    // is kept current via ConnectionStateChanged (see OnSessionChanged below)
    // so bindings on THIS view model update live.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnectionModalVisible))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private ConnectionEndpoint? _selectedEndpoint;

    public bool IsConnectionModalVisible => Session is null || ConnectionState != ConnectionState.Connected;

    public ObservableCollection<ConnectionEndpoint> AvailableEndpoints { get; } = new()
    {
        new ConnectionEndpoint("real", "Устройство", ConnectionEndpointKind.RealDevice),
        new ConnectionEndpoint("demo", "Демо", ConnectionEndpointKind.Demo),
    };

    public ConnectionViewModel(
        IDeviceTransport realTransport,
        Func<IDeviceTransport> createDemoTransport,
        IDeviceSessionFactory sessionFactory)
    {
        _realTransport = realTransport;
        _createDemoTransport = createDemoTransport;
        _sessionFactory = sessionFactory;
        SelectedEndpoint = AvailableEndpoints[0];
    }

    partial void OnSessionChanged(IDeviceSession? oldValue, IDeviceSession? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.ConnectionStateChanged -= OnSessionConnectionStateChanged;
        }

        if (newValue is not null)
        {
            newValue.ConnectionStateChanged += OnSessionConnectionStateChanged;
        }

        ConnectionState = newValue?.ConnectionState ?? ConnectionState.Disconnected;
    }

    private void OnSessionConnectionStateChanged()
    {
        ConnectionState = Session?.ConnectionState ?? ConnectionState.Disconnected;
    }

    private bool CanConnect() =>
        SelectedEndpoint is not null &&
        ConnectionState is not (ConnectionState.Connecting or ConnectionState.Reconnecting);

    [RelayCommand(CanExecute = nameof(CanConnect))]
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

        Session = _sessionFactory.Create(transport);
        await Session.ConnectAsync(SelectedEndpoint.Id);
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (Session is not null)
        {
            await Session.DisconnectAsync();
            Session = null;
        }
    }

    [RelayCommand]
    private Task HomeAsync() => Session?.HomeAsync() ?? Task.CompletedTask;

    [RelayCommand]
    private Task ResetAlarmAsync() => Session?.ResetAlarmAsync() ?? Task.CompletedTask;
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build ArctZ/ArctZ.csproj`
Expected: `Build succeeded.` — this exercises the CommunityToolkit.Mvvm source generator against the new attributes (`NotifyPropertyChangedFor`, `NotifyCanExecuteChangedFor`, `RelayCommand(CanExecute = ...)`, the two-parameter `OnSessionChanged` partial hook). A generator mismatch (e.g. wrong partial method signature) shows up here as a compile error, not a runtime one.

- [ ] **Step 3: Commit**

```bash
git add ArctZ/ViewModels/ConnectionViewModel.cs
git commit -m "feat: mirror live ConnectionState and add IsConnectionModalVisible to ConnectionViewModel"
```

---

## Task 2: `ConnectionView.axaml` — status-only header

**Files:**
- Modify: `ArctZ/Views/ConnectionView.axaml` (full file, currently 33 lines)

**Interfaces:**
- Consumes: `ConnectionViewModel.ConnectionState` and `ConnectionViewModel.HomeCommand`/`ResetAlarmCommand`/`DisconnectCommand` (all produced by Task 1 / already existing). `Converters.ConnectionStateToLabelConverter`, `Converters.ConnectionStateToBrushConverter` (existing, `ArctZ/Converters/ConnectionStateConverters.cs`).
- Produces: nothing new consumed elsewhere — this view is only ever hosted via `ViewLocator` from `ProgramViewModel.Connection` in `MainView.axaml`'s header (`Views/MainView.axaml:74`).

- [ ] **Step 1: Replace the file contents**

Replace the entire contents of `ArctZ/Views/ConnectionView.axaml` with:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:ArctZ.ViewModels"
             xmlns:conv="using:ArctZ.Converters"
             x:Class="ArctZ.Views.ConnectionView"
             x:DataType="vm:ConnectionViewModel">

    <UserControl.Resources>
        <conv:ConnectionStateToLabelConverter x:Key="StateToLabel" />
        <conv:ConnectionStateToBrushConverter x:Key="StateToBrush" />
    </UserControl.Resources>

    <StackPanel Orientation="Horizontal" Spacing="10" VerticalAlignment="Center">
        <Border Background="{StaticResource HudPanelElevatedBrush}"
                BorderBrush="{StaticResource HudBorderStrongBrush}" BorderThickness="1"
                Padding="10,6">
            <StackPanel Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
                <Ellipse Width="8" Height="8" VerticalAlignment="Center"
                         Fill="{Binding ConnectionState, Converter={StaticResource StateToBrush}}" />
                <TextBlock VerticalAlignment="Center"
                           Text="{Binding ConnectionState, Converter={StaticResource StateToLabel}}" />
            </StackPanel>
        </Border>

        <Button Content="Homing" Command="{Binding HomeCommand}" />
        <Button Classes="danger" Content="Сброс аварии" Command="{Binding ResetAlarmCommand}" />
        <Button Content="Отключить" Command="{Binding DisconnectCommand}" />
    </StackPanel>
</UserControl>
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build ArctZ/ArctZ.csproj`
Expected: `Build succeeded.` — compiled-binding validation catches a typo'd property/command name (e.g. `ConnectionState`, `HomeCommand`) as a build error, since `x:DataType="vm:ConnectionViewModel"` is set.

- [ ] **Step 3: Commit**

```bash
git add ArctZ/Views/ConnectionView.axaml
git commit -m "feat: reduce header connection panel to status + Homing + Reset Alarm + Disconnect"
```

---

## Task 3: `MainView.axaml` — full-screen connection modal overlay

**Files:**
- Modify: `ArctZ/Views/MainView.axaml:1-12` (root `UserControl` opening tag/attributes), `ArctZ/Views/MainView.axaml:67` (`<DockPanel>` open tag), `ArctZ/Views/MainView.axaml:257-258` (`</DockPanel>` close tag + `</UserControl>`)

**Interfaces:**
- Consumes: `ProgramViewModel.Connection` (existing, type `ConnectionViewModel`), `ConnectionViewModel.IsConnectionModalVisible`, `ConnectionState`, `AvailableEndpoints`, `SelectedEndpoint`, `ConnectCommand` (all produced by Task 1). `Converters.ConnectionStateToLabelConverter`/`ConnectionStateToBrushConverter` (existing).
- Produces: nothing new consumed by other files — this is the outermost view.

- [ ] **Step 1: Add the `conv` namespace to the root `UserControl` tag**

In `ArctZ/Views/MainView.axaml`, the root tag currently reads (lines 1–12):

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:vm="using:ArctZ.ViewModels"
             xmlns:js="using:ArctZ.Components.VirtualJoystick"
             xmlns:program="using:ArctZ.Services.Program"
             mc:Ignorable="d" d:DesignWidth="1000" d:DesignHeight="600"
             x:Class="ArctZ.Views.MainView"
             x:DataType="vm:ProgramViewModel"
             Background="{StaticResource HudBackgroundBrush}"
             Foreground="{StaticResource HudTextPrimaryBrush}">
```

Add `xmlns:conv="using:ArctZ.Converters"` after the `xmlns:program` line, so it reads:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:vm="using:ArctZ.ViewModels"
             xmlns:js="using:ArctZ.Components.VirtualJoystick"
             xmlns:program="using:ArctZ.Services.Program"
             xmlns:conv="using:ArctZ.Converters"
             mc:Ignorable="d" d:DesignWidth="1000" d:DesignHeight="600"
             x:Class="ArctZ.Views.MainView"
             x:DataType="vm:ProgramViewModel"
             Background="{StaticResource HudBackgroundBrush}"
             Foreground="{StaticResource HudTextPrimaryBrush}">

    <UserControl.Resources>
        <conv:ConnectionStateToLabelConverter x:Key="StateToLabel" />
        <conv:ConnectionStateToBrushConverter x:Key="StateToBrush" />
    </UserControl.Resources>
```

(The existing `<UserControl.Styles>` block stays immediately after — `Resources` and `Styles` are both direct children of `UserControl` and can coexist in either order.)

- [ ] **Step 2: Wrap the root `DockPanel` in a `Grid` and add the modal overlay**

The file currently ends with (the `RootPanel` grid closes, then `DockPanel`, then `UserControl`):

```xml
        </Grid>
    </DockPanel>
</UserControl>
```

Replace the opening `    <DockPanel>` (the line right after `</UserControl.Styles>`) with:

```xml
    <Grid>
        <DockPanel>
```

and replace the closing sequence above with:

```xml
        </Grid>
        </DockPanel>

        <Border IsVisible="{Binding Connection.IsConnectionModalVisible}" Background="#CC0A0E12">
            <Border x:DataType="vm:ConnectionViewModel" DataContext="{Binding Connection}"
                    Width="360" Background="{StaticResource HudPanelElevatedBrush}"
                    BorderBrush="{StaticResource HudBorderStrongBrush}" BorderThickness="1"
                    Padding="20" HorizontalAlignment="Center" VerticalAlignment="Center">
                <StackPanel Spacing="14">
                    <TextBlock Classes="section-heading" Text="ПОДКЛЮЧЕНИЕ" />
                    <StackPanel Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
                        <Ellipse Width="8" Height="8" VerticalAlignment="Center"
                                 Fill="{Binding ConnectionState, Converter={StaticResource StateToBrush}}" />
                        <TextBlock VerticalAlignment="Center"
                                   Text="{Binding ConnectionState, Converter={StaticResource StateToLabel}}" />
                    </StackPanel>
                    <ComboBox ItemsSource="{Binding AvailableEndpoints}"
                              SelectedItem="{Binding SelectedEndpoint}"
                              DisplayMemberBinding="{Binding DisplayName}"
                              HorizontalAlignment="Stretch" />
                    <Button Classes="primary" Content="Подключить" Command="{Binding ConnectCommand}"
                            HorizontalAlignment="Stretch" />
                </StackPanel>
            </Border>
        </Border>
    </Grid>
</UserControl>
```

Note the indentation shift: everything that used to be a direct child of `<DockPanel>` (the header `Border`, the library `Border`, and `<Grid x:Name="RootPanel">`) is now one level deeper because of the new wrapping `<Grid>`. Re-indenting those lines is cosmetic (XAML doesn't care), but do it for readability — every line between the old `<DockPanel>` and `</DockPanel>` shifts right by 4 spaces.

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build ArctZ/ArctZ.csproj`
Expected: `Build succeeded.` — this validates the two-hop compiled binding `Connection.IsConnectionModalVisible` (outer `x:DataType="vm:ProgramViewModel"`) and the nested-DataContext block (`x:DataType="vm:ConnectionViewModel"`, mirroring the existing `IsEditingKeyPoint`/`PendingConfirmation` overlay pattern already in this file at lines 214–255).

- [ ] **Step 4: Commit**

```bash
git add ArctZ/Views/MainView.axaml
git commit -m "feat: add full-screen connection modal overlay to MainView"
```

---

## Task 4: Manual verification pass

**Files:** none (no code changes — this task only runs the app)

**Interfaces:** N/A

- [ ] **Step 1: Launch the Desktop head**

Run: `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj`

- [ ] **Step 2: Verify the startup modal**

Expected: on launch, the connection modal is visible, centered, covering the entire window (header and library panel behind it are dimmed/inaccessible). Header behind the overlay shows "Не подключено" status with Homing/Reset Alarm/Disconnect buttons (visually present but covered by the overlay — clicking through should have no effect since the overlay Border sits on top and is hit-test visible via its `Background`).

- [ ] **Step 3: Verify connecting via the Демо endpoint**

In the modal, leave "Демо" selected (default) and click "Подключить". Expected: status briefly reads "Подключение…", then the modal disappears once state reaches "Подключено", revealing the full main screen (header now shows only status/Homing/Reset Alarm/Disconnect — no combobox or Connect button in the header).

- [ ] **Step 4: Verify Disconnect reopens the modal**

Click "Отключить" in the header. Expected: modal reappears immediately over the full screen, header status reads "Не подключено" (visible behind the overlay), endpoint combobox in the modal defaults back to "Демо".

- [ ] **Step 5: Verify reconnect attempt while Connect is disabled**

In the modal, click "Подключить" and — while status still reads "Подключение…" — try clicking "Подключить" again. Expected: the button is disabled (no double-connect) until the state resolves to "Подключено" or back to "Не подключено".

- [ ] **Step 6: Close the app**

No commit for this task (verification only). If any step fails, stop and fix the relevant task before proceeding — do not commit further work on top of a failing verification pass.
