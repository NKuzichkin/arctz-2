# Панель лога отправленных G-code команд — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Показать в UI живой список всех G-code/`$`-строк, отправленных на устройство (реальное или Демо), с автопрокруткой, ограниченный последними 200 записями и сбрасываемый при каждом новом подключении.

**Architecture:** Новый декоратор `LoggingDeviceTransport` оборачивает выбранный транспорт внутри `ConnectionViewModel.ConnectAsync` и поднимает событие `LineSent` на каждую отправленную строку. `ConnectionViewModel` подписывается на это событие через `Observable.FromEvent(...).ObserveOn(RxSchedulers.MainThreadScheduler)` (тот же паттерн, что уже используется для `Session.ConnectionStateChanged`) и копит строки в `ObservableCollection<string> SentGCodeLines`. `MainView.axaml` добавляет кнопку в шапке и оверлей-панель со списком; код-behind автоматически прокручивает `ListBox` к последней строке.

**Tech Stack:** Avalonia UI (.NET 10), ReactiveUI (`ReactiveViewModelBase`, `[Reactive]`, `ReactiveCommand`), xUnit.

## Global Constraints

- Лог логирует только текстовые строки, идущие через `IDeviceTransport.SendLineAsync` (джог включительно) — realtime-байты (`SendRawByteAsync`) не логируются.
- Лимит: 200 записей, хронологический порядок (старые сверху, новые снизу); при превышении удаляется элемент с индексом 0.
- Лог сбрасывается (`Clear()`) в начале каждого `ConnectAsync`, до создания новой сессии.
- Кнопка открытия лога в шапке видна всегда — не зависит от типа endpoint'а (Демо/реальное устройство).
- Маршалинг на UI-поток — через `Observable.FromEvent(...).ObserveOn(RxSchedulers.MainThreadScheduler)`, тот же механизм, что уже используется в `ConnectionViewModel` для `Session.ConnectionStateChanged`. Не использовать `Avalonia.Threading.Dispatcher.UIThread` напрямую.
- Спецификация: `docs/superpowers/specs/2026-07-31-gcode-sent-log-panel-design.md`.

---

## Task 1: `LoggingDeviceTransport` — декоратор транспорта

**Files:**
- Create: `ArctZ/Services/Device/LoggingDeviceTransport.cs`
- Test: `ArctZ.Tests/Services/Device/LoggingDeviceTransportTests.cs`

**Interfaces:**
- Consumes: `ArctZ.Services.Device.IDeviceTransport` (существующий интерфейс: `bool IsConnected`, `event Action<string>? LineReceived`, `event Action? Disconnected`, `Task ConnectAsync(string, CancellationToken)`, `Task DisconnectAsync()`, `Task SendLineAsync(string, CancellationToken)`, `Task SendRawByteAsync(byte, CancellationToken)`); `ArctZ.Tests.Services.Device.FakeDeviceTransport` (тестовая реализация с `SentLines`/`SentRawBytes`/`SimulateReceivedLine`/`SimulateDisconnect`).
- Produces: `public sealed class LoggingDeviceTransport : IDeviceTransport` с конструктором `LoggingDeviceTransport(IDeviceTransport inner)` и `event Action<string>? LineSent` — используется в Task 2.

- [ ] **Step 1: Написать падающий тест — `LineSent` поднимается и строка форвардится дальше**

