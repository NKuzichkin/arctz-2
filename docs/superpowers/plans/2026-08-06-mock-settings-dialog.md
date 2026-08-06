# Настройки мока для демо-режима — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Добавить пункт «Настройки мока» в боковое меню, открывающий диалог с кнопками принудительной отправки сигнала ошибки/аварии от `MockDeviceTransport` и слайдером дополнительной задержки ответа на команду.

**Architecture:** Новый узкий интерфейс `IMockDeviceControl` (`ForceNextCommandError`, `TriggerAlarm`, `SetResponseDelay`), который реализует `MockDeviceTransport`; `ConnectionViewModel` хранит приведённую к этому интерфейсу ссылку на текущий демо-транспорт и три новых команды/свойства (по образцу уже существующей панели «Лог G-code»); `ProgramViewModel` получает тонкую команду открытия диалога; `MainView.axaml` получает пункт бокового меню и модальный оверлей.

**Tech Stack:** Avalonia 12 (XAML), ReactiveUI (`ConnectionViewModel`), CommunityToolkit.Mvvm (`ProgramViewModel`), xUnit (`ArctZ.Tests`).

## Global Constraints

- Код ошибки фиксирован: `9` (спека: `docs/superpowers/specs/2026-08-06-mock-settings-dialog-design.md`).
- Код аварии фиксирован: `1`.
- Слайдер задержки: `Minimum="0" Maximum="5000"`, шаг `250` мс (`TickFrequency="250" IsSnapToTickEnabled="True"`).
- Кнопки триггеров и слайдер задержки доступны всегда (не блокируются вне демо-подключения); вне живого демо-транспорта — тихий no-op, без исключений и без визуальной обратной связи.
- Пункт «Настройки мока» в боковом меню виден всегда, независимо от текущего/выбранного эндпоинта подключения.
- `IMockDeviceControl` — единственный канал, через который `ConnectionViewModel` трогает демо-специфичные возможности транспорта; никаких кастов к конкретному классу `MockDeviceTransport` за пределами его собственного файла.
- UI-изменения проверяются только по стандартному воркфлоу проекта (build → run → пользователь тестирует → `AskUserQuestion` по каждому пункту) — см. `CLAUDE.md`, раздел «Тестирование UI».

---

### Task 1: `IMockDeviceControl` и новые возможности `MockDeviceTransport`

**Files:**
- Create: `ArctZ/Services/Device/IMockDeviceControl.cs`
- Modify: `ArctZ/Services/Device/Simulation/MockDeviceTransport.cs`
- Test: `ArctZ.Tests/Services/Device/MockDeviceTransportTests.cs`

**Interfaces:**
- Produces: `ArctZ.Services.Device.IMockDeviceControl` — интерфейс с `void ForceNextCommandError(int code)`, `void TriggerAlarm(int code)`, `void SetResponseDelay(TimeSpan delay)`. `MockDeviceTransport` реализует его (`ForceNextCommandError` уже существовал и не меняется); `TriggerAlarm` и `SetResponseDelay` — новые публичные методы.

- [ ] **Step 1: Write the failing tests**

Открыть `ArctZ.Tests/Services/Device/MockDeviceTransportTests.cs` и добавить три новых `[Fact]` в конец класса (перед закрывающей `}` файла, после существующего `SendLineAsync_Dwell_BlocksMotionWithoutMovingUntilElapsed`):

```csharp
[Fact]
public async Task TriggerAlarm_SetsAlarmStateAndRaisesAlarmLineWithCode()
{
    await _mock.ConnectAsync("demo");
    string? raisedLine = null;
    _mock.LineReceived += line => raisedLine ??= line;

    _mock.TriggerAlarm(1);

    Assert.Equal("ALARM:1", raisedLine);
    var status = QueryStatus();
    Assert.Equal(MachineState.Alarm, status.State);
}

[Fact]
public async Task TriggerAlarm_StopsInFlightMotionImmediately()
{
    await _mock.ConnectAsync("demo");
    await _mock.SendLineAsync("$J=G91 G21 X10 Y0 Z0 A0 F600");
    _ticker.RaiseElapsed(); // ack + first 1-unit step

    _mock.TriggerAlarm(1);
    var atAlarm = QueryStatus();

    _ticker.RaiseElapsed();
    _ticker.RaiseElapsed();
    var afterMoreTicks = QueryStatus();

    Assert.Equal(atAlarm.WPos, afterMoreTicks.WPos);
    Assert.Equal(MachineState.Alarm, afterMoreTicks.State);
}

[Fact]
public async Task SetResponseDelay_CalledBeforeSending_DelaysFirstCommandByConfiguredTicks()
{
    await _mock.ConnectAsync("demo");
    _mock.SetResponseDelay(TimeSpan.FromMilliseconds(300)); // 300ms / 100ms tick = 3 ticks
    string? reply = null;
    _mock.LineReceived += line => reply ??= line;

    await _mock.SendLineAsync("G4 P0");

    _ticker.RaiseElapsed();
    Assert.Null(reply);
    _ticker.RaiseElapsed();
    Assert.Null(reply);
    _ticker.RaiseElapsed();
    Assert.Null(reply);
    _ticker.RaiseElapsed();
    Assert.Equal("ok", reply);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter MockDeviceTransportTests`
