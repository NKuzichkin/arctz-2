# Перенос кнопки «Захватить» из меню программы на панель программы — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Заменить пункт меню «Захватить» (`⋮` → «Захватить») отдельной квадратной кнопкой-иконкой без подписи слева от заголовка «ПРОГРАММА» в панели программы.

**Architecture:** Чисто разметочное изменение в `ArctZ/Views/MainView.axaml` — существующий `CaptureKeyPointCommand` перевязывается с `MenuItem` на новый `Button`. Первое использование `Material.Icons.Avalonia` в проекте требует регистрации стилей контрола в `App.axaml`.

**Tech Stack:** Avalonia UI (.NET 10), XAML, `Material.Icons.Avalonia` (пакет уже подключён в `ArctZ/ArctZ.csproj`, версия закреплена в `Directory.Packages.props`).

## Global Constraints

- Кнопка — квадратная 44×44 (стиль `Button.icon-action`, `ArctZ/Themes/HudControls.axaml:160`), без текстовой подписи, иконка `MaterialIconKind.Target`.
- Расположение: слева от `TextBlock Classes="section-heading" Text="ПРОГРАММА"` (`ArctZ/Views/MainView.axaml:143`).
- Пункт `MenuItem Header="Захватить"` и предшествующий ему `Separator` удаляются из `MenuFlyout` (`ArctZ/Views/MainView.axaml:149-157`) — команда перемещается, не дублируется.
- `ProgramViewModel.CaptureKeyPointCommand` не меняется.
- Регистрация иконок в `App.axaml` — по правилам из `CLAUDE.md` (`xmlns:materialIcons="using:Material.Icons.Avalonia"` + `<materialIcons:MaterialIconStyles />` в `Application.Styles`), актуальный тег `MaterialIconStyles`, не устаревший `StyleInclude`.
- Проверка UI — **только** через протокол из `CLAUDE.md` («Тестирование UI»): собрать → реально запустить → пользователь проверяет вживую → вопросы через `AskUserQuestion`, по одному на каждое изменённое поведение. В этом проекте для целых composed-view (`MainView`) headless Avalonia-тесты сознательно не пишутся (см. комментарий в `ArctZ.Tests/TestApp.cs:10-17`) — это чисто разметочное изменение без новой логики, поэтому отдельного unit/headless-теста в этом плане нет; шаг ручной проверки — обязательная часть выполнения плана, не опциональная.

---

### Task 1: Зарегистрировать стили Material.Icons.Avalonia в App.axaml

**Files:**
- Modify: `ArctZ/App.axaml:1` (корневой тег `Application`), `ArctZ/App.axaml:63-66` (`Application.Styles`)

**Interfaces:**
- Produces: доступность `<materialIcons:MaterialIconStyles />` и корректный `ControlTheme` для `Material.Icons.Avalonia.MaterialIcon` во всех последующих XAML-файлах проекта, которые объявят `xmlns:materialIcons="using:Material.Icons.Avalonia"`.

- [ ] **Step 1: Добавить xmlns и стили в App.axaml**

В `ArctZ/App.axaml` добавить пространство имён в корневой тег `Application` (после `xmlns:local="using:ArctZ"`):

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="using:ArctZ"
             xmlns:materialIcons="using:Material.Icons.Avalonia"
             x:Class="ArctZ.App"
             RequestedThemeVariant="Dark">
```

И добавить `MaterialIconStyles` в `Application.Styles`, после `VirtualJoystick.axaml`:

```xml
    <Application.Styles>
        <FluentTheme />
        <StyleInclude Source="avares://ArctZ/Themes/HudControls.axaml" />
        <StyleInclude Source="avares://ArctZ/Themes/VirtualJoystick.axaml" />
        <materialIcons:MaterialIconStyles />
```

(остальное содержимое `Application.Styles` — `Style Selector="Window"` — остаётся без изменений после этой строки).

- [ ] **Step 2: Собрать core-проект**

Run: `dotnet build ArctZ/ArctZ.csproj`
Expected: сборка проходит без ошибок (никакой другой XAML пока не ссылается на `materialIcons`, так что эта проверка только подтверждает, что регистрация стилей сама по себе не ломает сборку).

- [ ] **Step 3: Commit**

```bash
git add ArctZ/App.axaml
git commit -m "feat: register Material.Icons.Avalonia styles in App.axaml"
```

---

### Task 2: Перенести кнопку «Захватить» из меню в панель программы

**Files:**
- Modify: `ArctZ/Views/MainView.axaml:1-11` (добавить `xmlns:materialIcons`), `ArctZ/Views/MainView.axaml:143` (заголовок «ПРОГРАММА»), `ArctZ/Views/MainView.axaml:149-157` (`MenuFlyout`)

**Interfaces:**
- Consumes: `materialIcons:MaterialIconStyles`, зарегистрированные в Task 1; `Button.icon-action` из `ArctZ/Themes/HudControls.axaml:160` (`MinWidth`/`MinHeight`=44, `Padding`=10, `FontSize`=18); существующий `{Binding CaptureKeyPointCommand}` из `ProgramViewModel`.
- Produces: ничего для последующих задач — это последняя задача плана.

- [ ] **Step 1: Добавить xmlns:materialIcons в MainView.axaml**

В `ArctZ/Views/MainView.axaml` добавить в корневой тег `UserControl` (после `xmlns:conv="using:ArctZ.Converters"`):

```xml
             xmlns:conv="using:ArctZ.Converters"
             xmlns:materialIcons="using:Material.Icons.Avalonia"
             mc:Ignorable="d" d:DesignWidth="1000" d:DesignHeight="600"
