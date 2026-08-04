# MainView: единая трёхчастная раскладка — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `MainView.axaml` переходит с двух параллельных раскладок (широкая-колонки / узкая-строки, переключаемых по ширине окна) на единую раскладку для всех размеров экрана: сверху — шапка (статус подключения + Homing/Сброс/Отключить + Пуск/Пауза/Стоп/Лог), в середине — панель программы на всю ширину, снизу — оба джойстика по краям одной строки.

**Architecture:** Чистый view-слой, без изменений в ViewModel. `ContentGrid` становится статичным `RowDefinitions="*,Auto"` (без переключения в коде — это единственное состояние), с новым вложенным `Grid x:Name="JoystickBar" ColumnDefinitions="Auto,*,Auto"` во второй строке. Шапка — `Grid`+классы `.narrow` заменяются на простой `WrapPanel` (сам переносит контент на новую строку при нехватке ширины, без кода). `MainView.axaml.cs` лишается всей логики переключения (`_isNarrow`, брейкпоинт 700px, переключение `Classes`/`ColumnDefinitions`/`RowDefinitions` в рантайме) — остаётся только пересчёт `Radius` джойстиков на `SizeChanged` по новой формуле, использующей **фактическую** высоту шапки (`HeaderBorder.Bounds.Height`) вместо захардкоженной оценки.

**Tech Stack:** Avalonia UI 12.0.4 (WrapPanel `ItemSpacing`/`LineSpacing`), C# 12/.NET 10, xUnit (`ArctZ.Tests`).

## Global Constraints

- Новая формула радиуса — `MainView.ComputeJoystickRadius(mainViewWidth, mainViewHeight, headerHeight)`, чистая статическая функция (без побочных эффектов, без обращения к `this`/полям контрола) — тестируется напрямую в `ArctZ.Tests`.
- Константы (все `private const double` в `MainView.axaml.cs`):
  - `ContentGridChromeWidth = 54` (уже существует, переиспользуется как есть — горизонтальный хром вокруг `ContentGrid` не меняется).
  - `MinRadius = 50`, `MaxRadius = 110` (новый верхний предел — джойстики раньше сдерживались шириной звёздочной полуколонки, теперь сидят в `Auto`-колонках и ничем не ограничены сверху без явного предела).
  - `CenterGap = 24` (минимальный зазор между двумя джойстиками по центру нижней строки).
  - `ContentBorderVerticalChrome = 26` (вертикальные `Margin`+`BorderThickness` вокруг `ContentGrid`, без шапки).
  - `ContentGridVerticalMargin = 40` (вертикальный `Margin="20"` самого `ContentGrid`).
  - `JoystickBarTopMargin = 12` (отступ `JoystickBar` от панели программы).
  - `ProgramPanelMinHeight = 160` (минимум, который всегда остаётся панели программы).
  - `HeaderFallbackHeight = 44` (фолбэк, когда `HeaderBorder.Bounds.Height` ещё не посчитан layout-проходом — однострочная шапка, `Padding="12,10"` + одна строка контента).
- `ProgramPanel` (`ScrollViewer`) — без `MaxWidth`, всегда на всю доступную ширину (подтверждено пользователем в брейнсторминге).
- `Grid.RowDefinitions`/`ColumnDefinitions`, заданные **прямо в XAML-разметке** (не через `Style Setter`, не переключаемые в рантайме кодом), парсятся штатным XAML-конвертером один раз при загрузке — ограничение Avalonia («`RowDefinitionsProperty` не зарегистрированное `AvaloniaProperty`», см. `docs/superpowers/specs/2026-07-30-responsive-narrow-screen-layout-design.md`) касается **только** попытки менять их через `Style Setter` или переприсваивать в C# на лету; статичное значение в разметке — это не тот случай, писать как обычно.
- Спека: `docs/superpowers/specs/2026-08-04-three-band-main-layout-design.md` (заменяет `2026-07-30-responsive-narrow-screen-layout-design.md` и `2026-07-30-narrow-joystick-half-width-design.md`).
- `ArctZ.Tests` не содержит View-тестов (см. CLAUDE.md) — только для чистых функций вроде `ComputeJoystickRadius`. Остальная проверка — `dotnet build` + ручной/визуальный просмотр.

---

### Task 1: Новая функция `ComputeJoystickRadius` — TDD, аддитивно

