# Единый статус станка и программы — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Заменить два независимых текстовых статуса в шапке («Простой»/«Ожидание» и т.д. — `ConnectionViewModel.MachineStateLabel` + `ProgramViewModel.PlaybackStateLabel`) одним приоритетным `ProgramViewModel.StatusLabel`, и заодно починить давнюю особенность — `PlaybackState.Completed`/`Stopped`/`Faulted` больше не висят вечно, а сами возвращаются в `Idle` через 4 секунды.

**Architecture:** `StatusLabel` — новое вычисляемое свойство на `ProgramViewModel` (корневой `x:DataType` шапки), читает собственный `PlaybackState` и `Connection.DeviceStatus?.State` напрямую — тот же паттерн, что уже используется в файле для `Connection.Session`/`Connection.IsPlaybackLocked`. `ConnectionViewModel.MachineStateLabel` удаляется целиком (без замены — его единственный потребитель, `ConnectionView.axaml`, просто теряет эту строку). Автосброс терминальных состояний — `await Task.Delay(...)` с `CancellationTokenSource`, тот же идиом «`await` → мутация свойства», что уже в `PlayAsync`/`PauseAsync` этого файла; никакого нового диспетчер-механизма.

**Tech Stack:** Avalonia UI, C# 12/.NET 10, CommunityToolkit.Mvvm (`ProgramViewModel`), xUnit (`ArctZ.Tests`).

## Global Constraints

- Спека: `docs/superpowers/specs/2026-08-05-unified-status-label-design.md`. Опирается на уже реализованный `docs/superpowers/specs/2026-08-05-header-status-alarm-redesign-design.md` — единая панель статуса `Border(HeaderStatusRow)` в `MainView.axaml` не перестраивается, меняется только текст одного `TextBlock` внутри неё.
- Приоритет `StatusLabel` (сверху вниз, первое совпадение побеждает): `Faulted` → «Ошибка», `Running` → «Выполнение», `Paused` → «Пауза», `MachineState.Jog` → «Джог», `MachineState.Home` → «Homing», `Completed` → «Завершено», `Stopped` → «Остановлено», иначе → «Ожидание». `MachineState.Alarm` в этот список не входит (модалка аварии уже перекрывает экран). `MachineState.Hold` не проверяется отдельно (недостижим без `Paused`/`Stopped`, которые перехватывают раньше).
- Автосброс: `Completed`/`Stopped`/`Faulted` возвращаются в `PlaybackState.Idle` через `internal TimeSpan TerminalStatusResetDelay { get; set; } = TimeSpan.FromSeconds(4);` на `ProgramViewModel` (переопределяемо в тестах — `InternalsVisibleTo("ArctZ.Tests")` уже настроен в `ArctZ.csproj:34`). Отменяется при выходе из терминального состояния (новый `CancellationTokenSource` на каждый вход, `Cancel()` на каждый выход) — иначе устаревший таймер может затереть свежий `Running`.
- `ConnectionViewModel.MachineStateLabel` и `ProgramViewModel.PlaybackStateLabel` удаляются целиком вместе со всеми их `RaisePropertyChanged`/`NotifyPropertyChangedFor` упоминаниями — ни один тест на них не завязан (проверено).
- `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs` — существующий файл с `PlaybackState`-тестами, не трогается. Новые тесты на `StatusLabel` — в отдельном файле `ArctZ.Tests/ViewModels/ProgramViewModelStatusLabelTests.cs`, со своими копиями хелперов `CreateViewModel`/`SeedTwoSegmentProgram` — по прецеденту: `ConnectionViewModelTests.cs` и `ProgramViewModelPlaybackTests.cs` уже держат каждый свой собственный маленький `CreateVm`/`CreateViewModel`, не шарят общий.
- Ничего не меняется в `IDeviceSession`/`DeviceSession`/`FluidNcStatusParser`/`MachineState`/`PlaybackState` как перечислениях — только чтение существующих значений.

---