Expected: FAIL (build error — `TriggerAlarm` и `SetResponseDelay` не существуют на `MockDeviceTransport`).

- [ ] **Step 3: Create `IMockDeviceControl`**

```csharp
using System;

namespace ArctZ.Services.Device;

/// <summary>Demo-only knobs for MockDeviceTransport, surfaced to the UI via ConnectionViewModel.</summary>
public interface IMockDeviceControl
{
    /// <summary>Makes the next dequeued command report an error instead of ok, and skips its effect.</summary>
    void ForceNextCommandError(int code);

    /// <summary>Immediately puts the mock into the Alarm state and raises an ALARM: line, mirroring an unsolicited push from a real controller.</summary>
    void TriggerAlarm(int code);

    /// <summary>Extra delay before ok/error is returned for each queued line command, on top of the normal one-line-per-tick pacing.</summary>
    void SetResponseDelay(TimeSpan delay);
}
```

- [ ] **Step 4: Implement `TriggerAlarm` and `SetResponseDelay` on `MockDeviceTransport`**

Modify `ArctZ/Services/Device/Simulation/MockDeviceTransport.cs:23` — add the interface to the class declaration:

```csharp
public sealed class MockDeviceTransport : IDeviceTransport, IMockDeviceControl
```

Add two new private fields next to the existing ones (`ArctZ/Services/Device/Simulation/MockDeviceTransport.cs:40-41`, right after `_rxBytesInFlight`/`_forcedErrorForNextDequeue`):

```csharp
    private int _responseDelayTicks;
    private int _ticksUntilNextProcess;
```

Add the two new public methods, right after `ForceNextCommandError` (`ArctZ/Services/Device/Simulation/MockDeviceTransport.cs:57`):

```csharp
    public void TriggerAlarm(int code)
    {
        lock (_lock)
        {
            _alarm = true;
            _targetPose = null; // авария останавливает движение, как в реальном FluidNC
        }

        LineReceived?.Invoke($"ALARM:{code}");
    }

    public void SetResponseDelay(TimeSpan delay)
    {
        lock (_lock)
        {
            _responseDelayTicks = (int)Math.Round(delay.TotalMilliseconds / _tickInterval.TotalMilliseconds);
            _ticksUntilNextProcess = _responseDelayTicks;
        }
    }
```

Modify `ProcessOnePendingLine` (`ArctZ/Services/Device/Simulation/MockDeviceTransport.cs:131-150`) to wait out the delay before dequeuing:

