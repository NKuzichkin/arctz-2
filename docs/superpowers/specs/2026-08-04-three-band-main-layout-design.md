# MainView: единая трёхчастная раскладка (статус/управление — программа — джойстики) — дизайн

Дата: 2026-08-04

**Заменяет собой:** `docs/superpowers/specs/2026-07-30-responsive-narrow-screen-layout-design.md` и `docs/superpowers/specs/2026-07-30-narrow-joystick-half-width-design.md`. Оба спека вводили раздельные wide/narrow раскладки `MainView`; этот документ делает прежнюю «узкую» компоновку (панель программы сверху на всю ширину, джойстики снизу) единственной — для всех размеров экрана. Оставляю оба файла в `docs/superpowers/specs/` как исторические — не удаляю, только фиксирую, что их устроен `.narrow`-переключатель этим дизайном полностью убирается из кода.

## Проблема

Сейчас `MainView.axaml` держит две параллельные раскладки, переключаемые по ширине окна (`OnSizeChanged` в `MainView.axaml.cs`, порог 700px):
- **Широкая**: `HeaderGrid` — статус слева, Play/Пауза/Стоп справа в один ряд; `ContentGrid ColumnDefinitions="Auto,*,Auto"` — джойстик / панель программы (`MaxWidth=360`, по центру) / джойстик, все в одну строку.
- **Узкая (`.narrow`)**: `HeaderGrid` — статус сверху, кнопки снизу (два Auto-ряда); `ContentGrid ColumnDefinitions="*,*"` — панель программы сверху на всю ширину (`Row=0`), оба джойстика по центру каждой своей половины снизу (`Row=1`).

Требуется единая раскладка для всех размеров экрана: **верх** — статус подключения и общая панель управления (Homing/Сброс/Отключить/Пуск/Пауза/Стоп/Лог), **середина** — текущая программа (список точек, кнопки управления программой), **низ** — оба джойстика рядом, по краям. Никакого переключения по breakpoint — одна и та же структура элементов на любой ширине.

## Шапка (`HeaderGrid` → простой `WrapPanel`)

Именованный `Grid ColumnDefinitions="*,Auto"` с классами `.narrow`, стилями `Style Selector="Grid#HeaderGrid..."` и `Style Selector="Grid#HeaderGrid.narrow..."` удаляются целиком. Вместо `Grid` — `WrapPanel x:Name="HeaderPanel" ItemSpacing="12" LineSpacing="8"` с двумя детьми, в том же порядке, что и сейчас:
1. `ContentControl x:Name="ConnectionStatus" Content="{Binding Connection}"` (без изменений — по-прежнему рендерит `ConnectionView`: статус, Homing, Сброс аварии, Отключить, машинное состояние/позиция, ошибка).
2. `WrapPanel x:Name="PlaybackButtons" ItemSpacing="8" LineSpacing="8"` (без изменений внутри — Пуск/Пауза/Стоп/бейдж состояния/Лог G-code).

`WrapPanel` сам переносит второй блок на новую строку, если ширина не позволяет разместить оба в одну — без кода в `MainView.axaml.cs`. Внешний `Border` (шапка, `Classes="reveal-1"`, `DockPanel.Dock="Top"`) не меняется.

Раз `ConnectionView` — уже полноценная панель управления подключением/машиной (статус, Homing, Сброс, Отключить), а `PlaybackButtons` — панель управления воспроизведением, объединение их в одну шапку и есть требуемая «верхняя часть: статус + основная панель управления системой». Новый функционал не добавляется — переставляются существующие блоки.

## Средняя часть — панель программы (`ContentGrid`, Row 0)

Содержимое `ProgramPanel` (`ScrollViewer` → `StackPanel Spacing="10"`: заголовок «ПРОГРАММА», подсказка про джойстики, имя программы + меню «⋮», кнопка «Захватить точку», список точек `ItemsControl`, блок прогресса сегмента, баннер ошибки) переносится **без изменений внутри** — та же разметка, тот же `x:DataType`, те же биндинги и команды.

Меняется только внешнее позиционирование:
- `Grid.Row="0"` в новом `ContentGrid` (см. ниже), без `Grid.Column`/`ColumnSpan` — сетка теперь однoколоночная.
- `MaxWidth` (сейчас `360` на широком, снят на узком) убирается совсем — панель всегда занимает всю ширину `ContentGrid` (подтверждено пользователем: без ограничения ширины).
- `Margin="24,0"` (боковые отступы для центрирования в широком режиме) убирается — больше не нужен, панель и так на всю ширину; сохраняется `Margin="0,0,0,12"` снизу (отступ до ряда джойстиков), как было в узком режиме.

