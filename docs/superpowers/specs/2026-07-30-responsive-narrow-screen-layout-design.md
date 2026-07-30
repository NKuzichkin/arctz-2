# Адаптивная раскладка MainView для узких экранов — дизайн

Дата: 2026-07-30

## Проблема

`MainView.axaml` сейчас рассчитан только на широкие экраны: центральный `Grid ColumnDefinitions="Auto,*,Auto"` держит левый джойстик, панель программы (`MaxWidth=360`) и правый джойстик в один горизонтальный ряд, а шапка — статус подключения и кнопки Play/Пауза/Стоп — тоже в один ряд (`*,Auto`). На узких экранах (смартфон, ~360–430px) это не помещается: джойстики схлопываются или обрезаются, кнопки программы («Захватить точку»/«Новая»/«Сохранить»/«Библиотека») уже сейчас потенциально не влезают в `MaxWidth=360` даже на широком экране.

Нужно:
1. На узком экране джойстики уходят под панель программы (панель — сверху на всю ширину, оба джойстика — рядом в одну строку под ней, ближе к центру). На широком — раскладка не меняется.
2. Обе кнопочные панели (шапка Play/Пауза/Стоп и ряд кнопок программы) выравниваются и адаптируются под узкий экран, не ломая широкий.

## Механизм переключения раскладки

Avalonia не имеет media queries. Переключение делается кодом в `MainView.axaml.cs`: подписка на `SizeChanged`, при `NewSize.Width < 700` на двух именованных `Grid` (`HeaderGrid`, `ContentGrid`) выставляется класс `narrow`, иначе снимается. Большая часть остального — декларативные `Style Selector="Grid#X.narrow > Тип#Имя"`, переопределяющие присоединённые свойства (`Grid.Row/Column/ColumnSpan`, `Margin`, `MaxWidth`) у конкретных именованных детей. Порог: **700px** ширины `MainView`. Дерево элементов не перестраивается, ViewModel не участвует — чисто вью-слой.

**Уточнение (обнаружено при реализации Task 2):** `Grid.RowDefinitions`/`Grid.ColumnDefinitions` — обычные CLR-свойства на `Avalonia.Controls.Grid`, а не зарегистрированные `AvaloniaProperty` (нет полей `RowDefinitionsProperty`/`ColumnDefinitionsProperty`; подтверждено ошибкой компилятора Avalonia "Unable to find RowDefinitionsProperty field on type Avalonia.Controls.Grid"). `Style Setter` их менять не может — это ограничение платформы. Поэтому сами `RowDefinitions`/`ColumnDefinitions` переключаются в том же `OnSizeChanged` в code-behind, а через `Style Setter` в XAML настраиваются только per-child `Grid.Row/Column/ColumnSpan`, `Margin`, `MaxWidth`, `HorizontalAlignment` — они действительно являются `AvaloniaProperty` и стилизуются штатно.

Инициализация: обработчик `SizeChanged` также вызывается при первом лэйауте (переход от `Size.Empty` к фактическому размеру), так что дополнительного вызова при загрузке не требуется — но на всякий случай состояние синхронизируется и в конструкторе после `InitializeComponent()`, если `Bounds.Width > 0` уже известна на момент конструктора (защита от edge-case, когда контрол переиспользуется/переприкрепляется).

## `ContentGrid` (джойстики + панель программы)

Именуем текущий `Grid ColumnDefinitions="Auto,*,Auto" Margin="20"` внутри `Border.reveal-3` как `x:Name="ContentGrid"`. `RowDefinitions="Auto,Auto"` и `ColumnDefinitions` переключаются в `OnSizeChanged` (code-behind, не через `Style Setter` — см. уточнение выше) **только когда `isNarrow == true`**; на широком `ColumnDefinitions` возвращается к `"Auto,*,Auto"`, а `RowDefinitions` — к пустому значению (один неявный ряд). Neявный ряд без явных `RowDefinitions` растягивается на всю доступную высоту, что и держит текущее вертикальное центрирование джойстиков/панели на широком экране; явные Auto-строки без звёздочной строки такого растяжения не дают (лишняя высота осталась бы неиспользованной), поэтому переключение строк должно быть частью узкой раскладки, а не постоянным.

