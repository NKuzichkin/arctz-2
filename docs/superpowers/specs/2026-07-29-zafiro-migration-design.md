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

- **Берём сейчас**: `ReactiveObject`/`[Reactive]`/`IEnhancedCommand`/`CompositeDisposable`; `DataTypeViewLocator` (заменяет наш собственный `ArctZ/ViewLocator.cs`); весь layout-слой (`HeaderedContainer`/`EdgePanel`/`Card`, `{Icon}`, `Interaction.Behaviors` вместо конвертеров).
- **Не берём вообще** (не только откладываем): `CompositionRoot` как отдельный тип. Существующий DI (`AddArctZCore` + `ServiceCollection`/`App.Services` в каждой из 4 платформенных точек входа) уже делает то же самое, что иллюстративный `CompositionRoot.CreateMainViewModel(...)` из skill-доков — переименовывать/оборачивать его без нового поведения было бы абстракцией ради соответствия примеру, а не ради задачи. Корневой `DataContext = viewModel` на `MainWindow`/`MainView` в `App.axaml.cs` тоже никуда не девается: `DataTypeViewLocator` резолвит только `DataTemplate`-содержимое (`ContentControl`/`ContentPresenter`), а не корневой `Window`/`UserControl`, который создаётся кодом напрямую, а не через шаблон.
- **Откладываем**: `INavigator`/`[Section]`/`SlimWizard` — вводить только когда появится реальная многошаговая навигация (например, отдельный экран настроек, либо редактор ключевой точки оформится как мастер). Вводить их сейчас означало бы проектировать под несуществующую навигацию.
- Редактор `KeyPointEditorViewModel` **не** переводится на `SlimWizard` в рамках этого перехода — остаётся модалкой, как сейчас, до появления отдельного повода.

## Пакеты (версии проверены restore'ом в изолированном scratch-проекте)

Добавляются в `Directory.Packages.props` (централизованно, как `CommunityToolkit.Mvvm` сейчас):

- `Zafiro.Avalonia` `53.3.0` — containers (`Zafiro.Avalonia.Controls.HeaderedContainer`/`EdgePanel`/`Card`), `Zafiro.Avalonia.MarkupExtensions.IconExtension`, `Zafiro.Avalonia.ViewLocators.DataTypeViewLocator`. Тянет за собой `Avalonia 12.0.4`, `ReactiveUI.Avalonia 11.4.13`, `ReactiveUI 23.2.28`, `ReactiveUI.Validation 7.1.0`, `Zafiro`/`Zafiro.UI 47.1.1` (откуда `Zafiro.UI.Commands.IEnhancedCommand`/`EnhancedCommand`, `Zafiro.UI.Navigation.INavigator`, `Zafiro.UI.Wizards.Slim.SlimWizard`/`WizardBuilder`, `Zafiro.UI.Shell.Utils.SectionAttribute` — последние три используются только если/когда отложенные Navigator/Wizard/Section всё же понадобятся).
- `ReactiveUI` `23.2.28` — явный прямой `PackageReference` нужен, т.к. код ViewModels напрямую использует `ReactiveObject`/`WhenAnyValue`/`ReactiveCommand` (транзитивная ссылка через Zafiro.Avalonia не даёт compile-time доступа без явного пакета в проекте).
- `ReactiveUI.Avalonia` `11.4.13` — **это НЕ пакет `Avalonia.ReactiveUI`** (тот застрял на Avalonia 11.x и несовместим с нашим Avalonia 12). `ReactiveUI.Avalonia` (обратный порядок слов) — актуальный пакет от reactiveui, есть версии под Avalonia 12.x, даёт `AppBuilderExtensions.UseReactiveUI(...)`, `AvaloniaScheduler`.
- `ReactiveUI.SourceGenerators` `3.1.0` — атрибут `[Reactive]`; лежит **только** в `ReactiveUI.SourceGenerators` namespace (не в `ReactiveUI`) — необходим явный `using ReactiveUI.SourceGenerators;` в каждом файле, иначе `Reactive` резолвится в другой (не-атрибутный) тип из основного `ReactiveUI` неймспейса и компиляция падает с CS0616.
- `Projektanker.Icons.Avalonia.FontAwesome` `9.6.2` — реальные глифы для `{Icon fa-...}`, нужен только для Фазы 4 (иконки).