Никаких новых полей/настроек программы не добавляется — это подтверждённое ограничение скоупа.

## Нижняя часть — джойстики по краям (`ContentGrid`, Row 1)

Новый вложенный `Grid x:Name="JoystickBar" Grid.Row="1" ColumnDefinitions="Auto,*,Auto"`:
- `Grid.Column="0"` — `LeftJoystick` (как сейчас: `Mode="Fixed"`, `Shape="Circle"`, `IsEnabled="{Binding !IsProgramLocked}"`, те же обработчики `JoystickDown/Move/Up`, тот же `ToolTip.Tip`).
- `Grid.Column="1"` — пустая звёздочная колонка-распорок (без содержимого), раздвигает джойстики к краям.
- `Grid.Column="2"` — `RightJoystick` (аналогично).

Оба джойстика — `VerticalAlignment="Center"` внутри своей `Auto`-колонки (колонка обжимается по `Radius`, вертикальное центрирование сохраняет текущий визуальный эффект). `HorizontalAlignment` не задаётся — `Auto`-колонка и так равна размеру джойстика, лишних отступов от края `ContentGrid` не добавляется (совпадает с прежним широким поведением, где джойстики стояли вплотную к краю `Auto`-колонки).

## `ContentGrid`: итоговая форма

```
Grid x:Name="ContentGrid" RowDefinitions="*,Auto"
├─ Row 0: ScrollViewer x:Name="ProgramPanel"      (без Grid.Column — сетка однoколоночная)
└─ Row 1: Grid x:Name="JoystickBar" ColumnDefinitions="Auto,*,Auto"
   ├─ Col 0: LeftJoystick
   ├─ Col 1: (пусто, распорок)
   └─ Col 2: RightJoystick
```

`Style Selector="Grid#ContentGrid..."` и `Style Selector="Grid#ContentGrid.narrow..."` (все 6 текущих блоков в `MainView.axaml.Styles`) удаляются — новая раскладка описывается напрямую через `Grid.Row`/`ColumnDefinitions` в разметке, без переключаемых классов.

## `MainView.axaml.cs`: расчёт радиуса джойстиков

Убирается: поле `_isNarrow`, константа `NarrowLayoutBreakpoint`, весь код переключения классов `narrow` и `RowDefinitions`/`ColumnDefinitions` в `OnSizeChanged`, `ClearValue(VirtualJoystick.RadiusProperty)` для широкого режима (виджет больше не имеет статичного `Style Setter` радиуса — он всегда вычисляется).

`OnSizeChanged` упрощается до: на каждое изменение размера — вычислить и применить `Radius` для обоих джойстиков. Формула меняется из-за смены геометрии (джойстики больше не заперты в звёздочных полуколонках, а сидят в `Auto`-колонках по краям на всю ширину экрана):

```csharp
private const double ContentGridChromeWidth = 54;   // как раньше: Border.Margin(12+12) + BorderThickness(2) + ContentGrid.Margin(20+20)
private const double MinRadius = 50;
private const double MaxRadius = 110;                // верхний предел — раньше сдерживался шириной полуколонки, теперь нужен явно
private const double CenterGap = 24;                 // минимальный зазор между двумя джойстиками по центру нижней строки
private const double ContentBorderVerticalChrome = 26; // Border.Margin(12+12 верт.) + BorderThickness(2) вокруг ContentGrid, без учёта шапки
private const double ContentGridVerticalMargin = 40;    // ContentGrid.Margin(20+20 верт.)
private const double ProgramPanelMinHeight = 160;        // минимум, который всегда остаётся панели программы

internal static double ComputeJoystickRadius(double mainViewWidth, double mainViewHeight, double headerHeight)
{
    var contentGridWidth = mainViewWidth - ContentGridChromeWidth;
    var widthRadius = (contentGridWidth - CenterGap) / 4;

    var contentGridHeight = mainViewHeight - headerHeight - ContentBorderVerticalChrome - ContentGridVerticalMargin;
    var joystickRowBudget = contentGridHeight - ProgramPanelMinHeight;
    var heightRadius = joystickRowBudget / 2;

    return Math.Clamp(Math.Min(widthRadius, heightRadius), MinRadius, MaxRadius);
}
```

