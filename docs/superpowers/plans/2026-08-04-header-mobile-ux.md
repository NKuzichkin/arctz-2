# Шапка MainView: мобильный UX — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Шапка `MainView` переходит с одного плоского `WrapPanel` (8 разнородных элементов вперемешку) на две фиксированные зоны: строка статуса (связь/станок/состояние воспроизведения) сверху и горизонтально прокручиваемая свайпом строка команд (Homing/Сброс аварии/Отключить/Пуск/Пауза/Стоп/Лог G-code) снизу — с более крупными touch-таргетами и визуальной группировкой по смыслу.

**Architecture:** Чистый view-слой, без изменений в ViewModel-ах. `ConnectionView.axaml` лишается трёх кнопок действий (Homing/Сброс аварии/Отключить) — остаётся только статус (индикатор, метка состояния, машинное состояние/позиция, баннер ошибки). `MainView.axaml` перестраивает шапку: `Grid ColumnDefinitions="*,Auto"` для строки статуса (слева — `ContentControl` со статусом подключения, справа — бейдж состояния воспроизведения) и `ScrollViewer HorizontalScrollBarVisibility="Auto"` вокруг горизонтального `StackPanel` для строки команд, куда переезжают три кнопки подключения (с биндингом напрямую на `Connection.HomeCommand`/`Connection.ResetAlarmCommand`/`Connection.DisconnectCommand`, по уже существующему в файле прецеденту `Connection.ToggleGCodeLogCommand`) вперемешку с Пуск/Пауза/Стоп/Лог, разделённые тонкими visual-разделителями между группами. Новый общий style-класс `Button.header-action` (в `Themes/HudControls.axaml`) даёт всем кнопкам этой строки минимум 44px высоты — независимо от базового `ButtonPadding`.

**Tech Stack:** Avalonia UI (ScrollViewer touch-панорамирование "из коробки" на Android/iOS), C# 12/.NET 10, xUnit + `Avalonia.Headless` (`ArctZ.Tests`, коллекция `AvaloniaHeadless`).

## Global Constraints