### Task 1: `ProgramViewModel.StatusLabel` заменяет `PlaybackStateLabel` — TDD

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs`
- Modify: `ArctZ/Views/MainView.axaml`
- Create: `ArctZ.Tests/ViewModels/ProgramViewModelStatusLabelTests.cs`

**Interfaces:**
- Consumes: `ProgramViewModel.PlaybackState` (существует), `ConnectionViewModel.DeviceStatus` (существует, публичное через `[Reactive]`), `MachineState` enum (`ArctZ.Services.Device`, уже импортирован в файл).
- Produces: `ProgramViewModel.StatusLabel` (`string`) — потребляется в Task 3 (автосброс не меняет саму формулу, только `PlaybackState`, от которого она уже зависит).

Одна атомарная задача: XAML (`MainView.axaml:96`) биндится на `PlaybackStateLabel`, поэтому переименование в C# и правка биндинга должны попасть в один коммит — иначе на промежуточном шаге проект не соберётся (компилируемые биндинги Avalonia проверяют существование свойства на этапе сборки).

- [ ] **Step 1: Написать падающие тесты**

Создать `ArctZ.Tests/ViewModels/ProgramViewModelStatusLabelTests.cs`:

```csharp
using System;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.Device;
using ArctZ.Tests.Services.Program;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class ProgramViewModelStatusLabelTests
{
    private static ProgramViewModel CreateViewModel(out FakeDeviceTransport transport)
    {
        transport = new FakeDeviceTransport();
        var storage = new FakeProgramStorage();
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default));
        return new ProgramViewModel(connection, storage, new TrajectoryCompiler());
    }

    private static void SeedTwoSegmentProgram(ProgramViewModel vm, FakeDeviceTransport transport)
    {
        foreach (var pose in new[] { "0,0,0,0", "10,0,0,0", "20,0,0,0" })
        {
            transport.SimulateReceivedLine($"<Idle|WPos:{pose}|FS:0,0>");
            vm.CaptureKeyPointCommand.Execute(null);
        }

        for (var i = 0; i < vm.KeyPoints.Count; i++)
        {
            vm.KeyPoints[i] = vm.KeyPoints[i] with { FeedRateUnitsPerMin = 500, DwellSeconds = 0, Ease = EaseMode.None, ContinuousBlend = true };
        }
    }

    [Fact]
    public async Task StatusLabel_Idle_ByDefault()
    {
        var vm = CreateViewModel(out _);
        await vm.Connection.ConnectCommand.Execute();

        Assert.Equal("Ожидание", vm.StatusLabel);
    }

    [Fact]
    public async Task StatusLabel_Running_WhilePlaybackRunning()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        Assert.Equal("Выполнение", vm.StatusLabel);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await playTask;
    }

    [Fact]
    public async Task StatusLabel_Paused_WhilePlaybackPaused()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        await vm.PauseCommand.ExecuteAsync(null);

        Assert.Equal("Пауза", vm.StatusLabel);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await playTask;
    }

    [Fact]
    public async Task StatusLabel_Faulted_OnError()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("error:9");
        await playTask;

        Assert.Equal("Ошибка", vm.StatusLabel);
    }

    [Fact]
    public async Task StatusLabel_Completed_AfterProgramFinishes()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await playTask;

        Assert.Equal("Завершено", vm.StatusLabel);
    }

    [Fact]
    public async Task StatusLabel_Stopped_AfterStop()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.Equal("Остановлено", vm.StatusLabel);

        // Both compiled G1 lines were already sent (fit the default RX buffer in one shot,
        // per PlayAsync_DispatchesAllStepsBeforeAwaitingAcks_ThenTracksProgress) and are still
        // in-flight — AbortPendingCommands only drains not-yet-sent commands, so both need an
        // "ok" to fully drain the queue, same as PlayAsync_AfterStop_SendsResumeBeforeDispatchingFreshProgram.
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await playTask;
    }

    [Fact]
    public async Task StatusLabel_Jog_WhenMachineJoggingAndPlaybackIdle()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();

        transport.SimulateReceivedLine("<Jog|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        Assert.Equal("Джог", vm.StatusLabel);
    }

    [Fact]
    public async Task StatusLabel_Homing_WhenMachineHomingAndPlaybackIdle()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();

        transport.SimulateReceivedLine("<Home|WPos:0.000,0.000,0.000,0.000|FS:0,0>");

        Assert.Equal("Homing", vm.StatusLabel);
    }
}
```

- [ ] **Step 2: Запустить тесты, убедиться что они падают (ошибка компиляции)**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter ProgramViewModelStatusLabelTests`
Expected: сборка падает — `'ProgramViewModel' does not contain a definition for 'StatusLabel'`, потому что свойства ещё нет.

