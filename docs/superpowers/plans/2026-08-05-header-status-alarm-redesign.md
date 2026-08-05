# Редизайн шапки: единая панель статуса + модалка аварии — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Шапка `MainView` получает единую панель статуса (индикатор связи + машинное состояние/позиция + метка воспроизведения + иконка отключения `⏻` вместо текстовой кнопки), кнопка `Homing` и её функционал полностью убираются, а `Сброс аварии` переезжает из общего ряда действий в новое блокирующее модальное окно, которое появляется автоматически при аварии станка и закрывается само после успешного сброса.

**Architecture:** `ConnectionViewModel` получает два новых чисто вычисляемых свойства (`IsAlarmModalVisible`, `IsAnyModalVisible`) поверх уже существующих `LastAlarmCode`/`IsConnectionModalVisible` — без новых подписок, только добавление в уже существующий `RaisePropertyChanged`-блок. `HomeCommand` (обёртка над `IDeviceSession.HomeAsync`) удаляется из `ConnectionViewModel` вместе с кнопкой; сам `IDeviceSession.HomeAsync`/`DeviceSession.HomeAsync` (`$H`) не трогается — это независимая возможность уровня сессии устройства. `ConnectionView.axaml` лишается фоновых рамок вокруг индикатора связи и блока машинного состояния/позиции и текстовой подписи состояния подключения — остаётся голый `Ellipse` + `StackPanel` с телеметрией + баннер ошибки (без изменений). `MainView.axaml` оборачивает всё это в один `Border` (`Grid ColumnDefinitions="*,Auto,Auto,Auto,Auto"`, первая колонка растягивается и обрезается — тот же приём, что уже используется в текущей шапке для `ContentControl(ConnectionStatus)`, только теперь на всю объединённую строку, а не только на блок статуса подключения), добавляет новый `Border`-оверлей для модалки аварии по прецеденту существующей модалки подключения, и продлевает `DockPanel.IsEnabled` на оба модальных состояния через новое `IsAnyModalVisible`.

**Tech Stack:** Avalonia UI, C# 12/.NET 10, xUnit + `Avalonia.Headless` (`ArctZ.Tests`, коллекция `AvaloniaHeadless`) для style-тестов, ReactiveUI + Zafiro.UI.Commands для VM-тестов.

## Global Constraints

- Спека: `docs/superpowers/specs/2026-08-05-header-status-alarm-redesign-design.md`. Опирается на `docs/superpowers/specs/2026-08-04-header-mobile-ux-design.md` (уже реализован, план `docs/superpowers/plans/2026-08-04-header-mobile-ux.md`) — `HeaderBorder`/`HeaderBorder.Bounds.Height` → `ComputeJoystickRadius` (`MainView.axaml.cs`) и его тесты (`ArctZ.Tests/Views/MainViewJoystickRadiusTests.cs`) этим планом не трогаются; `x:Name="HeaderBorder"` не переименовывается.
- Новое вычисляемое свойство `ConnectionViewModel.IsAlarmModalVisible => LastAlarmCode is not null` (`ConnectionViewModel.cs`, рядом с `IsConnectionModalVisible`).
- Новое вычисляемое свойство `ConnectionViewModel.IsAnyModalVisible => IsConnectionModalVisible || IsAlarmModalVisible`.
- Оба новых свойства пересчитываются через уже существующий блок `this.WhenAnyValue(...).Subscribe(_ => this.RaisePropertyChanged(...))` (`ConnectionViewModel.cs:185-196`) — `x.LastAlarmCode` уже в списке отслеживаемых значений, новой подписки не требуется.
- `ConnectionViewModel.HomeCommand` и приватный `HomeAsync()`-обёртка удаляются целиком. `IDeviceSession.HomeAsync`/`DeviceSession.HomeAsync` (`$H`) и `ArctZ.Tests/Services/Device/DeviceSessionTests.cs` — **не в скоупе**, не трогаются.
- Новый style-класс `Button.icon-action` в `ArctZ/Themes/HudControls.axaml`: `MinWidth="44"`, `MinHeight="44"`, `Padding="10"`, `FontSize="18"` (тот же минимальный touch-таргет, что у `Button.header-action`, обоснование — `docs/superpowers/specs/2026-08-04-header-mobile-ux-design.md`, раздел «Touch-таргеты»).
- Иконка отключения — глиф `⏻`, командa `Connection.DisconnectCommand` (без изменений).
- `ArctZ.Tests` не содержит View-тестов для XAML-разметки/биндингов — для XAML-only задач проверка через `dotnet build` на обоих затронутых head'ах (`ArctZ.Desktop`, `ArctZ.Browser`). Новые style-классы тестируются headless-рендерингом отдельного контрола, по прецеденту `ArctZ.Tests/Themes/HudControlsHeaderActionTests.cs`.
- Баннер ошибки (`Connection.HasError`/`Connection.ErrorMessage`, т.е. `LastError` — некритичные ошибки соединения) остаётся внутри `ConnectionView.axaml` без изменений; новая модалка аварии реагирует только на `IsAlarmModalVisible` (`LastAlarmCode`), не на `HasError`.
- Ничего не меняется в `ProgramViewModel.cs`, `MainView.axaml.cs`, `IDeviceSession`/`DeviceSession` — только `ConnectionViewModel.cs`, `ConnectionView.axaml`, `MainView.axaml`, `HudControls.axaml`.