**Files:**
- Create: `ArctZ.Tests/Views/MainViewJoystickRadiusTests.cs`
- Modify: `ArctZ/Views/MainView.axaml.cs`

**Interfaces:**
- Produces: `internal static double MainView.ComputeJoystickRadius(double mainViewWidth, double mainViewHeight, double headerHeight)` — Task 2 подключает её к `OnSizeChanged` вместо старой `ComputeNarrowJoystickRadius`.

Эта задача **не трогает** существующую `OnSizeChanged`/`ComputeNarrowJoystickRadius`/`_isNarrow` — новая функция и её тесты добавляются рядом, старое поведение продолжает работать без изменений до Task 2. Сборка остаётся зелёной на каждом шаге.

- [ ] **Step 1: Написать падающий тест**

Создать `ArctZ.Tests/Views/MainViewJoystickRadiusTests.cs`:

```csharp
using ArctZ.Views;

namespace ArctZ.Tests.Views;

public class MainViewJoystickRadiusTests
{
    [Theory]
    [InlineData(1200, 800, 60, 110)]     // просторный десктоп: упирается в верхний предел MaxRadius
    [InlineData(360, 700, 90, 70.5)]     // узкий телефон-портрет: ширина ограничивает
    [InlineData(250, 800, 60, 50)]       // вырожденная ширина: floor-clamp по MinRadius
    [InlineData(800, 300, 60, 50)]       // очень низкое окно: высота floor-clamp по MinRadius
    [InlineData(1000, 500, 60, 107)]     // высота ограничивает, но не floor/ceiling
    [InlineData(500, 400, 0, 59)]        // headerHeight=0 → включается HeaderFallbackHeight
    [InlineData(500, 400, -10, 59)]      // отрицательный headerHeight тоже триггерит фолбэк
    [InlineData(500, 400, 1, 80.5)]      // headerHeight=1 (>0) — фолбэк НЕ включается, используется как есть
    public void ComputeJoystickRadius_ReturnsExpectedRadius(
        double mainViewWidth, double mainViewHeight, double headerHeight, double expectedRadius)
    {
        var radius = MainView.ComputeJoystickRadius(mainViewWidth, mainViewHeight, headerHeight);

        Assert.Equal(expectedRadius, radius, precision: 3);
    }
}
```

- [ ] **Step 2: Убедиться, что тест не компилируется (функции ещё нет)**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter MainViewJoystickRadiusTests`
Expected: FAIL — ошибка компиляции `CS0117` ('MainView' does not contain a definition for 'ComputeJoystickRadius') либо аналогичная.

- [ ] **Step 3: Добавить константы и реализовать функцию**

В `ArctZ/Views/MainView.axaml.cs` найти (текущий блок констант, заканчивающийся перед конструктором):

```csharp
        private const double MainViewChromeHeight = 166;
        private const double NarrowProgramPanelMinHeight = 160;

        public MainView()
```

Заменить на (старые константы остаются нетронутыми, новые добавлены следом):

```csharp
        private const double MainViewChromeHeight = 166;
        private const double NarrowProgramPanelMinHeight = 160;

        // Новая геометрия для единой (не breakpoint-based) раскладки — см. ComputeJoystickRadius.
        private const double MinRadius = 50;
        private const double MaxRadius = 110;
        private const double CenterGap = 24;
        private const double ContentBorderVerticalChrome = 26;
        private const double ContentGridVerticalMargin = 40;
        private const double JoystickBarTopMargin = 12;
        private const double ProgramPanelMinHeight = 160;
        private const double HeaderFallbackHeight = 44;

        public MainView()
```

Затем найти конец существующего метода `ComputeNarrowJoystickRadius`:

```csharp
            return Math.Min(widthRadius, heightRadius);
        }

        private void OnLeftJoystickDown(object? sender, JoystickEventArgs e) => ViewModel?.OnLeftJoystickDown(e);