```csharp
    /// <summary>Caller must hold `_lock`. Returns the line to raise via LineReceived (after releasing the lock), or null.</summary>
    private string? ProcessOnePendingLine()
    {
        if (_pendingLines.Count == 0)
        {
            return null;
        }

        if (_ticksUntilNextProcess > 0)
        {
            _ticksUntilNextProcess--;
            return null;
        }

        var line = _pendingLines.Dequeue();
        _rxBytesInFlight -= line.Length + 1;
        _ticksUntilNextProcess = _responseDelayTicks;

        if (_forcedErrorForNextDequeue is { } code)
        {
            _forcedErrorForNextDequeue = null;
            return $"error:{code}";
        }

        ApplyCommand(line);
        return "ok";
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter MockDeviceTransportTests`
Expected: PASS, все тесты (старые и три новых).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Services/Device/IMockDeviceControl.cs ArctZ/Services/Device/Simulation/MockDeviceTransport.cs ArctZ.Tests/Services/Device/MockDeviceTransportTests.cs
git commit -m "feat: add IMockDeviceControl with alarm trigger and response delay to MockDeviceTransport"
```

---

### Task 2: `ConnectionViewModel` — состояние диалога и проводка к моку

**Files:**
- Modify: `ArctZ/ViewModels/ConnectionViewModel.cs`
- Test: `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs`

**Interfaces:**
- Consumes: `ArctZ.Services.Device.IMockDeviceControl` (Task 1); `ArctZ.Services.Device.Simulation.MockDeviceTransport` (только в тестах, для конструирования реального демо-транспорта).
- Produces: `ConnectionViewModel.IsMockSettingsOpen` (`bool`), `ConnectionViewModel.MockResponseDelayMs` (`int`), `ConnectionViewModel.ToggleMockSettingsCommand`, `ConnectionViewModel.TriggerMockErrorCommand`, `ConnectionViewModel.TriggerMockAlarmCommand` (все три — `IEnhancedCommand<Unit>`, как остальные команды в этом классе).

- [ ] **Step 1: Write the failing tests**

Открыть `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs`. Добавить в блок `using` наверху файла (после `using ArctZ.Services.Device;`):

```csharp
using ArctZ.Services.Device.Simulation;
```

и (нужен для `TimeSpan.FromMilliseconds` в новых тестах) добавить самой первой строкой файла:

```csharp
using System;
```

Добавить четыре новых `[Fact]`/`[Fact]` в конец класса `ConnectionViewModelTests` (перед закрывающей `}` файла, после `IsAlarmModalVisible_TracksAlarmTriggerAndReset`/`UnsolicitedDisconnect_DuringAlarm_ConnectionModalWinsOverAlarmModal`):

```csharp
[Fact]
public void ToggleMockSettingsCommand_TogglesIsMockSettingsOpen()
{
    var vm = CreateVm(new FakeDeviceTransport());
    Assert.False(vm.IsMockSettingsOpen);

    vm.ToggleMockSettingsCommand.Execute(null);
    Assert.True(vm.IsMockSettingsOpen);

    vm.ToggleMockSettingsCommand.Execute(null);
    Assert.False(vm.IsMockSettingsOpen);
}

[Fact]
public async Task TriggerMockErrorAndAlarmCommands_ConnectedToNonMockDemoTransport_DoNotThrow()
{
    // The default demo transport in these tests is FakeDeviceTransport, which does not
    // implement IMockDeviceControl — this exercises the cast-miss no-op path, not just
    // "never connected".
    var vm = CreateVm(new FakeDeviceTransport());
    vm.SelectedEndpoint = vm.AvailableEndpoints.Single(e => e.Kind == ConnectionEndpointKind.Demo);
    await vm.ConnectCommand.Execute();

    var errorException = Record.Exception(() => vm.TriggerMockErrorCommand.Execute(null));
    var alarmException = Record.Exception(() => vm.TriggerMockAlarmCommand.Execute(null));

    Assert.Null(errorException);
    Assert.Null(alarmException);
}

[Fact]
public async Task TriggerMockAlarmCommand_WhileConnectedToRealMockTransport_SetsLastAlarmCodeToOne()
{
    var realTransport = new FakeDeviceTransport();
    var mockTransport = new MockDeviceTransport(MachineLimits.Default, new ManualPeriodicTimer(), TimeSpan.FromMilliseconds(100));
    var vm = CreateVm(realTransport, mockTransport);
    vm.SelectedEndpoint = vm.AvailableEndpoints.Single(e => e.Kind == ConnectionEndpointKind.Demo);
    await vm.ConnectCommand.Execute();
    Assert.False(vm.IsAlarmModalVisible);

    vm.TriggerMockAlarmCommand.Execute(null);

    Assert.Equal(1, vm.LastAlarmCode);
    Assert.True(vm.IsAlarmModalVisible);
}

