# Настройки мока для демо-режима — дизайн

Дата: 2026-08-06

## Проблема

Демонстрация приложения без реального устройства (эндпоинт «Демо», `MockDeviceTransport`) сейчас не даёт способа показать, как приложение ведёт себя при ошибке или аварии контроллера, и не позволяет замедлить отклик мока, чтобы наглядно показать буферизацию команд. `MockDeviceTransport.ForceNextCommandError(code)` уже существует, но не вызывается ниоткуда в UI; сигнала аварии (`ALARM:`) в моке нет вообще; задержка ответа не настраивается.

## Что добавляется

Пункт «Настройки мока» в боковой панели «МЕНЮ» (кнопка ☰), видимый всегда независимо от текущего подключения. Открывает модальный диалог с:
- кнопкой «Смоделировать ошибку» — следующая отправленная команда получит `error:9` вместо `ok`;
- кнопкой «Смоделировать аварию» — мгновенно переводит мок в состояние `Alarm` с кодом `1`;
- слайдером «Задержка ответа» (0–5000 мс, шаг 250 мс) — дополнительная задержка перед `ok`/`error` на каждую отправленную строковую команду.

Диалог доступен всегда (пункт меню не скрывается и не блокируется вне демо-подключения), но кнопки и слайдер реально влияют на поведение только когда есть живой демо-транспорт — иначе это тихий no-op, значение задержки при этом запоминается и применяется к следующему демо-подключению.

## `IMockDeviceControl` — новый интерфейс

`ConnectionViewModel` не должен знать о конкретном классе `MockDeviceTransport` (из тестового `FakeDeviceTransport`, который сегодня подставляется в `ConnectionViewModelTests` по умолчанию, к нему всё равно не привести кастом) — вместо этого выделяется узкий интерфейс, который реализует только `MockDeviceTransport`:

`ArctZ/Services/Device/IMockDeviceControl.cs` (namespace `ArctZ.Services.Device`, рядом с `IDeviceTransport`):

```csharp
/// <summary>Demo-only knobs for MockDeviceTransport, surfaced to the UI via ConnectionViewModel.</summary>
public interface IMockDeviceControl
{
    void ForceNextCommandError(int code);
    void TriggerAlarm(int code);
    void SetResponseDelay(TimeSpan delay);
}
```

`MockDeviceTransport` объявляется как `public sealed class MockDeviceTransport : IDeviceTransport, IMockDeviceControl` — существующий `ForceNextCommandError` уже соответствует сигнатуре, ничего в нём менять не нужно.

## `MockDeviceTransport` — новые методы

```csharp
public void TriggerAlarm(int code)
{
    lock (_lock)
    {
        _alarm = true;
        _targetPose = null; // авария останавливает движение, как в реальном FluidNC
    }

    LineReceived?.Invoke($"ALARM:{code}");
}

public void SetResponseDelay(TimeSpan delay)
{
    lock (_lock)
    {
        _responseDelayTicks = (int)Math.Round(delay.TotalMilliseconds / _tickInterval.TotalMilliseconds);
    }
}
```

Новые приватные поля: `_responseDelayTicks` (по умолчанию `0`, сохраняет текущее поведение без изменений), `_ticksUntilNextProcess` (счётчик текущей задержки).

`ProcessOnePendingLine` (вызывается только из `OnTick`, уже под `_lock`) меняется так, чтобы перед обработкой головы очереди сначала выждать `_responseDelayTicks` тиков:

```csharp
private string? ProcessOnePendingLine()
{
    if (_pendingLines.Count == 0)
    {
        return null;
    }

    if (_ticksUntilNextProcess > 0)
    {
        _ticksUntilNextProcess--;
        return null;
    }

    var line = _pendingLines.Dequeue();
    _rxBytesInFlight -= line.Length + 1;
    _ticksUntilNextProcess = _responseDelayTicks;

    if (_forcedErrorForNextDequeue is { } code)
    {
        _forcedErrorForNextDequeue = null;
        return $"error:{code}";
    }

    ApplyCommand(line);
    return "ok";
}
```

Во время ожидания (`_ticksUntilNextProcess > 0`) `AdvanceMotion()` в том же тике продолжает выполняться как обычно — задержка тормозит только подтверждение команды, не симуляцию движения по осям. Realtime-байты (`?`, `!`, `~`, `0x85` в `SendRawByteAsync`) задержке не подвержены — обрабатываются синхронно в обход очереди, как и сейчас.

При `delay = 0` (значение по умолчанию при создании транспорта) поведение идентично текущему — существующие тесты в `MockDeviceTransportTests.cs` не меняются.

`ForceNextCommandError` не меняется — переиспользуется как есть.

## `ConnectionViewModel`