```

Заменить на (новый метод вставлен между старым методом и первым обработчиком джойстика):

```csharp
            return Math.Min(widthRadius, heightRadius);
        }

        internal static double ComputeJoystickRadius(double mainViewWidth, double mainViewHeight, double headerHeight)
        {
            var effectiveHeaderHeight = headerHeight > 0 ? headerHeight : HeaderFallbackHeight;

            var contentGridWidth = mainViewWidth - ContentGridChromeWidth;
            var widthRadius = (contentGridWidth - CenterGap) / 4;

            var contentGridHeight = mainViewHeight - effectiveHeaderHeight - ContentBorderVerticalChrome
                - ContentGridVerticalMargin - JoystickBarTopMargin;
            var joystickRowBudget = contentGridHeight - ProgramPanelMinHeight;
            var heightRadius = joystickRowBudget / 2;

            return Math.Clamp(Math.Min(widthRadius, heightRadius), MinRadius, MaxRadius);
        }

        private void OnLeftJoystickDown(object? sender, JoystickEventArgs e) => ViewModel?.OnLeftJoystickDown(e);
```

- [ ] **Step 4: Запустить тесты и убедиться, что проходят**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter MainViewJoystickRadiusTests`
Expected: PASS, 8/8.

- [ ] **Step 5: Полная сборка (убедиться, что старое поведение не сломано)**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: `Build succeeded`, 0 ошибок.

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Views/MainView.axaml.cs ArctZ.Tests/Views/MainViewJoystickRadiusTests.cs
git commit -m "feat: add ComputeJoystickRadius for unified header/program/joystick layout"
```

---

### Task 2: Перестроить `MainView.axaml` и переключить `MainView.axaml.cs` на новую функцию

**Files:**
- Modify: `ArctZ/Views/MainView.axaml`
- Modify: `ArctZ/Views/MainView.axaml.cs`
- Delete: `ArctZ.Tests/Views/MainViewNarrowJoystickRadiusTests.cs`

**Interfaces:**
- Consumes: `MainView.ComputeJoystickRadius(double, double, double)` (Task 1).
- Produces: финальную структуру `MainView.axaml`/`.cs` — Task 3 её только проверяет, ничего от неё не потребляет программно.

Это одна атомарная задача: XAML (шапка + `ContentGrid`) и code-behind (`OnSizeChanged`) должны смениться вместе — старый `OnSizeChanged` обращается к элементам (`HeaderGrid`, `LeftJoystick`/`RightJoystick` в старых колонках), которых после XAML-правки не станет, а новый `OnSizeChanged` использует `HeaderBorder`, которого до XAML-правки не существует. Раздельные коммиты оставили бы дерево нерабочим между ними.

- [ ] **Step 1: Удалить весь блок стилей `HeaderGrid`/`ContentGrid` (включая `.narrow`)**

В `ArctZ/Views/MainView.axaml`, внутри `<UserControl.Styles>`, найти блок (10 `Style`-элементов, от `Grid#HeaderGrid > ContentControl#ConnectionStatus` до `Grid#ContentGrid.narrow > js|VirtualJoystick#RightJoystick`):

```xml
        <Style Selector="Grid#HeaderGrid > ContentControl#ConnectionStatus">
            <Setter Property="Grid.Row" Value="0" />
            <Setter Property="Grid.Column" Value="0" />
        </Style>
        <Style Selector="Grid#HeaderGrid > WrapPanel#PlaybackButtons">
            <Setter Property="Grid.Row" Value="0" />
            <Setter Property="Grid.Column" Value="1" />
        </Style>

        <Style Selector="Grid#HeaderGrid.narrow > ContentControl#ConnectionStatus">
            <Setter Property="Grid.Row" Value="0" />
            <Setter Property="Grid.Column" Value="0" />
            <Setter Property="Grid.ColumnSpan" Value="2" />
        </Style>
        <Style Selector="Grid#HeaderGrid.narrow > WrapPanel#PlaybackButtons">
            <Setter Property="Grid.Row" Value="1" />
            <Setter Property="Grid.Column" Value="0" />
            <Setter Property="Grid.ColumnSpan" Value="2" />
            <Setter Property="HorizontalAlignment" Value="Left" />
            <Setter Property="Margin" Value="0,8,0,0" />
        </Style>

        <Style Selector="Grid#ContentGrid > js|VirtualJoystick#LeftJoystick">
            <Setter Property="Grid.Column" Value="0" />
            <Setter Property="Radius" Value="80" />
        </Style>
        <Style Selector="Grid#ContentGrid > ScrollViewer#ProgramPanel">
            <Setter Property="Grid.Column" Value="1" />
            <Setter Property="MaxWidth" Value="360" />
            <Setter Property="Margin" Value="24,0" />
        </Style>
        <Style Selector="Grid#ContentGrid > js|VirtualJoystick#RightJoystick">
            <Setter Property="Grid.Column" Value="2" />
            <Setter Property="Radius" Value="80" />
        </Style>
        <Style Selector="Grid#ContentGrid.narrow > ScrollViewer#ProgramPanel">
            <Setter Property="Grid.Row" Value="0" />
            <Setter Property="Grid.Column" Value="0" />
            <Setter Property="Grid.ColumnSpan" Value="2" />
            <Setter Property="MaxWidth" Value="Infinity" />
            <Setter Property="Margin" Value="0,0,0,12" />
        </Style>
        <Style Selector="Grid#ContentGrid.narrow > js|VirtualJoystick#LeftJoystick">
            <Setter Property="Grid.Row" Value="1" />
            <Setter Property="Grid.Column" Value="0" />
            <Setter Property="HorizontalAlignment" Value="Center" />
        </Style>
        <Style Selector="Grid#ContentGrid.narrow > js|VirtualJoystick#RightJoystick">
            <Setter Property="Grid.Row" Value="1" />
            <Setter Property="Grid.Column" Value="1" />
            <Setter Property="HorizontalAlignment" Value="Center" />
        </Style>
    </UserControl.Styles>
```