[Fact]
public async Task MockResponseDelayMs_ChangedWhileConnected_DelaysNextCommandAck()
{
    var realTransport = new FakeDeviceTransport();
    var ticker = new ManualPeriodicTimer();
    var mockTransport = new MockDeviceTransport(MachineLimits.Default, ticker, TimeSpan.FromMilliseconds(100));
    var vm = CreateVm(realTransport, mockTransport);
    vm.SelectedEndpoint = vm.AvailableEndpoints.Single(e => e.Kind == ConnectionEndpointKind.Demo);
    await vm.ConnectCommand.Execute();

    vm.MockResponseDelayMs = 200; // 2 ticks at the 100ms tick interval

    var sendTask = vm.Session!.SendGCodeAsync("G4 P0");
    ticker.RaiseElapsed();
    Assert.False(sendTask.IsCompleted);
    ticker.RaiseElapsed();
    Assert.False(sendTask.IsCompleted);
    ticker.RaiseElapsed();

    Assert.True(sendTask.IsCompletedSuccessfully);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter ConnectionViewModelTests`
Expected: FAIL (build error — `IsMockSettingsOpen`, `MockResponseDelayMs`, `ToggleMockSettingsCommand`, `TriggerMockErrorCommand`, `TriggerMockAlarmCommand` не существуют).

- [ ] **Step 3: Add the new reactive fields and commands**

Modify `ArctZ/ViewModels/ConnectionViewModel.cs:19` — add a new field next to `_sentGCodeSubscription`:

```csharp
    private IMockDeviceControl? _currentMockControl;
```

Modify `ArctZ/ViewModels/ConnectionViewModel.cs:20` — add two new consts next to `MaxSentGCodeLines`:

```csharp
    private const int MockErrorCode = 9;
    private const int MockAlarmCode = 1;
```

Modify `ArctZ/ViewModels/ConnectionViewModel.cs:34` — add two new reactive properties right after `isGCodeLogOpen`:

```csharp
    [Reactive] private bool isMockSettingsOpen;
    [Reactive] private int mockResponseDelayMs;
```

Modify `ArctZ/ViewModels/ConnectionViewModel.cs:101` — add three new command properties right after `ToggleGCodeLogCommand`:

```csharp
    public IEnhancedCommand<Unit> ToggleMockSettingsCommand { get; }
    public IEnhancedCommand<Unit> TriggerMockErrorCommand { get; }
    public IEnhancedCommand<Unit> TriggerMockAlarmCommand { get; }
```

Modify `ArctZ/ViewModels/ConnectionViewModel.cs:129-130` — add three new command initializations right after `ToggleGCodeLogCommand`'s:

```csharp
        ToggleMockSettingsCommand = Track(ReactiveCommand.Create(() => { IsMockSettingsOpen = !IsMockSettingsOpen; })
            .Enhance(text: "Настройки мока", name: "ToggleMockSettingsCommand"));
        TriggerMockErrorCommand = Track(ReactiveCommand.Create(() => { _currentMockControl?.ForceNextCommandError(MockErrorCode); })
            .Enhance(text: "Смоделировать ошибку", name: "TriggerMockErrorCommand"));
        TriggerMockAlarmCommand = Track(ReactiveCommand.Create(() => { _currentMockControl?.TriggerAlarm(MockAlarmCode); })
            .Enhance(text: "Смоделировать аварию", name: "TriggerMockAlarmCommand"));
```

Modify `ArctZ/ViewModels/ConnectionViewModel.cs:198` — right before the constructor's closing `}` (immediately after the last `.DisposeWith(Disposables);` of the `IsConnectionModalVisible`/... raising subscription), add a subscription that forwards live delay changes to the current mock:

```csharp

        this.WhenAnyValue(x => x.MockResponseDelayMs)
            .Subscribe(ms => _currentMockControl?.SetResponseDelay(TimeSpan.FromMilliseconds(ms)))
            .DisposeWith(Disposables);
```

- [ ] **Step 4: Wire `_currentMockControl` into connect/disconnect**

Modify `ArctZ/ViewModels/ConnectionViewModel.cs:219-221` — right after `innerTransport` is chosen, before the `if (_sentGCodeSubscription is not null)` block:

```csharp
        var innerTransport = SelectedEndpoint.Kind == ConnectionEndpointKind.Demo
            ? _createDemoTransport()
            : _realTransport;

        _currentMockControl = innerTransport as IMockDeviceControl;
        _currentMockControl?.SetResponseDelay(TimeSpan.FromMilliseconds(MockResponseDelayMs));