**Важное отличие от иллюстративных примеров в skill-файлах** (те написаны под более старый ReactiveUI): в ReactiveUI `23.2.28` статического класса `RxApp` **не существует**. Эквивалент — `ReactiveUI.RxSchedulers.MainThreadScheduler`/`.TaskpoolScheduler` (статические свойства). Кроме того, ReactiveUI требует явной инициализации до первого обращения к `WhenAnyValue`/`[Reactive]`/`ReactiveCommand` — без неё бросает `InvalidOperationException: ReactiveUI has not been initialized`:

```csharp
ReactiveUI.Builder.RxAppBuilder.CreateReactiveUIBuilder()
    .WithCoreServices()
    .BuildApp();
```

В реальном приложении это делает `AppBuilder.Configure<App>().UseReactiveUI(b => b.WithAvalonia())...` в момент, когда сам `AppBuilder` реально стартует (`StartWithClassicDesktopLifetime`/`SetupWithoutStarting` и т.п.) — подтверждено пробным запуском: после этого `RxSchedulers.MainThreadScheduler` автоматически становится `ReactiveUI.Avalonia.AvaloniaScheduler`. Для `ArctZ.Tests` (голый xUnit-процесс без Avalonia lifetime) нужна отдельная, отдельно вызываемая инициализация — см. Фазу 0.

`.DisposeWith(disposables)` — это **не** ReactiveUI API, а `System.Reactive.Disposables.Fluent.DisposableExtensions.DisposeWith` (современный Rx.NET, требует `using System.Reactive.Disposables.Fluent;`).

`CommunityToolkit.Mvvm` остаётся в `Directory.Packages.props`, пока не мигрирует последний ViewModel (фаза 5).

## Фазы

Каждая фаза заканчивается рабочим приложением (`dotnet build` + ручной прогон в Desktop-хосте) и зелёным `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`.

### Фаза 0 — Фундамент

- Добавить пакеты, поднять пины `Avalonia`/`Avalonia.Themes.Fluent`/`Avalonia.Fonts.Inter`/`Avalonia.Desktop`/`Avalonia.iOS`/`Avalonia.Browser`/`Avalonia.Android` с `12.0.3` на `12.0.4` (минимум, который требует `Zafiro.Avalonia 53.3.0`; без этого `dotnet restore` падает с `NU1605`, подтверждено пробным restore).
- В каждой из 4 точек входа (`ArctZ.Desktop/Program.cs`, `ArctZ.Android/Application.cs`, `ArctZ.iOS/AppDelegate.cs`, `ArctZ.Browser/Program.cs`) добавить `.UseReactiveUI(b => b.WithAvalonia())` в цепочку `AppBuilder` — это единственное место, где реально инициализируется ReactiveUI и `RxSchedulers.MainThreadScheduler` становится `ReactiveUI.Avalonia.AvaloniaScheduler` (подтверждено пробным запуском: без вызова `UseReactiveUI` любой `WhenAnyValue`/`[Reactive]`/`ReactiveCommand` бросает `InvalidOperationException: ReactiveUI has not been initialized`).
- `ArctZ.Tests`: добавить `ReactiveUIBootstrap.cs` с `[ModuleInitializer]`-методом, вызывающим `ReactiveUI.Builder.RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().BuildApp();`, и сразу после — `ReactiveUI.RxSchedulers.MainThreadScheduler = System.Reactive.Concurrency.ImmediateScheduler.Instance;`. `ModuleInitializer` гарантирует однократный запуск при загрузке сборки тестов, до первого теста — без этого каждый тест мигрированного ViewModel упадёт с той же `InvalidOperationException`. `ImmediateScheduler` — тот же выбор, что и раньше в дизайне, просто под правильным именем API (`RxSchedulers`, не `RxApp`).
- Завести `ReactiveViewModelBase : ReactiveObject, IDisposable` (с `CompositeDisposable`) рядом со старым `ViewModelBase : ObservableObject` — сосуществуют, пока не мигрируют все ViewModels.
- `App.axaml`: зарегистрировать `<DataTypeViewLocator />` (маппится в дефолтный `xmlns="https://github.com/avaloniaui"` через `XmlnsDefinition`, подтверждено рефлексией сборки — отдельный xml-namespace-префикс не нужен) вместо `<local:ViewLocator/>` в `Application.DataTemplates`. `DataTypeViewLocator` реализует тот же `IDataTemplate` (`Build(object?)`/`Match(object?)`, публичный parameterless-конструктор) — drop-in замена подтверждена рефлексией пакета. Единственное реальное использование через DataTemplate в проекте — `ContentControl Content="{Binding Connection}"` в `MainView.axaml:81`; остальные VM (`KeyPointEditorViewModel`, `ConfirmationRequest`, повторно `ConnectionViewModel` в модалке) привязаны напрямую через `x:DataType`/`DataContext` на литеральных `Border`, DataTemplate их не резолвит. Удалить `ArctZ/ViewLocator.cs` только после визуальной/тестовой проверки, что `ConnectionView` по-прежнему рендерится на месте `ContentControl`.
- Корневой `App.axaml.cs.OnFrameworkInitializationCompleted` (резолв `ProgramViewModel` из DI, ручной `DataContext = viewModel` на `MainWindow`/`MainView`) **не меняется** — `DataTypeViewLocator` резолвит только DataTemplate-содержимое, не корневой Window/View.
- Эта фаза не меняет поведение ни одного ViewModel — существующий `ArctZ.Tests` должен остаться зелёным без изменений в тестах ViewModels (кроме самой инициализации ReactiveUI, которая ничего не проверяет по бизнес-логике).