Заменить на (весь блок удалён, остаётся только закрывающий тег):

```xml
    </UserControl.Styles>
```

- [ ] **Step 2: Заменить шапку — `Grid` на `WrapPanel`, добавить `x:Name="HeaderBorder"`**

Найти:

```xml
            <Border DockPanel.Dock="Top" Classes="reveal-1"
                    Background="{StaticResource HudPanelBrush}"
                    BorderBrush="{StaticResource HudBorderBrush}"
                    BorderThickness="0,0,0,1"
                    Padding="12,10">
                <Grid x:Name="HeaderGrid" ColumnDefinitions="*,Auto">
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
                </Grid>
            </Border>
```

Заменить на:

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

- [ ] **Step 3: Перестроить открывающую часть `ContentGrid` — убрать левый джойстик из колонки, сделать однoколоночный `RowDefinitions="*,Auto"`**

Найти (включая обе строки-комментарии над `Border` и над `Grid` — обе ссылаются на константы, которые Step 6 переименовывает):

```xml
                <!-- radius formula in MainView.axaml.cs depends on these margins — see ContentGridChromeWidth/MainViewChromeHeight -->
                <Border Classes="reveal-3" Margin="0,12,12,12"
                        Background="{StaticResource HudPanelBrush}"
                        BorderBrush="{StaticResource HudBorderBrush}"
                        BorderThickness="1">
                    <!-- radius formula in MainView.axaml.cs depends on this margin — see ContentGridChromeWidth/MainViewChromeHeight -->
                    <Grid x:Name="ContentGrid" ColumnDefinitions="Auto,*,Auto" Margin="20">
                        <js:VirtualJoystick x:Name="LeftJoystick" Mode="Fixed" Shape="Circle"
                                             VerticalAlignment="Center" IsEnabled="{Binding !IsProgramLocked}"
                                             ToolTip.Tip="Левый джойстик: подъём и поворот стрелы (X · Y)"
                                             JoystickDown="OnLeftJoystickDown" JoystickMove="OnLeftJoystickMove" JoystickUp="OnLeftJoystickUp" />

                        <ScrollViewer x:Name="ProgramPanel" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
```

Заменить на:

```xml
                <!-- radius formula in MainView.axaml.cs (ComputeJoystickRadius) depends on these margins —
                     see ContentGridChromeWidth/ContentBorderVerticalChrome/ContentGridVerticalMargin/JoystickBarTopMargin -->
                <Border Classes="reveal-3" Margin="0,12,12,12"
                        Background="{StaticResource HudPanelBrush}"
                        BorderBrush="{StaticResource HudBorderBrush}"
                        BorderThickness="1">
                    <Grid x:Name="ContentGrid" RowDefinitions="*,Auto" Margin="20">
                        <ScrollViewer x:Name="ProgramPanel" Grid.Row="0" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
```

- [ ] **Step 4: Перестроить закрывающую часть `ContentGrid` — правый джойстик выносится в новый `JoystickBar` вместе с левым**

