# Слайдер скорости джойстика — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Добавить слайдер 5–100%, масштабирующий скорость перемещения устройства при управлении джойстиками, с адаптивным расположением (на всю ширину экрана на узких экранах, узкий слайдер между джойстиками на широких).

**Architecture:** Чистая функция масштабирования (`JoystickSpeedScaler`) применяется к `JoystickAxisInput` в точке отправки `UpdateJog` внутри `ProgramViewModel`, не трогая хранимые «сырые» значения стиков. Порог узкого/широкого экрана — статическая чистая функция в `MainView`, её результат пробрасывается в новое bool-свойство VM, к которому биндится `IsVisible` двух вариантов `Slider` в XAML.

**Tech Stack:** Avalonia UI, CommunityToolkit.Mvvm (`[ObservableProperty]`), xUnit.

## Global Constraints

- Диапазон слайдера: 5–100% (из дизайна, спек `docs/superpowers/specs/2026-08-11-joystick-speed-slider-design.md`).
- Стартовое значение: 100% при каждом запуске, без персистентности.
- Порог узкий/широкий экран: `Bounds.Width < 700` (тот же порог, что уже используется в проекте, см. спек).
- Проценты отображаются текстом рядом со слайдером, формат `{0:0}%`.
- Единый коэффициент скейлит X/Y/Force входа джойстика целиком (шаг и feed-rate меняются согласованно).
- Изменение слайдера во время удержания джойстика должно немедленно пересчитывать отправляемую скорость (без ожидания следующего движения стика).
- UI-изменения проверяются только через живой запуск приложения и вопросы пользователю через `AskUserQuestion` (см. `CLAUDE.md`, раздел «Тестирование UI») — не считать задачу завершённой без этого шага.

---

### Task 1: `JoystickSpeedScaler` — чистая функция масштабирования

**Files:**
- Create: `ArctZ/ViewModels/JoystickSpeedScaler.cs`
- Test: `ArctZ.Tests/ViewModels/JoystickSpeedScalerTests.cs`

**Interfaces:**
- Consumes: `ArctZ.Services.Device.JoystickAxisInput` (record struct `(double X, double Y, double Force)`, уже существует в `ArctZ/Services/Device/DualJoystickState.cs`).
- Produces: `public static class JoystickSpeedScaler { public static JoystickAxisInput Scale(JoystickAxisInput input, double speedPercent); }` — используется в Task 3.

- [ ] **Step 1: Написать падающий тест**

```csharp
using ArctZ.Services.Device;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class JoystickSpeedScalerTests
{
    [Fact]
    public void Scale_HundredPercent_ReturnsInputUnchanged()
    {
        var input = new JoystickAxisInput(0.8, -0.5, 0.9);

        var result = JoystickSpeedScaler.Scale(input, 100);

        Assert.Equal(0.8, result.X, 3);
        Assert.Equal(-0.5, result.Y, 3);
        Assert.Equal(0.9, result.Force, 3);
    }

    [Fact]
    public void Scale_FiftyPercent_HalvesXYAndForce()
    {
        var input = new JoystickAxisInput(0.8, -0.5, 0.9);

        var result = JoystickSpeedScaler.Scale(input, 50);

        Assert.Equal(0.4, result.X, 3);
        Assert.Equal(-0.25, result.Y, 3);
        Assert.Equal(0.45, result.Force, 3);
    }

    [Fact]
    public void Scale_FivePercent_ScalesToOneTwentieth()
    {
        var input = new JoystickAxisInput(1.0, 1.0, 1.0);

        var result = JoystickSpeedScaler.Scale(input, 5);

        Assert.Equal(0.05, result.X, 3);
        Assert.Equal(0.05, result.Y, 3);
        Assert.Equal(0.05, result.Force, 3);
    }

    [Fact]
    public void Scale_ZeroInput_RemainsZeroRegardlessOfPercent()
    {
        var result = JoystickSpeedScaler.Scale(default, 42);

        Assert.Equal(default, result);
    }
}
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~JoystickSpeedScalerTests"`
Expected: FAIL (сборка не проходит — `JoystickSpeedScaler` не существует).