- [ ] **Step 3: Заменить `PlaybackStateLabel` на `StatusLabel` в `ProgramViewModel.cs`**

В `ArctZ/ViewModels/ProgramViewModel.cs` найти:

```csharp
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(IsProgramLocked))]
    [NotifyPropertyChangedFor(nameof(PlaybackStateLabel))]
    private PlaybackState _playbackState = PlaybackState.Idle;

    public bool IsProgramLocked => PlaybackState is PlaybackState.Running or PlaybackState.Paused;

    public string PlaybackStateLabel => PlaybackState switch
    {
        PlaybackState.Idle => "Ожидание",
        PlaybackState.Running => "Выполняется",
        PlaybackState.Paused => "Пауза",
        PlaybackState.Completed => "Завершено",
        PlaybackState.Faulted => "Ошибка",
        PlaybackState.Stopped => "Остановлено",
        _ => "—",
    };
```

Заменить на:

```csharp
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(IsProgramLocked))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private PlaybackState _playbackState = PlaybackState.Idle;

    public bool IsProgramLocked => PlaybackState is PlaybackState.Running or PlaybackState.Paused;

    // Единый статус станка и программы — приоритет сверху вниз, первое совпадение побеждает.
    // MachineState.Alarm сюда не входит: авария уже перекрывает экран отдельной блокирующей
    // модалкой (ConnectionViewModel.IsAlarmModalVisible), StatusLabel под ней всё равно не виден.
    // MachineState.Hold тоже не проверяется отдельно: единственный путь к нему — FeedHoldAsync()
    // из PauseAsync/StopAsync, которые уже выставляют Paused/Stopped раньше по списку.
    public string StatusLabel
    {
        get
        {
            if (PlaybackState == PlaybackState.Faulted) return "Ошибка";
            if (PlaybackState == PlaybackState.Running) return "Выполнение";
            if (PlaybackState == PlaybackState.Paused) return "Пауза";
            if (Connection.DeviceStatus?.State == MachineState.Jog) return "Джог";
            if (Connection.DeviceStatus?.State == MachineState.Home) return "Homing";
            if (PlaybackState == PlaybackState.Completed) return "Завершено";
            if (PlaybackState == PlaybackState.Stopped) return "Остановлено";
            return "Ожидание";
        }
    }
```

- [ ] **Step 4: Пробросить уведомление от `Connection.DeviceStatus`**

В том же файле найти:

```csharp
    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionViewModel.DeviceStatus))
        {
            CaptureKeyPointCommand.NotifyCanExecuteChanged();
            FillKeyPointFromCurrentPositionCommand.NotifyCanExecuteChanged();
            return;
        }
```

Заменить на:

```csharp
    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionViewModel.DeviceStatus))
        {
            CaptureKeyPointCommand.NotifyCanExecuteChanged();
            FillKeyPointFromCurrentPositionCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(StatusLabel));
            return;
        }
```

- [ ] **Step 5: Перевести биндинг в `MainView.axaml`**

В `ArctZ/Views/MainView.axaml` найти:

```xml
                            <TextBlock Grid.Column="2" Classes="telemetry" FontSize="14" VerticalAlignment="Center" Text="{Binding PlaybackStateLabel}" />
```

Заменить на:

```xml
                            <TextBlock Grid.Column="2" Classes="telemetry" FontSize="14" VerticalAlignment="Center" Text="{Binding StatusLabel}" />
```

