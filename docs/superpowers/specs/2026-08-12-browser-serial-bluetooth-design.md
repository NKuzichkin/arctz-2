# Bluetooth в браузере через Web Serial API — дизайн

Дата: 2026-08-12

## Проблема

`ArctZ.Browser` сейчас использует `NotSupportedDeviceTransport` — реальное устройство недоступно, работает только «Демо». FluidNC подключается по Bluetooth Classic SPP, который на Desktop доступен как обычный COM-порт через `System.IO.Ports.SerialPort` (`ArctZ.Desktop/DesktopSerialTransport.cs`). В браузере тот же виртуальный COM-порт доступен через **Web Serial API** (`navigator.serial`) — это НЕ Web Bluetooth (GATT/BLE): FluidNC BT не поддерживает GATT-профиль, поэтому Web Bluetooth здесь непригоден в принципе.

## Область действия

Только `ArctZ.Browser` — платформа с уже спаренным на уровне ОС Bluetooth Classic SPP устройством. Android/iOS не затрагиваются (там нет Web Serial). Desktop не меняется.

## Изменения в ядре (`ArctZ/Services/Device/`)

`IDeviceTransport` получает новый член с default-реализацией интерфейса (C# DIM), чтобы не ломать существующие платформы:

```csharp
bool IsSupported => true;
```

`DesktopSerialTransport`, Android/iOS `NotSupportedDeviceTransport`, `MockDeviceTransport` — не меняются, наследуют `true` по умолчанию (кроме `NotSupportedDeviceTransport`, у которого и так `IsConnected` всегда `false`, но `IsSupported` не относится к «поддержке», а к «доступен ли API в среде выполнения» — оставляем `true`, т.к. это не про Web Serial-специфику).

## `ArctZ.Browser/wwwroot/serial.js` (новый JS-модуль)

ES-модуль, подключаемый через net10 WASM `[JSImport]`/`[JSExport]` (не classic Blazor interop). Функции:

- `isSupported()` → `'serial' in navigator`, синхронно.
- `async requestPort()` — `navigator.serial.requestPort()` (показывает нативный пикер браузера, требует user gesture), затем `port.open({ baudRate: 115200 })`. Запоминает выбранный `port`-объект в модульной переменной как «текущий» (один активный порт — транспорт регистрируется как singleton, второй одновременный порт не нужен).
- `async reopenSavedPort()` — `navigator.serial.getPorts()`, берёт первый элемент (если есть), `port.open({ baudRate: 115200 })` без пикера. Возвращает `false`, если сохранённых портов нет.
- `async write(bytes)` — пишет в `port.writable`.
- `async closePort()` — закрывает `port.readable`/`port.writable`, сам `port`.
- Цикл чтения: после `open()` крутит `while` с `reader.read()` на `port.readable`, буферизует байты до `\n` (аналог `NewLine = "\n"` на Desktop), декодирует UTF-8 и на каждую собранную строку дёргает C#-callback через заранее сохранённый `[JSExport]`-делегат. При ошибке чтения/закрытии потока (кабель/BT разрыв) — дёргает callback «disconnected».

## `ArctZ.Browser/BrowserSerialTransport.cs` (новый класс)

Реализует `IDeviceTransport`:

- `IsSupported` → результат `SerialInterop.IsSupported()` (закешированный при старте, т.к. не меняется в рантайме).
- `ConnectAsync(deviceId, ct)` — `deviceId` не используется (Web Serial не даёт стабильных строковых идентификаторов портов, выбор идёт через пикер/сохранённые permissions браузера, как и на Desktop сейчас, где реального выбора порта тоже нет). Сначала пробует `reopenSavedPort()`; если `false` — вызывает `requestPort()` (пикер, только в контексте вызова с активным user gesture — см. ниже про реконнект).
- `DisconnectAsync()` → `closePort()`.
- `SendLineAsync`/`SendRawByteAsync` → `write(bytes)`.
- `LineReceived`/`Disconnected` — С#-события, вызываемые из статических `[JSExport]`-методов (JSExport требует static; храним статическую ссылку на текущий активный экземпляр транспорта, т.к. он singleton, и маршрутизируем коллбэк в его события).

### Реконнект без пикера

`DeviceSession` при обрыве связи сам вызывает `ConnectAsync` повторно (реконнект-петля) без нового клика пользователя — в этот момент показать пикер нельзя (браузер требует user gesture). `reopenSavedPort()` решает это: JS-модуль хранит ссылку на уже выданный `SerialPort`-объект между вызовами `ConnectAsync`, и повторные попытки просто вызывают `port.open()` заново без пикера — так же, как Desktop просто переоткрывает уже известный COM-порт.

## `ArctZ.Browser/Program.cs`

Регистрация `services.AddSingleton<IDeviceTransport, NotSupportedDeviceTransport>()` заменяется на `services.AddSingleton<IDeviceTransport, BrowserSerialTransport>()`.

## `ConnectionViewModel` — сообщение о неподдерживаемом браузере

Новое computed-свойство:

```csharp
public bool IsRealDeviceUnsupported => !_realTransport.IsSupported;
```

Проверяется сразу при старте (не только при попытке подключения), т.к. `IsSupported` — статичное свойство рантайма, доступное сразу после DI. В модалке подключения (`MainView.axaml`, см. `2026-07-28-connection-modal-design.md`) при `IsRealDeviceUnsupported == true`:

- показывается баннер «Web Serial API не поддерживается этим браузером — используйте Chrome/Edge»;
- пункт «Устройство» в `AvailableEndpoints` недоступен для выбора (или ComboBox фильтрует его), доступно только «Демо».

На Desktop/Android/iOS `IsRealDeviceUnsupported` всегда `false` (их `IsSupported` — `true` по умолчанию), баннер не появляется.

## Тестирование

`BrowserSerialTransport` и `serial.js` не покрываются юнит-тестами `ArctZ.Tests` — тонкая обёртка над браузерным API, недоступным вне браузера (тот же принцип, что и для `DesktopSerialTransport`, тоже не тестируемого юнит-тестами). Новая логика `IsRealDeviceUnsupported` в `ConnectionViewModel` тестируется через фейковый `IDeviceTransport` с `IsSupported = false` и `= true`.

Живая проверка — обязательный запуск `dotnet run --project ArctZ.Browser/ArctZ.Browser.csproj` в Chrome/Edge (реальное Web Serial подключение к FluidNC) согласно правилу «Тестирование UI» в `CLAUDE.md`: собрать → запустить → пользователь проверяет → `AskUserQuestion` по каждой проверяемой функции (пикер порта, подключение, отправка G-code, обрыв/реконнект без повторного пикера, сообщение о неподдерживаемом браузере — если есть возможность проверить в Firefox/Safari).
