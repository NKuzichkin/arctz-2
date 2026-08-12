# Полноценная работа ArctZ на Android — дизайн

Дата: 2026-08-12

## Цель

Сделать Android-голову полноценной: реальное управление контроллером FluidNC через
Bluetooth Classic (RFCOMM/SPP), выбор устройства из приложения (спаренные + поиск новых
+ спаривание), установка APK на телефон по USB. Сейчас на Android зарегистрирован
`NotSupportedDeviceTransport`, и доступен только режим «Демо».

Вне объёма: адаптация раскладки UI под телефон (размеры кнопок, портретный режим,
кнопка «Назад», запрет засыпания экрана) — отдельная задача. Меняется только модалка
подключения, потому что без неё функция выбора устройства не существует.

## Ограничение проверки

Физического контроллера FluidNC нет. Проверяется: сборка и установка APK, выдача
разрешений, перечисление спаренных устройств, поиск и спаривание, режим «Демо»,
внятные сообщения об ошибках. Реальный обмен G-code с железом остаётся непроверенным
и должен быть явно отмечен как таковой в итоговом отчёте.

## Транспорт

FluidNC отдаёт Bluetooth Classic SPP (только на ESP32 WROOM/WROVER); BLE не подходит.
На Android это `BluetoothSocket` поверх RFCOMM с UUID SPP
`00001101-0000-1000-8000-00805F9B34FB`.

`ArctZ.Android/AndroidBluetoothTransport.cs` реализует `IDeviceTransport`:

- `IsSupported` — `BluetoothAdapter.DefaultAdapter is not null`.
- `ConnectAsync(deviceId)`, где `deviceId` — MAC-адрес устройства:
  1. **Guard в начале**: закрыть и обнулить предыдущий сокет, остановить read-loop.
     `DeviceSession` в цикле переподключения вызывает `ConnectAsync` повторно **без**
     промежуточного `DisconnectAsync` (см. `DeviceSession.cs:146`), и живой сокет иначе
     держал бы соединение, ломая каждую попытку. Тот же приём, что в
     `DesktopSerialTransport.cs:31-42`.
  2. `adapter.CancelDiscovery()` — активный discovery резко замедляет и роняет connect.
  3. `adapter.GetRemoteDevice(mac).CreateRfcommSocketToServiceRecord(SppUuid)` →
     `socket.ConnectAsync()` на фоновом потоке.
  4. Запустить read-loop: блокирующее чтение из `InputStream` в буфер, байты подаются в
     `LineAssembler`, каждая собранная строка → `LineReceived`. `IOException` или чтение
     `-1` → `IsConnected = false` и `Disconnected`.
- `SendLineAsync` / `SendRawByteAsync` — запись в `OutputStream` + `Flush()` под общим
  `SemaphoreSlim`, чтобы realtime-байты (`?`, `!`, `~`, `0x18`) не вклинивались в середину
  G-code-строки.
- `DisconnectAsync` — отмена `CancellationTokenSource` read-loop, закрытие потоков и
  сокета, `IsConnected = false`. Идемпотентно.
- Ошибки `ConnectAsync` пробрасываются наружу: `ConnectionViewModel.ConnectAsync` уже
  ловит их и корректно разворачивает сессию.

### `LineAssembler` (ядро)

`ArctZ/Services/Device/LineAssembler.cs` — чистый класс без зависимостей от Android:
`IEnumerable<string> Append(byte[] buffer, int count)`. Разделители `\n` и `\r\n`,
неполный хвост сохраняется до следующего вызова, пустые строки отбрасываются
(FluidNC шлёт `\r\n` и периодические пустые строки), ограничение длины строки
(4 КБ) защищает от бесконечного роста буфера на мусорном потоке.

Вынесен в ядро именно ради тестов: `ArctZ.Tests` (`net10.0`) не видит типы из
`ArctZ.Android` (`net10.0-android`), а разбор потока — единственная нетривиальная
чистая логика транспорта.

## Выбор устройства

### Абстракция в ядре

`ArctZ/Services/Device/DeviceEndpointInfo.cs`:

```csharp
public sealed record DeviceEndpointInfo(string Id, string Name, bool IsPaired);
```

`ArctZ/Services/Device/IDeviceEndpointProvider.cs`:

```csharp
public interface IDeviceEndpointProvider
{
    /// Умеет ли платформа искать новые устройства и спаривать их.
    bool SupportsDiscovery { get; }

    /// Уже известные (спаренные) устройства.
    Task<IReadOnlyList<DeviceEndpointInfo>> GetKnownEndpointsAsync(CancellationToken ct = default);

    /// Поиск в эфире: подписка запускает discovery, dispose — останавливает.
    /// Последовательность завершается сама по окончании скана.
    IObservable<DeviceEndpointInfo> Discover();

    /// Спаривание. true — устройство спарено к моменту возврата.
    Task<bool> PairAsync(string deviceId, CancellationToken ct = default);
}
```

Тип живёт в `Services/Device`, а не в `ViewModels`, чтобы сервисный слой не зависел от
типов представления; `ConnectionViewModel` сам маппит `DeviceEndpointInfo` в
`ConnectionEndpoint`.

Реализация по умолчанию `SingleRealDeviceEndpointProvider` (ядро) сохраняет текущее
поведение Desktop и Browser: `SupportsDiscovery = false`, один эндпоинт
`("real", "Устройство", IsPaired: true)`, `Discover()` — пустая последовательность,
`PairAsync` → `true`. Регистрируется в `AddArctZCore()`; Android-голова регистрирует свою
реализацию после `AddArctZCore()` — в Microsoft.Extensions.DependencyInjection побеждает
последняя регистрация.

### Android-реализация

`ArctZ.Android/AndroidBluetoothEndpointProvider.cs`:

- `GetKnownEndpointsAsync` — `adapter.BondedDevices` → `(Address, Name ?? Address, IsPaired: true)`.
  Фильтра по имени нет: пользователь выбирает сам.
- `Discover()` — `Observable.Create`: регистрируется `BroadcastReceiver` на
  `BluetoothDevice.ActionFound` и `BluetoothAdapter.ActionDiscoveryFinished`,
  вызывается `adapter.StartDiscovery()`. Найденные устройства отдаются как
  `IsPaired: false` (если MAC уже есть среди спаренных — `true`). `ActionDiscoveryFinished`
  завершает последовательность; dispose снимает receiver и зовёт `CancelDiscovery()`.
- `PairAsync` — если `BondState == Bonded`, сразу `true`; иначе `device.CreateBond()` и
  ожидание `ActionBondStateChanged` до `Bonded`/`None` с таймаутом 60 с (системный
  диалог PIN показывается ОС).
- Перед любой из операций запрашиваются разрешения (ниже). Отказ → исключение с
  текстом для пользователя.

### Разрешения

Манифест `ArctZ.Android/Properties/AndroidManifest.xml`:

```xml
<uses-permission android:name="android.permission.BLUETOOTH" android:maxSdkVersion="30" />
<uses-permission android:name="android.permission.BLUETOOTH_ADMIN" android:maxSdkVersion="30" />
<uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" android:maxSdkVersion="30" />
<uses-permission android:name="android.permission.BLUETOOTH_CONNECT" />
<uses-permission android:name="android.permission.BLUETOOTH_SCAN"
                 android:usesPermissionFlags="neverForLocation" />
```

На Android ≤11 (API ≤30) классический discovery не возвращает устройств без
`ACCESS_FINE_LOCATION`, поэтому она запрашивается в рантайме на этих версиях.
На API 31+ вместо неё — `BLUETOOTH_SCAN` с `neverForLocation` (приложение не выводит
местоположение из результатов скана) и `BLUETOOTH_CONNECT`.

`ArctZ.Android/AndroidPermissions.cs` — `Task<bool> RequestAsync(params string[])`:
уже выданные пропускаются, остальные запрашиваются через
`ActivityCompat.RequestPermissions` у текущей `MainActivity`; результат приходит в
`MainActivity.OnRequestPermissionsResult` и резолвит `TaskCompletionSource`.
`MainActivity` хранит статическую ссылку на себя (`Instance`), выставляемую в
`OnCreate`/`OnDestroy`.

### `ConnectionViewModel`

Добавляется параметр конструктора `IDeviceEndpointProvider`. Логика:

- `AvailableEndpoints` заполняется как: реальные эндпоинты провайдера (только если
  `_realTransport.IsSupported`) + всегда «Демо». Конструктор синхронно добавляет «Демо»
  и запускает `RefreshEndpointsCommand` (перечисление устройств асинхронно и требует
  разрешений, синхронно его сделать нельзя). Если `GetKnownEndpointsAsync` завершается
  синхронно (как у `SingleRealDeviceEndpointProvider`, который возвращает
  `Task.FromResult`), список готов уже к моменту возврата из конструктора — на этом
  держатся существующие тесты, читающие `AvailableEndpoints` сразу после него, без
  изменений. Новые тесты для сценариев, где провайдер асинхронный (задержка, ошибка,
  отменённый скан), явно ждут `await vm.RefreshEndpointsCommand.Execute()`, а не
  полагаются на синхронное завершение.