Новые члены:
- `[Reactive] private bool isMockSettingsOpen;` → `IsMockSettingsOpen`
- `[Reactive] private int mockResponseDelayMs;` → `MockResponseDelayMs` (по умолчанию `0`)
- `private IMockDeviceControl? _currentMockControl;`
- `ToggleMockSettingsCommand` — `ReactiveCommand.Create(() => IsMockSettingsOpen = !IsMockSettingsOpen)`, через `Track(...).Enhance(...)`, как `ToggleGCodeLogCommand`.
- `TriggerMockErrorCommand` — `ReactiveCommand.Create(() => _currentMockControl?.ForceNextCommandError(9))`.
- `TriggerMockAlarmCommand` — `ReactiveCommand.Create(() => _currentMockControl?.TriggerAlarm(1))`.

В `ConnectAsync`, там же, где сегодня выбирается `innerTransport`:

```csharp
var innerTransport = SelectedEndpoint.Kind == ConnectionEndpointKind.Demo
    ? _createDemoTransport()
    : _realTransport;

_currentMockControl = innerTransport as IMockDeviceControl;
_currentMockControl?.SetResponseDelay(TimeSpan.FromMilliseconds(MockResponseDelayMs));
```

`DisconnectAsync` дополнительно обнуляет `_currentMockControl = null`.

Подписка на изменение `MockResponseDelayMs` (в конструкторе, рядом с остальными `WhenAnyValue`-подписками) применяет новое значение к живому транспорту сразу, без ожидания реконнекта:

```csharp
this.WhenAnyValue(x => x.MockResponseDelayMs)
    .Subscribe(ms => _currentMockControl?.SetResponseDelay(TimeSpan.FromMilliseconds(ms)))
    .DisposeWith(Disposables);
```

## `ProgramViewModel`

Новая тонкая команда, по образцу существующей `OpenGCodeLogCommand`:

```csharp
[RelayCommand]
private void OpenMockSettings() => Connection.IsMockSettingsOpen = true;
```

Генерирует `OpenMockSettingsCommand` — тот же `[RelayCommand]`-паттерн (CommunityToolkit.Mvvm), что и остальные простые команды `ProgramViewModel` (например, `OpenGCodeLog` на строке 105).

## UI: `MainView.axaml`

**Пункт бокового меню** — в `StackPanel` внутри блока `IsVisible="{Binding IsSideMenuOpen}"`, рядом с «Лог G-code»:

```xml
<Button Classes="header-action" HorizontalAlignment="Stretch" HorizontalContentAlignment="Left"
        Content="Настройки мока" Command="{Binding OpenMockSettingsCommand}" />
```

**Диалог** — новый `Border`-оверлей, сосед `IsEditingKeyPoint`/`PendingRename`/`PendingConfirmation`/`IsLibraryOpen` внутри `Grid#RootPanel`, тот же центрированный паттерн (scrim + панель `Width="320"`):

```xml
<Border IsVisible="{Binding Connection.IsMockSettingsOpen}" Background="{StaticResource HudScrimBrush}">
    <Border Width="320" Background="{StaticResource HudPanelElevatedBrush}"
            BorderBrush="{StaticResource HudBorderStrongBrush}" BorderThickness="1"
            Padding="20" HorizontalAlignment="Center" VerticalAlignment="Center">
        <StackPanel Spacing="14">
            <Grid ColumnDefinitions="*,Auto">
                <TextBlock Grid.Column="0" Classes="section-heading" Text="НАСТРОЙКИ МОКА" VerticalAlignment="Center" />
                <Button Grid.Column="1" Content="✕" Padding="8,2" Command="{Binding Connection.ToggleMockSettingsCommand}" />
            </Grid>
            <Button Content="Смоделировать ошибку" Command="{Binding Connection.TriggerMockErrorCommand}"
                    HorizontalAlignment="Stretch" />
            <Button Content="Смоделировать аварию" Command="{Binding Connection.TriggerMockAlarmCommand}"
                    HorizontalAlignment="Stretch" />
            <StackPanel Spacing="4">
                <TextBlock Text="{Binding Connection.MockResponseDelayMs, StringFormat='Задержка ответа: {0} мс'}" />
                <Slider Minimum="0" Maximum="5000" TickFrequency="250" IsSnapToTickEnabled="True"
                        Value="{Binding Connection.MockResponseDelayMs}" />
            </StackPanel>
        </StackPanel>
    </Border>
</Border>
```

## Обработка ошибок / граничные случаи

