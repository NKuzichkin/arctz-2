# Переход на Avalonia Zafiro (layout + ViewModels) — дизайн

Дата: 2026-07-29

## Проблема / цель

ArctZ сейчас построен на `CommunityToolkit.Mvvm` (`ObservableObject`/`[ObservableProperty]`/`[RelayCommand]`) и обычной Avalonia-разметке (`Grid`/`Border`/конвертеры), со своим `IUiDispatcher` для тестируемости. Требуется перейти на полный стек `Zafiro.Avalonia` + `ReactiveUI`: реактивные ViewModels (`ReactiveObject`, `IEnhancedCommand`), семантические layout-контейнеры (`HeaderedContainer`/`EdgePanel`/`Card`), иконки через `{Icon}`, behaviors вместо конвертеров.

Переход выполняется **поэтапно, экран за экраном**, с рабочим приложением и зелёными тестами на каждом шаге — не одним rewrite-branch.

## Текущее состояние (baseline)

- **ViewModels**: `ViewModelBase : ObservableObject`; `ConnectionViewModel`, `ProgramViewModel` (501 строк — самый крупный), `KeyPointEditorViewModel`, `JoystickInputMapper` — все на `[ObservableProperty]`/`[RelayCommand]`.
- **Потоковость**: `IUiDispatcher`/`AvaloniaUiDispatcher`(прод)/`InlineUiDispatcher`(тесты) — единственный механизм детерминированных тестов; `ConnectionViewModel.OnSessionConnectionStateChanged` — типичный пример ручного `CheckAccess()`/`Post(...)`.
- **DI**: `Microsoft.Extensions.DependencyInjection`, регистрация в `Services/Device/ServiceCollectionExtensions.cs` (`AddArctZCore`). `App.axaml.cs.OnFrameworkInitializationCompleted` вручную резолвит `ProgramViewModel` и присваивает `DataContext` на `MainWindow`/`MainView`/`singleViewPlatform.MainView`.
- **Views**: один главный экран `MainView.axaml` (библиотека программ, авторинг ключевых точек, воспроизведение) с модальными оверлеями (подключение, редактор точки, подтверждение) — **не** табы/сайдбар/множественные секции. `ConnectionView.axaml` — панель статуса. `Components/VirtualJoystick/` — кастомный `TemplatedControl`, стилизуется через `Themes/VirtualJoystick.axaml`.
- **Тесты**: `ArctZ.Tests` — xUnit, покрывает `Services/Device`, `Services/Program`, часть ViewModels, все опираются на `InlineUiDispatcher`.

## Осознанное сужение объёма (принято)

Zafiro's `[Section]`/`IShellViewModel`/`INavigator`/`SlimWizard` рассчитаны на приложение с несколькими секциями (сайдбар/табы/мастер). ArctZ — один экран с модальными оверлеями, не набор секций.

- **Берём сейчас**: `ReactiveObject`/`[Reactive]`/`IEnhancedCommand`/`CompositeDisposable`; `DataTypeViewLocator` + `CompositionRoot` (заменяет ручное присваивание `DataContext`); весь layout-слой (`HeaderedContainer`/`EdgePanel`/`Card`, `{Icon}`, `Interaction.Behaviors` вместо конвертеров).
- **Откладываем**: `INavigator`/`[Section]`/`SlimWizard` — вводить только когда появится реальная многошаговая навигация (например, отдельный экран настроек, либо редактор ключевой точки оформится как мастер). Вводить их сейчас означало бы проектировать под несуществующую навигацию.
- Редактор `KeyPointEditorViewModel` **не** переводится на `SlimWizard` в рамках этого перехода — остаётся модалкой, как сейчас, до появления отдельного повода.

## Пакеты

Добавляются в `Directory.Packages.props` (централизованно, как `CommunityToolkit.Mvvm` сейчас):

- `ReactiveUI`, `Avalonia.ReactiveUI` (интеграция с Avalonia lifecycle)
- `ReactiveUI.SourceGenerators` (атрибут `[Reactive]`)
- `Zafiro.Avalonia` (containers, `IconExtension`, `DataTypeViewLocator`, `.Enhance()`/`IEnhancedCommand`, `INavigator`, `SlimWizard`/`WizardBuilder`, `[Section]` — используем сейчас только первые три группы)
- `Projektanker.Icons.Avalonia.FontAwesome` + `ProjektankerIconControlProvider` — реальные глифы для `{Icon fa-...}` (нужен, только если фаза 4 берёт иконки; иначе можно исключить)

`CommunityToolkit.Mvvm` остаётся в `Directory.Packages.props`, пока не мигрирует последний ViewModel (фаза 5).

## Фазы

Каждая фаза заканчивается рабочим приложением (`dotnet build` + ручной прогон в Desktop-хосте) и зелёным `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`.

### Фаза 0 — Фундамент