### Фаза 1 — Пилот: `ConnectionViewModel` + `ConnectionView`

- Самый маленький VM (`Session`/`ConnectionState`/`SelectedEndpoint`, 4 команды: `ConnectAsync`/`DisconnectAsync`/`HomeAsync`/`ResetAlarmAsync`) — переписать на `ReactiveObject`+`[Reactive]` (`using ReactiveUI.SourceGenerators;` — атрибут лежит не в основном `ReactiveUI` неймспейсе), команды на `ReactiveCommand.CreateFromTask(...).Enhance(text:, name:)` (`Zafiro.UI.Commands.CommandExtensions.Enhance`).
- Убрать `IUiDispatcher` из этого VM: `OnSessionConnectionStateChanged` (сейчас ручной `CheckAccess()`/`Post(...)`) заменяется на `Observable.FromEvent<Action>(h => session.ConnectionStateChanged += h, h => session.ConnectionStateChanged -= h).ObserveOn(ReactiveUI.RxSchedulers.MainThreadScheduler)`.
- Тесты `ConnectionViewModel` не нуждаются в per-test подмене scheduler'а — `ReactiveUIBootstrap` из Фазы 0 уже фиксирует `RxSchedulers.MainThreadScheduler = ImmediateScheduler.Instance` глобально на весь тестовый процесс (это безопаснее, чем save/restore вокруг каждого теста, — `RxSchedulers.MainThreadScheduler` статическое изменяемое состояние, а xUnit может параллелить разные test-классы).
- `ConnectionView.axaml`: перевести на `HeaderedContainer`/`EdgePanel`, убрать `ConnectionStateToLabelConverter` (текст — чистое форматирование, переносится в вычисляемое свойство VM), но **оставить** `ConnectionStateToBrushConverter` как есть — сопоставление состояния с цветом кисти явно подпадает под разрешённое исключение "purely visual, highly reusable" из `behaviors.md`, замена его на классы/стили не даёт выигрыша здесь.
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

- Каждая мигрированная VM: тесты переключаются с `InlineUiDispatcher` на глобальную (заданную один раз в `ReactiveUIBootstrap`, Фаза 0) подмену `RxSchedulers.MainThreadScheduler` на `ImmediateScheduler.Instance` — весь тестовый процесс работает синхронно, отдельного per-test scope не требуется.
- Ничего не мигрирует без проходящих тестов на этой фазе — это и есть критерий «рабочее приложение на каждом шаге».

## Ветвление

Работа продолжается на `master` — по прошлой практике в этом проекте (см. память: пользователь один раз отказался от изоляции через worktree). Учитывая масштаб (5 фаз, полная смена MVVM-стека), стоит перепроверять этот выбор перед каждой крупной фазой, а не считать решённым на весь переход.

## Не в скоупе

- `INavigator`/`[Section]`/`SlimWizard` — см. «Осознанное сужение объёма».
- Изменение поведения `Services/Device/*`/`Services/Program/*` — это чисто UI/ViewModel-слой миграция, протокол и бизнес-логика не трогаются.
- Визуальный редизайн (цвета/бренд) — только приведение к конвенции Zafiro (`DynamicResource`, стили по классам), не смена внешнего вида.