- Клик по «Смоделировать ошибку»/«Смоделировать аварию» вне демо-подключения (`_currentMockControl == null`) — no-op, диалог остаётся открытым, никакой обратной связи не показывается (осознанное решение, см. Q&A).
- «Смоделировать ошибку» не имеет мгновенного эффекта, если в моменте нет отправленной команды в очереди — она «взводит» следующую команду, как и раньше делал `ForceNextCommandError`. Это соответствует протоколу FluidNC: ошибка — всегда ответ на конкретную команду, а не асинхронный пуш.
- «Смоделировать аварию», напротив, отправляется мгновенно и асинхронно (как и реальный `ALARM:` от контроллера) — сразу открывает модалку аварии поверх основного экрана.
- Существующая кнопка «Сброс аварии» (`$X`) уже работает с моком без изменений — `ApplyCommand` сбрасывает `_alarm = false` на `$X`.
- Переключение с «Демо» на «Устройство» между подключениями обнуляет `_currentMockControl` в `DisconnectAsync`, так что случайный вызов методов мока после переключения невозможен.
- Подключение через реальный эндпоинт (`_realTransport`, не реализующий `IMockDeviceControl`) тоже даёт `_currentMockControl == null` через тот же каст — отдельной ветки на `RealDevice` не нужно.
- Тестовый дублёр `FakeDeviceTransport` (используется по умолчанию в `ConnectionViewModelTests` как демо-транспорт) не реализует `IMockDeviceControl`, так что для позитивных проверок triggr-команд тесты должны явно передать в `CreateVm(...)` настоящий `MockDeviceTransport` в качестве демо-транспорта.
- Задержка ответа не влияет на realtime-статус-опрос (`?`), поэтому UI продолжает получать живые координаты/состояние, даже когда очередь строковых команд «зависла» в задержке.

## Тесты

`ArctZ.Tests/Services/Device/MockDeviceTransportTests.cs` (новые кейсы):
- `TriggerAlarm` → статус-запрос сразу после вызова отдаёт `MachineState.Alarm`; поднятая строка `LineReceived` равна `ALARM:{code}`; `_targetPose` сброшен (движение останавливается).
- `SetResponseDelay` → после отправки команды и `delay = N` тиков `ok` не приходит раньше `N` вызовов `_ticker.RaiseElapsed()`, но приходит ровно на `N+1`-м; движение по осям (`WPos`) в промежуточных тиках всё равно продолжается, если задержка относится к следующей команде, а не к уже выполняющемуся движению.
- `delay = 0` (по умолчанию) — существующие тесты проходят без изменений.

`ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs` (новые кейсы):
- `ToggleMockSettingsCommand` переключает `IsMockSettingsOpen`.
- `TriggerMockErrorCommand`/`TriggerMockAlarmCommand` вне подключения (демо-транспорт по умолчанию — `FakeDeviceTransport`, не реализующий `IMockDeviceControl`) не бросают исключений (no-op).
- После подключения к «Демо» с настоящим `MockDeviceTransport`, переданным в `CreateVm(...)` как демо-транспорт, `TriggerMockAlarmCommand` приводит к тому, что `LastAlarmCode` становится `1` (через реальный `AlarmTriggered` пайплайн `DeviceSession`).
- Изменение `MockResponseDelayMs` во время активного демо-подключения (тот же настоящий `MockDeviceTransport`) форвардится в `SetResponseDelay(...)` — проверяется через наблюдаемый эффект (задержку `ok` на реально отправленной команде).

## Затронутые файлы

- `ArctZ/Services/Device/IMockDeviceControl.cs` — новый интерфейс
- `ArctZ/Services/Device/Simulation/MockDeviceTransport.cs` — реализует `IMockDeviceControl`; `TriggerAlarm`, `SetResponseDelay`, изменение `ProcessOnePendingLine`
- `ArctZ/ViewModels/ConnectionViewModel.cs` — `IsMockSettingsOpen`, `MockResponseDelayMs`, `_currentMockControl`, три новые команды, подписка на задержку
- `ArctZ/ViewModels/ProgramViewModel.cs` — `OpenMockSettingsCommand`
- `ArctZ/Views/MainView.axaml` — пункт бокового меню, диалог настроек мока
- `ArctZ.Tests/Services/Device/MockDeviceTransportTests.cs` — новые тесты
- `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs` — новые тесты

## Не в скоупе

- Выбор произвольного кода ошибки/аварии — оба фиксированы (`9` и `1` соответственно), без ввода/выбора пользователем.
- Визуальная индикация в диалоге, что клик по кнопке не подействовал (нет демо-подключения) — тихий no-op, без баннеров/тултипов.
- Задержка realtime-байтов (`?`, `!`, `~`, jog-cancel) — не запрашивалось, они остаются синхронными.
- Персистентность `MockResponseDelayMs` между запусками приложения — значение живёт только в рамках текущей сессии `ConnectionViewModel` (singleton на время жизни приложения, но не сохраняется на диск).