Найти:

```xml
                        </StackPanel>
                        </ScrollViewer>

                        <js:VirtualJoystick x:Name="RightJoystick" Mode="Fixed" Shape="Circle"
                                             VerticalAlignment="Center" IsEnabled="{Binding !IsProgramLocked}"
                                             ToolTip.Tip="Правый джойстик: пан и наклон камеры (Z · A)"
                                             JoystickDown="OnRightJoystickDown" JoystickMove="OnRightJoystickMove" JoystickUp="OnRightJoystickUp" />
                    </Grid>
                </Border>
```

Заменить на:

```xml
                        </StackPanel>
                        </ScrollViewer>

                        <Grid x:Name="JoystickBar" Grid.Row="1" ColumnDefinitions="Auto,*,Auto" Margin="0,12,0,0">
                            <js:VirtualJoystick x:Name="LeftJoystick" Grid.Column="0" Mode="Fixed" Shape="Circle"
                                                 VerticalAlignment="Center" IsEnabled="{Binding !IsProgramLocked}"
                                                 ToolTip.Tip="Левый джойстик: подъём и поворот стрелы (X · Y)"
                                                 JoystickDown="OnLeftJoystickDown" JoystickMove="OnLeftJoystickMove" JoystickUp="OnLeftJoystickUp" />
                            <js:VirtualJoystick x:Name="RightJoystick" Grid.Column="2" Mode="Fixed" Shape="Circle"
                                                 VerticalAlignment="Center" IsEnabled="{Binding !IsProgramLocked}"
                                                 ToolTip.Tip="Правый джойстик: пан и наклон камеры (Z · A)"
                                                 JoystickDown="OnRightJoystickDown" JoystickMove="OnRightJoystickMove" JoystickUp="OnRightJoystickUp" />
                        </Grid>
                    </Grid>
                </Border>
```

(Обратите внимание: `LeftJoystick` целиком переехал сюда из Step 3 — там он был удалён из позиции перед `ScrollViewer`.)

- [ ] **Step 5: Собрать (ожидаются ошибки — code-behind ещё ссылается на удалённые элементы)**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: FAIL — `MainView.axaml.cs` ссылается на `HeaderGrid`/старую сигнатуру `ContentGrid`, которых больше нет. Это ожидаемо на этом шаге, переходим к правке code-behind.

- [ ] **Step 6: Переписать `MainView.axaml.cs`**

