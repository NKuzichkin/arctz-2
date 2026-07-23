# Архитектура приложения ArctZ: слой управления джибом

## Назначение

`docs/software/app-architecture.md` фиксирует текущее состояние кода
(`VirtualJoystick`, пустая `MainViewModel`) и список открытых вопросов по
подключению к устройству. Этот документ отвечает на эти вопросы и
проектирует слой между вводом с джойстика и Bluetooth-соединением с платой
FluidNC — command-модель, доставку команд, состояние соединения и то, как
всё это встраивается в существующий MVVM-скелет (`CommunityToolkit.Mvvm`,
`ViewLocator`, compiled bindings).

Протокол устройства (G-code/jog/realtime-команды, формат статус-ответов)
уже описан в [`../protocol/bluetooth-gcode-control.md`](../protocol/bluetooth-gcode-control.md)
и здесь не переопределяется — используется как есть.

## Область (scope)

**В скоуп:**
- Слой сервисов `ArctZ/Services/Device/*`: модель команд, сериализация,
  доставка (realtime/очередь/jog-throttling), парсинг статуса, оркестрация
  соединения.
- ViewModels: `ConnectionViewModel` + расширение `MainViewModel` для
  композиции и проброса событий джойстика.
- DI-разводка между `ArctZ` (core) и четырьмя платформенными головами.
- Новый тестовый проект `ArctZ.Tests`.

**Вне скоупа (открытые вопросы других документов, не блокируют этот дизайн):**
- Маппинг осей джойстика на физические оси джиба — зависит от
  [`../hardware/mechanics.md`](../hardware/mechanics.md), пока не решён.
  Архитектура принимает это как параметр (`IJogCommandFactory`), не как
  готовое решение.
- Реальные `IDeviceTransport` для iOS/Browser (BLE-мост или другая
  прошивка) — см. раздел "Платформенные ограничения" ниже.
- Точный интервал throttling для `$J=` и поведение буфера/flow control —
  требует экспериментов на реальном железе.

## Принятые решения (кратко)

| Вопрос | Решение |
|---|---|
| Платформы для связи с устройством | Все 4 головы имеют `IDeviceTransport`; реально реализованы Desktop + Android, iOS/Browser — заглушка (см. ниже) |
| Тестовый проект | Добавляется сейчас (`ArctZ.Tests`, xUnit) |
| Экран подключения | Не отдельная навигация — `ConnectionViewModel` как под-VM `MainViewModel`, отображается через `ViewLocator`-композицию на том же экране |
| DI-контейнер | `Microsoft.Extensions.DependencyInjection`, платформенные головы регистрируют свой `IDeviceTransport` |
| Декомпозиция команд | Разделены: модель команды / сериализация в текст протокола / политика отправки (realtime, очередь с ack, throttled jog) |

## Платформенные ограничения (важно)

FluidNC на ESP32 использует **Bluetooth Classic SPP**, не BLE. Публичные
API платформ поддерживают это не везде:

| Платформа | Classic BT SPP | Реализация `IDeviceTransport` |
|---|---|---|
| Desktop (Win/macOS/Linux) | Да — сопряжённый SPP-канал ОС видит как обычный COM-порт | `System.IO.Ports.SerialPort` |
| Android | Да — `BluetoothSocket`/RFCOMM в публичном API | нативный `BluetoothSocket` |
| iOS | Нет — `CoreBluetooth` только BLE; classic SPP только через `ExternalAccessory` для MFi-сертифицированных устройств | `NotSupportedDeviceTransport`-заглушка |
| Browser (WASM) | Нет — Web Bluetooth API только BLE, поддержка браузеров ограничена | `NotSupportedDeviceTransport`-заглушка |

Заглушка реализует `IDeviceTransport`, но `ConnectAsync` сразу завершается
состоянием "недоступно на этой платформе" — `ConnectionViewModel`
отображает это как обычную ошибку подключения. Когда появится решение по
BLE-мосту или альтернативной прошивке для iOS/Browser, меняется только
регистрация в DI этой головы — `IDeviceSession` и всё, что выше, об этом
не знают.

## Структура проекта