- [ ] **Step 3: Написать минимальную реализацию**

```csharp
using ArctZ.Services.Device;

namespace ArctZ.ViewModels;

/// <summary>
/// Масштабирует вход джойстика единым коэффициентом (0..100 -> 0..1),
/// применяемым к X/Y/Force сразу — так шаг перемещения и feed-rate
/// (оба производные от Force в JogCommandFactory) меняются согласованно.
/// </summary>
public static class JoystickSpeedScaler
{
    public static JoystickAxisInput Scale(JoystickAxisInput input, double speedPercent)
    {
        var factor = speedPercent / 100.0;
        return new JoystickAxisInput(input.X * factor, input.Y * factor, input.Force * factor);
    }
}
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~JoystickSpeedScalerTests"`
Expected: PASS (4 теста)

- [ ] **Step 5: Commit**

```bash
git add ArctZ/ViewModels/JoystickSpeedScaler.cs ArctZ.Tests/ViewModels/JoystickSpeedScalerTests.cs
git commit -m "feat: add JoystickSpeedScaler for jog speed scaling"
```

---

### Task 2: Порог узкого/широкого экрана в `MainView`

**Files:**
- Modify: `ArctZ/Views/MainView.axaml.cs:14-17` (константы), `:32-38` (конструктор/подписки), `:61-68` (`OnLayoutSizeChanged`/`UpdateJoystickRadius`)
- Test: `ArctZ.Tests/Views/MainViewNarrowLayoutTests.cs`

**Interfaces:**
- Consumes: ничего нового (используется тот же `Bounds.Width`, что уже читает `ComputeJoystickRadius`).
- Produces: `internal static bool ComputeIsNarrowLayout(double mainViewWidth)` — статический метод на `MainView`, используется в Task 4 (запись в `ViewModel.IsNarrowJoystickLayout`).

- [ ] **Step 1: Написать падающий тест**

```csharp
using ArctZ.Views;

namespace ArctZ.Tests.Views;

public class MainViewNarrowLayoutTests
{
    [Theory]
    [InlineData(360, true)]    // телефон-портрет
    [InlineData(699, true)]    // прямо под порогом
    [InlineData(700, false)]   // порог не включён (строго <)
    [InlineData(1200, false)]  // десктоп
    public void ComputeIsNarrowLayout_ReturnsExpectedResult(double mainViewWidth, bool expectedIsNarrow)
    {
        var isNarrow = MainView.ComputeIsNarrowLayout(mainViewWidth);

        Assert.Equal(expectedIsNarrow, isNarrow);
    }
}
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~MainViewNarrowLayoutTests"`
Expected: FAIL (метод `ComputeIsNarrowLayout` не существует).

- [ ] **Step 3: Добавить константу и метод в `MainView.axaml.cs`**

В `ArctZ/Views/MainView.axaml.cs` рядом с существующими константами (после строки `private const double CenterGap = 24;`, то есть после текущей строки 17):

```csharp
        private const double NarrowLayoutWidthThreshold = 700;
```

Рядом с `ComputeJoystickRadius` (после метода, который заканчивается строкой `return Math.Clamp(...)` на текущей строке 82-83) добавить:

```csharp
        internal static bool ComputeIsNarrowLayout(double mainViewWidth) => mainViewWidth < NarrowLayoutWidthThreshold;
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~MainViewNarrowLayoutTests"`
Expected: PASS (4 теста)

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Views/MainView.axaml.cs ArctZ.Tests/Views/MainViewNarrowLayoutTests.cs
git commit -m "feat: add narrow-layout width threshold helper to MainView"
```

---

### Task 3: `FakeDeviceSession` тест-дублёр и joystick-скейлинг в `ProgramViewModel`

**Files:**
- Create: `ArctZ.Tests/Services/Device/FakeDeviceSession.cs`
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs:22-25` (поля), `:549-562` (`OnStickMove`), `:564-585` (`OnStickUp`)
- Test: `ArctZ.Tests/ViewModels/ProgramViewModelJoystickSpeedTests.cs`