Заменить содержимое файла целиком на:

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
        // Border(reveal-3).Margin(0,12,12,12→12) + BorderThickness(1+1=2) + ContentGrid.Margin(20+20=40)
        private const double ContentGridChromeWidth = 54;
        private const double MinRadius = 50;
        private const double MaxRadius = 110;
        private const double CenterGap = 24;

        // Border(reveal-3).Margin(0,12,12,12→12+12=24 верт.) + BorderThickness(1+1=2)
        private const double ContentBorderVerticalChrome = 26;
        // ContentGrid.Margin(20+20=40 верт.)
        private const double ContentGridVerticalMargin = 40;
        private const double JoystickBarTopMargin = 12;
        private const double ProgramPanelMinHeight = 160;

        // Фолбэк для HeaderBorder.Bounds.Height на первом кадре, до первого layout-прохода
        // (однострочная шапка: Padding="12,10" + одна строка контента).
        private const double HeaderFallbackHeight = 44;

        public MainView()
        {
            InitializeComponent();
            SizeChanged += OnSizeChanged;
            DataContextChanged += OnDataContextChanged;
        }

        private ProgramViewModel? ViewModel => DataContext as ProgramViewModel;

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
                GCodeLogList.IsEffectivelyVisible &&
                sender is ObservableCollection<string> { Count: > 0 } lines)
            {
                GCodeLogList.ScrollIntoView(lines.Count - 1);
            }
        }

        private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            var radius = ComputeJoystickRadius(e.NewSize.Width, e.NewSize.Height, HeaderBorder.Bounds.Height);
            LeftJoystick.Radius = radius;
            RightJoystick.Radius = radius;
        }

        internal static double ComputeJoystickRadius(double mainViewWidth, double mainViewHeight, double headerHeight)
        {
            var effectiveHeaderHeight = headerHeight > 0 ? headerHeight : HeaderFallbackHeight;

            var contentGridWidth = mainViewWidth - ContentGridChromeWidth;
            var widthRadius = (contentGridWidth - CenterGap) / 4;

            var contentGridHeight = mainViewHeight - effectiveHeaderHeight - ContentBorderVerticalChrome
                - ContentGridVerticalMargin - JoystickBarTopMargin;
            var joystickRowBudget = contentGridHeight - ProgramPanelMinHeight;
            var heightRadius = joystickRowBudget / 2;

            return Math.Clamp(Math.Min(widthRadius, heightRadius), MinRadius, MaxRadius);
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

Это убирает `_isNarrow`, `NarrowLayoutBreakpoint`, старые константы (`NarrowJoystickMinRadius`, `NarrowJoystickEdgeMargin`, `MainViewChromeHeight`, `NarrowProgramPanelMinHeight`) и старый метод `ComputeNarrowJoystickRadius` — всё это существовало только для breakpoint-переключения, которого больше нет.

- [ ] **Step 7: Удалить устаревший тестовый файл**

`ComputeNarrowJoystickRadius` больше не существует — тесты на него не скомпилируются.

```bash
git rm ArctZ.Tests/Views/MainViewNarrowJoystickRadiusTests.cs
```

- [ ] **Step 8: Собрать и прогнать тесты**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: `Build succeeded`, 0 ошибок.

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: все тесты проходят (включая `MainViewJoystickRadiusTests` из Task 1), без упоминаний `MainViewNarrowJoystickRadiusTests`.

- [ ] **Step 9: Commit**

```bash
git add ArctZ/Views/MainView.axaml ArctZ/Views/MainView.axaml.cs
git add ArctZ.Tests/Views/MainViewNarrowJoystickRadiusTests.cs
git commit -m "refactor: unify MainView layout into header/program/joystick bands, drop breakpoint switching"
```

---

### Task 3: Финальная проверка

**Files:** нет изменений (если не найдены дефекты — тогда точечные правки в `ArctZ/Views/MainView.axaml`/`.cs` с отдельным коммитом).

- [ ] **Step 1: Полная сборка всех платформенных head'ов**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Run: `dotnet build ArctZ.Browser/ArctZ.Browser.csproj`
Expected: оба — `Build succeeded`, 0 ошибок.

- [ ] **Step 2: Полный прогон тестов**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: все тесты проходят, 0 failed.

- [ ] **Step 3: Визуальная проверка**

Если доступны инструменты браузерной автоматизации (Playwright MCP или аналог) — поднять `dotnet run --project ArctZ.Browser/ArctZ.Browser.csproj` и сделать скриншоты на нескольких размерах вьюпорта: 360×800 (телефон-портрет), 700×800 (граничный случай бывшего брейкпоинта), 1200×800 (десктоп), 1920×1080 (широкий десктоп). Проверить на каждом:
- шапка не обрезает и не накладывает кнопки (при нехватке ширины — переносит блок Пуск/Пауза/Стоп на новую строку);
- панель программы — на всю ширину, список точек читаем;
- оба джойстика — по краям нижней строки, не касаются друг друга и не наезжают на панель программы;
- на 1920×1080 джойстики не выглядят непропорционально огромными (проверка `MaxRadius=110`).

Если инструментов браузерной автоматизации нет — собрать и запустить `ArctZ.Desktop` (`dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj`), вручную изменить размер окна на нескольких промежуточных ширинах и визуально подтвердить те же четыре пункта. (Примечание по этой машине: окно VS Code перекрывает окно ArctZ в live-сессии — если нужна автоматизация кликов/скриншотов десктоп-окна, а не просто визуальный просмотр, уточнить у пользователя перед тем, как использовать глобальные события мыши.)

Expected: раскладка корректна на всех проверенных размерах, без наложений/обрезаний/непропорциональных джойстиков.

- [ ] **Step 4: Зафиксировать найденные точечные исправления (если есть)**

Если на Step 3 обнаружены дефекты — исправить в `ArctZ/Views/MainView.axaml`/`.cs`, повторить Step 1-3, затем:

```bash
git add ArctZ/Views/MainView.axaml ArctZ/Views/MainView.axaml.cs
git commit -m "fix: address visual issues found in unified-layout verification pass"
```

Если дефектов не найдено — коммит не требуется, задача считается завершённой по результатам Task 1-2.