```

- [ ] **Step 2: Заменить заголовок «ПРОГРАММА» на ряд [кнопка][заголовок]**

Заменить (строка 143):

```xml
                                <TextBlock Classes="section-heading" Text="ПРОГРАММА" />
```

на:

```xml
                                <Grid ColumnDefinitions="Auto,*" VerticalAlignment="Center">
                                    <Button Grid.Column="0" Classes="icon-action" Command="{Binding CaptureKeyPointCommand}"
                                            ToolTip.Tip="Захватить" Margin="0,0,8,0">
                                        <materialIcons:MaterialIcon Kind="Target" />
                                    </Button>
                                    <TextBlock Grid.Column="1" Classes="section-heading" Text="ПРОГРАММА" VerticalAlignment="Center" />
                                </Grid>
```

- [ ] **Step 3: Удалить пункт «Захватить» и разделитель из меню «⋮»**

Заменить (строки 149-157):

```xml
                                        <MenuFlyout>
                                            <MenuItem Header="Переименовать" Command="{Binding RenameProgramCommand}" />
                                            <MenuItem Header="Новая" Command="{Binding NewProgramCommand}" />
                                            <MenuItem Header="Сохранить" Command="{Binding SaveProgramCommand}" />
                                            <MenuItem Header="Настройки завершения" Command="{Binding EditCompletionSettingsCommand}" />
                                            <MenuItem Header="Библиотека" Command="{Binding OpenLibraryCommand}" />
                                            <Separator />
                                            <MenuItem Header="Захватить" Command="{Binding CaptureKeyPointCommand}" />
                                        </MenuFlyout>
```

на:

```xml
                                        <MenuFlyout>
                                            <MenuItem Header="Переименовать" Command="{Binding RenameProgramCommand}" />
                                            <MenuItem Header="Новая" Command="{Binding NewProgramCommand}" />
                                            <MenuItem Header="Сохранить" Command="{Binding SaveProgramCommand}" />
                                            <MenuItem Header="Настройки завершения" Command="{Binding EditCompletionSettingsCommand}" />
                                            <MenuItem Header="Библиотека" Command="{Binding OpenLibraryCommand}" />
                                        </MenuFlyout>
```

- [ ] **Step 4: Собрать core-проект**

Run: `dotnet build ArctZ/ArctZ.csproj`
Expected: сборка проходит без ошибок (XAML компилируется, `x:DataType="vm:ProgramViewModel"` уже покрывает `CaptureKeyPointCommand` — это тот же биндинг, что был у `MenuItem`).

- [ ] **Step 5: Прогнать существующий тестовый набор**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: все тесты проходят как раньше — `CaptureKeyPointCommand` не переименовывался и не менял сигнатуру, тесты в `ArctZ.Tests/ViewModels/*` обращаются к нему напрямую через ViewModel, а не через XAML.

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Views/MainView.axaml
git commit -m "feat: move Захватить button from program menu to program panel"
```

---

### Task 3: Ручная UI-проверка (обязательный протокол CLAUDE.md)

**Files:** нет изменений кода — только проверка.

**Interfaces:**
- Consumes: результат Task 1 и Task 2.

- [ ] **Step 1: Собрать Desktop head**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: сборка без ошибок.

- [ ] **Step 2: Запустить приложение**

Run: `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj`
Приложение должно реально запуститься (не просто собраться) и остаться открытым для проверки пользователем.

- [ ] **Step 3: Попросить пользователя проверить функции**

Попросить пользователя вживую: открыть/загрузить программу (или создать новую), убедиться, что панель «ПРОГРАММА» видна, нажать новую кнопку слева от заголовка, при необходимости открыть меню «⋮».

- [ ] **Step 4: Задать вопросы через AskUserQuestion — по одному на каждое изменённое поведение**

Отдельные вопросы (не один общий «выглядит нормально?»):
1. Кнопка отображается слева от заголовка «ПРОГРАММА», квадратная, без подписи, с иконкой-мишенью?
2. Нажатие на кнопку выполняет захват точки (появляется новая точка в списке «ТОЧКИ»)?
3. В меню «⋮» пункта «Захватить» больше нет, остальные пункты (Переименовать/Новая/Сохранить/Настройки завершения/Библиотека) на месте?
4. Если программа заблокирована (`IsProgramLocked` — например, во время воспроизведения) — кнопка визуально неактивна вместе с остальной панелью редактирования?

Дождаться ответов пользователя на каждый вопрос. Если что-то не так — вернуться к соответствующему шагу Task 2 и исправить, затем повторить проверку.

- [ ] **Step 5: Закрыть приложение**

Закрыть запущенный процесс после подтверждения пользователем.
