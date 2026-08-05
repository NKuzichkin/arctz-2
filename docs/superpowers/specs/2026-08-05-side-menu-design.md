# Боковое меню + перенос кнопки лога G-code

Дата: 2026-08-05

## Проблема

Кнопка «Лог G-code» сейчас живёт в свайп-ряду действий шапки (`HeaderActions`), рядом с `Пуск`/`Пауза`/`Стоп` — хотя по смыслу это не команда воспроизведения программы, а переключатель вспомогательной панели. Кроме того, в приложении нет места для будущих пунктов навигации (настройки, о программе и т.п.), кроме растущего свайп-ряда шапки.

## Решение

### 1. Боковое меню (drawer)

Новый оверлей в `RootPanel` (`MainView.axaml`), тем же паттерном, что и существующая модалка «Библиотека» (`MainView.axaml:272-292`): `Border` со `Background="{StaticResource HudScrimBrush}"` на весь `RootPanel`, внутри — панель, прижатая к левому краю на всю высоту:

```xml
<Border IsVisible="{Binding IsSideMenuOpen}" Background="{StaticResource HudScrimBrush}">
    <Border Width="240" Background="{StaticResource HudPanelElevatedBrush}"
            BorderBrush="{StaticResource HudBorderStrongBrush}" BorderThickness="0,0,1,0"
            Padding="20" HorizontalAlignment="Left" VerticalAlignment="Stretch">
        <DockPanel>
            <Grid DockPanel.Dock="Top" ColumnDefinitions="*,Auto" Margin="0,0,0,14">
                <TextBlock Grid.Column="0" Classes="section-heading" Text="МЕНЮ" VerticalAlignment="Center" />
                <Button Grid.Column="1" Content="✕" Padding="8,2" Command="{Binding CloseSideMenuCommand}" />
            </Grid>
            <StackPanel Spacing="8">
                <Button Classes="header-action" HorizontalAlignment="Stretch" HorizontalContentAlignment="Left"
                        Content="Лог G-code" Command="{Binding OpenGCodeLogCommand}" />
            </StackPanel>
        </DockPanel>
    </Border>
</Border>
```

Закрытие только по явным действиям (✕ или выбор пункта меню) — по прецеденту всех текущих модалок в `MainView.axaml` (Библиотека, редактор точки, подтверждение, переименование): ни у одной нет закрытия по клику на скрим, только явные кнопки. Анимация выезда (slide-in) не добавляется — в проекте сейчас есть только fade-in при первом появлении (`Border.reveal-1/2/3`), новый паттерн не вводим.

### 2. Вызов меню — гамбургер в шапке

Новая первая колонка в `HeaderStatusRow` (`MainView.axaml:93`, `Grid.ColumnDefinitions="*,Auto,Auto,Auto,Auto"` → `"Auto,*,Auto,Auto,Auto,Auto"`, остальные колонки сдвигаются на одну позицию):

```xml
<Button Grid.Column="0" Classes="icon-action" Content="☰" Command="{Binding ToggleSideMenuCommand}" />
```

`Button.icon-action` — уже существующий класс (`Themes/HudControls.axaml`), используется кнопкой отключения `⏻`; переиспользуется без изменений.

### 3. Кнопка «Лог G-code» уходит из свайп-ряда

`MainView.axaml:107` (`<Button Classes="header-action" Content="Лог G-code" .../>`) удаляется из `HeaderActions`. Комментарий над `Button.header-action` в `Themes/HudControls.axaml:139-141` («Header action row (Пуск/Пауза/Стоп/Лог G-code)») обновляется — убирается упоминание «Лог G-code».

Сама панель лога (`MainView.axaml:294-315`, привязана к `Connection.IsGCodeLogOpen`, якорь top-right) не переносится и не меняется — меняется только источник переключения.

### 4. ViewModel (`ProgramViewModel`)

```csharp
[ObservableProperty]
private bool _isSideMenuOpen;

[RelayCommand]
private void ToggleSideMenu() => IsSideMenuOpen = !IsSideMenuOpen;

[RelayCommand]
private void CloseSideMenu() => IsSideMenuOpen = false;

[RelayCommand]
private void OpenGCodeLog()
{
    Connection.IsGCodeLogOpen = true;
    IsSideMenuOpen = false;
}
```

Паттерн симметричен уже существующим `OpenLibraryAsync`/`CloseLibrary` (`ProgramViewModel.cs:76-87`). `OpenGCodeLogCommand` обращается к `Connection` напрямую — по прецеденту `MainView.axaml`, где `Connection.ToggleGCodeLogCommand` уже биндится в обход `ConnectionView.axaml`.

Будущие пункты меню добавляются тем же способом: команда выполняет действие и выставляет `IsSideMenuOpen = false`.

## Затронутые файлы

- `ArctZ/Views/MainView.axaml` — гамбургер в `HeaderStatusRow`, удаление кнопки «Лог G-code» из `HeaderActions`, новый оверлей бокового меню в `RootPanel`.
- `ArctZ/ViewModels/ProgramViewModel.cs` — `IsSideMenuOpen`, `ToggleSideMenuCommand`, `CloseSideMenuCommand`, `OpenGCodeLogCommand`.
- `ArctZ/Themes/HudControls.axaml` — обновление комментария над `Button.header-action` (без изменений стиля).
- `ArctZ.Tests/ViewModels/ProgramViewModelTests.cs` — новый файл (если ещё не существует) с тестами на три новые команды, по образцу `ToggleGCodeLogCommand_TogglesIsGCodeLogOpen` (`ConnectionViewModelTests.cs:245-256`).

## Не в скоупе

- Закрытие меню по клику на скрим — не соответствует текущим паттернам модалок в проекте.
- Анимация выезда панели (slide) — не вводится, только `IsVisible`-переключение, как у остальных оверлеев.
- Содержимое и позиционирование самой панели лога G-code (`MainView.axaml:294-315`) — не меняются.
- Дополнительные пункты меню (настройки, о программе и т.п.) — структура рассчитана на их добавление позже, но сами пункты не создаются сейчас.
- `ComputeJoystickRadius` — гамбургер добавляется внутрь `HeaderStatusRow`, которая не входит в формулу радиуса джойстика (зависит от `ContentGrid`/`RootPanel`, не от шапки); высота шапки не меняется, так как `icon-action` уже используется в той же строке.

## Тестирование

- `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj` — компиляция XAML после правки разметки.
- `dotnet test ArctZ.Tests/ArctZ.Tests.csproj` — новые тесты на `ToggleSideMenuCommand`/`CloseSideMenuCommand`/`OpenGCodeLogCommand`; существующие тесты (включая `MainViewJoystickRadiusTests`) не должны падать — оверлей и гамбургер не участвуют в формуле радиуса.
- Через skill `run` / `mobile-build-setup`: открыть меню гамбургером, убедиться что джойстики/панель программы перекрыты затемнением, выбрать «Лог G-code» — меню закрывается, панель лога открывается справа сверху; закрыть меню через ✕.