Создать `ArctZ.Tests/Services/Device/LoggingDeviceTransportTests.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class LoggingDeviceTransportTests
{
    [Fact]
    public async Task SendLineAsync_RaisesLineSentAndForwardsToInner()
    {
        var inner = new FakeDeviceTransport();
        var transport = new LoggingDeviceTransport(inner);
        var raised = new List<string>();
        transport.LineSent += raised.Add;

        await transport.SendLineAsync("G1 X10 Y20 F500");

        Assert.Equal(new[] { "G1 X10 Y20 F500" }, raised);
        Assert.Equal(new[] { "G1 X10 Y20 F500" }, inner.SentLines);
    }

    [Fact]
    public async Task SendRawByteAsync_ForwardsToInnerAndDoesNotRaiseLineSent()
    {
        var inner = new FakeDeviceTransport();
        var transport = new LoggingDeviceTransport(inner);
        var raised = new List<string>();
        transport.LineSent += raised.Add;

        await transport.SendRawByteAsync((byte)'?');

        Assert.Empty(raised);
        Assert.Equal(new byte[] { (byte)'?' }, inner.SentRawBytes);
    }

    [Fact]
    public async Task ConnectAsyncAndDisconnectAsync_ForwardToInner()
    {
        var inner = new FakeDeviceTransport();
        var transport = new LoggingDeviceTransport(inner);

        await transport.ConnectAsync("device-1");
        Assert.True(inner.IsConnected);
        Assert.True(transport.IsConnected);

        await transport.DisconnectAsync();
        Assert.False(inner.IsConnected);
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public void LineReceivedAndDisconnected_ForwardFromInner()
    {
        var inner = new FakeDeviceTransport();
        var transport = new LoggingDeviceTransport(inner);
        string? receivedLine = null;
        var disconnectedRaised = false;
        transport.LineReceived += line => receivedLine = line;
        transport.Disconnected += () => disconnectedRaised = true;

        inner.SimulateReceivedLine("ok");
        inner.SimulateDisconnect();

        Assert.Equal("ok", receivedLine);
        Assert.True(disconnectedRaised);
    }
}
```

- [ ] **Step 2: Запустить тесты и убедиться, что они падают**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter LoggingDeviceTransportTests`
Expected: FAIL (компиляция падает — `LoggingDeviceTransport` не существует)

- [ ] **Step 3: Реализовать `LoggingDeviceTransport`**

Создать `ArctZ/Services/Device/LoggingDeviceTransport.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

/// <summary>
/// Decorates an IDeviceTransport to expose every G-code/$-command line sent
/// to the device, for a demo-mode diagnostic log. Realtime bytes
/// (SendRawByteAsync: '?', '!', '~', jog-cancel) are not text G-code lines
/// and are intentionally not raised as LineSent.
/// </summary>
public sealed class LoggingDeviceTransport : IDeviceTransport
{
    private readonly IDeviceTransport _inner;

    public LoggingDeviceTransport(IDeviceTransport inner)
    {
        _inner = inner;
    }

    /// <summary>Raised synchronously on the caller's thread for every line passed to SendLineAsync.</summary>
    public event Action<string>? LineSent;

    public bool IsConnected => _inner.IsConnected;

    public event Action<string>? LineReceived
    {
        add => _inner.LineReceived += value;
        remove => _inner.LineReceived -= value;
    }

    public event Action? Disconnected
    {
        add => _inner.Disconnected += value;
        remove => _inner.Disconnected -= value;
    }

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default) =>
        _inner.ConnectAsync(deviceId, cancellationToken);

    public Task DisconnectAsync() => _inner.DisconnectAsync();

    public Task SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        LineSent?.Invoke(line);
        return _inner.SendLineAsync(line, cancellationToken);
    }

    public Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default) =>
        _inner.SendRawByteAsync(value, cancellationToken);
}
```

- [ ] **Step 4: Запустить тесты и убедиться, что они проходят**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter LoggingDeviceTransportTests`
Expected: PASS (4 теста)

- [ ] **Step 5: Закоммитить**

```bash
git add ArctZ/Services/Device/LoggingDeviceTransport.cs ArctZ.Tests/Services/Device/LoggingDeviceTransportTests.cs
git commit -m "feat: add LoggingDeviceTransport decorator for sent G-code lines"
```

---

## Task 2: `ConnectionViewModel` — накопление и сброс лога, команда открытия

**Files:**
- Modify: `ArctZ/ViewModels/ConnectionViewModel.cs`
- Test: `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs`

**Interfaces:**
- Consumes: `LoggingDeviceTransport` (Task 1) — `LineSent` событие, конструктор `LoggingDeviceTransport(IDeviceTransport inner)`.
- Produces:
  - `public ObservableCollection<string> SentGCodeLines { get; }` — читается из `MainView.axaml` в Task 3 (`Connection.SentGCodeLines`).
  - `public bool IsGCodeLogOpen { get; set; }` (via `[Reactive]`) — читается/переключается из `MainView.axaml` в Task 3 (`Connection.IsGCodeLogOpen`).
  - `public IEnhancedCommand<Unit> ToggleGCodeLogCommand { get; }` — биндится в `MainView.axaml` в Task 3 (`Connection.ToggleGCodeLogCommand`).

Текущее содержимое `ArctZ/ViewModels/ConnectionViewModel.cs` (для ориентира, до правок):