```

Modify `ArctZ/ViewModels/ConnectionViewModel.cs:256-269` (`DisconnectAsync`) — clear the reference alongside `Session`:

```csharp
    private async Task DisconnectAsync()
    {
        if (Session is not null)
        {
            await Session.DisconnectAsync();
            Session = null;
            _currentMockControl = null;
        }

        if (_sentGCodeSubscription is not null)
        {
            Disposables.Remove(_sentGCodeSubscription);
            _sentGCodeSubscription = null;
        }
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter ConnectionViewModelTests`
Expected: PASS, все тесты (старые и четыре новых).

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS — ничего в остальных тестах (`ProgramViewModel*Tests`, `MockDeviceTransportTests` из Task 1 и т.д.) не сломано.

- [ ] **Step 7: Commit**

```bash
git add ArctZ/ViewModels/ConnectionViewModel.cs ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs
git commit -m "feat: add mock settings state and trigger commands to ConnectionViewModel"
```

---

### Task 3: `ProgramViewModel` — команда открытия диалога

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs`
- Test: `ArctZ.Tests/ViewModels/ProgramViewModelSideMenuTests.cs`

**Interfaces:**
- Consumes: `ConnectionViewModel.IsMockSettingsOpen` (Task 2), `ProgramViewModel.IsSideMenuOpen` (existing).
- Produces: `ProgramViewModel.OpenMockSettingsCommand` (generated by `[RelayCommand]` from `OpenMockSettings()`, `ICommand`/`IRelayCommand`, same shape as `OpenGCodeLogCommand`).

- [ ] **Step 1: Write the failing tests**

Modify `ArctZ.Tests/ViewModels/ProgramViewModelSideMenuTests.cs` — add two new `[Fact]` at the end of the class (after `OpenGCodeLogCommand_WhenLogAlreadyOpen_LeavesItOpen`):

```csharp
    [Fact]
    public void OpenMockSettingsCommand_OpensDialogAndClosesMenu()
    {
        var vm = CreateViewModel();
        vm.ToggleSideMenuCommand.Execute(null);
        Assert.False(vm.Connection.IsMockSettingsOpen);

        vm.OpenMockSettingsCommand.Execute(null);

        Assert.True(vm.Connection.IsMockSettingsOpen);
        Assert.False(vm.IsSideMenuOpen);
    }

    [Fact]
    public void OpenMockSettingsCommand_WhenDialogAlreadyOpen_LeavesItOpen()
    {
        var vm = CreateViewModel();
        vm.OpenMockSettingsCommand.Execute(null);
        vm.OpenMockSettingsCommand.Execute(null);
        Assert.True(vm.Connection.IsMockSettingsOpen);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter ProgramViewModelSideMenuTests`
Expected: FAIL (build error — `OpenMockSettingsCommand` не существует).

- [ ] **Step 3: Add `OpenMockSettings` to `ProgramViewModel`**

Modify `ArctZ/ViewModels/ProgramViewModel.cs:104-109` — add a new `[RelayCommand]` method right after `OpenGCodeLog`:

```csharp
    [RelayCommand]
    private void OpenMockSettings()
    {
        Connection.IsMockSettingsOpen = true;
        IsSideMenuOpen = false;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter ProgramViewModelSideMenuTests`
Expected: PASS, все тесты (старые и два новых).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/ViewModels/ProgramViewModelSideMenuTests.cs
git commit -m "feat: add OpenMockSettingsCommand to ProgramViewModel"
```

---

### Task 4: UI — пункт бокового меню и диалог в `MainView.axaml`

**Files:**
- Modify: `ArctZ/Views/MainView.axaml:319-322` (боковое меню)
- Modify: `ArctZ/Views/MainView.axaml:327-348` (добавить новый оверлей-диалог рядом с оверлеем лога G-code)

**Interfaces:**
- Consumes: `ProgramViewModel.OpenMockSettingsCommand` (Task 3), `ConnectionViewModel.IsMockSettingsOpen`, `ConnectionViewModel.ToggleMockSettingsCommand`, `ConnectionViewModel.TriggerMockErrorCommand`, `ConnectionViewModel.TriggerMockAlarmCommand`, `ConnectionViewModel.MockResponseDelayMs` (все — Task 2/3).

- [ ] **Step 1: Добавить пункт в боковое меню**

Заменить блок (строки 319-322):

```xml
                            <StackPanel Spacing="8">
                                <Button Classes="header-action" HorizontalAlignment="Stretch" HorizontalContentAlignment="Left"
                                        Content="Лог G-code" Command="{Binding OpenGCodeLogCommand}" />
                            </StackPanel>
```

на:

```xml
                            <StackPanel Spacing="8">
                                <Button Classes="header-action" HorizontalAlignment="Stretch" HorizontalContentAlignment="Left"
                                        Content="Лог G-code" Command="{Binding OpenGCodeLogCommand}" />
                                <Button Classes="header-action" HorizontalAlignment="Stretch" HorizontalContentAlignment="Left"
                                        Content="Настройки мока" Command="{Binding OpenMockSettingsCommand}" />
                            </StackPanel>
```

- [ ] **Step 2: Добавить оверлей диалога**

Найти закрывающий оверлей лога G-code (строки 327-348, заканчивается на `</Border>` перед `</Grid>` на строке 349) и сразу после его закрывающего `</Border>` (после строки 348, перед строкой 349 `</Grid>`) добавить новый оверлей:

```xml
                <Border IsVisible="{Binding Connection.IsMockSettingsOpen}" Background="{StaticResource HudScrimBrush}">
                    <Border Width="320" Background="{StaticResource HudPanelElevatedBrush}"
                            BorderBrush="{StaticResource HudBorderStrongBrush}" BorderThickness="1"
                            Padding="20" HorizontalAlignment="Center" VerticalAlignment="Center">
                        <StackPanel Spacing="14">
                            <Grid ColumnDefinitions="*,Auto">
                                <TextBlock Grid.Column="0" Classes="section-heading" Text="НАСТРОЙКИ МОКА" VerticalAlignment="Center" />
                                <Button Grid.Column="1" Content="✕" Padding="8,2" Command="{Binding Connection.ToggleMockSettingsCommand}" />
                            </Grid>
                            <Button Content="Смоделировать ошибку" Command="{Binding Connection.TriggerMockErrorCommand}"
                                    HorizontalAlignment="Stretch" />
                            <Button Content="Смоделировать аварию" Command="{Binding Connection.TriggerMockAlarmCommand}"
                                    HorizontalAlignment="Stretch" />
                            <StackPanel Spacing="4">
                                <TextBlock Text="{Binding Connection.MockResponseDelayMs, StringFormat='Задержка ответа: {0} мс'}" />
                                <Slider Minimum="0" Maximum="5000" TickFrequency="250" IsSnapToTickEnabled="True"
                                        Value="{Binding Connection.MockResponseDelayMs}" />
                            </StackPanel>
                        </StackPanel>
                    </Border>
                </Border>
```

(Этот `Border` — прямой сосед оверлея лога G-code внутри `Grid#RootPanel`, тот же уровень вложенности, что `IsEditingKeyPoint`/`PendingRename`/`PendingConfirmation`/`IsLibraryOpen`/`Connection.IsGCodeLogOpen`.)

- [ ] **Step 3: Собрать десктоп-голову, чтобы проверить, что XAML валиден**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: Build succeeded — компилируемые биндинги (`x:DataType="vm:ProgramViewModel"` у корневого `UserControl`) упали бы на этапе сборки, если имена свойств/команд не совпадают.

- [ ] **Step 4: Прогнать полный набор тестов**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS — все тесты из Task 1-3 и уже существующие.

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Views/MainView.axaml
git commit -m "feat: add mock settings dialog to side menu"
```

---

### Task 5: Ручная UI-проверка

**Files:** нет изменений кода — только проверка поведения приложения.

**Interfaces:** нет (использует то, что произвели Task 1-4).

- [ ] **Step 1: Собрать и запустить Desktop-голову**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Затем: `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj`

Expected: приложение реально запущено (не просто собрано).

- [ ] **Step 2: Попросить пользователя пройти четыре сценария**

Попросить пользователя:
1. Подключиться к эндпоинту «Демо», открыть боковое меню (☰) и убедиться, что там появился пункт «Настройки мока», а его клик открывает диалог с двумя кнопками и слайдером.
2. Передвинуть слайдер задержки на большое значение (например, 3000-5000 мс) и запустить программу (Play) с несколькими точками — убедиться, что переход между точками стал заметно медленнее/с рывками по сравнению с задержкой = 0.
3. Установить небольшую/нулевую задержку, запустить программу и во время выполнения нажать «Смоделировать ошибку» — убедиться, что текущий сегмент помечается как ошибочный (баннер `FaultedMessage`/`СЕГМЕНТ` в панели программы).
4. Нажать «Смоделировать аварию» — убедиться, что поверх экрана появляется модалка «АВАРИЯ», и что кнопка «Сброс аварии» в ней закрывает модалку.

- [ ] **Step 3: Задать вопросы через `AskUserQuestion`**

Один вопрос на каждый из четырёх пунктов выше (не один общий «выглядит нормально?»), как того требует `CLAUDE.md` («Тестирование UI»).

- [ ] **Step 4: Зафиксировать результат**

Если пользователь подтвердил все четыре пункта — задача завершена, дополнительных коммитов не требуется (код уже закоммичен в Task 1-4). Если пользователь запросил правки — внести их точечно и повторить Step 1-3.
