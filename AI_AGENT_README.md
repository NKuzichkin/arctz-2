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

## 📦 Структура проекта
Проект разделен на несколько подпроектов (типичная структура Avalonia Cross-Platform Solution):
*   **`ArctZ/`** — главный кроссплатформенный проект (Core). Здесь находится вся логика, View, ViewModels и кастомные контролы.
*   **`ArctZ.Desktop/`** — точка входа для Desktop-платформ (Windows, macOS, Linux).
*   **`ArctZ.Android/`** — точка входа для Android-приложения.
*   **`ArctZ.iOS/`** — точка входа для iOS-приложения.
*   **`ArctZ.Browser/`** — точка входа для WebAssembly (запуск в браузере).

## 📂 Состав папок и компонентов (проект ArctZ)
*   **`Assets/`** — статические ресурсы (шрифты, изображения, иконки).
*   **`Components/`** — пользовательские элементы управления (Custom Controls).
    *   `VirtualJoystick/` — кастомный компонент виртуального джойстика для сенсорного ввода.
        *   `VirtualJoystick.cs` — логика работы контрола.
        *   `RadiusToSizeConverter.cs` — конвертер значений для привязки радиуса к размерам в UI.
*   **`Themes/`** — стили и ресурсы Avalonia (Styles / ResourceDictionaries).
    *   `VirtualJoystick.axaml` — стили для компонента VirtualJoystick.
*   **`ViewModels/`** — слой бизнес-логики и состояния UI.
    *   `ViewModelBase.cs` — базовый класс, наследуется от `ObservableObject` из MVVM Toolkit.
    *   `MainViewModel.cs` — вью-модель для главного экрана.
*   **`Views/`** — слой представления (UI разметка).
    *   `MainWindow.axaml` (.cs) — главное окно приложения (для Desktop).
    *   `MainView.axaml` (.cs) — главное представление, которое отображается как в `MainWindow`, так и в мобильных/браузерных точках входа.

## 💡 Особенности и рекомендации для ИИ
*   Используются **Compiled Bindings** (`<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>`), что требует строгой типизации привязок данных (`x:DataType` в разметке).
*   При создании свойств во ViewModels следует использовать возможности кодогенерации `CommunityToolkit.Mvvm` (атрибуты `[ObservableProperty]`, `[RelayCommand]`).
*   Стилизация кастомных компонентов происходит через XAML-файлы в папке `Themes/` (с использованием `ControlTheme`), которые затем должны подключаться в `App.axaml`.
