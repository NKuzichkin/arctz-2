# Автоматическая галерея скриншотов всех экранов

## Проблема

Нужен воспроизводимый способ получить скриншоты всех экранов/состояний ArctZ для документации: файл со списком экранов и PNG для каждого, сохранённые в `screenshots/` в корне репозитория. Экраны в ArctZ — не отдельные `View`, а оверлеи/модалки одного `MainView`, переключаемые булевыми свойствами `ProgramViewModel`/`ConnectionViewModel` (библиотека, редактор точки, модалка подключения/аварии и т.д.). Нужен способ попадать в каждое состояние напрямую (без прохождения UI кликами) и снимать кадр детерминированно.

## Архитектура: отдельный тестовый проект `ArctZ.Tests.Screenshots`

Новый xUnit-проект (net10.0), добавляется в `ArctZ.slnx` рядом с `ArctZ.Tests`. Референс — только на `ArctZ/ArctZ.csproj` (+ пакеты `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `Avalonia.Headless`).

Причина отдельного проекта: снимки должны использовать настоящий `ArctZ.App` (не `ArctZ.Tests.TestApp`), чтобы получить реальные `FluentTheme`, `MaterialIconStyles`, `DataTypeViewLocator` и print-палитру — без них стандартные контролы (Button/ComboBox/ListBox/Slider) не имеют `ControlTheme` и не отрисуются. Avalonia допускает только один `AppBuilder.Setup` за процесс, а `ArctZ.Tests` уже коммитится к своему `TestApp` через общий `AvaloniaHeadlessBootstrap`, используемый 9 существующими тестовыми классами. Отдельный процесс (= отдельный тестовый проект) даёт настоящий `App` без риска для существующих тестов.

Команда запуска (аналогично существующему `dotnet test ArctZ.Tests/...`):
```
dotnet test ArctZ.Tests.Screenshots/ArctZ.Tests.Screenshots.csproj
```
Этот проект не входит в обычный `dotnet test ArctZ.slnx`-флоу по умолчанию так же, как остальные — он такой же member solution-проекта, но не запускается автоматически из CLAUDE.md-команды `dotnet test ArctZ.Tests/ArctZ.Tests.csproj` (другой .csproj). Это осознанно: тест пишет ~11 PNG в `screenshots/` при каждом запуске — не тот тест, который должен гоняться в быстром цикле.

## Тема

Тема для скриншотов уже существует — это `--theme=print` (`App.PrintMode`, `PrintTheme.Apply`, `docs/superpowers/specs/2026-08-04-print-theme-design.md`): монохромная светлая палитра, специально спроектированная под чистые скриншоты (без свечения/градиентов/полутонов). Тест выставляет `App.PrintMode = true` **до** `AppBuilder.Configure<App>().UseHeadless(...).SetupWithoutStarting()` — дальше `App.Initialize()` применяет тему сам, как в проде. Плюс `window.Classes.Add("print")` на корневом `Window`, как в существующих print-тестах (`PrintThemeRenderingTests` и др.).

## Размер кадра

390×844 (мобильный) — `Window { Width = 390, Height = 844 }`.

## Детерминированность кадра

`MainView.axaml` вешает fade-in-анимации (классы `reveal-1`/`reveal-3`, `Opacity 0→1`, `FillMode=Forward`) на шапку и основную панель. В headless-режиме без прогона таймлайна анимации кадр может застрять на `Opacity=0`. Сразу после конструирования `MainView` тест рекурсивно обходит визуальное дерево и убирает классы `reveal-1`/`reveal-2`/`reveal-3` у всех найденных элементов — селекторы анимации перестают совпадать, кадр стабильно непрозрачен без гонки по времени.

## Демо-данные и подключение

DI собирается вручную (`ServiceCollection` + `AddArctZCore()`), как это делают платформенные `Program.cs`:
- `IDeviceTransport` (real-слот) и demo-транспорт — оба через `Simulation.MockDeviceTransport` (тот же тип, что и в проде для Demo-режима); real-слот не используется, т.к. подключение всегда идёт через Demo-эндпоинт.
- `IProgramStorage` — минимальная in-memory реализация (аналог `ArctZ.Tests.Services.Program.FakeProgramStorage`, продублированная в новом проекте, чтобы не тянуть `ArctZ.Tests` как зависимость), заранее засеянная одной `JibProgram` (2 точки с разными позициями).
- ReactiveUI бутстрап (`RxAppBuilder...BuildApp()` + `RxSchedulers.MainThreadScheduler = ImmediateScheduler.Instance`) — копия `ArctZ.Tests/ReactiveUIBootstrap.cs`, нужен `ConnectionViewModel` (наследник `ReactiveViewModelBase`).

Перед основным циклом скриншотов тест:
1. Создаёт `MainView` с `DataContext = ProgramViewModel` из DI, оборачивает в `Window`, `Show()`.
2. Снимает экран **`connection`** (см. каталог ниже) — единственный экран, снимаемый до подключения.
3. `Connection.SelectedEndpoint = AvailableEndpoints[Demo]`; `await Connection.ConnectCommand.Execute();` — ждёт, пока `Connection.DeviceStatus` не станет заполнен (первый статус-пул через `MockDeviceTransport`/`SystemPeriodicTimer`, короткий поллинг с таймаутом), чтобы телеметрия на "main" экране была реалистичной.
4. `await RefreshLibraryCommand.ExecuteAsync(null)` → `await LoadProgramCommand.ExecuteAsync(Library[0])` — грузит засеянную программу (имя, точки) в `ProgramViewModel`.
5. Снимает оставшиеся 10 экранов по каталогу.

## Каталог экранов (единый источник правды)

Список `(id, title, Setup, Teardown)` — один C#-массив в коде теста. `screenshots/SCREENS.md` генерируется тестом из этого же списка (id + title + порядковый номер) **до** цикла скриншотов — исключает рассинхронизацию документа и кода.

| # | id | Экран | Setup | Teardown |
|---|----|----|----|----|
| 1 | `connection` | Модалка подключения | (стартовое состояние, до Connect) | — |
| 2 | `main` | Главный экран (программа/точки/джойстики) | после Connect + LoadProgram, без оверлеев | — |
| 3 | `alarm` | Модалка аварии | `Connection.TriggerMockAlarmCommand.Execute()` | `Connection.LastAlarmCode = null` |
| 4 | `library` | Библиотека программ | `await OpenLibraryCommand.ExecuteAsync(null)` | `CloseLibraryCommand.Execute(null)` |
| 5 | `keypoint-editor` | Редактор точки | `EditKeyPointCommand.Execute(KeyPoints[0])` | `KeyPointEditor = null` |
| 6 | `completion-settings` | Настройки завершения | `EditCompletionSettingsCommand.Execute(null)` | `CompletionSettingsEditor = null` |
| 7 | `rename` | Переименование программы | `_ = RenameProgramCommand.ExecuteAsync(null)` (не ждать — виснет на диалоге) | `CancelRenameCommand.Execute(null)` |
| 8 | `confirm-delete` | Подтверждение удаления точки | `_ = RemoveKeyPointCommand.ExecuteAsync(KeyPoints[0])` (не ждать) | `ConfirmNoCommand.Execute(null)` (отклонить — точка остаётся для следующих экранов) |
| 9 | `side-menu` | Боковое меню | `ToggleSideMenuCommand.Execute(null)` | `CloseSideMenuCommand.Execute(null)` |
| 10 | `gcode-log` | Лог G-code | `Connection.ToggleGCodeLogCommand.Execute()` | тот же toggle повторно |
| 11 | `mock-settings` | Настройки мока | `Connection.ToggleMockSettingsCommand.Execute()` | тот же toggle повторно |

Между экранами: `Dispatcher.UIThread.RunJobs()` после Setup и после Teardown, чтобы binding/layout успели примениться.

Асинхронные диалоговые команды (`RenameProgramCommand`, `RemoveKeyPointCommand`) построены на `TaskCompletionSource`, который резолвится только соответствующей командой-ответом — такие команды **запускаются без `await`** (C#-async выполняется синхронно до первого незавершённого await, так что состояние типа `PendingRename`/`PendingConfirmation` уже установлено к моменту возврата управления), а закрываются явным вызовом команды-ответа. Это соответствует существующему предупреждению в `CLAUDE.md` про зависающие диалоги.

## Захват и сохранение кадра

`AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }` — реальный Skia-рендер (как в `PrintThemeRenderingTests`). Для каждого экрана: `var bitmap = window.CaptureRenderedFrame();` → `bitmap.Save(path)`. Путь к корню репозитория ищется подъёмом от `AppContext.BaseDirectory` вверх до каталога с `ArctZ.slnx`.

## Вывод

- `screenshots/SCREENS.md` — таблица id/название/файл, сгенерированная из каталога.
- `screenshots/01-connection.png` … `screenshots/11-mock-settings.png` — по одному кадру на экран, номер соответствует порядку в каталоге/MD.

## Вне рамок

- Не более одного размера кадра (390×844) — desktop-вариант не делаем.
- Не проверяются пиксельные диффы/регрессии — тест только генерирует свежий набор скриншотов при каждом запуске (перезаписывает файлы).
- `ArctZ.Tests.Screenshots` не подключается к обычному CI/тестовому циклу `dotnet test ArctZ.Tests/ArctZ.Tests.csproj` — запускается отдельно, по требованию.