Именуем детей: `LeftJoystick`, `ProgramPanel` (текущий средний `StackPanel`), `RightJoystick`.

- **Широкий (по умолчанию, без класса `narrow`)** — как сейчас: `ColumnDefinitions="Auto,*,Auto"`; `LeftJoystick` → Column 0, `ProgramPanel` → Column 1 (`MaxWidth=360` как сейчас), `RightJoystick` → Column 2. Все — Row 0 (неявно).
- **Узкий (`.narrow`)** — `ColumnDefinitions` (в `OnSizeChanged`) переключается на `*,Auto,Auto,*` (устарело — см. narrow-joystick-half-width-design.md, колонки теперь `*,*`); `Grid.Row/Column/ColumnSpan` per-child по-прежнему настраиваются через `Style Setter` в XAML:
  - `ProgramPanel`: `Grid.Row=0, Grid.Column=0, Grid.ColumnSpan=4`, `MaxWidth` снимается (`Infinity`) — панель растягивается на всю ширину экрана.
  - `LeftJoystick`: `Grid.Row=1, Grid.Column=1`, `Margin="0,0,20,0"`.
  - `RightJoystick`: `Grid.Row=1, Grid.Column=2`, `Margin="20,0,0,0"`.

  Крайние `*`-колонки (0 и 3) — гибкие распорки: пара джойстиков (обе `Auto`-колонки) всегда держится вместе по центру строки и не расползается к краям экрана независимо от того, 380px это экран или 690px.

## `HeaderGrid` (шапка: статус + Play/Пауза/Стоп)

Именуем текущий `Grid ColumnDefinitions="*,Auto"` в шапке как `x:Name="HeaderGrid"`. Так же, как у `ContentGrid`, `RowDefinitions="Auto,Auto"` переключается в `OnSizeChanged` только при `isNarrow == true` (см. выше про растяжение неявного ряда и про то, что `RowDefinitions` не стилизуется) — здесь на практике безразлично, поскольку шапка и так сжата по высоте содержимого, но правило применяется единообразно к обоим гридам. Именуем `ContentControl` (статус подключения) как `ConnectionStatus`, `StackPanel` с кнопками заменяется на `WrapPanel x:Name="PlaybackButtons"`.

- **Широкий**: как сейчас — `ConnectionStatus` Column 0, `PlaybackButtons` Column 1, обе Row 0.
- **Узкий (`.narrow`)**: `ConnectionStatus` → `Grid.Row=0, Grid.Column=0, Grid.ColumnSpan=2`; `PlaybackButtons` → `Grid.Row=1, Grid.Column=0, Grid.ColumnSpan=2`, `HorizontalAlignment=Left`, `Margin="0,8,0,0"`. Статус — сверху на всю ширину, кнопки — снизу.

## Кнопочные панели: `WrapPanel` вместо `StackPanel Orientation="Horizontal"`

Не зависит от breakpoint'а — работает одинаково на любой ширине:

- Шапка: `StackPanel Orientation="Horizontal"` с Play/Пауза/Стоп + бейдж состояния → `WrapPanel` (см. выше, `x:Name="PlaybackButtons"`).
- Панель программы (на момент написания этого раздела): ряд «Захватить точку»/«Новая»/«Сохранить»/«Библиотека» → `WrapPanel`. **Устарело:** этот ряд впоследствии заменён на `TextBlock` с именем программы + кнопку «⋮» с `MenuFlyout` («Переименовать»/«Новая»/«Сохранить»/«Библиотека»), а «Захватить точку» вынесена отдельной кнопкой ниже — см. `docs/superpowers/specs/2026-07-30-program-menu-and-rename-dialog-design.md`. `WrapPanel` в шапке (`PlaybackButtons`) этой заменой не затронут.