**Interfaces:**
- Consumes: `JoystickSpeedScaler.Scale(JoystickAxisInput, double)` из Task 1; `IDeviceSession` (`ArctZ/Services/Device/IDeviceSession.cs`); `DualJoystickState`/`JoystickAxisInput` (`ArctZ/Services/Device/DualJoystickState.cs`); `ConnectionViewModel.Session` (settable `[Reactive]`-свойство, `ArctZ/ViewModels/ConnectionViewModel.cs:25`).
- Produces: `ProgramViewModel.JoystickSpeedPercent` (`double`, `[ObservableProperty]`, генерируется как публичное свойство `JoystickSpeedPercent`) — используется в Task 5 (биндинг `Slider.Value`) и уже используется тестами этой задачи.

**Почему нужен `FakeDeviceSession`:** `ConnectionViewModel.Session` — сеттабельное свойство `IDeviceSession?`; подставив туда тест-дублёр вместо реального `DeviceSession` (который требует транспорт + реальный `JogScheduler` с периодическим таймером 100мс), тест перестаёт зависеть от времени и читает переданный в `UpdateJog` `DualJoystickState` синхронно.

- [ ] **Step 1: Написать `FakeDeviceSession`**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Tests.Services.Device;

/// <summary>Синхронный дублёр IDeviceSession: тесты читают UpdateJog-вызовы
/// напрямую, без реального JogScheduler/таймера/транспорта.</summary>
public sealed class FakeDeviceSession : ArctZ.Services.Device.IDeviceSession
{
    public ArctZ.Services.Device.ConnectionState ConnectionState => ArctZ.Services.Device.ConnectionState.Connected;

    public ArctZ.Services.Device.DeviceStatus? DeviceStatus => null;

    public string? LastError => null;

    public event Action? ConnectionStateChanged { add { } remove { } }

    public event Action? DeviceStatusChanged { add { } remove { } }

    public event Action<ArctZ.Services.Device.CommandRejectedEventArgs>? CommandRejected { add { } remove { } }

    public event Action<int>? AlarmTriggered { add { } remove { } }

    public ArctZ.Services.Device.DualJoystickState? LastJogState { get; private set; }

    public int UpdateJogCallCount { get; private set; }

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DisconnectAsync() => Task.CompletedTask;

    public void BeginJog()
    {
    }

    public void UpdateJog(ArctZ.Services.Device.DualJoystickState state)
    {
        LastJogState = state;
        UpdateJogCallCount++;
    }

    public void EndJog() => LastJogState = null;

    private static readonly ArctZ.Services.Device.CommandResult Acknowledged =
        new(ArctZ.Services.Device.CommandOutcome.Acknowledged, null);

    public Task<ArctZ.Services.Device.CommandResult> SendGCodeAsync(string line, CancellationToken cancellationToken = default) =>
        Task.FromResult(Acknowledged);

    public void AbortPendingCommands()
    {
    }

    public Task<ArctZ.Services.Device.CommandResult> HomeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Acknowledged);

    public Task<ArctZ.Services.Device.CommandResult> ResetAlarmAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Acknowledged);

    public Task FeedHoldAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ResumeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