- Если перечисление или скан падает (например нет разрешения), `AvailableEndpoints`
  не теряет уже показанные реальные устройства и «Демо» остаётся доступным — ошибка
  идёт только в `EndpointError`, без затирания списка.
- `ConnectionEndpoint` получает поле `IsPaired` и `StatusLabel` («спарено» / «не спарено»
  / пусто для «Демо»).
- Порядок в списке: реальные устройства сверху, «Демо» последним.
- `RefreshEndpointsCommand` — перечитать известные устройства. Текущий выбор
  сохраняется по `Id`, если он остался в списке; иначе выбирается первый реальный
  эндпоинт, а при его отсутствии — «Демо».
- `ScanCommand` — доступна при `IsDiscoverySupported` (= `provider.SupportsDiscovery`).
  Подписывается на `Discover()`, добавляет новые эндпоинты в `AvailableEndpoints`
  (по `Id`, без дублей), выставляет `IsScanning` на время скана. Повторный вызов во
  время скана останавливает его (dispose подписки).
- `ConnectAsync` — если выбранный эндпоинт `Kind == RealDevice && !IsPaired`, сначала
  `provider.PairAsync(Id)`; при `false` — сообщение об ошибке и выход без создания
  сессии. Дальше существующий путь без изменений.
- `EndpointError` (string?) — текст последней ошибки перечисления/скана/спаривания
  (например «Нет разрешения на Bluetooth»). Отдельно от `LastError`, который зеркалит
  ошибку сессии.

Все новые команды создаются через `Track(...)` — как остальные в этом ViewModel, иначе
непойманное исключение команды роняет процесс.

### Модалка подключения (`MainView.axaml`)

`ComboBox` заменяется на `ListBox` с `SelectedItem="{Binding SelectedEndpoint}"`:
строка = имя устройства + приглушённый `StatusLabel` справа. Над списком — строка
состояния и кнопка «Поиск» (`IsVisible="{Binding IsDiscoverySupported}"`, надпись
меняется на «Стоп» при `IsScanning`). Под списком — блок ошибки
(`IsVisible` по `EndpointError`) и существующая кнопка «Подключить». Высота списка
ограничена (`MaxHeight`), список скроллится.

На Desktop/Browser видимых изменений нет: провайдер по умолчанию отдаёт один эндпоинт,
кнопка «Поиск» скрыта.

## Сборка и установка

- `ApplicationId` меняется с `com.CompanyName.ArctZ` на `com.arctz.app`, `Label`
  активности — на `ArctZ` (сейчас `ArctZ.Android`).
- Установка: `dotnet build ArctZ.Android/ArctZ.Android.csproj -t:Install` при
  подключённом по USB телефоне с включённой отладкой. Логи — `adb logcat`.
- Подпись Release не настраивается: для установки на своё устройство достаточно
  Debug-сборки с debug-keystore.

## Тесты

`ArctZ.Tests`:

- `LineAssemblerTests` — сборка строки из нескольких чтений, `\r\n`, несколько строк в
  одном буфере, пустые строки, превышение лимита длины.
- `ConnectionViewModelTests` — новые: список строится из провайдера; неспаренное
  устройство спаривается перед подключением; отказ спаривания не создаёт сессию;
  скан добавляет найденные устройства без дублей; ошибка провайдера попадает в
  `EndpointError`. Существующие тесты продолжают работать на
  `SingleRealDeviceEndpointProvider` (2 эндпоинта: «Устройство» + «Демо»).

Android-специфичный код (`AndroidBluetoothTransport`, провайдер, разрешения) юнит-тестами
не покрывается — недоступен из тестового проекта; проверяется на устройстве.

## Проверка на устройстве

По правилу проекта (CLAUDE.md, «Тестирование UI»): собрать, установить на телефон,
запустить, попросить пользователя проверить и задать отдельный вопрос через
`AskUserQuestion` по каждому пункту:

1. Приложение ставится и запускается, экран подключения виден.
2. Запрос разрешений Bluetooth появляется и после согласия список спаренных устройств
   заполняется.
3. Кнопка «Поиск» находит устройства в эфире, статус «не спарено» виден.
4. Выбор неспаренного устройства запускает системное спаривание.
5. Режим «Демо» работает как на Desktop (движение, программа, лог G-code).
6. Попытка подключения к недоступному устройству даёт понятную ошибку, а не зависание.
