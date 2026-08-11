# Слайдер скорости джойстика — дизайн

Дата: 2026-08-11

## Проблема

Джойстики (`MainView.axaml:283-300`) шлют в `JogCommandFactory` вектор `X/Y/Force`, который одновременно определяет и размер шага перемещения, и feed-rate подачи (`JogCommandFactory.cs:24-38`). Сейчас максимальное отклонение стика всегда даёт максимальную скорость устройства — нет способа временно ограничить скорость перемещения при ручном управлении джойстиком (например, для точной подстройки позиции). Нужен слайдер 5–100%, масштабирующий эту скорость.

## Масштабирование скорости

В `ProgramViewModel` добавляется:

- `[ObservableProperty] private double _joystickSpeedPercent = 100;` (диапазон 5–100, не персистится — при каждом запуске приложения стартует с 100%).
- Приватный метод `Scale(JoystickAxisInput input) => new(input.X * JoystickSpeedPercent / 100, input.Y * JoystickSpeedPercent / 100, input.Force * JoystickSpeedPercent / 100);`

`_leftInput`/`_rightInput` (`ProgramViewModel.cs:551-561`, `564-585`) остаются нескейленными «сырыми» значениями стика, как сейчас. Скейлинг применяется только в точке отправки:

- `OnStickMove` (строка 561): `Connection.Session?.UpdateJog(new DualJoystickState(Scale(_leftInput), Scale(_rightInput)));`
- `OnStickUp` (строка 583, ветка когда один стик всё ещё активен): аналогично через `Scale(...)`.

Сгенерированный partial-метод `OnJoystickSpeedPercentChanged(double oldValue, double newValue)` пересылает `UpdateJog` с текущими скейленными значениями, если `_leftActive || _rightActive` — чтобы движение слайдера во время удержания джойстика сразу меняло скорость подачи, а не только со следующего движения стика.

Скейлится единым коэффициентом весь входной вектор — и размер шага, и feed-rate меняются согласованно (не два независимых значения).

## Адаптивная раскладка

В `MainView.axaml.cs` в `OnLayoutSizeChanged`/`UpdateJoystickRadius` (строки 61-68) добавляется вычисление `isNarrow = Bounds.Width < 700` (тот же порог, что уже используется в проекте для узкий/широкий переключений — см. `docs/superpowers/specs/2026-07-30-responsive-narrow-screen-layout-design.md`). Результат пишется в новое свойство VM `ProgramViewModel.IsNarrowJoystickLayout` (`[ObservableProperty] private bool _isNarrowJoystickLayout;`) — состояние читает XAML через `IsVisible`-биндинги, отдельного класс-переключения в code-behind не требуется (в текущей версии `MainView.axaml` такого механизма уже нет, узкая/широкая раскладка джойстиков определяется только через `ComputeJoystickRadius`).

В `JoystickBar` (`MainView.axaml:283`, `Grid ColumnDefinitions="Auto,*,Auto"`) добавляются два `Slider`, оба биндятся на `JoystickSpeedPercent` (`Minimum="5" Maximum="100"`), различаются только `IsVisible` и расположением:

- **Широкий экран** (`IsVisible="{Binding !IsNarrowJoystickLayout}"`): `Slider` шириной ~160 в среднюй колонке `Grid.Column="1"` (сейчас пустой `*`-спейсер между джойстиками), `VerticalAlignment="Center"`, `HorizontalAlignment="Center"`.
- **Узкий экран** (`IsVisible="{Binding IsNarrowJoystickLayout}"`): текущий `Grid x:Name="JoystickBar"` получает новую строку (`RowDefinitions="Auto,Auto"`, слайдер — `Grid.Row="1" Grid.ColumnSpan="3"`), `HorizontalAlignment="Stretch"`, во всю доступную ширину.

Рядом с каждым `Slider` — `TextBlock Text="{Binding JoystickSpeedPercent, StringFormat='{}{0:0}%'}"` с текущим значением в процентах.

## Затронутые файлы

- `ArctZ/ViewModels/ProgramViewModel.cs` — свойства `JoystickSpeedPercent`, `IsNarrowJoystickLayout`, метод `Scale`, правки в `OnStickMove`/`OnStickUp`, partial-метод `OnJoystickSpeedPercentChanged`.
- `ArctZ/Views/MainView.axaml` — два `Slider` + подписи-процент в `JoystickBar`, новая строка `RowDefinitions` для узкого варианта.
- `ArctZ/Views/MainView.axaml.cs` — вычисление `isNarrow` в `OnLayoutSizeChanged`, запись в `ViewModel.IsNarrowJoystickLayout`.

## Тестирование

Только через живой UI-прогон (единственный допустимый способ для этого проекта — см. `CLAUDE.md`, раздел «Тестирование UI»): собрать `ArctZ.Desktop`, запустить, попросить пользователя подвигать слайдер при разных ширинах окна и при удержании джойстика, задать вопросы через `AskUserQuestion` по каждому проверяемому поведению.

## Не в скоупе

- Персистентность значения слайдера между запусками приложения.
- Отдельные слайдеры/коэффициенты для левого и правого джойстика — один общий коэффициент на оба.
- Изменение `_maxStepDegrees`/`_maxFeedUnitsPerMin` в `JogCommandFactory` — масштабирование делается только на уровне входного вектора джойстика.