```csharp
namespace ArctZ.ViewModels;

public partial class ConnectionViewModel : ReactiveViewModelBase
{
    private readonly IDeviceTransport _realTransport;
    private readonly Func<IDeviceTransport> _createDemoTransport;
    private readonly IDeviceSessionFactory _sessionFactory;

    [Reactive] private IDeviceSession? session;
    [Reactive] private ConnectionState connectionState = ConnectionState.Disconnected;
    [Reactive] private ConnectionEndpoint? selectedEndpoint;

    public bool IsConnectionModalVisible => Session is null || ConnectionState != ConnectionState.Connected;
    public string ConnectionStateLabel => ConnectionState switch { /* ... */ };

    public ObservableCollection<ConnectionEndpoint> AvailableEndpoints { get; } = new() { /* ... */ };

    public IEnhancedCommand<Unit> ConnectCommand { get; }
    public IEnhancedCommand<Unit> DisconnectCommand { get; }
    public IEnhancedCommand<Unit> HomeCommand { get; }
    public IEnhancedCommand<Unit> ResetAlarmCommand { get; }

    public ConnectionViewModel(/* ... */)
    {
        // ... existing wiring (canConnect, ConnectCommand..ResetAlarmCommand, Session subscription with .Switch(), IsConnectionModalVisible/ConnectionStateLabel re-raise)
    }

    private async Task ConnectAsync()
    {
        if (SelectedEndpoint is null) return;

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
            await session.DisconnectAsync();
            Session = null;
        }
    }

    private async Task DisconnectAsync() { /* ... */ }
    private Task HomeAsync() => Session?.HomeAsync() ?? Task.CompletedTask;
    private Task ResetAlarmAsync() => Session?.ResetAlarmAsync() ?? Task.CompletedTask;
}
```

- [ ] **Step 1: Написать падающие тесты**

Добавить в конец класса `ConnectionViewModelTests` (файл `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs`), перед закрывающей `}` класса:

```csharp
    [Fact]
    public async Task SendGCode_AfterConnect_AppendsLineToSentGCodeLines()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = CreateVm(realTransport);
        await vm.ConnectCommand.Execute();

        _ = vm.Session!.SendGCodeAsync("G1 X10 Y20 F500");

        Assert.Equal(new[] { "G1 X10 Y20 F500" }, vm.SentGCodeLines);
    }

    [Fact]
    public async Task ConnectCommand_Reconnecting_ClearsPreviousSentGCodeLines()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = CreateVm(realTransport);
        await vm.ConnectCommand.Execute();
        _ = vm.Session!.SendGCodeAsync("G1 X10");
        Assert.Single(vm.SentGCodeLines);

        await vm.ConnectCommand.Execute();

        Assert.Empty(vm.SentGCodeLines);
    }

    [Fact]
    public async Task SendGCode_Over200Lines_DropsOldestNotNewest()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = CreateVm(realTransport);
        await vm.ConnectCommand.Execute();

        for (var i = 0; i < 205; i++)
        {
            _ = vm.Session!.SendGCodeAsync($"G1 X{i}");
            realTransport.SimulateReceivedLine("ok");
        }

        Assert.Equal(200, vm.SentGCodeLines.Count);
        Assert.Equal("G1 X5", vm.SentGCodeLines[0]);
        Assert.Equal("G1 X204", vm.SentGCodeLines[^1]);
    }

    [Fact]
    public void ToggleGCodeLogCommand_TogglesIsGCodeLogOpen()
    {
        var vm = CreateVm(new FakeDeviceTransport());
        Assert.False(vm.IsGCodeLogOpen);

        vm.ToggleGCodeLogCommand.Execute(null);
        Assert.True(vm.IsGCodeLogOpen);

        vm.ToggleGCodeLogCommand.Execute(null);
        Assert.False(vm.IsGCodeLogOpen);
    }
```

- [ ] **Step 2: Запустить тесты и убедиться, что они падают**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter ConnectionViewModelTests`
Expected: FAIL (компиляция падает — `SentGCodeLines`, `IsGCodeLogOpen`, `ToggleGCodeLogCommand` не существуют)

- [ ] **Step 3: Реализовать изменения в `ConnectionViewModel`**

В `ArctZ/ViewModels/ConnectionViewModel.cs`:

1. Добавить поле рядом с `_sessionFactory` (после строки `private readonly IDeviceSessionFactory _sessionFactory;`):

```csharp
    private IDisposable? _sentGCodeSubscription;
    private const int MaxSentGCodeLines = 200;
