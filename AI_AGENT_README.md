# Обзор проекта ArctZ

Этот файл предназначен для ИИ-агентов и содержит базовый контекст проекта, стек технологий, архитектуру и состав папок.

## 🛠 Стек технологий и библиотеки
*   **Платформа:** .NET 10.0
*   **Язык программирования:** C#
*   **Пользовательский интерфейс (UI):** Avalonia UI (кроссплатформенный фреймворк)
*   **Архитектурный паттерн:** MVVM (Model-View-ViewModel)
*   **Библиотеки:**
    *   `Avalonia` — базовый пакет фреймворка.
    *   `Avalonia.Themes.Fluent` — тема оформления Fluent Design.
    *   `Avalonia.Fonts.Inter` — шрифт по умолчанию (Inter).
    *   `CommunityToolkit.Mvvm` — современный набор инструментов для реализации паттерна MVVM (генерация кода, ObservableObject, команды).
    *   `AvaloniaUI.DiagnosticsSupport` — поддержка средств диагностики (F12) в режиме отладки.
    *   `Microsoft.Extensions.DependencyInjection` — DI-контейнер, регистрация в `Services/Device/ServiceCollectionExtensions.cs` (`AddArctZCore`).
    *   `System.IO.Ports` — доступ к serial-портам на платформах, где это возможно (см. платформенные точки входа).

## 📦 Структура проекта
Проект разделен на несколько подпроектов (типичная структура Avalonia Cross-Platform Solution):
*   **`ArctZ/`** — главный кроссплатформенный проект (Core). Здесь находится вся логика, View, ViewModels, сервисы устройства/программ и кастомные контролы.
*   **`ArctZ.Desktop/`, `ArctZ.Android/`, `ArctZ.iOS/`, `ArctZ.Browser/`** — тонкие точки входа (bootstrap + платформенный манифест/конфиг), без прикладной логики.
*   **`ArctZ.Tests/`** — модульные тесты (xUnit) для `Services/Device`, `Services/Program` и части `ViewModels`. Запуск: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`.
*   **`docs/`** — проектная документация (механика, прошивка, протокол, архитектура ПО) — см. `docs/README.md`.

## 📂 Состав папок и компонентов (проект ArctZ)
*   **`Assets/`** — статические ресурсы (шрифты, изображения, иконки).
*   **`Components/`** — пользовательские элементы управления (Custom Controls).
    *   `VirtualJoystick/` — кастомный компонент виртуального джойстика для сенсорного ввода. Полный дизайн-спек — `Components/VirtualJoystick/virtual-joystick.md`.
*   **`Converters/`** — конвертеры для XAML-биндингов (`ConnectionStateConverters.cs` — состояние соединения → текст/цвет).
*   **`Themes/`** — стили и ресурсы Avalonia (Styles / ResourceDictionaries): `Colors.axaml`, `HudControls.axaml`, `VirtualJoystick.axaml`.
*   **`Services/`** — вся логика, не относящаяся к UI:
    *   `Services/Device/` — связь с контроллером FluidNC: `IDeviceTransport`/`IDeviceSession`/`DeviceSession` (жизненный цикл соединения), `FluidNcCommandSerializer`/`FluidNcStatusParser` (диалект G-code и статус-ответы), `JogCommandFactory`/`JogScheduler` (джойстик → `$J=`), `BufferAwareCommandQueue` (очередь с учётом буфера контроллера), `FixedDelayReconnectPolicy` (переподключение), `Simulation/MockDeviceTransport` (демо-режим без реального устройства). См. `docs/protocol/bluetooth-gcode-control.md`.
    *   `Services/Program/` — программы-траектории: `JibProgram`/`KeyPoint`/`TrajectoryCompiler` (компиляция ключевых точек в сегменты движения), `JsonFileProgramStorage` (сохранение/загрузка программ).
*   **`ViewModels/`** — слой бизнес-логики и состояния UI.
    *   `ViewModelBase.cs` — базовый класс, наследуется от `ObservableObject` из MVVM Toolkit.
    *   `ProgramViewModel.cs` — вью-модель главного экрана: режимы «Программирование»/«Выполнение», библиотека программ, авторинг ключевых точек, воспроизведение траектории.
    *   `ConnectionViewModel.cs` — вью-модель подключения к устройству (реальное/демо), зеркалит `IDeviceSession.ConnectionState`, управляет модальным окном подключения.
    *   `KeyPointEditorViewModel.cs`, `JoystickInputMapper.cs` — вспомогательные вью-модели/мапперы для авторинга точек и перевода событий джойстика в оси устройства.
*   **`Views/`** — слой представления (UI разметка).
    *   `MainWindow.axaml` (.cs) — главное окно приложения (для Desktop).
    *   `MainView.axaml` (.cs) — главное представление (DataContext — `ProgramViewModel`), отображается как в `MainWindow`, так и в мобильных/браузерных точках входа. Содержит библиотеку программ, панели авторинга/воспроизведения и модальные оверлеи (редактор точки, подтверждение, подключение).
    *   `ConnectionView.axaml` (.cs) — панель статуса подключения, встраивается в `MainView`.

## 💡 Особенности и рекомендации для ИИ
*   Используются **Compiled Bindings** (`<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>`), что требует строгой типизации привязок данных (`x:DataType` в разметке).
*   При создании свойств во ViewModels следует использовать возможности кодогенерации `CommunityToolkit.Mvvm` (атрибуты `[ObservableProperty]`, `[RelayCommand]`).
*   Стилизация кастомных компонентов происходит через XAML-файлы в папке `Themes/` (с использованием `ControlTheme`), которые затем должны подключаться в `App.axaml`.
*   Не вызывайте `Dispatcher.UIThread` напрямую во ViewModels. Мигрированные на `Zafiro.Avalonia`/ReactiveUI вью-модели (например, `ConnectionViewModel`) маршалят на UI-поток через `RxSchedulers.MainThreadScheduler`/`.ObserveOn(...)` — `ArctZ.Tests/ReactiveUIBootstrap.cs` глобально подменяет его на `ImmediateScheduler.Instance` для всего тестового процесса, иначе тесты теряют детерминированность. (Старый seam `IUiDispatcher`/`AvaloniaUiDispatcher` использовался только `ConnectionViewModel` до этой миграции и был удалён вместе с последним вызывающим кодом.)
*   Перед изменением поведения `Services/Device/*` смотрите `docs/protocol/bluetooth-gcode-control.md` и соответствующие тесты в `ArctZ.Tests/Services/Device/` — там зафиксированы нюансы протокола GRBL/FluidNC (real-time команды, character-counting и т.п.).