---

### Task 1: `ConnectionViewModel` — `IsAlarmModalVisible` / `IsAnyModalVisible`, TDD

**Files:**
- Modify: `ArctZ/ViewModels/ConnectionViewModel.cs`
- Test: `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs`

**Interfaces:**
- Consumes: существующие `ConnectionViewModel.LastAlarmCode` (`int?`), `IsConnectionModalVisible` (`bool`), `ResetAlarmCommand` (`IEnhancedCommand<Unit>`) — без изменений.
- Produces: `ConnectionViewModel.IsAlarmModalVisible` (`bool`), `ConnectionViewModel.IsAnyModalVisible` (`bool`) — потребляются в Task 5 (модалка аварии, `DockPanel.IsEnabled`).

- [ ] **Step 1: Написать падающий тест**

В `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs` добавить перед закрывающей `}` класса (после `ToggleGCodeLogCommand_TogglesIsGCodeLogOpen`):

```csharp

    [Fact]
    public async Task IsAlarmModalVisible_TracksAlarmTriggerAndReset()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = CreateVm(realTransport);
        await vm.ConnectCommand.Execute();
        Assert.False(vm.IsAlarmModalVisible);
        Assert.False(vm.IsAnyModalVisible);

        realTransport.SimulateReceivedLine("ALARM:1");
        Assert.True(vm.IsAlarmModalVisible);
        Assert.True(vm.IsAnyModalVisible);

        // ResetAlarmCommand is IEnhancedCommand<Unit> (ReactiveUI), which has no ExecuteAsync —
        // Execute() returns a cold IObservable<Unit> that only starts running once subscribed.
        // .GetAwaiter() (System.Reactive.Linq.Observable, already global-used via GlobalUsings.cs)
        // subscribes immediately (starting ResetAlarmAsync's execution up to its "$X" send, which
        // suspends until the queue's TaskCompletionSource resolves) and returns an AsyncSubject<Unit>
        // that is itself awaitable, so it can be captured now and awaited after unblocking the
        // in-flight "$X" with a simulated "ok" — same fire-now/unblock/await-later idiom as
        // ProgramViewModelPlaybackTests' `var playTask = vm.PlayCommand.ExecuteAsync(null); ...
        // transport.SimulateReceivedLine("ok"); await playTask;`, adapted to this VM's ReactiveUI
        // commands instead of ProgramViewModel's CommunityToolkit IAsyncRelayCommand ones.
        var resetAwaiter = vm.ResetAlarmCommand.Execute().GetAwaiter();
        realTransport.SimulateReceivedLine("ok");
        await resetAwaiter;

        Assert.False(vm.IsAlarmModalVisible);
        Assert.False(vm.IsAnyModalVisible);
    }
```

- [ ] **Step 2: Запустить тест, убедиться что он падает**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter IsAlarmModalVisible_TracksAlarmTriggerAndReset`
Expected: сборка падает с ошибкой компиляции — `'ConnectionViewModel' does not contain a definition for 'IsAlarmModalVisible'` (и `'IsAnyModalVisible'`), потому что свойств ещё нет.

- [ ] **Step 3: Добавить вычисляемые свойства**

В `ArctZ/ViewModels/ConnectionViewModel.cs` найти:

```csharp
    public bool IsConnectionModalVisible => Session is null || ConnectionState != ConnectionState.Connected;
```

Заменить на:

```csharp
    public bool IsConnectionModalVisible => Session is null || ConnectionState != ConnectionState.Connected;

    // Авария (LastAlarmCode) блокирует основной экран отдельной модалкой; обычная ошибка
    // соединения (LastError) остаётся баннером внутри ConnectionView — см. HasError/ErrorMessage.
    public bool IsAlarmModalVisible => LastAlarmCode is not null;

    public bool IsAnyModalVisible => IsConnectionModalVisible || IsAlarmModalVisible;
```

Затем найти блок пересчёта уведомлений:

```csharp
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(IsConnectionModalVisible));
                this.RaisePropertyChanged(nameof(ConnectionStateLabel));
                this.RaisePropertyChanged(nameof(MachineStateLabel));
                this.RaisePropertyChanged(nameof(PositionLabel));
                this.RaisePropertyChanged(nameof(HasError));
                this.RaisePropertyChanged(nameof(ErrorMessage));
            })
```

Заменить на:

```csharp
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(IsConnectionModalVisible));
                this.RaisePropertyChanged(nameof(IsAlarmModalVisible));
                this.RaisePropertyChanged(nameof(IsAnyModalVisible));
                this.RaisePropertyChanged(nameof(ConnectionStateLabel));
                this.RaisePropertyChanged(nameof(MachineStateLabel));
                this.RaisePropertyChanged(nameof(PositionLabel));
                this.RaisePropertyChanged(nameof(HasError));
                this.RaisePropertyChanged(nameof(ErrorMessage));
            })
```

- [ ] **Step 4: Запустить тест, убедиться что проходит**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter IsAlarmModalVisible_TracksAlarmTriggerAndReset`
Expected: PASS, 1/1.

- [ ] **Step 5: Прогнать весь набор VM-тестов на регрессию**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter ConnectionViewModelTests`
Expected: все тесты в файле проходят (существующие + новый).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/ViewModels/ConnectionViewModel.cs ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs
git commit -m "feat: add IsAlarmModalVisible/IsAnyModalVisible to ConnectionViewModel"
```

---

### Task 2: Style-класс `Button.icon-action` — TDD

**Files:**
- Create: `ArctZ.Tests/Themes/HudControlsIconActionTests.cs`
- Modify: `ArctZ/Themes/HudControls.axaml`

**Interfaces:**
- Produces: CSS-подобный класс `icon-action` на `Button` — применяется в Task 4 к кнопке отключения в единой панели статуса `MainView.axaml`.

- [ ] **Step 1: Написать падающий тест**

Создать `ArctZ.Tests/Themes/HudControlsIconActionTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Threading;

namespace ArctZ.Tests.Themes;

[Collection("AvaloniaHeadless")]
public class HudControlsIconActionTests
{
    public HudControlsIconActionTests() => AvaloniaHeadlessBootstrap.EnsureInitialized();

    [Fact]
    public void IconActionButton_GetsMinimumTouchTarget()
    {
        var button = new Button();
        button.Classes.Add("icon-action");

        var window = new Window { Content = button };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(44, button.MinWidth);
        Assert.Equal(44, button.MinHeight);

        window.Close();
    }
}
```

- [ ] **Step 2: Запустить тест, убедиться что падает**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter HudControlsIconActionTests`
Expected: FAIL — `button.MinWidth`/`button.MinHeight` по умолчанию `NaN`, не `44`; класса `icon-action` ещё нет ни в одном `Style`.

- [ ] **Step 3: Добавить стиль**

В `ArctZ/Themes/HudControls.axaml` найти конец файла:

```xml
  <!-- Hairline separator between command groups (connection vs. playback) in the header action row. -->
  <Style Selector="Border.header-divider">
    <Setter Property="Width" Value="1" />
    <Setter Property="Margin" Value="4,4" />
    <Setter Property="Background" Value="{DynamicResource HudBorderBrush}" />
  </Style>

</Styles>
```

Заменить на:

```xml
  <!-- Hairline separator between command groups (connection vs. playback) in the header action row. -->
  <Style Selector="Border.header-divider">
    <Setter Property="Width" Value="1" />
    <Setter Property="Margin" Value="4,4" />
    <Setter Property="Background" Value="{DynamicResource HudBorderBrush}" />
  </Style>

  <!-- Icon-only header action (disconnect ⏻): lives in the unified status panel instead of the
       swiping action row, so it needs the same 44px minimum touch target as Button.header-action
       even though its content is a single glyph, not a text label. -->
  <Style Selector="Button.icon-action">
    <Setter Property="MinWidth" Value="44" />
    <Setter Property="MinHeight" Value="44" />
    <Setter Property="Padding" Value="10" />
    <Setter Property="FontSize" Value="18" />
  </Style>

</Styles>
```

- [ ] **Step 4: Запустить тест, убедиться что проходит**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter HudControlsIconActionTests`
Expected: PASS, 1/1.

- [ ] **Step 5: Полная сборка**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: `Build succeeded`, 0 ошибок.

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Themes/HudControls.axaml ArctZ.Tests/Themes/HudControlsIconActionTests.cs
git commit -m "feat: add icon-action button style for header status panel"
```

---

### Task 3: Убрать рамки/подпись из `ConnectionView.axaml`

**Files:**
- Modify: `ArctZ/Views/ConnectionView.axaml`

**Interfaces:**
- Consumes: `ConnectionViewModel.ConnectionState`, `IsConnectionModalVisible`, `MachineStateLabel`, `PositionLabel`, `HasError`, `ErrorMessage` — без изменений (все уже существуют).
- Produces: визуальная разметка компонента без собственных фоновых рамок вокруг индикатора связи и блока машинного состояния — потребляется визуально в Task 4 (единая панель статуса `MainView.axaml`, где `ConnectionView` рендерится через `ContentControl(ConnectionStatus)`).

- [ ] **Step 1: Убрать `HeaderedContainer` вокруг индикатора и подпись состояния подключения, убрать фон/рамку у блока машинного состояния**

В `ArctZ/Views/ConnectionView.axaml` найти:

```xml
    <WrapPanel ItemSpacing="10" LineSpacing="10" VerticalAlignment="Center">
        <HeaderedContainer Padding="10,6">
            <EdgePanel VerticalAlignment="Center">
                <EdgePanel.StartContent>
                    <Ellipse Width="8" Height="8" VerticalAlignment="Center"
                             Fill="{Binding ConnectionState, Converter={StaticResource StateToBrush}}" />
                </EdgePanel.StartContent>
                <TextBlock Text="{Binding ConnectionStateLabel}" Margin="8,0,0,0" VerticalAlignment="Center" />
            </EdgePanel>
        </HeaderedContainer>

        <Border Background="{StaticResource HudPanelElevatedBrush}" BorderBrush="{StaticResource HudBorderStrongBrush}"
                BorderThickness="1" Padding="10,6" IsVisible="{Binding !IsConnectionModalVisible}">
            <StackPanel Orientation="Horizontal" Spacing="10">
                <TextBlock Classes="telemetry" FontSize="13" Text="{Binding MachineStateLabel}" />
                <TextBlock Classes="telemetry" FontSize="13" Text="{Binding PositionLabel}" />
            </StackPanel>
        </Border>

        <Border IsVisible="{Binding HasError}" Background="{StaticResource HudWarningDimBrush}"
                BorderBrush="{StaticResource HudWarningBrush}" BorderThickness="1" Padding="10,6">
            <TextBlock Foreground="{StaticResource HudWarningBrush}" Text="{Binding ErrorMessage}" />
        </Border>

    </WrapPanel>
```

Заменить на:

```xml
    <WrapPanel ItemSpacing="10" LineSpacing="10" VerticalAlignment="Center">
        <Ellipse Width="8" Height="8" VerticalAlignment="Center"
                 Fill="{Binding ConnectionState, Converter={StaticResource StateToBrush}}" />

        <StackPanel Orientation="Horizontal" Spacing="10" VerticalAlignment="Center"
                    IsVisible="{Binding !IsConnectionModalVisible}">
            <TextBlock Classes="telemetry" FontSize="13" Text="{Binding MachineStateLabel}" />
            <TextBlock Classes="telemetry" FontSize="13" Text="{Binding PositionLabel}" />
        </StackPanel>

        <Border IsVisible="{Binding HasError}" Background="{StaticResource HudWarningDimBrush}"
                BorderBrush="{StaticResource HudWarningBrush}" BorderThickness="1" Padding="10,6">
            <TextBlock Foreground="{StaticResource HudWarningBrush}" Text="{Binding ErrorMessage}" />
        </Border>

    </WrapPanel>
```