```

2. Добавить `[Reactive]`-поле рядом с `selectedEndpoint` (после `[Reactive] private ConnectionEndpoint? selectedEndpoint;`):

```csharp
    [Reactive] private bool isGCodeLogOpen;
```

3. Добавить публичное свойство коллекции рядом с `AvailableEndpoints` (после закрывающей `};` инициализатора `AvailableEndpoints`):

```csharp
    public ObservableCollection<string> SentGCodeLines { get; } = new();
```

4. Добавить свойство команды рядом с остальными команд-свойствами (после `public IEnhancedCommand<Unit> ResetAlarmCommand { get; }`):

```csharp
    public IEnhancedCommand<Unit> ToggleGCodeLogCommand { get; }
```

5. В конструкторе, сразу после блока создания `ResetAlarmCommand` (после строки `.Enhance(text: "Сброс аварии", name: "ResetAlarmCommand"));`), добавить:

```csharp
        ToggleGCodeLogCommand = Track(ReactiveCommand.Create(() => IsGCodeLogOpen = !IsGCodeLogOpen)
            .Enhance(text: "Лог G-code", name: "ToggleGCodeLogCommand"));
```

6. В `ConnectAsync`, заменить блок выбора транспорта и создания сессии:

```csharp
        var transport = SelectedEndpoint.Kind == ConnectionEndpointKind.Demo
            ? _createDemoTransport()
            : _realTransport;

        var session = _sessionFactory.Create(transport);
        Session = session;
```

на:

```csharp
        var innerTransport = SelectedEndpoint.Kind == ConnectionEndpointKind.Demo
            ? _createDemoTransport()
            : _realTransport;

        _sentGCodeSubscription?.Dispose();
        var loggingTransport = new LoggingDeviceTransport(innerTransport);
        SentGCodeLines.Clear();
        _sentGCodeSubscription = Observable.FromEvent<string>(
                h => loggingTransport.LineSent += h,
                h => loggingTransport.LineSent -= h)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(AppendSentGCodeLine);

        var session = _sessionFactory.Create(loggingTransport);
        Session = session;
```

7. Добавить приватный метод рядом с `ConnectAsync`/`DisconnectAsync` (после `SetConnectionState`-подобных приватных методов; проще всего — сразу после метода `DisconnectAsync`):

```csharp
    private void AppendSentGCodeLine(string line)
    {
        SentGCodeLines.Add(line);
        if (SentGCodeLines.Count > MaxSentGCodeLines)
        {
            SentGCodeLines.RemoveAt(0);
        }
    }
```

Никаких новых `using` не требуется: `System.Collections.ObjectModel` и `System.Reactive.Linq` уже импортированы в файле.

- [ ] **Step 4: Запустить тесты и убедиться, что они проходят**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "ConnectionViewModelTests|LoggingDeviceTransportTests"`
Expected: PASS (все тесты, включая ранее существовавшие в `ConnectionViewModelTests`)

- [ ] **Step 5: Прогнать полный набор тестов, чтобы убедиться, что ничего не сломалось**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS (все тесты проекта, включая `ProgramViewModelPlaybackTests`/`ProgramViewModelAuthoringTests`, которые создают `ConnectionViewModel` напрямую)

- [ ] **Step 6: Закоммитить**

```bash
git add ArctZ/ViewModels/ConnectionViewModel.cs ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs
git commit -m "feat: track sent G-code lines and log-panel toggle on ConnectionViewModel"
```

---

## Task 3: UI — кнопка в шапке, оверлей и автопрокрутка

**Files:**
- Modify: `ArctZ/Views/MainView.axaml`
- Modify: `ArctZ/Views/MainView.axaml.cs`

**Interfaces:**
- Consumes: `ConnectionViewModel.SentGCodeLines` (`ObservableCollection<string>`), `ConnectionViewModel.IsGCodeLogOpen` (`bool`), `ConnectionViewModel.ToggleGCodeLogCommand` (`IEnhancedCommand<Unit>`) — все из Task 2, доступны в `MainView` как `Connection.SentGCodeLines`/`Connection.IsGCodeLogOpen`/`Connection.ToggleGCodeLogCommand` (`ProgramViewModel.Connection` — существующее свойство).
- Produces: ничего, потребляемого другими задачами (последняя задача в плане).

