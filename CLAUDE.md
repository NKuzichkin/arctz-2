# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

See also `AI_AGENT_README.md` for a Russian-language overview of the tech stack and folder layout.

## Вопросы к пользователю

Любой вопрос, адресованный пользователю — уточняющий, подтверждение/согласование, выбор варианта, а также вопросы с ответом да/нет (подтверждаю/отклоняю) — всегда задавать через инструмент `AskUserQuestion`, никогда простым текстом в диалоге. Это правило действует всегда, без исключений для "простых" или риторических на первый взгляд вопросов.

## Commands

Build the full solution:
```
dotnet build ArctZ.slnx
```

Build/run a single platform head:
```
dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj
dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj

dotnet build ArctZ.Browser/ArctZ.Browser.csproj
# PowerShell (the shell used on this machine):
$env:DOTNET_USE_POLLING_FILE_WATCHER = "1"; dotnet run --project ArctZ.Browser/ArctZ.Browser.csproj
# bash/sh equivalent:
DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet run --project ArctZ.Browser/ArctZ.Browser.csproj
```

Browser (WASM) build requirements:
- The `wasm-tools` workload is required (`dotnet workload install wasm-tools`). Without it the `browser-wasm` RID never gets built and `dotnet run` fails with `The application to execute does not exist: ...\browser-wasm\ArctZ.Browser.dll`.
- Do **not** work around a failing native link with `-p:WasmBuildNative=false`. It builds fine but produces an app that dies at startup with `DllNotFoundException: libSkiaSharp`, because Avalonia's Skia/HarfBuzz native assets only reach the app through that link step. `ArctZ.Browser.csproj` instead relocates just this project's `obj/` to a local disk when the checkout is on drive `Z:` — see the comment there for why.
- `DOTNET_USE_POLLING_FILE_WATCHER=1` is needed when running from drive `Z:`: the VirtualBox shared folder does not deliver native filesystem events, and the static-web-assets watcher then recurses until the host process dies with a stack overflow right after printing its `App url:` lines.

Android/iOS build requirements (already set up on this machine):
- `android` and `ios` .NET workloads installed (`dotnet workload install android ios`).
- Android build additionally needs a JDK and the Android SDK (platform-tools, `platforms;android-36`, `build-tools;36.0.0`) — installed at `%LOCALAPPDATA%\Android\Jdk` and `%LOCALAPPDATA%\Android\Sdk` (the default locations the tooling auto-detects). `JAVA_HOME` is set as a user environment variable pointing at the JDK.
- On Windows, `ArctZ.iOS` builds (targets `iossimulator-x64`) but cannot produce a device-signed app without a paired Mac/Xcode — that limitation is inherent to iOS tooling, not fixable from Windows.

