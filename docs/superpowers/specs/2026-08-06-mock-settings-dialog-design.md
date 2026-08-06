# Настройки мока для демо-режима — дизайн

Дата: 2026-08-06

## Проблема

Демонстрация приложения без реального устройства (эндпоинт «Демо», `MockDeviceTransport`) сейчас не даёт способа показать, как приложение ведёт себя при ошибке или аварии контроллера, и не позволяет замедлить отклик мока, чтобы наглядно показать буферизацию команд. `MockDeviceTransport.ForceNextCommandError(code)` уже существует, но не вызывается ниоткуда в UI; сигнала аварии (`ALARM:`) в моке нет вообще; задержка ответа не настраивается.

## Что добавляется

Пункт «Настройки мока» в боковой панели «МЕНЮ» (кнопка ☰). Открывает модальный диалог с:
- кнопкой «Смоделировать ошибку» — следующая отправленная команда получит `error:9` вместо `ok`;
- кнопкой «Смоделировать аварию» — мгновенно переводит мок в состояние `Alarm` с кодом `1`;
- слайдером «Задержка ответа» (0–5000 мс, шаг 250 мс) — дополнительная задержка перед `ok`/`error` на каждую отправленную строковую команду.

Пункт меню и диалог не скрываются и не выключаются по типу выбранного эндпоинта — при переключении на «Устройство» триггеры так и остаются тихим no-op. **Однако физически они лежат внутри той же части экрана (`DockPanel` в `MainView.axaml`), что и остальной UI приложения, и потому недоступны, пока модалка подключения или аварии перекрывает экран** (`IsEnabled="{Binding !Connection.IsAnyModalVisible}"`, тот же механизм, что блокирует шапку/боковое меню/джойстики) — то есть до подключения и во время активной аварии открыть диалог нельзя. Это осознанный компромисс, а не баг: поднимать диалог выше существующих блокирующих модалок потребовало бы более крупной переделки структуры оверлеев в `MainView.axaml`, непропорциональной размеру этой фичи (обнаружено на финальном ревью реализации, 2026-08-06; решение — оставить как есть). Кнопки и слайдер внутри диалога реально влияют на поведение только когда есть живой демо-транспорт — иначе это тихий no-op, значение задержки при этом запоминается и применяется к следующему демо-подключению.

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
        _ticksUntilNextProcess = _responseDelayTicks;
    }
}
```

Новые приватные поля: `_responseDelayTicks` (по умолчанию `0`, сохраняет текущее поведение без изменений), `_ticksUntilNextProcess` (счётчик текущей задержки, тоже по умолчанию `0`).

`SetResponseDelay` обязательно перезаписывает и `_ticksUntilNextProcess`, а не только `_responseDelayTicks` — иначе первая же команда, отправленная после включения задержки, проскочила бы без неё (счётчик стоял бы на `0` с момента создания транспорта). Перезапись срабатывает, даже если в очереди сейчас ничего нет — `_ticksUntilNextProcess` просто не тратится (`ProcessOnePendingLine` проверяет пустую очередь раньше, чем свой countdown), пока не появится реальная команда для отсчёта.

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

    var trimmed = line.Trim();
    if (_alarm &&
        !trimmed.Equals("$X", StringComparison.OrdinalIgnoreCase) &&
        !trimmed.Equals("$H", StringComparison.OrdinalIgnoreCase))
    {
        return "error:9";
    }

    ApplyCommand(line);
    return "ok";
}
```

Во время ожидания (`_ticksUntilNextProcess > 0`) `AdvanceMotion()` в том же тике продолжает выполняться как обычно — задержка тормозит только подтверждение команды, не симуляцию движения по осям. Realtime-байты (`?`, `!`, `~`, `0x85` в `SendRawByteAsync`) задержке не подвержены — обрабатываются синхронно в обход очереди, как и сейчас.

При `delay = 0` (значение по умолчанию при создании транспорта) поведение идентично текущему — существующие тесты в `MockDeviceTransportTests.cs` не меняются.

**Найдено на финальном ревью и исправлено:** первоначальная версия `TriggerAlarm` вместо `_alarm`-гейта в `ProcessOnePendingLine` очищала `_pendingLines`/`_rxBytesInFlight` напрямую. Это ломало `BufferAwareCommandQueue`: строки, уже лежавшие в его собственной очереди `_inFlight` с `TaskCompletionSource`, ожидающими `ok`/`error`, просто исчезали из `MockDeviceTransport` без ответа — эти `TaskCompletionSource` никогда не резолвились. `ProgramViewModel.PlayAsync` зависал навсегда на `await completion`, а `$X` (эксклюзивная команда) вообще не мог быть отправлен, пока в `_inFlight` что-то висит — то есть «Сброс аварии», вызванный во время выполнения программы, зависал навечно, и единственная кнопка модалки аварии становилась недоступной без перезапуска приложения. Исправление — не молчаливо ронять команды из очереди, а отвечать на них `error:9` (тот же код, что уже используется для «Смоделировать ошибку»), пока авария активна, кроме `$X`/`$H` — так каждая команда всё равно получает ответ (её `TaskCompletionSource` резолвится), а `$X`/`$H` по-прежнему проходят и снимают аварию.

`ForceNextCommandError` защищена тем же `_lock`, что и остальное мутируемое состояние класса (по документированному в шапке файла инварианту):

```csharp
public void ForceNextCommandError(int code)
{
    lock (_lock)
    {
        _forcedErrorForNextDequeue = code;
    }
}
```

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
private void OpenMockSettings()
{
    Connection.IsMockSettingsOpen = true;
    IsSideMenuOpen = false;
}
```

Генерирует `OpenMockSettingsCommand` — тот же `[RelayCommand]`-паттерн (CommunityToolkit.Mvvm) и та же форма, что у `OpenGCodeLog` (строка 104-109): открывает диалог и заодно закрывает боковое меню.

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
- `SetResponseDelay(delay)`, где `delay` соответствует `N` тикам (например, `300`мс при интервале тика `100`мс → `N=3`): после отправки команды первые `N` вызовов `_ticker.RaiseElapsed()` не производят `ok`, он приходит ровно на `(N+1)`-м вызове (тот же расклад, что показан пользователю на этапе уточнений: «3 тика простаивает → на 4-м тике приходит ok»).
- `SetResponseDelay`, вызванный ДО отправки команды (пока очередь пуста), всё равно применяется к самой первой последующей команде — не только к командам после первой (регресс-тест на то, что `_ticksUntilNextProcess` прайм-ится в `SetResponseDelay`, а не только сбрасывается после каждой обработанной строки).
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