Эта задача не имеет автоматических unit-тестов (XAML-разметка и Avalonia `ScrollIntoView` не покрыты существующей тестовой инфраструктурой — единственный прецедент, `MainViewNarrowJoystickRadiusTests`, тестирует чистую статическую функцию, а не рендеринг). Проверка — сборка + ручной прогон в Desktop-хосте, как предписывает `CLAUDE.md` для UI-изменений.

- [ ] **Step 1: Добавить кнопку в шапку**

В `ArctZ/Views/MainView.axaml`, внутри `<WrapPanel x:Name="PlaybackButtons" ...>`, после кнопки «Стоп» и её `Border`-обёртки с `PlaybackState` (то есть последним элементом `WrapPanel`), добавить:

```xml
                        <Button Content="Лог G-code" Command="{Binding Connection.ToggleGCodeLogCommand}" />
```

Итоговый `WrapPanel` должен выглядеть так:

```xml
                    <WrapPanel x:Name="PlaybackButtons" ItemSpacing="8" LineSpacing="8" VerticalAlignment="Center">
                        <Button Classes="primary" Content="Play" Command="{Binding PlayCommand}" />
                        <Button Content="Пауза" Command="{Binding PauseCommand}" />
                        <Button Classes="danger" Content="Стоп" Command="{Binding StopCommand}" />
                        <Border Background="{StaticResource HudPanelElevatedBrush}" BorderBrush="{StaticResource HudBorderStrongBrush}"
                                BorderThickness="1" Padding="10,6" Margin="8,0,0,0">
                            <TextBlock Classes="telemetry" FontSize="14" Text="{Binding PlaybackState}" />
                        </Border>
                        <Button Content="Лог G-code" Command="{Binding Connection.ToggleGCodeLogCommand}" />
                    </WrapPanel>
```

- [ ] **Step 2: Добавить оверлей со списком**

В `ArctZ/Views/MainView.axaml`, внутри `<Grid x:Name="RootPanel">`, после оверлея `IsLibraryOpen` (то есть после закрывающего `</Border>` блока `IsVisible="{Binding IsLibraryOpen}"` и перед закрывающим `</Grid>` самого `RootPanel`), добавить:

```xml
                <Border IsVisible="{Binding Connection.IsGCodeLogOpen}" Background="#CC0A0E12">
                    <Border Width="420" MaxHeight="480" Background="{StaticResource HudPanelElevatedBrush}"
                            BorderBrush="{StaticResource HudBorderStrongBrush}" BorderThickness="1"
                            Padding="20" HorizontalAlignment="Center" VerticalAlignment="Center">
                        <DockPanel>
                            <Grid DockPanel.Dock="Top" ColumnDefinitions="*,Auto" Margin="0,0,0,10">
                                <TextBlock Grid.Column="0" Classes="section-heading" Text="ЛОГ G-CODE" VerticalAlignment="Center" />
                                <Button Grid.Column="1" Content="✕" Padding="8,2" Command="{Binding Connection.ToggleGCodeLogCommand}" />
                            </Grid>
                            <ListBox x:Name="GCodeLogList" ItemsSource="{Binding Connection.SentGCodeLines}">
                                <ListBox.Styles>
                                    <Style Selector="ListBoxItem">
                                        <Setter Property="FontFamily" Value="{StaticResource HudFontMono}" />
                                        <Setter Property="FontSize" Value="13" />
                                        <Setter Property="Padding" Value="0,2" />
                                    </Style>
                                </ListBox.Styles>
                            </ListBox>
                        </DockPanel>
                    </Border>
                </Border>
```

Порядок среди оверлеев (Z-order) не важен для функциональности — они не пересекаются одновременно в обычном использовании, но для консистентности разместить последним, сразу перед закрывающим `</Grid>` тега `RootPanel`.

- [ ] **Step 3: Собрать проект и убедиться, что XAML компилируется**

Run: `dotnet build ArctZ/ArctZ.csproj`
Expected: Build succeeded, без ошибок компиляции биндингов (`x:DataType="vm:ProgramViewModel"` на корневом `UserControl` уже покрывает `Connection.SentGCodeLines`/`Connection.IsGCodeLogOpen`/`Connection.ToggleGCodeLogCommand` — `Connection` это `ConnectionViewModel`, публичное свойство `ProgramViewModel.Connection`).

- [ ] **Step 4: Добавить автопрокрутку в код-behind**

