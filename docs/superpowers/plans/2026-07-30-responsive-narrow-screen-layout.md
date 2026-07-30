# Адаптивная раскладка MainView для узких экранов — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `MainView.axaml` переключается между широкой (текущей) и узкой раскладкой по ширине контрола: на узком экране джойстики уходят в один центрированный ряд под панелью программы, обе кнопочные панели (шапка и панель программы) переносят кнопки на новую строку вместо обрезания/наложения.

**Architecture:** Чистый view-слой, без изменений в ViewModel. Код-behind `MainView.axaml.cs` слушает `SizeChanged` и переключает CSS-класс `narrow` на двух именованных `Grid` (`HeaderGrid`, `ContentGrid`) по порогу 700px. Вся раскладка — декларативные `Style Selector="Grid#X.narrow > ..."`, переопределяющие `Grid.Row/Column/ColumnSpan/Margin/MaxWidth` у конкретных именованных детей и `ColumnDefinitions` самого `Grid`. `StackPanel Orientation="Horizontal"` в обеих кнопочных панелях заменяется на `WrapPanel` (не зависит от breakpoint'а). Основной контентный блок оборачивается в `ScrollViewer` для случая, когда содержимое не помещается по высоте на узком экране.

**Tech Stack:** Avalonia UI 12.0.4, XAML Styles/Selectors, `WrapPanel` (`ItemSpacing`/`LineSpacing` — подтверждено наличие в `Avalonia.Controls.dll` 12.0.4), code-behind `SizeChanged`.

## Global Constraints

- Порог переключения раскладки: ширина `MainView` **< 700px** → узкая раскладка.
- Класс переключения раскладки называется `narrow`, применяется к `Grid#HeaderGrid` и `Grid#ContentGrid`.
- Зазор в `WrapPanel` — `ItemSpacing="8"` (между кнопками в строке), `LineSpacing="8"` (между перенесёнными строками).
- Зазор между джойстиками в узкой раскладке — `Margin="0,0,20,0"` (левый) / `Margin="20,0,0,0"` (правый), итого 40px между ними.
- `Grid.RowDefinitions`/`Grid.ColumnDefinitions` — не `AvaloniaProperty` (обычные CLR-свойства), через `Style Setter` не задаются — переключаются в code-behind (`OnSizeChanged`), не в XAML-стилях. Обнаружено при реализации Task 2 (ошибка компилятора Avalonia), задним числом исправлено и в Task 1/2 (см. историю коммитов).
- `ArctZ.Tests` не содержит View-тестов (см. CLAUDE.md) — проверка каждой задачи через `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj` и визуально через `dotnet run --project ArctZ.Browser/ArctZ.Browser.csproj` + Playwright (ресайз вьюпорта, скриншоты).
- Спека: `docs/superpowers/specs/2026-07-30-responsive-narrow-screen-layout-design.md`.

---

### Task 1: Именование элементов, WrapPanel вместо StackPanel, обёртка ScrollViewer

**Files:**
- Modify: `ArctZ/Views/MainView.axaml`

**Interfaces:**
- Produces: именованные элементы `HeaderGrid`, `ConnectionStatus`, `PlaybackButtons`, `ContentGrid`, `LeftJoystick`, `ProgramPanel`, `RightJoystick` — Task 2 и Task 3 адресуют их в стилях и в code-behind по этим именам.

Это чисто структурная задача — визуально ничего не меняется (переименование, замена `StackPanel`→`WrapPanel` с теми же кнопками, `ScrollViewer` без активного скролла на широком экране, т.к. контент по-прежнему помещается).

- [ ] **Step 1: Именовать шапку и заменить кнопочный StackPanel на WrapPanel**

В `ArctZ/Views/MainView.axaml` найти (текущие строки ~79-90):

```xml
                <Grid ColumnDefinitions="*,Auto">
                    <ContentControl Grid.Column="0" Content="{Binding Connection}" />
                    <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
                        <Button Classes="primary" Content="Play" Command="{Binding PlayCommand}" />
                        <Button Content="Пауза" Command="{Binding PauseCommand}" />
                        <Button Classes="danger" Content="Стоп" Command="{Binding StopCommand}" />
                        <Border Background="{StaticResource HudPanelElevatedBrush}" BorderBrush="{StaticResource HudBorderStrongBrush}"
                                BorderThickness="1" Padding="10,6" Margin="8,0,0,0">
                            <TextBlock Classes="telemetry" FontSize="14" Text="{Binding PlaybackState}" />
                        </Border>
                    </StackPanel>
                </Grid>
```

Заменить на:

```xml
                <Grid x:Name="HeaderGrid" ColumnDefinitions="*,Auto">
                    <ContentControl x:Name="ConnectionStatus" Grid.Row="0" Grid.Column="0" Content="{Binding Connection}" />
                    <WrapPanel x:Name="PlaybackButtons" Grid.Row="0" Grid.Column="1" ItemSpacing="8" LineSpacing="8" VerticalAlignment="Center">
                        <Button Classes="primary" Content="Play" Command="{Binding PlayCommand}" />
                        <Button Content="Пауза" Command="{Binding PauseCommand}" />
                        <Button Classes="danger" Content="Стоп" Command="{Binding StopCommand}" />
                        <Border Background="{StaticResource HudPanelElevatedBrush}" BorderBrush="{StaticResource HudBorderStrongBrush}"
                                BorderThickness="1" Padding="10,6" Margin="8,0,0,0">
                            <TextBlock Classes="telemetry" FontSize="14" Text="{Binding PlaybackState}" />
                        </Border>
                    </WrapPanel>
                </Grid>
```

- [ ] **Step 2: Именовать ContentGrid, левый джойстик, панель программы; заменить кнопочный StackPanel программы на WrapPanel**

Найти (текущие строки ~100-114):

```xml
                    <Grid ColumnDefinitions="Auto,*,Auto" Margin="20">
                        <js:VirtualJoystick Grid.Column="0" Radius="80" Mode="Fixed" Shape="Circle"
                                             VerticalAlignment="Center" IsEnabled="{Binding !IsProgramLocked}"
                                             JoystickDown="OnLeftJoystickDown" JoystickMove="OnLeftJoystickMove" JoystickUp="OnLeftJoystickUp" />

                        <StackPanel Grid.Column="1" Spacing="10" Margin="24,0" VerticalAlignment="Center" MaxWidth="360">
                            <StackPanel Spacing="10" IsEnabled="{Binding !IsProgramLocked}">
                                <TextBlock Classes="section-heading" Text="ПРОГРАММА" />
                                <TextBox Text="{Binding ProgramName}" PlaceholderText="Имя программы" />
                                <StackPanel Orientation="Horizontal" Spacing="8">
                                    <Button Classes="primary" Content="Захватить точку" Command="{Binding CaptureKeyPointCommand}" />
                                    <Button Content="Новая" Command="{Binding NewProgramCommand}" />
                                    <Button Content="Сохранить" Command="{Binding SaveProgramCommand}" />
                                    <Button Content="Библиотека" Command="{Binding OpenLibraryCommand}" />
                                </StackPanel>
```

Заменить на:

```xml
                    <Grid x:Name="ContentGrid" ColumnDefinitions="Auto,*,Auto" Margin="20">
                        <js:VirtualJoystick x:Name="LeftJoystick" Grid.Column="0" Radius="80" Mode="Fixed" Shape="Circle"
                                             VerticalAlignment="Center" IsEnabled="{Binding !IsProgramLocked}"
                                             JoystickDown="OnLeftJoystickDown" JoystickMove="OnLeftJoystickMove" JoystickUp="OnLeftJoystickUp" />

                        <StackPanel x:Name="ProgramPanel" Grid.Column="1" Spacing="10" Margin="24,0" VerticalAlignment="Center" MaxWidth="360">
                            <StackPanel Spacing="10" IsEnabled="{Binding !IsProgramLocked}">
                                <TextBlock Classes="section-heading" Text="ПРОГРАММА" />
                                <TextBox Text="{Binding ProgramName}" PlaceholderText="Имя программы" />
                                <WrapPanel ItemSpacing="8" LineSpacing="8">
                                    <Button Classes="primary" Content="Захватить точку" Command="{Binding CaptureKeyPointCommand}" />
                                    <Button Content="Новая" Command="{Binding NewProgramCommand}" />
                                    <Button Content="Сохранить" Command="{Binding SaveProgramCommand}" />
                                    <Button Content="Библиотека" Command="{Binding OpenLibraryCommand}" />
                                </WrapPanel>
```

- [ ] **Step 3: Именовать правый джойстик**

Найти (текущие строки ~174-176):

```xml
                        <js:VirtualJoystick Grid.Column="2" Radius="80" Mode="Fixed" Shape="Circle"
                                             VerticalAlignment="Center" IsEnabled="{Binding !IsProgramLocked}"
                                             JoystickDown="OnRightJoystickDown" JoystickMove="OnRightJoystickMove" JoystickUp="OnRightJoystickUp" />
```

Заменить на:

```xml
                        <js:VirtualJoystick x:Name="RightJoystick" Grid.Column="2" Radius="80" Mode="Fixed" Shape="Circle"
                                             VerticalAlignment="Center" IsEnabled="{Binding !IsProgramLocked}"
                                             JoystickDown="OnRightJoystickDown" JoystickMove="OnRightJoystickMove" JoystickUp="OnRightJoystickUp" />
```

- [ ] **Step 4: Обернуть основной контентный Border в ScrollViewer**

Найти:

```xml
            <Grid x:Name="RootPanel">
                <Border Classes="reveal-3" Margin="0,12,12,12"
```

Заменить на:

```xml
            <Grid x:Name="RootPanel">
                <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                <Border Classes="reveal-3" Margin="0,12,12,12"
```

Найти (закрытие того же `Border`, перед первым оверлеем):

```xml
                </Border>

                <Border IsVisible="{Binding IsEditingKeyPoint}" Background="#CC0A0E12">
```

Заменить на:

```xml
                </Border>
                </ScrollViewer>

                <Border IsVisible="{Binding IsEditingKeyPoint}" Background="#CC0A0E12">
```

Три модальных оверлея (`IsEditingKeyPoint`, `PendingConfirmation`, `IsLibraryOpen`) остаются **вне** `ScrollViewer` — не трогать.

- [ ] **Step 5: Собрать и убедиться, что регрессий нет**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: `Build succeeded`, 0 ошибок.

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Views/MainView.axaml
git commit -m "refactor: name layout grid elements, use WrapPanel for button rows, wrap content in ScrollViewer"
```

---

### Task 2: Механизм переключения раскладки + узкая шапка

**Files:**
- Modify: `ArctZ/Views/MainView.axaml.cs`
- Modify: `ArctZ/Views/MainView.axaml`

**Interfaces:**
- Consumes: `HeaderGrid`, `ConnectionStatus`, `PlaybackButtons` (из Task 1).
- Produces: класс `narrow` на `HeaderGrid`/`ContentGrid`, переключаемый по `SizeChanged` — Task 3 полагается на то, что `ContentGrid` уже получает класс `narrow` тем же методом.

- [ ] **Step 1: Добавить обработчик SizeChanged, переключение класса narrow и RowDefinitions**

`Grid.RowDefinitions`/`Grid.ColumnDefinitions` — обычные CLR-свойства на `Avalonia.Controls.Grid`, а не зарегистрированные `AvaloniaProperty` (нет полей `RowDefinitionsProperty`/`ColumnDefinitionsProperty` — подтверждено ошибкой компилятора Avalonia "Unable to find RowDefinitionsProperty field on type Avalonia.Controls.Grid" и бинарной проверкой `Avalonia.Controls.dll`). Значит, задать их через `Setter` в `Style` **нельзя** — это ограничение Avalonia, не решение на усмотрение реализатора. Переключение `RowDefinitions` делается прямо в code-behind, в том же `OnSizeChanged`, что и переключение класса `narrow`. (`Grid.Row`/`Grid.Column`/`Grid.ColumnSpan`, `Margin`, `MaxWidth`, `HorizontalAlignment` — это обычные attached/styled `AvaloniaProperty`, их через `Style Setter` менять можно, и Task 2/3 продолжают делать это в XAML.)

В `ArctZ/Views/MainView.axaml.cs` (текущее содержимое — только конструктор и обработчики джойстика/библиотеки) изменить класс так:

```csharp
using ArctZ.Components.VirtualJoystick;
using ArctZ.Services.Program;
using ArctZ.ViewModels;
using Avalonia.Controls;

namespace ArctZ.Views
{
    public partial class MainView : UserControl
    {
        private const double NarrowLayoutBreakpoint = 700;

        public MainView()
        {
            InitializeComponent();
            SizeChanged += OnSizeChanged;
        }

        private ProgramViewModel? ViewModel => DataContext as ProgramViewModel;

        private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            var isNarrow = e.NewSize.Width < NarrowLayoutBreakpoint;
            HeaderGrid.Classes.Set("narrow", isNarrow);
            ContentGrid.Classes.Set("narrow", isNarrow);

            HeaderGrid.RowDefinitions = new RowDefinitions(isNarrow ? "Auto,Auto" : "");
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

(Единственные изменения относительно текущего файла: добавлена константа `NarrowLayoutBreakpoint`, `SizeChanged += OnSizeChanged;` в конструкторе, и новый метод `OnSizeChanged`.)

- [ ] **Step 2: Убрать локальные Grid.Row/Grid.Column у ConnectionStatus/PlaybackButtons; добавить безусловные и узкие стили для HeaderGrid**

**Важно (обнаружено ревью Task 2):** в Avalonia значение свойства, заданное напрямую в XAML («local value»), всегда побеждает `Style Setter` на то же свойство — даже если селектор стиля совпадает по классу (`LocalValue` выше `Style` в порядке приоритета: `Animation > Local value > Style trigger > Template > Style > Inherited > Default` — из официальной документации Avalonia, страница «Style precedence»). `ConnectionStatus`/`PlaybackButtons` уже имеют `Grid.Row="0" Grid.Column="0"`/`"1"` как локальные атрибуты (из Task 1) — значит, стиль `.narrow`, пытающийся переопределить эти же свойства, молча игнорируется в узком режиме. Правило Avalonia для конфликта между двумя *стилями*, совпадающими с одним и тем же элементом, — не про порядок объявления в файле: стиль с селектором по классу (`.narrow`) вычисляется на уровне приоритета «Style trigger», который стоит выше обычного (всегда совпадающего) уровня «Style» в том же списке приоритетов. Поэтому `.narrow`-стиль побеждает безусловный **независимо от того, в каком порядке они объявлены** — этим и пользуемся: убираем локальные атрибуты и переносим оба состояния (широкое и узкое) в стили (порядок ниже сохраняем безусловный-затем-`.narrow` для читаемости, а не потому что от него что-то зависит).

В `ArctZ/Views/MainView.axaml` найти:

```xml
                <Grid x:Name="HeaderGrid" ColumnDefinitions="*,Auto">
                    <ContentControl x:Name="ConnectionStatus" Grid.Row="0" Grid.Column="0" Content="{Binding Connection}" />
                    <WrapPanel x:Name="PlaybackButtons" Grid.Row="0" Grid.Column="1" ItemSpacing="8" LineSpacing="8" VerticalAlignment="Center">
```

Заменить на (убраны `Grid.Row`/`Grid.Column` — они переезжают в стили ниже):

```xml
                <Grid x:Name="HeaderGrid" ColumnDefinitions="*,Auto">
                    <ContentControl x:Name="ConnectionStatus" Content="{Binding Connection}" />
                    <WrapPanel x:Name="PlaybackButtons" ItemSpacing="8" LineSpacing="8" VerticalAlignment="Center">
```

Затем, внутри `<UserControl.Styles>`, сразу после стиля `Border.loaded-entry` (последний существующий стиль, перед закрывающим `</UserControl.Styles>`), добавить (порядок между безусловными и `.narrow`-стилями значения не имеет — `.narrow` побеждает за счёт более высокого приоритета «Style trigger» у класс-селектора, а не за счёт места в файле; ниже безусловные стили идут первыми просто для читаемости):

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
```

`RowDefinitions="Auto,Auto"` уже переключается в Step 1 (`OnSizeChanged`, code-behind) — `Grid.Row/Column/ColumnSpan`, `HorizontalAlignment`, `Margin` (в отличие от `RowDefinitions`) являются обычными `AvaloniaProperty` и настраиваются через `Style Setter` штатно, при условии что на элементе нет конкурирующего локального значения того же свойства (см. предупреждение выше).

- [ ] **Step 3: Собрать**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: `Build succeeded`, 0 ошибок.

- [ ] **Step 4: Визуально проверить в браузерной сборке**

Run: `dotnet run --project ArctZ.Browser/ArctZ.Browser.csproj` (в фоне), затем через Playwright:
- `browser_navigate` на локальный адрес приложения
- `browser_resize` на 1000x700 → `browser_take_screenshot`: шапка в одну строку (статус слева, Play/Пауза/Стоп справа) — без изменений от исходного вида.
- `browser_resize` на 400x800 → `browser_take_screenshot`: шапка в две строки (статус сверху на всю ширину, кнопки снизу слева).

Expected: оба скриншота подтверждают переключение без наложения/обрезания текста.

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Views/MainView.axaml ArctZ/Views/MainView.axaml.cs
git commit -m "feat: switch header to two-row layout below 700px width"
```

---

### Task 3: Узкая раскладка ContentGrid — джойстики под панелью

**Files:**
- Modify: `ArctZ/Views/MainView.axaml`
- Modify: `ArctZ/Views/MainView.axaml.cs`

**Interfaces:**
- Consumes: `ContentGrid`, `ProgramPanel`, `LeftJoystick`, `RightJoystick` (Task 1), класс `narrow` и метод `OnSizeChanged` (Task 2 — уже устанавливает `HeaderGrid.RowDefinitions` там же, этот шаг добавляет в тот же метод переключение `ContentGrid.ColumnDefinitions`/`RowDefinitions`).

- [ ] **Step 1: Переключать ColumnDefinitions/RowDefinitions у ContentGrid в OnSizeChanged**

Как и `HeaderGrid.RowDefinitions` в Task 2, `ContentGrid.ColumnDefinitions`/`RowDefinitions` — обычные CLR-свойства, не `AvaloniaProperty`, поэтому через `Style Setter` их менять нельзя (та же причина, что и в Task 2 Step 1). Переключаются в code-behind, в том же `OnSizeChanged`.

В `ArctZ/Views/MainView.axaml.cs` найти (текущее состояние после Task 2):

```csharp
        private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            var isNarrow = e.NewSize.Width < NarrowLayoutBreakpoint;
            HeaderGrid.Classes.Set("narrow", isNarrow);
            ContentGrid.Classes.Set("narrow", isNarrow);

            HeaderGrid.RowDefinitions = new RowDefinitions(isNarrow ? "Auto,Auto" : "");
        }
```

Заменить на:

```csharp
        private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            var isNarrow = e.NewSize.Width < NarrowLayoutBreakpoint;
            HeaderGrid.Classes.Set("narrow", isNarrow);
            ContentGrid.Classes.Set("narrow", isNarrow);

            HeaderGrid.RowDefinitions = new RowDefinitions(isNarrow ? "Auto,Auto" : "");
            ContentGrid.ColumnDefinitions = new ColumnDefinitions(isNarrow ? "*,Auto,Auto,*" : "Auto,*,Auto");
            ContentGrid.RowDefinitions = new RowDefinitions(isNarrow ? "Auto,Auto" : "");
        }
```

- [ ] **Step 2: Убрать локальные Grid.Column/MaxWidth у LeftJoystick/ProgramPanel/RightJoystick; добавить безусловные и узкие стили для ContentGrid**

**Важно (то же ограничение, что заставило исправить Task 2):** в Avalonia локальное значение свойства в XAML всегда побеждает `Style Setter` на то же свойство. `LeftJoystick`/`ProgramPanel`/`RightJoystick` уже имеют `Grid.Column="0"/"1"/"2"` как локальные атрибуты (из Task 1), а `ProgramPanel` — ещё и `MaxWidth="360"` локально. Стили `.narrow` ниже пытаются переопределить именно эти свойства — значит, локальные атрибуты нужно убрать и перенести оба состояния (широкое и узкое) в стили (`.narrow` побеждает безусловный стиль благодаря более высокому приоритету уровня «Style trigger» у класс-селектора, независимо от порядка объявления — см. разбор в Task 2 Step 2).

В `ArctZ/Views/MainView.axaml` найти:

```xml
                        <js:VirtualJoystick x:Name="LeftJoystick" Grid.Column="0" Radius="80" Mode="Fixed" Shape="Circle"
                                             VerticalAlignment="Center" IsEnabled="{Binding !IsProgramLocked}"
                                             JoystickDown="OnLeftJoystickDown" JoystickMove="OnLeftJoystickMove" JoystickUp="OnLeftJoystickUp" />

                        <StackPanel x:Name="ProgramPanel" Grid.Column="1" Spacing="10" Margin="24,0" VerticalAlignment="Center" MaxWidth="360">
```

Заменить на (убраны `Grid.Column` на обоих элементах и `MaxWidth` на `ProgramPanel` — переезжают в стили ниже; `Margin="24,0"` на `ProgramPanel` остаётся локальным, стили его не трогают):

```xml
                        <js:VirtualJoystick x:Name="LeftJoystick" Radius="80" Mode="Fixed" Shape="Circle"
                                             VerticalAlignment="Center" IsEnabled="{Binding !IsProgramLocked}"
                                             JoystickDown="OnLeftJoystickDown" JoystickMove="OnLeftJoystickMove" JoystickUp="OnLeftJoystickUp" />

                        <StackPanel x:Name="ProgramPanel" Spacing="10" Margin="24,0" VerticalAlignment="Center">
```

И найти:

```xml
                        <js:VirtualJoystick x:Name="RightJoystick" Grid.Column="2" Radius="80" Mode="Fixed" Shape="Circle"
                                             VerticalAlignment="Center" IsEnabled="{Binding !IsProgramLocked}"
                                             JoystickDown="OnRightJoystickDown" JoystickMove="OnRightJoystickMove" JoystickUp="OnRightJoystickUp" />
```

Заменить на:

```xml
                        <js:VirtualJoystick x:Name="RightJoystick" Radius="80" Mode="Fixed" Shape="Circle"
                                             VerticalAlignment="Center" IsEnabled="{Binding !IsProgramLocked}"
                                             JoystickDown="OnRightJoystickDown" JoystickMove="OnRightJoystickMove" JoystickUp="OnRightJoystickUp" />
```

Затем, в `<UserControl.Styles>`, сразу после стилей `HeaderGrid.narrow` (добавленных в Task 2), добавить (безусловные стили ПЕРЕД `.narrow`-стилями):

```xml
        <Style Selector="Grid#ContentGrid > js|VirtualJoystick#LeftJoystick">
            <Setter Property="Grid.Column" Value="0" />
            <Setter Property="Radius" Value="80" />
        </Style>
        <Style Selector="Grid#ContentGrid > StackPanel#ProgramPanel">
            <Setter Property="Grid.Column" Value="1" />
            <Setter Property="MaxWidth" Value="360" />
        </Style>
        <Style Selector="Grid#ContentGrid > js|VirtualJoystick#RightJoystick">
            <Setter Property="Grid.Column" Value="2" />
            <Setter Property="Radius" Value="80" />
        </Style>
        <Style Selector="Grid#ContentGrid.narrow > StackPanel#ProgramPanel">
            <Setter Property="Grid.Row" Value="0" />
            <Setter Property="Grid.Column" Value="0" />
            <Setter Property="Grid.ColumnSpan" Value="4" />
            <Setter Property="MaxWidth" Value="Infinity" />
        </Style>
        <Style Selector="Grid#ContentGrid.narrow > js|VirtualJoystick#LeftJoystick">
            <Setter Property="Grid.Row" Value="1" />
            <Setter Property="Grid.Column" Value="1" />
            <Setter Property="Margin" Value="0,0,20,0" />
            <Setter Property="Radius" Value="55" />
        </Style>
        <Style Selector="Grid#ContentGrid.narrow > js|VirtualJoystick#RightJoystick">
            <Setter Property="Grid.Row" Value="1" />
            <Setter Property="Grid.Column" Value="2" />
            <Setter Property="Margin" Value="20,0,0,0" />
            <Setter Property="Radius" Value="55" />
        </Style>
```

(`js|VirtualJoystick` использует уже объявленный в корне файла `xmlns:js="using:ArctZ.Components.VirtualJoystick"`. `ColumnDefinitions`/`RowDefinitions` здесь больше не переопределяются стилем — они уже переключены в Step 1, в `OnSizeChanged`; эти стили отвечают только за `Grid.Row/Column/ColumnSpan/Margin/MaxWidth/Radius` — обычные `AvaloniaProperty`, `Style Setter` для них работает штатно.

`Radius` тоже нужно убрать как локальный XAML-атрибут с обоих `VirtualJoystick` — иначе тот же конфликт «локальное значение бьёт стиль», уже дважды сломавший Task 2/3. **Пересмотрено по итогам Task 4 (расчёт при финальной проверке):** при `Radius=80` (диаметр 160px) два джойстика + отступ между ними (40px) + поля `ContentGrid`/`Border` (52px) требуют ≈412px минимальной ширины — больше типичной ширины телефона (360–430px), а `HorizontalScrollBarVisibility="Disabled"` не даёт докрутить вбок, то есть джойстики бы обрезались. Поэтому в узком режиме `Radius` уменьшается до `55` (диаметр 110px, минимальная требуемая ширина ≈312px) — умещается на реальных телефонах и уместнее по размеру для маленького экрана.)

- [ ] **Step 3: Собрать**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: `Build succeeded`, 0 ошибок.

- [ ] **Step 4: Визуально проверить в браузерной сборке**

Через Playwright (сервер из Task 3 Step 4 всё ещё поднят, либо перезапустить `dotnet run --project ArctZ.Browser/ArctZ.Browser.csproj`):
- `browser_resize` на 1000x700 → `browser_take_screenshot`: раскладка идентична исходной — джойстики по краям, панель (≤360px) по центру, всё в один ряд.
- `browser_resize` на 400x800 → `browser_take_screenshot`: панель программы на всю ширину сверху, оба джойстика — рядом друг с другом по центру в строке под ней.
- `browser_resize` на 650x800 (граничный случай, близко к 700px) → `browser_take_screenshot`: та же узкая раскладка, джойстики всё ещё рядом по центру (не расползаются к краям).

Expected: во всех трёх случаях нет наложения элементов и обрезанного текста; переход происходит ровно на границе 700px.

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Views/MainView.axaml ArctZ/Views/MainView.axaml.cs
git commit -m "feat: move joysticks below the program panel on narrow screens"
```

---

### Task 4: Финальная проверка

**Files:** нет изменений файлов (если не найдены дефекты — тогда правки по месту с последующим коммитом).

- [ ] **Step 1: Полная сборка обеих головных платформ**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Run: `dotnet build ArctZ.Browser/ArctZ.Browser.csproj`
Expected: оба — `Build succeeded`, 0 ошибок.

- [ ] **Step 2: Сквозная визуальная проверка через Playwright**

`dotnet run --project ArctZ.Browser/ArctZ.Browser.csproj` (в фоне), затем:
- `browser_resize` 1000x700, `browser_take_screenshot` — базовая широкая раскладка.
- `browser_resize` 380x700, `browser_take_screenshot` — узкая раскладка, панель занимает всю ширину, джойстики рядом снизу, обе кнопочные панели переносятся на новую строку без наложения.
- `browser_resize` 380x500 (намеренно малая высота) — если панель+джойстики не помещаются, `browser_take_screenshot` до и после `browser_evaluate` со скроллом контейнера вниз, чтобы подтвердить, что `ScrollViewer` даёт добраться до обоих джойстиков.
- Открыть библиотеку программ (`Библиотека`) при ширине 380px — `browser_take_screenshot`: модальное окно центрировано в видимой области, а не уезжает вслед за прокруткой контента.

Expected: во всех сценариях — корректная раскладка, доступность всех элементов управления, модалка остаётся по центру экрана.

**Фактический результат (2026-07-30):** `dotnet run --project ArctZ.Browser` стабильно (3 попытки) падал с ошибкой генерации XAML ("InitializeComponent не существует") — детерминированная проблема окружения при команде `run` для WASM-головы (`dotnet build` того же проекта при этом собирается чисто), не связанная с изменениями этого плана. Прямая проверка Desktop-сборки через GUI-автоматизацию также не использовалась (см. память `feedback_gui_automation_shared_desktop` — окно VS Code перекрывает окно ArctZ на этой машине). Вместо живого рендеринга проведена тщательная ручная проверка вёрстки на всех четырёх контрольных ширинах по актуальному состоянию `MainView.axaml`/`.cs` (Grid columns/rows, порядок стилей, точные размеры). Эта проверка нашла реальный дефект (см. ниже) — расчёт подтверждён количественно, не только "на глаз".

- [ ] **Step 2a: Найденный и исправленный дефект — переполнение по ширине джойстиков на узких экранах**

При `Radius=80` (диаметр 160px) два джойстика + отступ между ними (`Margin="0,0,20,0"`/`"20,0,0,0"` = 40px) + поля `ContentGrid Margin="20"` (40px) + поле `Border.reveal-3 Margin="0,12,12,12"` (12px справа) = **минимум ≈412px** ширины, чтобы не обрезаться. Это больше типичной ширины смартфона (360–430px), а `ScrollViewer.HorizontalScrollBarVisibility="Disabled"` не даёт прокрутить вбок — джойстики бы обрезались молча.

Исправление: `Radius` уменьшается до `55` (диаметр 110px, минимум ≈312px) в узком режиме через `Style Setter` — `Radius` является `AvaloniaProperty` (`RadiusProperty` зарегистрирован в `VirtualJoystick.cs`), поэтому стилизуется штатно, но по той же причине, что и `Grid.Column` в Task 3 Step 2, локальный атрибут `Radius="80"` с обоих `VirtualJoystick`-элементов нужно убрать и перенести в безусловный (`80`) + `.narrow` (`55`) стили — иначе локальное значение снова перебьёт стиль.

```bash
git add ArctZ/Views/MainView.axaml
git commit -m "fix: shrink joystick radius on narrow screens to avoid horizontal overflow"
```

- [ ] **Step 3: Зафиксировать любые найденные точечные исправления**

Если на Step 2 обнаружены визуальные дефекты — исправить в `ArctZ/Views/MainView.axaml` / `MainView.axaml.cs`, повторить Step 1-2, затем:

```bash
git add ArctZ/Views/MainView.axaml ArctZ/Views/MainView.axaml.cs
git commit -m "fix: address visual issues found in narrow-layout verification pass"
```

Если дефектов не найдено — коммит не требуется, задача считается завершённой по результатам Task 1-3.