Зазор задаётся свойствами самого `WrapPanel` (Avalonia 12.0.4 их поддерживает): `ItemSpacing="8"` — между кнопками в строке, `LineSpacing="8"` — между перенесёнными строками.

## Прокрутка

**Пересмотрено после первой реализации:** изначально весь `ContentGrid` (оба джойстика + панель программы) оборачивался в один `ScrollViewer` — из-за этого длинный список точек мог утащить джойстики за пределы экрана при прокрутке. Финальная версия: `ScrollViewer` оборачивает **только содержимое `ProgramPanel`** — сам `ProgramPanel` теперь `ScrollViewer x:Name="ProgramPanel"` (не `StackPanel`; внутри — `StackPanel Spacing="10"` с тем же содержимым, что и раньше), стилизуется и позиционируется в точности как раньше (`Grid.Column/Row/ColumnSpan/MaxWidth/Margin` через те же `.narrow`/безусловные стили, только селектор типа поменялся с `StackPanel` на `ScrollViewer`).

В узком режиме `ContentGrid.RowDefinitions` в `OnSizeChanged` теперь `"*,Auto"` (было `"Auto,Auto"`): строка панели (`Row=0`) — звёздочная, забирает всё оставшееся пространство и сама прокручивается внутри себя, если контент выше выделенной высоты; строка джойстиков (`Row=1`) — `Auto`, всегда получает ровно свою естественную высоту и потому прижата ровно к нижнему краю `ContentGrid`, никогда не оказываясь внутри прокручиваемой области. Джойстики физически — соседи `ProgramPanel` внутри `ContentGrid`, а не его потомки, поэтому прокрутка панели их не касается вообще.

Подтверждено headless-прогоном Avalonia (не визуально, а измерено): нижняя граница ряда джойстиков совпадает с нижней границей `ContentGrid` на всех проверенных ширинах; `LeftJoystick`/`RightJoystick` не являются визуальными потомками `ScrollViewer`; на очень низком окне (380×500) джойстик остаётся полностью в границах окна, при этом `ScrollViewer` панели показывает `V-SCROLL=YES`.

В широком режиме `ContentGrid` без явных `RowDefinitions` (один неявный растягивающийся ряд, как и раньше) — `ProgramPanel` теперь тоже `ScrollViewer`, поэтому если его содержимое когда-нибудь станет выше доступной высоты, прокрутится только он, джойстики (в соседних Auto-колонках) не затронуты.

Три модальных оверлея (`IsEditingKeyPoint`, `PendingConfirmation`, `IsLibraryOpen`) остаются прямыми детьми `RootPanel`, вне `ContentGrid` — их центрирование не зависит от прокрутки панели программы.

## Затронутые файлы

- `ArctZ/Views/MainView.axaml` — именование `HeaderGrid`/`ContentGrid`/детей, стили `.narrow`, замена `StackPanel`→`WrapPanel` в двух местах, `ProgramPanel` — `StackPanel`→`ScrollViewer`.
- `ArctZ/Views/MainView.axaml.cs` — обработчик `SizeChanged`, метод переключения классов `narrow` на `HeaderGrid`/`ContentGrid`, переключение `RowDefinitions`/`ColumnDefinitions` (не стилизуется — см. выше).

## Не в скоупе

- Адаптация модальных оверлеев (редактор точки, подтверждение, библиотека программ) под узкий экран — не запрошено, они и так по центру с фиксированной шириной ≤360px, что помещается на любом смартфоне.
- Адаптация `ConnectionView` (модалка подключения) — отдельный компонент, не упомянут в задаче.
- Тесты — в решении нет тестового покрытия UI-層 (`ArctZ.Tests` не содержит View-тестов); проверка через `dotnet build` и визуально через `ArctZ.Browser` + Playwright (ресайз вьюпорта, скриншоты обеих раскладок).