В `ArctZ/Views/MainView.axaml.cs`:

1. Добавить `using`-директивы в начало файла:

```csharp
using System.Collections.ObjectModel;
using System.Collections.Specialized;
```

2. Изменить конструктор:

```csharp
        public MainView()
        {
            InitializeComponent();
            SizeChanged += OnSizeChanged;
            DataContextChanged += OnDataContextChanged;
        }
```

3. Добавить методы (после `private ProgramViewModel? ViewModel => DataContext as ProgramViewModel;`):

```csharp
        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (DataContext is ProgramViewModel vm)
            {
                vm.Connection.SentGCodeLines.CollectionChanged -= OnSentGCodeLinesChanged;
                vm.Connection.SentGCodeLines.CollectionChanged += OnSentGCodeLinesChanged;
            }
        }

        private void OnSentGCodeLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add &&
                sender is ObservableCollection<string> { Count: > 0 } lines)
            {
                GCodeLogList.ScrollIntoView(lines[^1]);
            }
        }
```

Полный файл после правок:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ArctZ.Components.VirtualJoystick;
using ArctZ.Services.Program;
using ArctZ.ViewModels;
using Avalonia.Controls;

namespace ArctZ.Views
{
    public partial class MainView : UserControl
    {
        private const double NarrowLayoutBreakpoint = 700;

        private const double ContentGridChromeWidth = 54;
        private const double NarrowJoystickMinRadius = 50;
        private const double NarrowJoystickEdgeMargin = 12;

        private const double MainViewChromeHeight = 166;
        private const double NarrowProgramPanelMinHeight = 160;

        public MainView()
        {
            InitializeComponent();
            SizeChanged += OnSizeChanged;
            DataContextChanged += OnDataContextChanged;
        }

        private ProgramViewModel? ViewModel => DataContext as ProgramViewModel;

        private bool? _isNarrow;

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (DataContext is ProgramViewModel vm)
            {
                vm.Connection.SentGCodeLines.CollectionChanged -= OnSentGCodeLinesChanged;
                vm.Connection.SentGCodeLines.CollectionChanged += OnSentGCodeLinesChanged;
            }
        }