- Добавить пакеты.
- Завести `ReactiveViewModelBase : ReactiveObject, IDisposable` (с `CompositeDisposable`) рядом со старым `ViewModelBase : ObservableObject` — сосуществуют, пока не мигрируют все ViewModels.
- `App.axaml`: зарегистрировать `<DataTypeViewLocator />` + `<DataTemplateInclude Source="avares://Zafiro.Avalonia/DataTemplates.axaml" />` в `Application.DataTemplates`.
- Ввести `CompositionRoot` поверх текущего `AddArctZCore` — не переписывая регистрации `Services/Device`/`Services/Program`.
- `App.axaml.cs`: заменить ручное `DataContext = viewModel` на резолюцию через `DataTypeViewLocator`.
- Эта фаза не меняет поведение ни одного ViewModel — существующий `ArctZ.Tests` должен остаться зелёным без изменений в тестах.

### Фаза 1 — Пилот: `ConnectionViewModel` + `ConnectionView`

- Самый маленький VM (`Session`/`ConnectionState`/`SelectedEndpoint`, 4 команды: `ConnectAsync`/`DisconnectAsync`/`HomeAsync`/`ResetAlarmAsync`) — переписать на `ReactiveObject`+`[Reactive]`, команды на `ReactiveCommand.Create...().Enhance()`.
- Убрать `IUiDispatcher` из этого VM: `OnSessionConnectionStateChanged` (сейчас ручной `CheckAccess()`/`Post(...)`) заменяется на `Observable.FromEvent(...).ObserveOn(RxApp.MainThreadScheduler)`.
- Тесты `ConnectionViewModel` переписать: вместо `InlineUiDispatcher` — `RxApp.MainThreadScheduler` подменяется на `ImmediateScheduler.Instance` на время теста.
- `ConnectionView.axaml`: перевести на `HeaderedContainer`/`EdgePanel`, убрать `ConnectionStateConverters` там, где логику можно перенести в VM-свойство или behavior.
- Эта фаза фиксирует итоговый паттерн (VM + тест + View) — последующие фазы повторяют его на большем масштабе.

### Фаза 2 — `ProgramViewModel` + `MainView` (самая крупная)

- Разбить миграцию по под-фичам самого VM (библиотека программ / авторинг ключевых точек / воспроизведение траектории), мигрировать последовательно — не одним диффом на 501 строку.
- `MainView.axaml`: заменить вложенные `Grid`/`StackPanel` на `EdgePanel`/`Card`/`HeaderedContainer` там, где это реально спрямляет разметку — не переписывать разметку, которая уже плоская.
- Обновить тесты `ProgramViewModel` по тому же паттерну, что в фазе 1.

### Фаза 3 — `KeyPointEditorViewModel`, `JoystickInputMapper`

- Меньшие VM, тот же паттерн, что в фазе 1.
- `SlimWizard` для модалки редактора точки — не в этой фазе (см. «Осознанное сужение объёма»).

### Фаза 4 — Тема/иконки/поведения проходом по всем View

- `Themes/Colors.axaml`/`HudControls.axaml` — привести к конвенции Zafiro (`DynamicResource`, группировка стилей по категориям), без смены визуального языка приложения.
- `Components/VirtualJoystick/` — это `TemplatedControl`, не ViewModel; ReactiveUI его не касается, только точечная темизация через `Themes/VirtualJoystick.axaml`, если потребуется для консистентности.
- Иконки через `{Icon fa-...}` вместо любых текстовых/эмодзи-заглушек, если такие остались.
- Оставшиеся конвертеры в `Converters/` — заменить на `Interaction.Behaviors` или VM-свойства, где применимо.

### Фаза 5 — Уборка

- Удалить `ViewModelBase`(`ObservableObject`)/`IUiDispatcher`/`AvaloniaUiDispatcher`/`InlineUiDispatcher`/пакет `CommunityToolkit.Mvvm` — только когда во всех ViewModels не осталось ссылок.
- `ReactiveViewModelBase` переименовать обратно в `ViewModelBase`.
- Обновить `AI_AGENT_README.md`/`CLAUDE.md` под новый стек (базовый класс, DI, threading-конвенция).

## Тестирование (сквозная линия через все фазы)

- Каждая мигрированная VM: тесты переключаются с `InlineUiDispatcher` на подмену `RxApp.MainThreadScheduler` на `ImmediateScheduler.Instance` (через `using` scope вокруг теста).
- Ничего не мигрирует без проходящих тестов на этой фазе — это и есть критерий «рабочее приложение на каждом шаге».

## Ветвление

Работа продолжается на `master` — по прошлой практике в этом проекте (см. память: пользователь один раз отказался от изоляции через worktree). Учитывая масштаб (5 фаз, полная смена MVVM-стека), стоит перепроверять этот выбор перед каждой крупной фазой, а не считать решённым на весь переход.

## Не в скоупе

- `INavigator`/`[Section]`/`SlimWizard` — см. «Осознанное сужение объёма».
- Изменение поведения `Services/Device/*`/`Services/Program/*` — это чисто UI/ViewModel-слой миграция, протокол и бизнес-логика не трогаются.
- Визуальный редизайн (цвета/бренд) — только приведение к конвенции Zafiro (`DynamicResource`, стили по классам), не смена внешнего вида.