- Спека: `docs/superpowers/specs/2026-08-04-header-mobile-ux-design.md`. Опирается на уже реализованный `docs/superpowers/specs/2026-08-04-three-band-main-layout-design.md` (`HeaderBorder`/`HeaderBorder.Bounds.Height` → `ComputeJoystickRadius` в `MainView.axaml.cs` — эта функция и её тесты в `ArctZ.Tests/Views/MainViewJoystickRadiusTests.cs` в этом плане не трогаются).
- Новый style-класс `Button.header-action` в `ArctZ/Themes/HudControls.axaml`: `MinHeight="44"`, `Padding="18,12"`.
- Новый style-класс `Border.header-divider` в `ArctZ/Themes/HudControls.axaml`: `Width="1"`, `Margin="4,4"`, `Background="{DynamicResource HudBorderBrush}"` (используем `DynamicResource`, не `StaticResource` — как и все остальные Hud*Brush-Setter'ы в этом файле ниже блока `Style.Resources`, см. комментарий в файле про порядок загрузки print-темы).
- Команды подключения биндятся напрямую из `MainView.axaml` как `Connection.HomeCommand` / `Connection.ResetAlarmCommand` / `Connection.DisconnectCommand` (точные имена — `ConnectionViewModel.cs:94-98`), а не через `ConnectionView` — тот же паттерн, что уже применён к `Connection.ToggleGCodeLogCommand`.
- `ArctZ.Tests` не содержит View-тестов для разметки/биндингов (см. `CLAUDE.md` и прецедент `2026-08-04-three-band-main-layout.md`) — для XAML-only задачи (Task 2) проверка через `dotnet build`. Новые style-классы (Task 1) — тестируемы headless-рендерингом отдельного `Button`/`Border`, по прецеденту `ArctZ.Tests/Themes/HudControlsPrintThemeTests.cs`.
- Ничего не меняется в `ViewModels/ConnectionViewModel.cs`, `ViewModels/ProgramViewModel.cs`, `MainView.axaml.cs` — только `HudControls.axaml`, `MainView.axaml`, `ConnectionView.axaml`.

---

### Task 1: Style-классы `Button.header-action` и `Border.header-divider` — TDD

**Files:**
- Create: `ArctZ.Tests/Themes/HudControlsHeaderActionTests.cs`
- Modify: `ArctZ/Themes/HudControls.axaml`

**Interfaces:**
- Produces: CSS-подобные классы `header-action` (на `Button`) и `header-divider` (на `Border`), применяемые в Task 2 внутри строки команд `MainView.axaml`. Никакого C#-API — только `Classes.Add("header-action")` / `Classes.Add("header-divider")` в XAML или коде.

- [ ] **Step 1: Написать падающий тест**

Создать `ArctZ.Tests/Themes/HudControlsHeaderActionTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Threading;

namespace ArctZ.Tests.Themes;

[Collection("AvaloniaHeadless")]
public class HudControlsHeaderActionTests
{
    public HudControlsHeaderActionTests() => AvaloniaHeadlessBootstrap.EnsureInitialized();

    [Fact]
    public void HeaderActionButton_GetsMinimumTouchHeight()
    {
        var button = new Button();
        button.Classes.Add("header-action");

        var window = new Window { Content = button };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(44, button.MinHeight);

        window.Close();
    }

    [Fact]
    public void HeaderDividerBorder_GetsHairlineWidth()
    {
        var border = new Border();
        border.Classes.Add("header-divider");

        var window = new Window { Content = border };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, border.Width);

        window.Close();
    }
}
```

- [ ] **Step 2: Запустить тесты, убедиться что оба падают**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter HudControlsHeaderActionTests`
Expected: FAIL, 2/2 — `button.MinHeight` по умолчанию `0` (не `44`), `border.Width` по умолчанию `NaN` (не `1`); классов ещё нет ни в одном `Style`.

- [ ] **Step 3: Добавить стили**

В `ArctZ/Themes/HudControls.axaml` найти конец файла:

```xml
  <Style Selector="Window.print Button.danger">
    <Setter Property="Background" Value="{DynamicResource HudBackgroundBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource HudBorderStrongBrush}" />
    <Setter Property="Foreground" Value="{DynamicResource HudTextPrimaryBrush}" />
    <Setter Property="BorderThickness" Value="2" />
  </Style>

</Styles>
```

Заменить на (два новых блока вставлены перед закрывающим тегом):

```xml
  <Style Selector="Window.print Button.danger">
    <Setter Property="Background" Value="{DynamicResource HudBackgroundBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource HudBorderStrongBrush}" />
    <Setter Property="Foreground" Value="{DynamicResource HudTextPrimaryBrush}" />
    <Setter Property="BorderThickness" Value="2" />
  </Style>

  <!-- Header action row (Homing/Сброс аварии/Отключить/Пуск/Пауза/Стоп/Лог G-code): runs inside
       a horizontally swiping ScrollViewer on phones, so it needs a touch target bigger than the
       shared ButtonPadding gives buttons elsewhere in the app. -->
  <Style Selector="Button.header-action">
    <Setter Property="MinHeight" Value="44" />
    <Setter Property="Padding" Value="18,12" />
  </Style>

  <!-- Hairline separator between command groups (connection vs. playback) in the header action row. -->
  <Style Selector="Border.header-divider">
    <Setter Property="Width" Value="1" />
    <Setter Property="Margin" Value="4,4" />
    <Setter Property="Background" Value="{DynamicResource HudBorderBrush}" />
  </Style>

</Styles>
```

- [ ] **Step 4: Запустить тесты, убедиться что проходят**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter HudControlsHeaderActionTests`
Expected: PASS, 2/2.

- [ ] **Step 5: Полная сборка**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: `Build succeeded`, 0 ошибок.

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Themes/HudControls.axaml ArctZ.Tests/Themes/HudControlsHeaderActionTests.cs
git commit -m "feat: add header-action/header-divider styles for mobile-friendly header buttons"
```

---

### Task 2: Перестроить шапку `MainView.axaml` и вынести кнопки подключения из `ConnectionView.axaml`

**Files:**
- Modify: `ArctZ/Views/MainView.axaml`
- Modify: `ArctZ/Views/ConnectionView.axaml`

**Interfaces:**
- Consumes: `Button.header-action`, `Border.header-divider` (Task 1); `ConnectionViewModel.HomeCommand`/`ResetAlarmCommand`/`DisconnectCommand` (`ConnectionViewModel.cs:94-98`, уже существуют, не меняются).
- Produces: финальную структуру шапки — ничего последующего от неё программно не зависит (Task 3 — только проверка).

Одна атомарная задача: `ConnectionView.axaml` (кнопки удаляются) и `MainView.axaml` (кнопки появляются в новом месте с новыми биндингами) должны смениться вместе — иначе три команды подключения на промежуточном шаге не будут доступны ни там, ни там.

- [ ] **Step 1: Убрать три кнопки действий из `ConnectionView.axaml`**

В `ArctZ/Views/ConnectionView.axaml` найти:

```xml
        <Button Content="Homing" Command="{Binding HomeCommand}" />
        <Button Classes="danger" Content="Сброс аварии" Command="{Binding ResetAlarmCommand}" />
        <Button Content="Отключить" Command="{Binding DisconnectCommand}" />
    </WrapPanel>
</UserControl>
```

Заменить на:

```xml
    </WrapPanel>
</UserControl>
```

Компонент теперь показывает только статус: индикатор связи, метку состояния, машинное состояние/позицию и (если есть) баннер ошибки.

- [ ] **Step 2: Перестроить шапку в `MainView.axaml`**

В `ArctZ/Views/MainView.axaml` найти:

```xml
            <Border x:Name="HeaderBorder" DockPanel.Dock="Top" Classes="reveal-1"
                    Background="{StaticResource HudPanelBrush}"
                    BorderBrush="{StaticResource HudBorderBrush}"
                    BorderThickness="0,0,0,1"
                    Padding="12,10">
                <WrapPanel x:Name="HeaderPanel" ItemSpacing="12" LineSpacing="8">
                    <ContentControl x:Name="ConnectionStatus" Content="{Binding Connection}" />
                    <WrapPanel x:Name="PlaybackButtons" ItemSpacing="8" LineSpacing="8" VerticalAlignment="Center">
                        <Button Classes="primary" Content="Пуск" Command="{Binding PlayCommand}" />
                        <Button Content="Пауза" Command="{Binding PauseCommand}" />
                        <Button Classes="danger" Content="Стоп" Command="{Binding StopCommand}" />
                        <Border Background="{StaticResource HudPanelElevatedBrush}" BorderBrush="{StaticResource HudBorderStrongBrush}"
                                BorderThickness="1" Padding="10,6" Margin="8,0,0,0">
                            <TextBlock Classes="telemetry" FontSize="14" Text="{Binding PlaybackStateLabel}" />
                        </Border>
                        <Button Content="Лог G-code" Command="{Binding Connection.ToggleGCodeLogCommand}" />
                    </WrapPanel>
                </WrapPanel>
            </Border>
```

Заменить на:

```xml
            <Border x:Name="HeaderBorder" DockPanel.Dock="Top" Classes="reveal-1"
                    Background="{StaticResource HudPanelBrush}"
                    BorderBrush="{StaticResource HudBorderBrush}"
                    BorderThickness="0,0,0,1"
                    Padding="12,10">
                <StackPanel x:Name="HeaderPanel" Spacing="8">
                    <Grid x:Name="HeaderStatusRow" ColumnDefinitions="*,Auto">
                        <ContentControl x:Name="ConnectionStatus" Grid.Column="0" Content="{Binding Connection}" />
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
            </Border>
```

- [ ] **Step 3: Собрать**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: `Build succeeded`, 0 ошибок. (Если есть ошибка о несуществующем `Connection.HomeCommand`/`Connection.ResetAlarmCommand`/`Connection.DisconnectCommand` — сверить точные имена в `ArctZ/ViewModels/ConnectionViewModel.cs:94-98`, они не должны были измениться в этом плане.)

- [ ] **Step 4: Собрать остальные платформенные head'ы**

Run: `dotnet build ArctZ.Browser/ArctZ.Browser.csproj`
Expected: `Build succeeded`, 0 ошибок.

- [ ] **Step 5: Прогнать полный набор тестов**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: все тесты проходят, включая `HudControlsHeaderActionTests` (Task 1) и уже существующие `MainViewJoystickRadiusTests`, `DataTypeViewLocatorTests` (последний — `Build_ConnectionViewModel_ResolvesToConnectionView`, проверяет только тип резолва, не внутреннюю структуру `ConnectionView`, так что не сломан удалением кнопок).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Views/MainView.axaml ArctZ/Views/ConnectionView.axaml
git commit -m "refactor: split MainView header into fixed status row and swipeable action row"
```

---

### Task 3: Визуальная проверка на мобильной ширине

**Files:** нет изменений (если не найдены дефекты — тогда точечные правки в `ArctZ/Views/MainView.axaml`/`ArctZ/Views/ConnectionView.axaml`/`ArctZ/Themes/HudControls.axaml` с отдельным коммитом).

- [ ] **Step 1: Запустить приложение и проверить шапку на узкой ширине**

Использовать skill `run` (или `mobile-build-setup`, если нужен реальный Android-эмулятор/устройство — см. `.claude/skills/mobile-build-setup/`) либо `dotnet run --project ArctZ.Browser/ArctZ.Browser.csproj` с ресайзом окна браузера до ~360–400px по ширине.

Проверить:
- строка статуса (индикатор связи, машинное состояние/позиция, бейдж состояния воспроизведения) не переносится и не обрезается на 360px;
- строка действий не переносит кнопки на вторую строку ни при какой ширине — вместо этого появляется горизонтальный скролл/свайп, когда кнопки не помещаются;
- разделители между группами команд видны и не сливаются с кнопками;
- кнопки `Homing`/`Сброс аварии`/`Отключить` по-прежнему рабочие (клик/тап вызывает соответствующую команду — то же поведение, что было в `ConnectionView`, но по новому месту в разметке);
- высота шапки визуально не меняется при изменении ширины окна (кроме случая появления баннера ошибки).

Expected: все пункты выполняются, без наложений/обрезаний.

- [ ] **Step 2: Проверить на широком экране (регрессия)**

Ресайз до ~1200–1920px (или запуск `ArctZ.Desktop`).

Проверить: строка действий по-прежнему помещается в одну строку без скролла (либо скролл не активен визуально, если все кнопки влезли — `ScrollViewer` не показывает полосу прокрутки, когда контент помещается), джойстики и панель программы не задеты (эта задача их не трогает).

Expected: без регрессий по сравнению с состоянием до Task 1-2.

- [ ] **Step 3: Зафиксировать найденные точечные исправления (если есть)**

Если на Step 1-2 обнаружены дефекты — исправить, повторить проверку, затем:

```bash
git add ArctZ/Views/MainView.axaml ArctZ/Views/ConnectionView.axaml ArctZ/Themes/HudControls.axaml
git commit -m "fix: address visual issues found in mobile header verification pass"
```

Если дефектов не найдено — коммит не требуется, задача считается завершённой по результатам Task 1-2.