        private void OnSentGCodeLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add &&
                sender is ObservableCollection<string> { Count: > 0 } lines)
            {
                GCodeLogList.ScrollIntoView(lines[^1]);
            }
        }

        private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            var isNarrow = e.NewSize.Width < NarrowLayoutBreakpoint;
            if (_isNarrow != isNarrow)
            {
                _isNarrow = isNarrow;

                HeaderGrid.Classes.Set("narrow", isNarrow);
                ContentGrid.Classes.Set("narrow", isNarrow);

                HeaderGrid.RowDefinitions = new RowDefinitions(isNarrow ? "Auto,Auto" : "");
                ContentGrid.ColumnDefinitions = new ColumnDefinitions(isNarrow ? "*,*" : "Auto,*,Auto");
                ContentGrid.RowDefinitions = new RowDefinitions(isNarrow ? "*,Auto" : "");

                if (!isNarrow)
                {
                    LeftJoystick.ClearValue(VirtualJoystick.RadiusProperty);
                    RightJoystick.ClearValue(VirtualJoystick.RadiusProperty);
                }
            }

            if (isNarrow)
            {
                var radius = ComputeNarrowJoystickRadius(e.NewSize.Width, e.NewSize.Height);
                LeftJoystick.Radius = radius;
                RightJoystick.Radius = radius;
            }
        }

        internal static double ComputeNarrowJoystickRadius(double mainViewWidth, double mainViewHeight)
        {
            var contentGridWidth = mainViewWidth - ContentGridChromeWidth;
            var columnWidth = contentGridWidth / 2;
            var widthRadius = Math.Max(NarrowJoystickMinRadius, columnWidth / 2 - NarrowJoystickEdgeMargin);

            var contentGridHeight = mainViewHeight - MainViewChromeHeight;
            var joystickRowBudget = contentGridHeight - NarrowProgramPanelMinHeight;
            var heightRadius = Math.Max(NarrowJoystickMinRadius, joystickRowBudget / 2);

            return Math.Min(widthRadius, heightRadius);
        }

        private void OnLeftJoystickDown(object? sender, JoystickEventArgs e) => ViewModel?.OnLeftJoystickDown(e);

        private void OnLeftJoystickMove(object? sender, JoystickEventArgs e) => ViewModel?.OnLeftJoystickMove(e);

        private void OnLeftJoystickUp(object? sender, JoystickEventArgs e) => ViewModel?.OnLeftJoystickUp(e);

        private void OnRightJoystickDown(object? sender, JoystickEventArgs e) => ViewModel?.OnRightJoystickDown(e);

        private void OnRightJoystickMove(object? sender, JoystickEventArgs e) => ViewModel?.OnRightJoystickMove(e);

        private void OnRightJoystickUp(object? sender, JoystickEventArgs e) => ViewModel?.OnRightJoystickUp(e);

        private async void OnLibrarySelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ViewModel is { } vm && sender is ListBox { SelectedItem: ProgramLibraryItem summary })
            {
                await vm.LoadProgramCommand.ExecuteAsync(summary);
            }
        }
    }
}
```

- [ ] **Step 5: Собрать проект**

Run: `dotnet build ArctZ/ArctZ.csproj`
Expected: Build succeeded

- [ ] **Step 6: Ручная проверка в Desktop-хосте**

Run: `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj`

Проверить вручную:
1. При старте видна модалка подключения — выбрать endpoint «Демо», нажать «Подключить».
2. После подключения в шапке видна кнопка «Лог G-code» — нажать её, открывается оверлей с пустым списком.
3. Подвигать любой джойстик — в списке появляются строки `$J=...`, список автоматически прокручивается вниз по мере поступления новых строк.
4. Захватить точку и нажать Play — в списке появляются координатные `G1 ...` команды точек программы.
5. Нажать «✕» в оверлее — оверлей закрывается; повторное нажатие кнопки «Лог G-code» открывает его снова с теми же строками (лог не сброшен).
6. Отключиться и подключиться заново («Демо» или «Устройство») — открыть лог снова, убедиться, что он пуст (сброшен новым подключением).

- [ ] **Step 7: Закоммитить**

```bash
git add ArctZ/Views/MainView.axaml ArctZ/Views/MainView.axaml.cs
git commit -m "feat: show sent G-code log panel with auto-scroll in MainView"
```

---

## Self-Review

**Spec coverage:**
- `LoggingDeviceTransport` (декоратор, `LineSent`, проксирование остального) — Task 1. ✅
- Джог-команды логируются наравне с явными (оба идут через `SendLineAsync`, декоратор не различает источник) — Task 1 + Task 2 Step 6 (декоратор оборачивает транспорт, который передаётся и в `BufferAwareCommandQueue`, и в `JogScheduler` внутри `DeviceSessionFactory.Create`). ✅
- Realtime-байты не логируются — Task 1 Step 1 тест `SendRawByteAsync_ForwardsToInnerAndDoesNotRaiseLineSent`. ✅
- `SentGCodeLines`, лимит 200, хронологический порядок, обрезка с начала — Task 2. ✅
- Сброс лога при каждом `ConnectAsync` — Task 2 Step 3.6, тест `ConnectCommand_Reconnecting_ClearsPreviousSentGCodeLines`. ✅
- `IsGCodeLogOpen`/`ToggleGCodeLogCommand`, кнопка видна всегда — Task 2 + Task 3 Step 1. ✅
- Оверлей, моноширинный шрифт — Task 3 Step 2. ✅
- Автопрокрутка к последней строке — Task 3 Step 4. ✅
- Тесты `LoggingDeviceTransportTests` и новые кейсы `ConnectionViewModelTests` — Task 1 Step 1, Task 2 Step 1. ✅
- Не в скоупе (персистентность, фильтрация, входящие строки) — сознательно не реализовано ни в одной задаче. ✅

**Placeholder scan:** нет `TBD`/`TODO`/«добавить обработку ошибок» — все шаги содержат полный код.

**Type consistency:** `LoggingDeviceTransport(IDeviceTransport inner)` (Task 1) используется одинаково в Task 2 Step 3.6 (`new LoggingDeviceTransport(innerTransport)`). `SentGCodeLines` (`ObservableCollection<string>`), `IsGCodeLogOpen` (`bool`), `ToggleGCodeLogCommand` (`IEnhancedCommand<Unit>`) — имена и типы совпадают между Task 2 (объявление) и Task 3 (использование в XAML/код-behind). `GCodeLogList` — имя `x:Name` в XAML (Task 3 Step 2) совпадает с использованием в код-behind (Task 3 Step 4).