```

Сигнатуры уже проверены при написании плана: `ConnectionState` — enum (`ArctZ/Services/Device/ConnectionState.cs`); `DeviceStatus` — `readonly record struct DeviceStatus(MachineState State, MachinePose WPos, int? PlannerBlocksAvailable, int? RxBytesAvailable)`; `CommandRejectedEventArgs` — `sealed record CommandRejectedEventArgs(GCodeLineCommand Command, int? ErrorCode)`; `CommandResult` — `readonly record struct CommandResult(CommandOutcome Outcome, int? ErrorCode)`, где `CommandOutcome` — enum `{ Acknowledged, Rejected, Aborted }` (нет значения `Ok` — код выше уже использует `Acknowledged`).

- [ ] **Step 2: Написать падающий тест**

```csharp
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.Device;
using ArctZ.Tests.Services.Program;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class ProgramViewModelJoystickSpeedTests
{
    private static (ProgramViewModel vm, FakeDeviceSession session) CreateViewModelWithFakeSession()
    {
        var transport = new FakeDeviceTransport();
        var storage = new FakeProgramStorage();
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default));
        var session = new FakeDeviceSession();
        connection.Session = session;
        var vm = new ProgramViewModel(connection, storage, new TrajectoryCompiler());
        return (vm, session);
    }

    [Fact]
    public void OnLeftJoystickMove_DefaultSpeed_SendsUnscaledInput()
    {
        var (vm, session) = CreateViewModelWithFakeSession();

        vm.OnLeftJoystickDown(new JoystickEventArgs { Force = 1.0, AngleDeg = 0 });

        Assert.Equal(1.0, session.LastJogState!.Value.Left.X, 3);
        Assert.Equal(1.0, session.LastJogState!.Value.Left.Force, 3);
    }

    [Fact]
    public void OnLeftJoystickMove_FiftyPercentSpeed_SendsHalvedInput()
    {
        var (vm, session) = CreateViewModelWithFakeSession();
        vm.JoystickSpeedPercent = 50;

        vm.OnLeftJoystickDown(new JoystickEventArgs { Force = 1.0, AngleDeg = 0 });

        Assert.Equal(0.5, session.LastJogState!.Value.Left.X, 3);
        Assert.Equal(0.5, session.LastJogState!.Value.Left.Force, 3);
    }

    [Fact]
    public void ChangingSpeedWhileStickHeld_ResendsScaledStateImmediately()
    {
        var (vm, session) = CreateViewModelWithFakeSession();
        vm.OnLeftJoystickDown(new JoystickEventArgs { Force = 1.0, AngleDeg = 0 });
        var callCountBefore = session.UpdateJogCallCount;

        vm.JoystickSpeedPercent = 25;

        Assert.True(session.UpdateJogCallCount > callCountBefore);
        Assert.Equal(0.25, session.LastJogState!.Value.Left.X, 3);
    }

    [Fact]
    public void ChangingSpeedWithNoStickHeld_DoesNotCallUpdateJog()
    {
        var (vm, session) = CreateViewModelWithFakeSession();

        vm.JoystickSpeedPercent = 25;

        Assert.Equal(0, session.UpdateJogCallCount);
    }
}
```

- [ ] **Step 3: Запустить тесты и убедиться, что они падают**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelJoystickSpeedTests"`
Expected: FAIL (`JoystickSpeedPercent` не существует на `ProgramViewModel`; `OnLeftJoystickMove_FiftyPercentSpeed...` и `ChangingSpeedWhileStickHeld...` падают по значению — исходный код шлёт нескейленный вход).

- [ ] **Step 4: Добавить `JoystickSpeedPercent` и скейлинг в `ProgramViewModel`**

В `ArctZ/ViewModels/ProgramViewModel.cs`, рядом с полями `_leftInput`/`_rightInput`/`_leftActive`/`_rightActive` (строки 22-25), добавить после них публичное свойство:

```csharp
    [ObservableProperty]
    private double _joystickSpeedPercent = 100;

    [ObservableProperty]
    private bool _isNarrowJoystickLayout;
```

(`IsNarrowJoystickLayout` используется в Task 4/5 — добавляется здесь вместе, т.к. это соседний однострочный `[ObservableProperty]` того же характера, без собственной тестируемой логики.)

В конце класса (после `OnStickUp`, т.е. после текущей строки 585, перед `private bool _pausedForLinkLoss;`) добавить partial-метод и приватный помощник:

```csharp
    partial void OnJoystickSpeedPercentChanged(double value)
    {
        if (_leftActive || _rightActive)
        {
            Connection.Session?.UpdateJog(new DualJoystickState(ScaledLeftInput(), ScaledRightInput()));
        }
    }

    private JoystickAxisInput ScaledLeftInput() => JoystickSpeedScaler.Scale(_leftInput, JoystickSpeedPercent);

    private JoystickAxisInput ScaledRightInput() => JoystickSpeedScaler.Scale(_rightInput, JoystickSpeedPercent);
```

Заменить оба текущих вызова `Connection.Session?.UpdateJog(new DualJoystickState(_leftInput, _rightInput));`:
- В `OnStickMove` (текущая строка 561) — на `Connection.Session?.UpdateJog(new DualJoystickState(ScaledLeftInput(), ScaledRightInput()));`
- В `OnStickUp` (текущая строка 583, ветка `else`) — на `Connection.Session?.UpdateJog(new DualJoystickState(ScaledLeftInput(), ScaledRightInput()));`

`_leftInput`/`_rightInput` при этом остаются нескейленными — они читаются и в `OnJoystickSpeedPercentChanged`, и при следующем движении стика, так что скейлинг всегда пересчитывается из актуального сырого значения и актуального процента.

- [ ] **Step 5: Запустить тесты и убедиться, что они проходят**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelJoystickSpeedTests"`
Expected: PASS (4 теста)

- [ ] **Step 6: Прогнать весь `ArctZ.Tests`, чтобы убедиться в отсутствии зависаний/регрессий**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS, весь прогон укладывается в обычное время (без зависаний — см. CLAUDE.md про TaskCompletionSource-диалоги; в этой задаче новых async-диалогов не добавляется, но полный прогон — минимальная защита от регрессии).

- [ ] **Step 7: Commit**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/Services/Device/FakeDeviceSession.cs ArctZ.Tests/ViewModels/ProgramViewModelJoystickSpeedTests.cs
git commit -m "feat: scale joystick jog input by JoystickSpeedPercent"
```

---

### Task 4: Пробросить `IsNarrowJoystickLayout` из `MainView.axaml.cs`

**Files:**
- Modify: `ArctZ/Views/MainView.axaml.cs:61-68` (`OnLayoutSizeChanged`/`UpdateJoystickRadius`)

**Interfaces:**
- Consumes: `MainView.ComputeIsNarrowLayout(double)` из Task 2; `ProgramViewModel.IsNarrowJoystickLayout` из Task 3; `MainView.ViewModel` (существующее `private ProgramViewModel? ViewModel => DataContext as ProgramViewModel;`, строка 40).
- Produces: живое обновление `ViewModel.IsNarrowJoystickLayout` при изменении размера окна — используется в Task 5 (`IsVisible`-биндинги в XAML).

Это чисто glue-код без отдельной тестируемой логики (сама логика уже покрыта тестом в Task 2) — проверяется визуально в Task 6.

- [ ] **Step 1: Обновить `UpdateJoystickRadius`**

В `ArctZ/Views/MainView.axaml.cs`, метод `UpdateJoystickRadius` (текущие строки 63-68):

```csharp
        private void UpdateJoystickRadius()
        {
            var radius = ComputeJoystickRadius(Bounds.Width, Bounds.Height, HeaderBorder.Bounds.Height);
            LeftJoystick.Radius = radius;
            RightJoystick.Radius = radius;

            if (ViewModel is { } vm)
            {
                vm.IsNarrowJoystickLayout = ComputeIsNarrowLayout(Bounds.Width);
            }
        }
```

- [ ] **Step 2: Собрать проект, убедиться в отсутствии ошибок компиляции**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add ArctZ/Views/MainView.axaml.cs
git commit -m "feat: wire narrow-layout detection to ProgramViewModel"
```

---

### Task 5: XAML — два варианта слайдера в `JoystickBar`

**Files:**
- Modify: `ArctZ/Views/MainView.axaml:283-300`

**Interfaces:**
- Consumes: `ProgramViewModel.JoystickSpeedPercent` (Task 3), `ProgramViewModel.IsNarrowJoystickLayout` (Task 3/4).
- Produces: видимый UI-элемент, дальше не потребляется другими задачами — проверяется в Task 6.

