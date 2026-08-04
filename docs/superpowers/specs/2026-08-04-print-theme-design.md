# Print-тема для монохромной печати скриншотов

## Проблема

Нужны скриншоты интерфейса ArctZ для печати на монохромном принтере (документация/инструкции). Текущая HUD-тема — тёмная, с цветным акцентом (циан), градиентами, свечением и полупрозрачными оверлеями — плохо печатается: полутона и свечение на чёрно-белом принтере превращаются в грязные серые пятна, а цветной акцент неотличим от фона в градациях серого.

Нужна альтернативная тема, где контролы — это чёрная граница на белом фоне (плюс серый для hover/pressed/disabled), без градиентов, свечения и теней. Тема активируется параметром командной строки при запуске Desktop-версии, не меняет поведение приложения по умолчанию.

## Область действия

Только `ArctZ.Desktop`. Android/iOS/Browser не получают этот параметр — печать скриншотов имеет смысл только как инструмент подготовки документации/руководства, для чего desktop-сборка достаточна. Расширение на другие платформы не планируется.

## Активация

`ArctZ.Desktop.exe --theme=print`

Формат параметра `--theme=<name>` выбран заранее расширяемым (позволяет в будущем добавить другие именованные темы, если понадобится), но в этой работе реализуется только значение `print`. Любое другое/отсутствующее значение — поведение не меняется (текущая HUD-тема).

`Program.cs` разбирает `args` до вызова `BuildAvaloniaApp()...StartWithClassicDesktopLifetime(args)` и выставляет `App.PrintMode` (статическое свойство) до создания `App`.

## Архитектура переключения темы

В `App.axaml.cs`, метод `Initialize()`, после `AvaloniaXamlLoader.Load(this)`:

1. Если `PrintMode == false` — ничего не делать, путь рендера не меняется вообще (нулевой риск регрессии текущей HUD-темы).
2. Если `PrintMode == true`:
   - `RequestedThemeVariant = ThemeVariant.Light` — переключает встроенную палитру FluentTheme (немаркированные части стандартных контролов) на светлую без ручного переопределения базовых `System*`-ключей.
   - В `Resources.MergedDictionaries` добавляется `avares://ArctZ/Themes/PrintColors.axaml` (см. ниже) — перекрывает все `Hud*Color` ключи, определённые в `Colors.axaml`. Работает, потому что эти ключи существуют только в merged-словарях, а не как прямые дочерние ключи корневого `Application.Resources` — добавление словаря последним в `MergedDictionaries` даёт ему приоритет.
   - `SystemAccentColor` и его 6 вариантов (`Light1-3`/`Dark1-3`) переопределяются **в коде**, напрямую через индексатор `Resources["SystemAccentColor"] = ...`: эти ключи заданы как прямые дочерние элементы корневого `ResourceDictionary` в App.axaml, поэтому не перекрываются подключением merged-словаря (прямые ключи имеют приоритет над merged) — единственный надёжный способ их переопределить это переприсвоить сами ключи.
   - К корневому `Window` добавляется StyleClass `"print"` — точка входа для точечных стилевых оверрайдов (джойстик, различие primary/danger), которые не сводятся к перекраске ресурсов.

## Палитра `Themes/PrintColors.axaml`

Новый файл, структура зеркалит `Colors.axaml`, но переопределяет только `Color`-ключи (брейши в `Colors.axaml` уже ссылаются на них через `StaticResource`, поэтому их трогать не нужно):

| Ключ | Тёмная тема (текущая) | Print-тема |
|---|---|---|
| `HudBackgroundColor` | `#0A0E12` | `#FFFFFF` |
| `HudPanelColor` | `#12181F` | `#FFFFFF` |
| `HudPanelElevatedColor` | `#171F27` | `#FFFFFF` |
| `HudBackgroundDeepColor` *(новый ключ, см. «Джойстик»)* | `#0C1116` | `#FFFFFF` |
| `HudBorderColor` | `#1E2830` | `#000000` |
| `HudBorderStrongColor` | `#2A3840` | `#000000` |
| `HudAccentColor` | `#3DDBD9` | `#000000` |
| `HudAccentDimColor` | `#1F4B4A` | `#CCCCCC` (серый, hover/pressed) |
| `HudAccentBrightColor` | `#7FF0EE` | `#000000` |
| `HudWarningColor` | `#E8A33D` | `#000000` |
| `HudWarningDimColor` | `#4D3818` | `#CCCCCC` |
| `HudTextPrimaryColor` | `#D8E4E8` | `#000000` |
| `HudTextSecondaryColor` | `#6B7A82` | `#666666` (серый, вторичный текст) |

`SystemAccentColor` (+ 6 вариантов), переопределяемые в коде — та же логика: базовый чёрный, `Light1-3` — оттенки серого посветлее (для hover/pressed стандартных контролов), `Dark1-3` — чёрный.

30 из 32 использований HUD-цветов во View уже идут через `StaticResource Hud*Brush` (`Colors.axaml` определяет и `Color`, и `SolidColorBrush` для каждого ключа) — один файл `PrintColors.axaml` автоматически перекрашивает почти весь интерфейс: кнопки, списки, комбобоксы, прогресс-бар, телеметрию, без правки самих View.

## HudScrimBrush — вынос затемнения модалок в ресурс