Индикатор связи теперь просто цветная точка (без текстовой подписи «Подключено»/«Не подключено» — модалка подключения уже показывает полный текст, пока станок не подключён).

- [ ] **Step 2: Собрать**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: `Build succeeded`, 0 ошибок.

- [ ] **Step 3: Прогнать тесты на регрессию (резолв `ConnectionView` через `DataTypeViewLocator`)**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter DataTypeViewLocatorTests`
Expected: PASS — тест проверяет только тип резолва (`ConnectionViewModel` → `ConnectionView`), не внутреннюю структуру, так что не ломается удалением элементов разметки.

- [ ] **Step 4: Commit**

```bash
git add ArctZ/Views/ConnectionView.axaml
git commit -m "refactor: strip per-item borders and connection-state label from ConnectionView"
```

---

### Task 4: Единая панель статуса в `MainView.axaml`

**Files:**
- Modify: `ArctZ/Views/MainView.axaml`

**Interfaces:**
- Consumes: `Button.icon-action` (Task 2), `Border.header-divider` (уже существует), `ConnectionViewModel.DisconnectCommand` (уже существует), обновлённый `ConnectionView.axaml` (Task 3, рендерится через `ContentControl(ConnectionStatus)`).
- Produces: финальную структуру панели статуса — ничего последующего от неё программно не зависит (Task 5/6/7 — независимые изменения в других частях того же файла).

- [ ] **Step 1: Заменить `HeaderStatusRow`-грид и убрать кнопку «Отключить» из ряда действий**

В `ArctZ/Views/MainView.axaml` найти:

```xml
                <StackPanel x:Name="HeaderPanel" Spacing="8">
                    <Grid x:Name="HeaderStatusRow" ColumnDefinitions="*,Auto">
                        <ContentControl x:Name="ConnectionStatus" Grid.Column="0" ClipToBounds="True" Content="{Binding Connection}" />
                        <Border Grid.Column="1" Background="{StaticResource HudPanelElevatedBrush}" BorderBrush="{StaticResource HudBorderStrongBrush}"
                                BorderThickness="1" Padding="10,6" Margin="8,0,0,0" VerticalAlignment="Center">
                            <TextBlock Classes="telemetry" FontSize="14" Text="{Binding PlaybackStateLabel}" />
                        </Border>
                    </Grid>
                    <ScrollViewer x:Name="HeaderActionsScroller" HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled">
                        <StackPanel x:Name="HeaderActions" Orientation="Horizontal" Spacing="8">
                            <Button Classes="header-action" Content="Homing" Command="{Binding Connection.HomeCommand}" />
                            <Button Classes="danger header-action" Content="Сброс аварии" Command="{Binding Connection.ResetAlarmCommand}" />
                            <Button Classes="header-action" Content="Отключить" Command="{Binding Connection.DisconnectCommand}" />
                            <Border Classes="header-divider" />
                            <Button Classes="primary header-action" Content="Пуск" Command="{Binding PlayCommand}" />
                            <Button Classes="header-action" Content="Пауза" Command="{Binding PauseCommand}" />
                            <Button Classes="danger header-action" Content="Стоп" Command="{Binding StopCommand}" />
                            <Border Classes="header-divider" />
                            <Button Classes="header-action" Content="Лог G-code" Command="{Binding Connection.ToggleGCodeLogCommand}" />
                        </StackPanel>
                    </ScrollViewer>
                </StackPanel>