```
ArctZ/Services/Device/
  Commands/
    IDeviceCommand.cs            — JogCommand, GCodeLineCommand, RealtimeCommand (records, чистые данные)
    ICommandSerializer.cs
    FluidNcCommandSerializer.cs  — модель → текст протокола FluidNC
  IDeviceTransport.cs            — абстракция байтового канала (реализуется в каждой платформенной голове)
  IJogCommandFactory.cs
  JogCommandFactory.cs           — JoystickState → JogCommand (маппинг осей/силы; параметризуемо)
  IJogScheduler.cs
  JogScheduler.cs                — throttling, cancel-on-release (шлёт 0x85 через realtime-канал)
  ICommandQueue.cs
  CommandQueue.cs                — FIFO для GCodeLineCommand с учётом ack/буфера контроллера
  IRealtimeCommandChannel.cs
  RealtimeCommandChannel.cs      — немедленная отправка однобайтовых realtime-команд
  IStatusParser.cs
  FluidNcStatusParser.cs         — парсинг `<Idle|WPos:...|...>`, `error:N`, `ALARM:N`
  IStatusPoller.cs
  StatusPoller.cs                — таймер, периодически шлёт `?` через realtime-канал
  DeviceStatus.cs, ConnectionState.cs
  IDeviceSession.cs
  DeviceSession.cs               — оркестратор/фасад для ViewModels
  ServiceCollectionExtensions.cs — AddArctZCore(IServiceCollection)

ArctZ/ViewModels/
  ConnectionViewModel.cs         — список устройств, Connect/Disconnect/Home/ResetAlarm-команды, статус
  MainViewModel.cs               — свойство Connection (ConnectionViewModel), приём событий джойстика

ArctZ/Views/
  ConnectionView.axaml(.cs)      — резолвится ViewLocator'ом автоматически по имени VM
  MainView.axaml(.cs)            — добавляет <ContentControl Content="{Binding Connection}"/>

ArctZ.Desktop/  — DesktopSerialBluetoothTransport : IDeviceTransport (System.IO.Ports)
ArctZ.Android/  — AndroidBluetoothSocketTransport : IDeviceTransport
ArctZ.iOS/      — NotSupportedDeviceTransport
ArctZ.Browser/  — NotSupportedDeviceTransport

ArctZ.Tests/    — новый проект, xUnit, референсит ArctZ
```

## Command-слой: модель / сериализация / политика отправки

Три независимых слоя вместо одного "джойстик → команда" конвейера:

1. **Модель (`IDeviceCommand`)** — чистые данные, без поведения:
   `JogCommand(AxisDeltas, Feed)`, `GCodeLineCommand(string Line)`,
   `RealtimeCommand(byte Value)`.
2. **Сериализация (`ICommandSerializer`)** — `string Serialize(IDeviceCommand)`,
   переводит модель в текст протокола FluidNC (пробелы, порядок параметров,
   `$J=G91 G21 X.. Y.. F..` и т.п.). Чистая функция — тестируется без
   транспорта и таймеров.
3. **Политика отправки** — три отдельных канала под три разных вида команд,
   потому что у них разное поведение доставки:
   - `IRealtimeCommandChannel` — `RealtimeCommand` шлётся немедленно, минуя
     очередь (это единственный способ прервать движение без ожидания).
   - `ICommandQueue` — `GCodeLineCommand` идёт в FIFO, следующая строка не
     отправляется до `ok`/ошибки на предыдущую (простой ack-based flow
     control; учёт `Bf:` из статус-ответа — по необходимости, не
     обязательно для первой версии).
   - `IJogScheduler` — по таймеру берёт текущее состояние джойстика через
     `IJogCommandFactory`, строит и **сразу шлёт** `JogCommand` в транспорт
     напрямую, не дожидаясь `ok` через `ICommandQueue` — throttling уже
     ограничивает частоту, ожидание ack сделало бы live-управление
     рваным. При `EndJog()` шлёт `0x85` через `IRealtimeCommandChannel`.

Ошибки на любую отправленную строку (включая jog) ловятся общим циклом
чтения `DeviceSession` и всплывают как событие `CommandRejected`, не
завязаны на то, через какой канал команда ушла.

`IJogCommandFactory` принимает `JoystickState` — небольшой тип на уровне
`Services/Device`, а не `JoystickEventArgs` из `Components/VirtualJoystick`
напрямую: сервисный слой не должен зависеть от типа события конкретного
UI-контрола. `MainViewModel` переводит `JoystickEventArgs` в `JoystickState`
при вызове `OnJoystickMove(e)` (см. раздел MVVM-слой).

## `DeviceSession`: состояние соединения и ошибки

`ConnectionState`: `Disconnected → Connecting → Connected ⇄ Reconnecting →
Disconnected`. Меняется только внутри `DeviceSession`, `ConnectionViewModel`
только подписан.

Обязанности `DeviceSession`:
- Фоновый цикл чтения строк от `IDeviceTransport`: статус-строки →
  `IStatusParser` → `DeviceStatus`; `ok` → в `ICommandQueue`; `error:N`/
  `ALARM:N` → событие `CommandRejected` / `DeviceStatus.State = Alarm`.
- `IStatusPoller` держит `DeviceStatus` свежим, пока нет активного
  движения (`?` раз в N мс).
- Публичный фасад: `ConnectAsync(deviceId)`, `DisconnectAsync()`,
  `BeginJog/UpdateJog(JoystickState)`, `EndJog()`, `Home()`, `ResetAlarm()`,
  свойства `ConnectionState`, `DeviceStatus`, событие `CommandRejected`.