`headerHeight` больше не оценка-константа (прежний `MainViewChromeHeight=166` включал захардкоженную «оценку высоты двухрядной узкой шапки ≈100» — теперь шапка это `WrapPanel`, который может занять 1–3 строки в зависимости от фактической ширины и локали кнопок, оценка ненадёжна). Вместо этого `OnSizeChanged` читает **фактическую** высоту через `HeaderBorder.Bounds.Height`, где `HeaderBorder` — новое имя для существующего `Border DockPanel.Dock="Top"`, оборачивающего `HeaderPanel` (сейчас у этого `Border` нет `x:Name`, только `Classes="reveal-1"`). `Bounds` в момент `SizeChanged` отражает layout после последнего прохода — при первом рендере может быть `0` до первого layout-пасса; при `headerHeight <= 0` код подставляет консервативный фолбэк в одну строку (высота шапки с `Padding="12,10"` и одной строкой контента, эмпирически ≈44px, задаётся константой `HeaderFallbackHeight`), чтобы не делить на некорректный бюджет на самом первом кадре — раскладка досчитается корректно на следующем `SizeChanged`/`LayoutUpdated`.

`Clamp` в `Math.Clamp(value, min, max)` требует `min <= max` — `MinRadius=50 <= MaxRadius=110`, корректно при любых входных размерах (в отличие от `Math.Min`/`Math.Max` по отдельности, где при отрицательном `joystickRowBudget` радиус раньше мог уйти в аномальные значения до применения нижнего порога — `Clamp` применяет оба предела разом).

## Прокрутка и оверлеи

Не меняются: `ProgramPanel` остаётся `ScrollViewer`, `JoystickBar` — сосед `ProgramPanel` внутри `ContentGrid` (не потомок `ScrollViewer`), так что прокрутка длинного списка точек не затрагивает джойстики — тот же принцип, что и в замещаемом дизайне. Модальные оверлеи (редактор точки, подтверждение, переименование, библиотека, лог G-code, модалка подключения) — прямые дети `RootPanel`/корневого `Grid`, вне `ContentGrid`, их центрирование не зависит от этой раскладки.

## Затронутые файлы

- `ArctZ/Views/MainView.axaml` — шапка `Grid`→`WrapPanel` (без `.narrow`-стилей), `ContentGrid` на `RowDefinitions="*,Auto"` с новым `JoystickBar`, `ProgramPanel` без `MaxWidth`/`Grid.Column`.
- `ArctZ/Views/MainView.axaml.cs` — упрощение `OnSizeChanged`, новая `ComputeJoystickRadius`, чтение `HeaderBorder.Bounds.Height`.

## Тестирование

В решении нет View-тестов (как и в замещаемых спеках) — `ArctZ.Tests` не содержит UI-слоя. Проверка:
- `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj` — компиляция XAML/code-behind.
- `ComputeJoystickRadius` — чистая статическая функция от трёх `double`, как и её предшественница `ComputeNarrowJoystickRadius`, для которой уже есть тесты в `ArctZ.Tests/Views/MainViewNarrowJoystickRadiusTests.cs`. Этот файл переносится на новую сигнатуру (`ComputeJoystickRadius(width, height, headerHeight)`) и новые граничные случаи: узкий телефон (360×640), широкий десктоп (1920×1080), экстремально низкое окно (высотный бюджет джойстиков уходит в отрицательное значение — проверить, что `Clamp` всё равно возвращает `MinRadius`, а не бросает/уходит в NaN), `headerHeight <= 0` (фолбэк на первом кадре).
- Визуально через `ArctZ.Browser` + Playwright: ресайз вьюпорта на нескольких ширинах (360, 700, 1200, 1920px) и высотах, проверить что джойстики не наезжают на панель программы и не касаются друг друга/краёв.

## Не в скоупе

- Изменение `ConnectionView.axaml` — отдельный компонент, встраивается как есть.
- Визуальный/стилевой редизайн (цвета, отступы внутри существующих блоков, оформление панелей) — подтверждено пользователем, только структура.
- Новые настройки программы (скорость, feed rate, повтор цикла, редактор переходов) — подтверждено пользователем, только реорганизация существующего.
- Адаптация модальных оверлеев под новую раскладку — не запрошено, не меняются.