```

Заменить на:

```xml
                <StackPanel x:Name="HeaderPanel" Spacing="8">
                    <Border x:Name="HeaderStatusRow" Background="{StaticResource HudPanelElevatedBrush}" BorderBrush="{StaticResource HudBorderStrongBrush}"
                            BorderThickness="1" Padding="10,6">
                        <Grid ColumnDefinitions="*,Auto,Auto,Auto,Auto" VerticalAlignment="Center">
                            <ContentControl x:Name="ConnectionStatus" Grid.Column="0" ClipToBounds="True" Content="{Binding Connection}" />
                            <Border Grid.Column="1" Classes="header-divider" />
                            <TextBlock Grid.Column="2" Classes="telemetry" FontSize="14" VerticalAlignment="Center" Text="{Binding PlaybackStateLabel}" />
                            <Border Grid.Column="3" Classes="header-divider" />
                            <Button Grid.Column="4" Classes="icon-action" Content="⏻" Command="{Binding Connection.DisconnectCommand}" />
                        </Grid>
                    </Border>
                    <ScrollViewer x:Name="HeaderActionsScroller" HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled">
                        <StackPanel x:Name="HeaderActions" Orientation="Horizontal" Spacing="8">
                            <Button Classes="header-action" Content="Homing" Command="{Binding Connection.HomeCommand}" />
                            <Button Classes="danger header-action" Content="Сброс аварии" Command="{Binding Connection.ResetAlarmCommand}" />
                            <Border Classes="header-divider" />
                            <Button Classes="primary header-action" Content="Пуск" Command="{Binding PlayCommand}" />
                            <Button Classes="header-action" Content="Пауза" Command="{Binding PauseCommand}" />
                            <Button Classes="danger header-action" Content="Стоп" Command="{Binding StopCommand}" />
                            <Border Classes="header-divider" />
                            <Button Classes="header-action" Content="Лог G-code" Command="{Binding Connection.ToggleGCodeLogCommand}" />
                        </StackPanel>
                    </ScrollViewer>
                </StackPanel>
```

`Homing`/`Сброс аварии` пока остаются в ряду действий — их удаление и удаление `Connection.HomeCommand` в VM происходит вместе, атомарно, в Task 6 (иначе кнопка `Homing` на промежуточном шаге ссылалась бы на несуществующую команду).

- [ ] **Step 2: Собрать оба затронутых head'а**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: `Build succeeded`, 0 ошибок.

Run: `dotnet build ArctZ.Browser/ArctZ.Browser.csproj`
Expected: `Build succeeded`, 0 ошибок.

- [ ] **Step 3: Commit**

```bash
git add ArctZ/Views/MainView.axaml
git commit -m "refactor: unify header status row into a single panel with disconnect icon"
```

---

### Task 5: Модалка аварии в `MainView.axaml`

**Files:**
- Modify: `ArctZ/Views/MainView.axaml`

**Interfaces:**
- Consumes: `ConnectionViewModel.IsAlarmModalVisible`, `IsAnyModalVisible` (Task 1), `ConnectionViewModel.ErrorMessage`/`ResetAlarmCommand` (уже существуют).
- Produces: третий модальный оверлей в корневом `Grid` — ничего последующего от него программно не зависит.

- [ ] **Step 1: Расширить `DockPanel.IsEnabled` на оба модальных состояния**

В `ArctZ/Views/MainView.axaml` найти:

```xml
        <DockPanel IsEnabled="{Binding !Connection.IsConnectionModalVisible}">
```

Заменить на:

```xml
        <DockPanel IsEnabled="{Binding !Connection.IsAnyModalVisible}">
```

- [ ] **Step 2: Добавить оверлей модалки аварии после модалки подключения**

В `ArctZ/Views/MainView.axaml` найти:

```xml
                    <Button Classes="primary" Content="Подключить" Command="{Binding ConnectCommand}"
                            HorizontalAlignment="Stretch" />
                </StackPanel>
            </Border>
        </Border>
    </Grid>
</UserControl>
```

Заменить на:

```xml
                    <Button Classes="primary" Content="Подключить" Command="{Binding ConnectCommand}"
                            HorizontalAlignment="Stretch" />
                </StackPanel>
            </Border>
        </Border>

        <Border IsVisible="{Binding Connection.IsAlarmModalVisible}" Background="{StaticResource HudScrimBrush}">
            <Border x:DataType="vm:ConnectionViewModel" DataContext="{Binding Connection}"
                    Width="360" Background="{StaticResource HudPanelElevatedBrush}"
                    BorderBrush="{StaticResource HudBorderStrongBrush}" BorderThickness="1"
                    Padding="20" HorizontalAlignment="Center" VerticalAlignment="Center">
                <StackPanel Spacing="14">
                    <TextBlock Classes="section-heading" Text="АВАРИЯ" />
                    <TextBlock TextWrapping="Wrap" Text="{Binding ErrorMessage}" />
                    <Button Classes="danger" Content="Сброс аварии" Command="{Binding ResetAlarmCommand}"
                            HorizontalAlignment="Stretch" />
                </StackPanel>
            </Border>
        </Border>
    </Grid>