Run tests: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`

Generate screenshot gallery (all 11 screens, rewrites `screenshots/`): `dotnet test ArctZ.Tests.Screenshots/ArctZ.Tests.Screenshots.csproj`

## Асинхронные диалоги-"ворота" в командах ViewModel и тесты

В `ArctZ`-ViewModel'ях диалоги пользователю (подтверждение, ввод имени и т.п.) реализованы через `TaskCompletionSource`, который разрешается только соответствующей `[RelayCommand]`-командой отклика (например, `ConfirmYes`/`ConfirmNo`/`ConfirmRename`/`CancelRename` в `ProgramViewModel`, см. `ConfirmAsync`/`RequestNameAsync`). Если такой диалог добавляется как новая асинхронная проверка внутри уже существующей команды (например, `EnsureProgramSavedAsync()` внутри `PlayAsync`), то любой тест, вызывающий эту команду и не эмулирующий ответ на диалог, **зависает навсегда** — TCS никогда не резолвится, а `await` на результате команды блокируется без таймаута. Это не "медленный" тест, а бесконечное зависание, которое останавливает весь прогон `dotnet test`.

При добавлении нового async-диалога/проверки в уже покрытую тестами команду:
1. Найти все тесты, вызывающие эту команду (`grep` по имени команды/метода в `ArctZ.Tests`).
2. Обновить тестовые хелперы так, чтобы новая проверка либо проходила мимо (например, заранее выставить нужное состояние — как `ProgramId`/`IsDirty` для флага "программа сохранена"), либо явно эмулировать ответ на диалог соответствующей `[RelayCommand]`-командой отклика.
3. Прогнать затронутые тестовые классы в изоляции через `dotnet test --filter "FullyQualifiedName~ИмяКласса"` **перед коммитом**. Если прогон не укладывается в разумное время (тесты, ранее занимавшие миллисекунды, не завершаются за секунды) — это почти всегда зависание на неотвеченном диалоге, а не флаки/медленный тест; не увеличивать таймауты, а исправлять тестовый хелпер по п. 2.

## Тестирование UI

Единственный допустимый способ проверки UI/поведенческих изменений — следующая последовательность (никаких других методов проверки, например самостоятельных скриншотов без подтверждения пользователем, недостаточно для того, чтобы считать задачу завершённой):

0. Это единственный способ проверки UI.
1. Подготовить приложение для запуска (собрать нужный platform head, например `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`).
2. Запустить приложение (`dotnet run` или запуск собранного exe) — оно должно быть реально запущено, а не просто собрано.
3. Попросить пользователя проверить функции приложения самостоятельно.
4. Задать пользователю вопросы по работе каждой проверяемой функции по задаче — через инструмент `AskUserQuestion`, по одному вопросу на каждое изменённое поведение/элемент, а не один общий вопрос "выглядит нормально?".

### Android

Для Android шаги 1–2 не выполняются автоматически: сборка APK, установка/отправка его на устройство и собственно запуск для проверки выполняются силами пользователя, а не автоматически из среды разработки. Вместо самостоятельной сборки/деплоя нужно через `AskUserQuestion` попросить пользователя собрать и установить актуальную сборку на устройство и подтвердить готовность к проверке, и только после этого переходить к шагам 3–4 (пользователь проверяет функции, затем поточечные вопросы через `AskUserQuestion`).

## Иконки (Material.Icons.Avalonia)

Пакет `Material.Icons.Avalonia` уже подключён (`ArctZ/ArctZ.csproj`, версия закреплена в `Directory.Packages.props`), но пока нигде не используется и не зарегистрирован в `App.axaml`. Перед первым использованием иконки в проекте:

1. Зарегистрировать стили контрола в `App.axaml`: добавить `xmlns:materialIcons="using:Material.Icons.Avalonia"` в корневой тег `Application`, затем `<materialIcons:MaterialIconStyles />` внутрь `Application.Styles`. Без этого шага `ControlTheme` контрола не найден и иконка не отрисуется. Устаревший вариант из старых примеров в интернете — `<StyleInclude Source="avares://Material.Icons.Avalonia/App.xaml"/>` — не использовать, актуальная версия пакета ожидает именно `MaterialIconStyles`.
2. В разметке подключать так же, через `xmlns:materialIcons="using:Material.Icons.Avalonia"`, и использовать `<materialIcons:MaterialIcon Kind="Home" />`. Значение `Kind` — член `Material.Icons.MaterialIconKind` (enum из транзитивного пакета `Material.Icons`); имена иконок ищите на pictogrammers.com/library/mdi или в IDE-автодополнении по строковому литералу.
3. Цвет задаётся через `Foreground` (наследуется как у текста, поддерживает `DynamicResource`) — использовать те же кисти HUD-палитры, что и для текста рядом (`Themes/Colors.axaml`), не хардкодить цвет напрямую в `MaterialIcon`.
4. Размер: приоритет `IconSize` (double) → `Width`/`Height` → `FontSize`, если остальное не задано. Для растяжения по контейнеру — `Classes="Fill"`.
5. Если `Kind` биндится из ViewModel (compiled bindings, `x:DataType`), тип свойства должен быть `Material.Icons.MaterialIconKind` (или nullable), а для enum-литералов в самой разметке нужен доп. `xmlns:material="using:Material.Icons"`. Строковый литерал `Kind="Home"` конвертируется type-converter'ом и с compiled bindings совместим без дополнительного namespace.

## Architecture

Avalonia UI cross-platform solution (.NET 10, MVVM via `CommunityToolkit.Mvvm`, compiled bindings). One shared core project plus four thin platform heads, all referencing the core:

- `ArctZ/` — core project: all Views, ViewModels, Components, Themes, Assets. This is where nearly all application logic lives.
- `ArctZ.Desktop/`, `ArctZ.Android/`, `ArctZ.iOS/`, `ArctZ.Browser/` — platform entry points only (bootstrap + platform manifest/config), no app logic.

Inside `ArctZ/`:
- `ViewModels/ViewModelBase.cs` — base class for all ViewModels, extends `ObservableObject` (MVVM Toolkit). New ViewModels should derive from this and use `[ObservableProperty]` / `[RelayCommand]` code-gen attributes rather than hand-written properties/commands.
- `Views/MainView.axaml` — the shared root view rendered on every platform (Desktop wraps it in `MainWindow.axaml`; mobile/browser heads host it directly).
- `Components/VirtualJoystick/` — custom `TemplatedControl` for touch-based joystick input (game/character control), styled via `Themes/VirtualJoystick.axaml`. `Components/VirtualJoystick/virtual-joystick.md` has the full design spec for this control (pointer handling, direction/force math, `Fixed`/`Semi`/`Dynamic` modes) — read it before modifying joystick behavior.
- Package versions are centrally managed in `Directory.Packages.props` (`ManagePackageVersionsCentrally`); add new package versions there, not in individual `.csproj` files. Keep all `Avalonia.*` package versions in sync.

Key conventions called out in `AI_AGENT_README.md`:
- Compiled bindings are enabled by default (`AvaloniaUseCompiledBindingsByDefault=true`), so XAML bindings require `x:DataType` for strict typing.
- Custom control styling lives in `Themes/*.axaml` (`ControlTheme`) and must be registered in `App.axaml` to take effect.