- Реконнект: обрыв транспорта → `Reconnecting`, повторные попытки
  `ConnectAsync` с задержкой, `LastError` для UI. Восстановленное
  соединение не сбрасывает `Alarm` автоматически — это отдельное явное
  действие пользователя (`ResetAlarm()`); нужен ли `$X` всегда после
  реконнекта — открытый вопрос, требует проверки на реальном железе, не
  блокирует архитектуру.

## MVVM-слой

- **Compiled bindings**: `ConnectionView.axaml` — обязательный
  `x:DataType="vm:ConnectionViewModel"`, как и везде в проекте.
- **Команды**: `ConnectionViewModel` использует `[RelayCommand]`
  (`ConnectCommand`, `DisconnectCommand`, `HomeCommand`,
  `ResetAlarmCommand`), `CanExecute` завязан на `ConnectionState`.
- **Композиция через `ViewLocator`**: в проекте уже зарегистрирован
  глобальный `ViewLocator` (`App.axaml.DataTemplates`), резолвящий
  `XyzView` по имени `XyzViewModel`. `MainViewModel.Connection` —
  свойство типа `ConnectionViewModel` (без сеттера, под-VM не меняется во
  время жизни приложения); `MainView.axaml` добавляет
  `<ContentControl Content="{Binding Connection}"/>` — `ConnectionView`
  резолвится автоматически, без ручной прописки во View.
- **Джойстик — события, не команды**: `JoystickMove/Down/Up` — обычные
  `EventHandler<JoystickEventArgs>`, не `RoutedEvent`, нативного XAML
  event→command байндинга для них нет без `Avalonia.Xaml.Interactivity`.
  Сохраняется нынешний паттерн: тонкий code-behind без логики,
  пробрасывающий `EventArgs` в VM (`vm.OnJoystickDown()`/
  `OnJoystickMove(e)`/`OnJoystickUp()`), а уже эти методы `MainViewModel`
  вызывают `IDeviceSession.BeginJog/UpdateJog/EndJog`.
- **Design-time данные**: `ConnectionViewModel` получает parameterless
  design-time конструктор (dummy `IDeviceSession`) для
  `<Design.DataContext>`, по аналогии с дефолтными значениями в текущей
  `MainViewModel`.
- **Создание VM через DI**: `App.axaml.cs` вместо `new MainViewModel()`
  резолвит `App.Services!.GetRequiredService<MainViewModel>()` во всех
  трёх ветках `OnFrameworkInitializationCompleted`.

## DI-разводка по головам

`Directory.Packages.props` получает `Microsoft.Extensions.DependencyInjection`
(референс — из `ArctZ.csproj`). `ArctZ/Services/Device/ServiceCollectionExtensions.cs`
добавляет `AddArctZCore(this IServiceCollection)`, регистрирующий всё
платформонезависимое (сериализатор, фабрику jog-команд, планировщик,
очередь, realtime-канал, парсер статуса, поллер, `IDeviceSession`,
`ConnectionViewModel`, `MainViewModel`). `IDeviceTransport` в этот метод не
входит.

Каждая голова до старта Avalonia:

```csharp
var services = new ServiceCollection();
services.AddArctZCore();
services.AddSingleton<IDeviceTransport, DesktopSerialBluetoothTransport>(); // своя реализация на голову
App.Services = services.BuildServiceProvider();
```

## Тестовый проект `ArctZ.Tests`

Новый проект в `ArctZ.slnx`, xUnit, референсит `ArctZ`. Покрывает:
- `FluidNcCommandSerializer` — модель команды → корректный текст протокола.
- `JogCommandFactory` — `JoystickState` → `JogCommand` (маппинг, когда
  будет определён на уровне механики).
- `JogScheduler` — throttling и cancel-on-release через фейковый таймер.
- `CommandQueue` — ack-flow через фейковый `IDeviceTransport`.
- `FluidNcStatusParser` — разбор статус-строк, `error:N`, `ALARM:N`.
- `DeviceSession` — сквозной сценарий (connect/reconnect/jog/ошибки) через
  `FakeDeviceTransport`, без реального устройства.

## Открытые вопросы (не блокируют реализацию этого дизайна)

- [ ] Маппинг осей джойстика на физические оси джиба — зависит от
      решений в `../hardware/mechanics.md`.
- [ ] BLE-мост или альтернативная прошивка для iOS/Browser.
- [ ] Точный интервал throttling для `$J=`, поведение при быстрой смене
      направления — нужны эксперименты на реальном железе.
- [ ] Нужен ли учёт `Bf:` (буфер) в `ICommandQueue`, или достаточно
      простого ack-per-line для первой версии.
- [ ] Точное поведение при реконнекте: всегда ли нужен `$X` после
      восстановления соединения.