`MainView.axaml` использует литерал `Background="#CC0A0E12"` в 5 местах (редактирование ключевой точки, переименование, подтверждение удаления, библиотека, окно подключения). Выношу в `Colors.axaml`:

```xml
<SolidColorBrush x:Key="HudScrimBrush" Color="#CC0A0E12" />
```

и заменяю все 5 использований в `MainView.axaml` на `{StaticResource HudScrimBrush}`. В `PrintColors.axaml` — переопределение под печать: `#B3FFFFFF` (полупрозрачный белый) — тёмный оверлей на светлой печатной теме выглядел бы инородно и плохо печатался бы сплошным серым пятном.

## VirtualJoystick — перевод хардкода на общую систему ресурсов

`Themes/VirtualJoystick.axaml` сейчас содержит 6 литеральных hex-значений вместо `StaticResource`:

- `#171F27`, `#2A3840` — совпадают день-в-день с `HudPanelElevatedColor` / `HudBorderStrongColor` → заменяются на `StaticResource`.
- `#0C1116` (внутренний стоп градиента базы) — близко к `HudBackgroundColor` (`#0A0E12`), но чуть светлее; чтобы не потерять нынешний визуал тёмной темы, вводится новый ключ `HudBackgroundDeepColor` в `Colors.axaml` (тёмная тема: `#0C1116`) с print-эквивалентом `#FFFFFF`.
- `DropShadowEffect Color="#3DDBD9"` (2 места) — совпадает с `HudAccentColor` → заменяется на `StaticResource`.

После этого джойстик красится через тот же `PrintColors.axaml`, отдельного цветового файла не требуется.

**Убрать glow/blur/тень в print-режиме.** Перекраски ресурсов недостаточно: `BlurEffect` на ambient-glow `Ellipse`, `DropShadowEffect` вокруг ручки и полупрозрачные (`Opacity="0.3"–"0.75"`) обводки дадут на чёрно-белой печати грязное серое пятно даже если их цвет станет чёрным. Используя StyleClass `"print"` на корневом `Window` (заданный в App.axaml.cs), добавляются в `VirtualJoystick.axaml` стилевые оверрайды с более специфичным селектором поверх существующих:

```
Window.print local|VirtualJoystick /template/ Ellipse#PART_Glow  → IsVisible=False
Window.print local|VirtualJoystick /template/ Ellipse#PART_Base  → Effect=null, Stroke.Opacity=1
Window.print local|VirtualJoystick /template/ Ellipse#PART_Knob  → Effect=null, Stroke.Opacity=1
Window.print local|VirtualJoystick:active /template/ Ellipse#PART_Base → Stroke.Opacity=1 (вместо 0.55)
Window.print local|VirtualJoystick:active /template/ Ellipse#PART_Knob → Effect=null (вместо активной DropShadowEffect)
```

(Именам `PART_Glow` пока нет — ambient-glow `Ellipse` в текущей разметке безымянный; при рефакторинге ему присваивается имя, чтобы селектор мог его адресовать.)

Джойстик остаётся одним `ControlTemplate` — не дублируется вторым шаблоном под печать, различия только в стилевых сеттерах поверх.

## Различие primary/danger кнопок в print-теме

`Button.primary` и `Button.danger` сейчас красятся через `HudAccentDimBrush`/`HudWarningDimBrush` соответственно — в print-теме оба ключа станут одинаковым светло-серым (`#CCCCCC`), и кнопки визуально сольются. Под тем же `Window.print`-селектором в `Themes/HudControls.axaml`:

```
Window.print Button.danger → BorderThickness="2"
```

Danger-кнопки отличаются от primary более толстой рамкой, а не цветом.

## Файлы, которые меняются

- `ArctZ.Desktop/Program.cs` — разбор `--theme=print`, установка `App.PrintMode`.
- `ArctZ/App.axaml.cs` — свойство `PrintMode`, применение print-темы в `Initialize()`.
- `ArctZ/Themes/PrintColors.axaml` — новый файл, палитра печати.
- `ArctZ/Themes/Colors.axaml` — новые ключи `HudScrimBrush`, `HudBackgroundDeepColor`.
- `ArctZ/Themes/VirtualJoystick.axaml` — хардкод → `StaticResource`; добавление `Window.print`-оверрайдов; имя для ambient-glow `Ellipse`.
- `ArctZ/Themes/HudControls.axaml` — оверрайд `Window.print Button.danger`.
- `ArctZ/Views/MainView.axaml` — 5 замен `Background="#CC0A0E12"` → `{StaticResource HudScrimBrush}`.

## Вне рамок

- Android/iOS/Browser — не поддерживаются (см. «Область действия»).
- Тема не переключается динамически в рантайме (нет UI-переключателя) — только через параметр запуска.
- Печать/экспорт как таковые (PDF, диалог печати) не реализуются — задача только про визуальную тему для ручных скриншотов.

## Тестирование

Ручная проверка (нет автоматических UI-тестов в проекте для визуальных тем):

1. `ArctZ.Desktop.exe` без параметра — визуально сверить, что тёмная HUD-тема не изменилась (регрессия недопустима).
2. `ArctZ.Desktop.exe --theme=print` — проверить: обычные контролы (Button/ComboBox/TextBox/ListBox/ProgressBar), джойстик в состоянии покоя и активном (`:active`), одну модалку (например подтверждение удаления), телеметрию — на предмет отсутствия остаточного цвета, градиента, свечения или тени; различимость primary/danger кнопок.