</UserControl>
```

- [ ] **Step 3: Собрать**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: `Build succeeded`, 0 ошибок. (Ошибка о несуществующем `Connection.IsAlarmModalVisible`/`IsAnyModalVisible` означала бы, что Task 1 не завершён или свойства названы иначе — сверить `ArctZ/ViewModels/ConnectionViewModel.cs`.)

- [ ] **Step 4: Прогнать полный набор тестов**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: все тесты проходят, включая новый `IsAlarmModalVisible_TracksAlarmTriggerAndReset` (Task 1) и `HudControlsIconActionTests` (Task 2).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Views/MainView.axaml
git commit -m "feat: add blocking alarm modal that surfaces on machine alarm"
```

---

### Task 6: Убрать `Homing` целиком и `Сброс аварии` из ряда действий

**Files:**
- Modify: `ArctZ/Views/MainView.axaml`
- Modify: `ArctZ/ViewModels/ConnectionViewModel.cs`

**Interfaces:**
- Consumes: ничего нового.
- Produces: `ConnectionViewModel` больше не содержит `HomeCommand`. Ряд действий в шапке содержит только `Пуск`/`Пауза`/`Стоп` │ `Лог G-code` (один разделитель).

Одна атомарная задача: кнопка `Homing` в `MainView.axaml` ссылается на `Connection.HomeCommand`, поэтому XAML и VM должны смениться вместе — иначе разметка на промежуточном шаге не скомпилируется.

- [ ] **Step 1: Убрать кнопки `Homing` и `Сброс аварии` из ряда действий**

В `ArctZ/Views/MainView.axaml` найти:

```xml
                        <StackPanel x:Name="HeaderActions" Orientation="Horizontal" Spacing="8">
                            <Button Classes="header-action" Content="Homing" Command="{Binding Connection.HomeCommand}" />
                            <Button Classes="danger header-action" Content="Сброс аварии" Command="{Binding Connection.ResetAlarmCommand}" />
                            <Border Classes="header-divider" />
                            <Button Classes="primary header-action" Content="Пуск" Command="{Binding PlayCommand}" />
```

Заменить на:

```xml
                        <StackPanel x:Name="HeaderActions" Orientation="Horizontal" Spacing="8">
                            <Button Classes="primary header-action" Content="Пуск" Command="{Binding PlayCommand}" />
```

- [ ] **Step 2: Убрать `HomeCommand` из `ConnectionViewModel`**

В `ArctZ/ViewModels/ConnectionViewModel.cs` найти:

```csharp
    public IEnhancedCommand<Unit> ConnectCommand { get; }
    public IEnhancedCommand<Unit> DisconnectCommand { get; }
    public IEnhancedCommand<Unit> HomeCommand { get; }
    public IEnhancedCommand<Unit> ResetAlarmCommand { get; }
    public IEnhancedCommand<Unit> ToggleGCodeLogCommand { get; }
```

Заменить на:

```csharp
    public IEnhancedCommand<Unit> ConnectCommand { get; }
    public IEnhancedCommand<Unit> DisconnectCommand { get; }
    public IEnhancedCommand<Unit> ResetAlarmCommand { get; }
    public IEnhancedCommand<Unit> ToggleGCodeLogCommand { get; }
```

Затем найти:

```csharp
        DisconnectCommand = Track(ReactiveCommand.CreateFromTask(DisconnectAsync, notPlaybackLocked)
            .Enhance(text: "Отключить", name: "DisconnectCommand"));
        HomeCommand = Track(ReactiveCommand.CreateFromTask(HomeAsync, notPlaybackLocked)
            .Enhance(text: "Homing", name: "HomeCommand"));
        ResetAlarmCommand = Track(ReactiveCommand.CreateFromTask(ResetAlarmAsync)
            .Enhance(text: "Сброс аварии", name: "ResetAlarmCommand"));
```

Заменить на:

```csharp
        DisconnectCommand = Track(ReactiveCommand.CreateFromTask(DisconnectAsync, notPlaybackLocked)
            .Enhance(text: "Отключить", name: "DisconnectCommand"));
        ResetAlarmCommand = Track(ReactiveCommand.CreateFromTask(ResetAlarmAsync)
            .Enhance(text: "Сброс аварии", name: "ResetAlarmCommand"));
```