- [ ] **Step 6: Запустить тесты, убедиться что проходят**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter ProgramViewModelStatusLabelTests`
Expected: PASS, 8/8.

- [ ] **Step 7: Собрать оба затронутых head'а**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: `Build succeeded`, 0 ошибок.

Run: `dotnet build ArctZ.Browser/ArctZ.Browser.csproj`
Expected: `Build succeeded`, 0 ошибок.

- [ ] **Step 8: Прогнать весь набор тестов на регрессию**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter ProgramViewModelPlaybackTests`
Expected: все существующие тесты по-прежнему проходят (файл не менялся, но `PlaybackState`'s `OnPlaybackStateChanged`/уведомления теперь ссылаются на `StatusLabel` вместо `PlaybackStateLabel` — эти тесты не проверяют лейблы, только `PlaybackState`/`IsProgramLocked`/отправленные G-code строки, так что регрессии не ожидается).

- [ ] **Step 9: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ/Views/MainView.axaml ArctZ.Tests/ViewModels/ProgramViewModelStatusLabelTests.cs
git commit -m "feat: replace PlaybackStateLabel with priority-based unified StatusLabel"
```

---

### Task 2: Убрать `ConnectionViewModel.MachineStateLabel`

**Files:**
- Modify: `ArctZ/ViewModels/ConnectionViewModel.cs`
- Modify: `ArctZ/Views/ConnectionView.axaml`

**Interfaces:**
- Consumes: ничего нового.
- Produces: `ConnectionViewModel` больше не содержит `MachineStateLabel`. `ConnectionView.axaml` показывает только индикатор связи + `PositionLabel` + баннер ошибки.

Одна атомарная задача: `ConnectionView.axaml:18` биндится на `MachineStateLabel`, поэтому C# и XAML меняются вместе — иначе на промежуточном шаге проект не соберётся.

- [ ] **Step 1: Убрать свойство и его биндинг в XAML**

В `ArctZ/Views/ConnectionView.axaml` найти:

```xml
        <StackPanel Orientation="Horizontal" Spacing="10" VerticalAlignment="Center"
                    IsVisible="{Binding !IsConnectionModalVisible}">
            <TextBlock Classes="telemetry" FontSize="13" Text="{Binding MachineStateLabel}" />
            <TextBlock Classes="telemetry" FontSize="13" Text="{Binding PositionLabel}" />
        </StackPanel>
```

Заменить на:

```xml
        <StackPanel Orientation="Horizontal" Spacing="10" VerticalAlignment="Center"
                    IsVisible="{Binding !IsConnectionModalVisible}">
            <TextBlock Classes="telemetry" FontSize="13" Text="{Binding PositionLabel}" />
        </StackPanel>
```

- [ ] **Step 2: Убрать свойство в `ConnectionViewModel.cs`**

В `ArctZ/ViewModels/ConnectionViewModel.cs` найти:

```csharp
    public string MachineStateLabel => DeviceStatus?.State switch
    {
        MachineState.Idle => "Простой",
        MachineState.Run => "Выполнение",
        MachineState.Jog => "Джог",
        MachineState.Hold => "Удержание",
        MachineState.Home => "Homing",
        MachineState.Alarm => "АВАРИЯ",
        _ => "—",
    };

    public string PositionLabel => DeviceStatus is { } status
```

Заменить на:

```csharp
    public string PositionLabel => DeviceStatus is { } status
```

Затем найти:

```csharp
                this.RaisePropertyChanged(nameof(ConnectionStateLabel));
                this.RaisePropertyChanged(nameof(MachineStateLabel));
                this.RaisePropertyChanged(nameof(PositionLabel));
```

Заменить на:

```csharp
                this.RaisePropertyChanged(nameof(ConnectionStateLabel));
                this.RaisePropertyChanged(nameof(PositionLabel));
```

- [ ] **Step 3: Собрать**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: `Build succeeded`, 0 ошибок.

- [ ] **Step 4: Прогнать тесты на регрессию**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter ConnectionViewModelTests`
Expected: все тесты проходят (ни один не проверяет `MachineStateLabel` — уже сверено, совпадений нет).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/ViewModels/ConnectionViewModel.cs ArctZ/Views/ConnectionView.axaml
git commit -m "refactor: remove ConnectionViewModel.MachineStateLabel, superseded by ProgramViewModel.StatusLabel"
```

---

### Task 3: Автосброс терминальных состояний в `Ожидание` — TDD

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs`
- Modify: `ArctZ.Tests/ViewModels/ProgramViewModelStatusLabelTests.cs`

**Interfaces:**
- Consumes: `ProgramViewModel.PlaybackState` (существует), `StatusLabel` (Task 1, формула не меняется — просто теперь `PlaybackState` реально возвращается в `Idle`).
- Produces: `ProgramViewModel.TerminalStatusResetDelay` (`internal TimeSpan`, по умолчанию 4 секунды, переопределяемо в тестах).

- [ ] **Step 1: Написать падающие тесты**

В `ArctZ.Tests/ViewModels/ProgramViewModelStatusLabelTests.cs` добавить перед закрывающей `}` класса (после `StatusLabel_Homing_WhenMachineHomingAndPlaybackIdle`):

```csharp

    [Fact]
    public async Task StatusLabel_Completed_ResetsToIdleAfterDelay()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        vm.TerminalStatusResetDelay = TimeSpan.FromMilliseconds(20);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await playTask;
        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);

        await WaitUntilAsync(() => vm.PlaybackState == PlaybackState.Idle, TimeSpan.FromSeconds(1));
        Assert.Equal("Ожидание", vm.StatusLabel);
    }

    [Fact]
    public async Task StatusLabel_Stopped_ResetsToIdleAfterDelay()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        vm.TerminalStatusResetDelay = TimeSpan.FromMilliseconds(20);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(PlaybackState.Stopped, vm.PlaybackState);

        await WaitUntilAsync(() => vm.PlaybackState == PlaybackState.Idle, TimeSpan.FromSeconds(1));
        Assert.Equal("Ожидание", vm.StatusLabel);

        // Both dispatched G1 lines are still in-flight (see StatusLabel_Stopped_AfterStop) —
        // drain both so playTask actually completes.
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await playTask;
    }

    [Fact]
    public async Task TerminalStatusReset_CancelledIfPlayPressedAgainBeforeDelayElapses()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        SeedTwoSegmentProgram(vm, transport);
        vm.TerminalStatusResetDelay = TimeSpan.FromMilliseconds(200);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        // Both dispatched G1 lines are still in-flight (see StatusLabel_Stopped_AfterStop) —
        // drain both so playTask actually completes.
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await playTask;
        Assert.Equal(PlaybackState.Stopped, vm.PlaybackState);

        // Re-play well before the original 200ms terminal-reset delay elapses.
        var secondPlayTask = vm.PlayCommand.ExecuteAsync(null);
        Assert.Equal(PlaybackState.Running, vm.PlaybackState);

        // Wait past the original delay window — the stale reset must not fire and stomp
        // the freshly-started Running back to Idle.
        await Task.Delay(400);
        Assert.Equal(PlaybackState.Running, vm.PlaybackState);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await secondPlayTask;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - start > timeout)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(20);
        }
    }
```

- [ ] **Step 2: Запустить тесты, убедиться что они падают**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "StatusLabel_Completed_ResetsToIdleAfterDelay|StatusLabel_Stopped_ResetsToIdleAfterDelay|TerminalStatusReset_CancelledIfPlayPressedAgainBeforeDelayElapses"`
Expected: сборка падает — `'ProgramViewModel' does not contain a definition for 'TerminalStatusResetDelay'`, потому что свойства ещё нет (автосброса тоже нет, так что `WaitUntilAsync` в первых двух тестах провисел бы весь таймаут и упал с `TimeoutException`, если бы компиляция прошла).

- [ ] **Step 3: Добавить поле задержки и `CancellationTokenSource`**

В `ArctZ/ViewModels/ProgramViewModel.cs` найти начало файла:

```csharp
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
```

Заменить на:

```csharp
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
```

Затем найти:

```csharp
    private bool _pausedForLinkLoss;
```

Заменить на:

```csharp
    private bool _pausedForLinkLoss;

    private CancellationTokenSource? _terminalStatusResetCts;

    /// <summary>Overridable in tests so the terminal-state auto-reset doesn't require waiting the real delay.</summary>
    internal TimeSpan TerminalStatusResetDelay { get; set; } = TimeSpan.FromSeconds(4);
```

- [ ] **Step 4: Запустить/отменять таймер в `OnPlaybackStateChanged`, добавить сам метод сброса**

В том же файле найти:

```csharp
    partial void OnPlaybackStateChanged(PlaybackState value)
    {
        Connection.IsPlaybackLocked = IsProgramLocked;

        if (IsProgramLocked && (_leftActive || _rightActive))
        {
            _leftActive = false;
            _rightActive = false;
            _leftInput = default;
            _rightInput = default;
            Connection.Session?.EndJog();
        }
    }
```

Заменить на:

```csharp
    partial void OnPlaybackStateChanged(PlaybackState value)
    {
        Connection.IsPlaybackLocked = IsProgramLocked;

        if (IsProgramLocked && (_leftActive || _rightActive))
        {
            _leftActive = false;
            _rightActive = false;
            _leftInput = default;
            _rightInput = default;
            Connection.Session?.EndJog();
        }

        // Completed/Stopped/Faulted never resolved back to Idle on their own before this — the
        // operator had to press Play again to clear the label. Auto-reset after a delay, but
        // cancel it the moment we leave the terminal state (e.g. Play redispatches a fresh run)
        // so a stale reset can never stomp a freshly-started Running back to Idle.
        _terminalStatusResetCts?.Cancel();
        _terminalStatusResetCts = null;

        if (value is PlaybackState.Completed or PlaybackState.Stopped or PlaybackState.Faulted)
        {
            var cts = new CancellationTokenSource();
            _terminalStatusResetCts = cts;
            _ = ResetToIdleAfterDelayAsync(value, cts.Token);
        }
    }

    private async Task ResetToIdleAfterDelayAsync(PlaybackState terminalState, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TerminalStatusResetDelay, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (PlaybackState == terminalState)
        {
            PlaybackState = PlaybackState.Idle;
        }
    }
```

- [ ] **Step 5: Запустить тесты, убедиться что проходят**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter ProgramViewModelStatusLabelTests`
Expected: PASS, 11/11.

- [ ] **Step 6: Прогнать весь набор playback-тестов на регрессию**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter ProgramViewModelPlaybackTests`
Expected: все проходят, включая `LinkLoss_DuringPlayback_PausesImmediatelyThenFaultsIfReconnectExhausted` и `PlayWhileReconnecting_IsIgnored_AndFaultedStillFiresOnceExhausted` (оба заканчиваются на `PlaybackState.Faulted` — по умолчанию `TerminalStatusResetDelay` остаётся 4 реальные секунды в этих тестах, они не переопределяют его, так что автосброс не успевает сработать до конца теста; это ожидаемо и не требует правок).

- [ ] **Step 7: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/ViewModels/ProgramViewModelStatusLabelTests.cs
git commit -m "feat: auto-reset terminal playback states (Completed/Stopped/Faulted) back to Idle after a delay"
```

---

### Task 4: Финальная проверка

**Files:** нет изменений (если не найдены дефекты — тогда точечные правки с отдельным коммитом).

- [ ] **Step 1: Полная сборка обоих затронутых head'ов**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: `Build succeeded`, 0 ошибок.

Run: `dotnet build ArctZ.Browser/ArctZ.Browser.csproj`
Expected: `Build succeeded`, 0 ошибок.

- [ ] **Step 2: Полный прогон тестов**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: все тесты проходят, включая новые из Task 1 и Task 3 (11 в `ProgramViewModelStatusLabelTests`).

- [ ] **Step 3: Визуальная проверка (skill `run`)**

Запустить приложение (Desktop или Browser head), подключиться в демо-режиме, проверить:
- в шапке одна строка статуса вместо прежних двух («Простой» из `ConnectionView` больше не появляется);
- в покое — «Ожидание»;
- при выполнении программы — «Выполнение» (не «Простой»/«Выполнение», дёргающихся независимо);
- при джоге (без активной программы) — «Джог»;
- после «Стоп»/завершения программы — терминальный текст («Остановлено»/«Завершено»), сам сменяющийся на «Ожидание» через ~4 секунды без каких-либо действий пользователя.

Expected: всё соответствует ожиданиям, без визуальных дефектов.

- [ ] **Step 4: Зафиксировать точечные исправления (если есть)**

Если на Step 1-3 обнаружены дефекты — исправить, повторить проверку, затем:

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ/ViewModels/ConnectionViewModel.cs ArctZ/Views/MainView.axaml ArctZ/Views/ConnectionView.axaml
git commit -m "fix: address issues found in unified status label verification pass"
```

Если дефектов не найдено — коммит не требуется, задача считается завершённой по результатам Task 1-3.
