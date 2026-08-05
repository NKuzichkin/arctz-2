# Редизайн шапки: единая панель статуса + модалка аварии

Дата: 2026-08-05

**Опирается на:** `docs/superpowers/specs/2026-08-04-header-mobile-ux-design.md`, который привёл шапку к структуре «Зона 1 — статус (не переносится)» / «Зона 2 — действия (горизонтальный swipe)» и перенёс `Homing`/`Сброс аварии`/`Отключить` из `ConnectionView.axaml` в единую зону действий `MainView.axaml`. Этот документ меняет состав обеих зон и добавляет третий оверлей (модалка аварии) к уже существующей модалке подключения.

## Проблема

1. Зона статуса — два визуально разных блока рядом (`ContentControl(ConnectionStatus)` + отдельная рамка с `PlaybackStateLabel`), а внутри `ConnectionView.axaml` — ещё два вложенных блока со своим фоном/рамкой (`HeaderedContainer` для индикатора связи, `Border` для машинного состояния/позиции). Итого 3 разных «карточки» там, где по смыслу один статус-ряд.
2. `Homing` в зоне действий не используется в текущем рабочем процессе и занимает место в свайп-ряду.
3. `Сброс аварии` — обычная кнопка в общем ряду действий, ничем не выделена среди `Пуск`/`Пауза`/`Стоп`, хотя авария — блокирующее состояние станка, требующее внимания оператора раньше, чем что-либо ещё.
4. `Отключить` — единственная кнопка в зоне действий, не связанная с воспроизведением программы; годный кандидат на компактную иконку в статусной зоне вместо текстовой кнопки в свайп-ряду.

## Решение

### 1. Единая панель статуса

Текущий `HeaderStatusRow` (Grid с двумя колонками: `ContentControl(ConnectionStatus)` и рамка `PlaybackStateLabel`) заменяется одним `Border` с горизонтальным `StackPanel` внутри:

```
[● цвет]  Idle   X 0.00 Y 0.00 Z 0.00 A 0.00   │   Ожидание   │   [⏻]
```

Слева направо: индикатор связи → машинное состояние/позиция → метка воспроизведения → иконка отключения. Между группами — `Border.header-divider` (уже существующий класс, переиспользуется здесь так же, как в зоне действий).

`ConnectionView.axaml` меняется:
- `HeaderedContainer`, оборачивающий индикатор связи, убирается; `Ellipse` индикатора остаётся, но без фонового блока.
- Текстовая подпись `ConnectionStateLabel` («Подключено»/«Не подключено»/…) убирается из ряда — остаётся только цвет `Ellipse` (через существующий `ConnectionStateToBrushConverter`). Причина: пока станок не подключён, весь экран и так перекрыт модалкой подключения с полным текстом состояния (`MainView.axaml:319-340`), так что дублирующий текст в шапке избыточен; в подключённом состоянии он тоже избыточен рядом с зелёной точкой.
- Свойство `ConnectionStateLabel` в `ConnectionViewModel.cs` не удаляется (используется модалкой подключения), меняется только разметка `ConnectionView.axaml`.
- `Border`, оборачивающий машинное состояние/позицию, теряет `Background`/`BorderBrush`/`BorderThickness` — остаётся `StackPanel` с теми же двумя `TextBlock`.
- Баннер ошибки (`HasError`/`ErrorMessage`, т.е. `LastError` — некритичные ошибки соединения, не авария) остаётся как есть: отдельная строка под основным рядом статуса, со своим фоном/рамкой (`HudWarningDimBrush`/`HudWarningBrush`) — это осознанный акцент, не убирается.

Иконка отключения — новая `Button` в `MainView.axaml` (не в `ConnectionView.axaml`, по прецеденту `ToggleGCodeLogCommand`, который тоже обращается к `ConnectionViewModel` в обход `ConnectionView`): `Content="⏻"`, `Command="{Binding Connection.DisconnectCommand}"`, новый style-класс `Button.icon-action` (см. ниже).

### 2. Модалка аварии

Новый оверлей в `MainView.axaml`, третий по счёту `Border`-scrim в корневом `Grid` (после `RootPanel`, рядом с существующей модалкой подключения `MainView.axaml:319-340`), тем же паттерном: `Border` c `Background="{StaticResource HudScrimBrush}"`, внутри центрированная карточка `HudPanelElevatedBrush`.

**Видимость:** новое вычисляемое свойство `ConnectionViewModel.IsAlarmModalVisible => LastAlarmCode is not null` — переиспользует уже существующее поле `LastAlarmCode`, которое `AlarmTriggered` выставляет и `ResetAlarmAsync`/смена `Session` сбрасывают в `null` (`ConnectionViewModel.cs:126-129,142,289`). Реализация симметрична `IsConnectionModalVisible`: добавляется в существующий блок `WhenAnyValue(...).Subscribe(_ => RaisePropertyChanged(...))` (`ConnectionViewModel.cs:185-196`), не требует новой подписки.

Модалка закрывается сама, как только `LastAlarmCode` становится `null` — то есть сразу после успешного `ResetAlarmAsync` (`IsAlarmModalVisible` пересчитывается через тот же `RaisePropertyChanged`-блок, отдельного кода на закрытие не нужно).