Затем найти:

```csharp
    private Task HomeAsync() => Session?.HomeAsync() ?? Task.CompletedTask;

    private async Task ResetAlarmAsync()
```

Заменить на:

```csharp
    private async Task ResetAlarmAsync()
```

`notPlaybackLocked` не удаляется — используется `DisconnectCommand`.

- [ ] **Step 3: Собрать**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: `Build succeeded`, 0 ошибок.

Run: `dotnet build ArctZ.Browser/ArctZ.Browser.csproj`
Expected: `Build succeeded`, 0 ошибок.

- [ ] **Step 4: Прогнать полный набор тестов**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: все тесты проходят. `ArctZ.Tests/Services/Device/DeviceSessionTests.cs` не затронут (тестирует `IDeviceSession.HomeAsync`, который не менялся).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Views/MainView.axaml ArctZ/ViewModels/ConnectionViewModel.cs
git commit -m "refactor: remove Homing button/command and move alarm reset into the alarm modal"
```

---

### Task 7: Визуальная проверка

**Files:** нет изменений (если не найдены дефекты — тогда точечные правки с отдельным коммитом).

- [ ] **Step 1: Запустить приложение и проверить единую панель статуса**

Использовать skill `run` (или `dotnet run --project ArctZ.Browser/ArctZ.Browser.csproj` с ресайзом окна браузера до ~360–400px по ширине; для реального сенсорного устройства — skill `mobile-build-setup`).

В демо-режиме (`ConnectionEndpointKind.Demo`) подключиться и проверить:
- панель статуса — один визуальный блок (общий фон/рамка), не два отдельных, как раньше;
- на узкой ширине (~360px) длинная строка машинного состояния/позиции обрезается, не наезжает на метку воспроизведения/иконку отключения (первая колонка `*` с `ClipToBounds`, остальные `Auto`);
- иконка `⏻` кликабельна, отключает сессию (после клика открывается модалка подключения);
- в ряду действий — только `Пуск`/`Пауза`/`Стоп` │ `Лог G-code`, один разделитель, кнопок `Homing`/`Сброс аварии`/`Отключить` там больше нет;
- высота шапки визуально не меняется при изменении ширины окна (кроме случая появления баннера ошибки).

Expected: все пункты выполняются, без наложений/обрезаний.

- [ ] **Step 2: Спровоцировать аварию и проверить модалку**

В демо-режиме вызвать `AlarmTriggered` (через демо-транспорт — см. `ArctZ.Tests/Services/Device/FakeDeviceTransport.cs`/`DemoDeviceTransport`, если демо-режим поддерживает симуляцию `ALARM:`; иначе — на реальном устройстве вручную спровоцировать аварию, например упором в концевик).

Проверить:
- модалка аварии перекрывает весь экран (джойстики и панель программы недоступны для тапа/клика);
- в модалке виден текст ошибки (`"Авария FluidNC: код N"`) и кнопка «Сброс аварии»;
- клик по «Сброс аварии» отправляет `$X`, модалка закрывается сама после подтверждения (`ok`), без ручного закрытия;
- баннер под панелью статуса (некритичная `LastError`, например обрыв связи) по-прежнему появляется отдельно и НЕ вызывает модалку аварии.

Expected: все пункты выполняются.

- [ ] **Step 3: Проверить на широком экране (регрессия)**

Ресайз до ~1200–1920px (или запуск `ArctZ.Desktop`).

Проверить: панель статуса и ряд действий помещаются в одну строку без визуальных дефектов, джойстики и панель программы не задеты.

Expected: без регрессий по сравнению с состоянием до Task 1-6.

- [ ] **Step 4: Зафиксировать найденные точечные исправления (если есть)**

Если на Step 1-3 обнаружены дефекты — исправить, повторить проверку, затем:

```bash
git add ArctZ/Views/MainView.axaml ArctZ/Views/ConnectionView.axaml ArctZ/Themes/HudControls.axaml ArctZ/ViewModels/ConnectionViewModel.cs
git commit -m "fix: address visual issues found in header status/alarm modal verification pass"
```

Если дефектов не найдено — коммит не требуется, задача считается завершённой по результатам Task 1-6.