- [ ] **Step 1: Заменить блок `JoystickBar`**

Заменить текущий блок (строки 283-300):

```xml
                        <Grid x:Name="JoystickBar" Grid.Row="1" ColumnDefinitions="Auto,*,Auto" Margin="0,12,0,0">
                            <StackPanel Grid.Column="0" Spacing="4" HorizontalAlignment="Center" VerticalAlignment="Center">
                                <js:VirtualJoystick x:Name="LeftJoystick" Mode="Fixed" Shape="Circle"
                                                     IsEnabled="{Binding !IsProgramLocked}"
                                                     JoystickDown="OnLeftJoystickDown" JoystickMove="OnLeftJoystickMove" JoystickUp="OnLeftJoystickUp" />
                                <TextBlock Text="Подъём / поворот стрелы" Opacity="0.6" FontSize="12"
                                           TextWrapping="Wrap" TextAlignment="Center" HorizontalAlignment="Center"
                                           MaxWidth="{Binding Radius, ElementName=LeftJoystick, Converter={StaticResource RadiusToSize}}" />
                            </StackPanel>
                            <StackPanel Grid.Column="2" Spacing="4" HorizontalAlignment="Center" VerticalAlignment="Center">
                                <js:VirtualJoystick x:Name="RightJoystick" Mode="Fixed" Shape="Circle"
                                                     IsEnabled="{Binding !IsProgramLocked}"
                                                     JoystickDown="OnRightJoystickDown" JoystickMove="OnRightJoystickMove" JoystickUp="OnRightJoystickUp" />
                                <TextBlock Text="Пан / наклон камеры" Opacity="0.6" FontSize="12"
                                           TextWrapping="Wrap" TextAlignment="Center" HorizontalAlignment="Center"
                                           MaxWidth="{Binding Radius, ElementName=RightJoystick, Converter={StaticResource RadiusToSize}}" />
                            </StackPanel>
                        </Grid>
```

на:

```xml
                        <Grid x:Name="JoystickBar" Grid.Row="1" ColumnDefinitions="Auto,*,Auto" RowDefinitions="Auto,Auto" Margin="0,12,0,0">
                            <StackPanel Grid.Row="0" Grid.Column="0" Spacing="4" HorizontalAlignment="Center" VerticalAlignment="Center">
                                <js:VirtualJoystick x:Name="LeftJoystick" Mode="Fixed" Shape="Circle"
                                                     IsEnabled="{Binding !IsProgramLocked}"
                                                     JoystickDown="OnLeftJoystickDown" JoystickMove="OnLeftJoystickMove" JoystickUp="OnLeftJoystickUp" />
                                <TextBlock Text="Подъём / поворот стрелы" Opacity="0.6" FontSize="12"
                                           TextWrapping="Wrap" TextAlignment="Center" HorizontalAlignment="Center"
                                           MaxWidth="{Binding Radius, ElementName=LeftJoystick, Converter={StaticResource RadiusToSize}}" />
                            </StackPanel>

                            <StackPanel Grid.Row="0" Grid.Column="1" Spacing="4" Width="160"
                                        HorizontalAlignment="Center" VerticalAlignment="Center"
                                        IsVisible="{Binding !IsNarrowJoystickLayout}">
                                <Slider Minimum="5" Maximum="100" Value="{Binding JoystickSpeedPercent}" />
                                <TextBlock Text="{Binding JoystickSpeedPercent, StringFormat='{}{0:0}%'}" Opacity="0.6" FontSize="12"
                                           HorizontalAlignment="Center" />
                            </StackPanel>

                            <StackPanel Grid.Row="0" Grid.Column="2" Spacing="4" HorizontalAlignment="Center" VerticalAlignment="Center">
                                <js:VirtualJoystick x:Name="RightJoystick" Mode="Fixed" Shape="Circle"
                                                     IsEnabled="{Binding !IsProgramLocked}"
                                                     JoystickDown="OnRightJoystickDown" JoystickMove="OnRightJoystickMove" JoystickUp="OnRightJoystickUp" />
                                <TextBlock Text="Пан / наклон камеры" Opacity="0.6" FontSize="12"
                                           TextWrapping="Wrap" TextAlignment="Center" HorizontalAlignment="Center"
                                           MaxWidth="{Binding Radius, ElementName=RightJoystick, Converter={StaticResource RadiusToSize}}" />
                            </StackPanel>

                            <StackPanel Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="3" Spacing="4" Margin="0,8,0,0"
                                        IsVisible="{Binding IsNarrowJoystickLayout}">
                                <Slider Minimum="5" Maximum="100" Value="{Binding JoystickSpeedPercent}" HorizontalAlignment="Stretch" />
                                <TextBlock Text="{Binding JoystickSpeedPercent, StringFormat='{}{0:0}%'}" Opacity="0.6" FontSize="12"
                                           HorizontalAlignment="Center" />
                            </StackPanel>
                        </Grid>
```