**Содержимое карточки:**
- `TextBlock Classes="section-heading" Text="АВАРИЯ"`
- `TextBlock Text="{Binding Connection.ErrorMessage}"` (уже форматирует `"Авария FluidNC: код {code}"`)
- `Button Classes="danger primary" Content="Сброс аварии" Command="{Binding Connection.ResetAlarmCommand}"`

Модалка блокирует основной экран так же, как модалка подключения: `DockPanel` с основным контентом получает `IsEnabled="{Binding !Connection.IsAlarmModalVisible}"` в дополнение к уже существующему `IsEnabled="{Binding !Connection.IsConnectionModalVisible}"` (оба условия объединяются в одно выражение или в multi-value convert — см. ниже техническую заметку).

**Техническая заметка про совмещение двух `IsEnabled`-условий:** Avalonia-биндинг не даёt напрямую написать `!(A || B)` в XAML без конвертера. Добавляется третье вычисляемое свойство в `ConnectionViewModel`: `IsAnyModalVisible => IsConnectionModalVisible || IsAlarmModalVisible`, тоже пересчитываемое в существующем `RaisePropertyChanged`-блоке. `DockPanel.IsEnabled="{Binding !Connection.IsAnyModalVisible}"`.

### 3. Зона действий — сокращённый состав

`Homing` и `Сброс аварии` убираются из свайп-ряда (`Сброс аварии` переехал в модалку, `Homing` убирается полностью — см. «Не в скоупе» про сам `HomeCommand`/`IDeviceSession.HomeAsync`). `Отключить` переезжает в панель статуса как иконка (см. п.1). Итоговый ряд:

```
[Пуск] [Пауза] [Стоп]  │  [Лог G-code]
```

Один `Border.header-divider` между группой воспроизведения и «Лог G-code» — как сейчас.

## Изменения в `Themes/HudControls.axaml`

Новый style-класс для иконки отключения:

```xml
<Style Selector="Button.icon-action">
  <Setter Property="MinWidth" Value="44" />
  <Setter Property="MinHeight" Value="44" />
  <Setter Property="Padding" Value="10" />
  <Setter Property="FontSize" Value="18" />
</Style>
```

44×44 — тот же минимальный touch-таргет, что у `Button.header-action` (обоснование в `2026-08-04-header-mobile-ux-design.md`, раздел «Touch-таргеты»).

## Затронутые файлы

- `ArctZ/Views/MainView.axaml` — единая панель статуса вместо `HeaderStatusRow`-грида, иконка отключения, новая модалка аварии, сокращённая зона действий, `IsEnabled` основного `DockPanel` учитывает оба модальных состояния.
- `ArctZ/Views/ConnectionView.axaml` — убираются фон/рамки индикатора связи и блока машинного состояния, убирается текстовая подпись состояния подключения; баннер ошибки не меняется.
- `ArctZ/ViewModels/ConnectionViewModel.cs` — новые вычисляемые свойства `IsAlarmModalVisible`, `IsAnyModalVisible` (без новых подписок, только добавление в существующий `RaisePropertyChanged`-блок); удаляется `HomeCommand` (свойство, `Enhance(...)`-регистрация, приватный метод `HomeAsync`-обёртка).
- `ArctZ/Themes/HudControls.axaml` — новый класс `Button.icon-action`.

## Не в скоупе

- `IDeviceSession.HomeAsync` / `DeviceSession.HomeAsync` (отправка `$H`) и связанный тест в `DeviceSessionTests.cs` — это возможность уровня сессии устройства, симметричная `ResetAlarmAsync`/`$X`, не привязанная к UI-кнопке. Убирается только `ConnectionViewModel.HomeCommand` (обёртка для кнопки) и сама кнопка; сессионный API остаётся для возможного будущего использования (консоль G-code, автосценарии).
- Логика самих команд (`ResetAlarmCommand`, `DisconnectCommand` и т.д.) — не меняется, только привязка к новым элементам UI.
- Средняя часть (панель программы) и нижняя часть (джойстики) `ContentGrid` — не трогаются.
- `ComputeJoystickRadius` — формула не меняется; новая единая панель статуса фиксированной высоты (не переносится), так что `HeaderBorder.Bounds.Height` остаётся стабильным по той же логике, что в `2026-08-04-header-mobile-ux-design.md`.

## Тестирование

View-тестов в решении нет. Проверка:
- `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj` — компиляция XAML/code-behind после переноса разметки.
- `dotnet test ArctZ.Tests/ArctZ.Tests.csproj` — `DeviceSessionTests.cs` не должен затрагиваться (никаких изменений в `DeviceSession`).
- Через skill `run` / `mobile-build-setup`: спровоцировать `AlarmTriggered` (в демо-режиме или реальном устройстве) — проверить, что модалка аварии перекрывает джойстики/панель программы, `Сброс аварии` в модалке работает и модалка закрывается сама; проверить, что иконка `⏻` отключает сессию; проверить, что при некритичной ошибке соединения (`LastError`) модалка НЕ появляется, только баннер под статус-панелью.
- Ручная проверка touch-таргета иконки отключения — не менее 44×44px.