- [ ] **Step 2: Собрать проект**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add ArctZ/Views/MainView.axaml
git commit -m "feat: add joystick speed slider to JoystickBar (wide and narrow layouts)"
```

---

### Task 6: Живая проверка в UI

**Files:** нет изменений кода — только запуск и проверка.

- [ ] **Step 1: Собрать и запустить Desktop-хост**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`, затем `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj`

- [ ] **Step 2: Попросить пользователя проверить поведение**

Развернуть окно широко (>700px) — слайдер должен быть узким, между джойстиками, с процентом рядом. Сжать окно узко (<700px) — слайдер должен переехать под ряд джойстиков и растянуться на всю ширину. При подключённом моке (`Connection.TriggerMockAlarmCommand`/подключение к mock-транспорту, уже присутствующему в UI) подвигать джойстик и слайдер одновременно, посмотреть, что процент обновляется, а не залипает.

- [ ] **Step 3: Задать вопросы через `AskUserQuestion`, по одному на каждое проверяемое поведение**

- Слайдер на широком окне отображается по центру между джойстиками и не наезжает на них?
- Слайдер на узком окне (< 700px) переезжает под джойстики на всю ширину?
- Процент рядом со слайдером обновляется при перетаскивании?
- Движение джойстика при уменьшенной скорости (например, 25%) ощутимо медленнее/меньше по шагу, чем при 100%?

---

## Self-Review

1. **Spec coverage:** масштабирование X/Y/Force — Task 1/3; узкий/широкий breakpoint 700px — Task 2/4; два варианта Slider с процентом — Task 5; живая UI-проверка — Task 6; диапазон 5–100 и дефолт 100 без персистентности — Global Constraints + Task 3 Step 4 + Task 5 XAML. Все пункты спека покрыты.
2. **Placeholder scan:** нет TBD/TODO; сигнатуры `CommandResult`/`ConnectionState`/`DeviceStatus`/`CommandRejectedEventArgs`, использованные в `FakeDeviceSession` (Task 3), сверены с фактическим кодом `ArctZ/Services/Device/*.cs` при написании плана.
3. **Type consistency:** `JoystickSpeedScaler.Scale(JoystickAxisInput, double)` (Task 1) используется в `ProgramViewModel` (Task 3) с тем же именем и сигнатурой; `ComputeIsNarrowLayout(double)` (Task 2) используется в `MainView.axaml.cs` (Task 4) с тем же именем; `JoystickSpeedPercent`/`IsNarrowJoystickLayout` — одинаковые имена во VM (Task 3), code-behind (Task 4) и XAML-биндингах (Task 5).
4. **Task granularity:** каждая задача заканчивается тестируемым результатом (unit-тест для 1-3, build для 4-5, живая проверка для 6); границы проведены так, что рецензент может принять/отклонить каждую независимо (например, отклонить XAML-раскладку Task 5, не трогая скейлинг-логику Task 3).
