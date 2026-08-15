# Автоподключение к FluidNC по имени + заставка — дизайн

Дата: 2026-08-15

## Цель

Сейчас пользователь всегда подключается вручную: выбирает эндпоинт из списка и жмёт
«Подключить» в модалке подключения ([ConnectionViewModel.cs](../../../ArctZ/ViewModels/ConnectionViewModel.cs),
[MainView.axaml:575-630](../../../ArctZ/Views/MainView.axaml#L575-L630)). Задача:

1. При старте приложения (и после потери связи) искать устройство с именем,
   начинающимся на «FluidNC» (без учёта регистра), и подключаться к нему автоматически —
   без участия пользователя.
2. Пока идёт поиск/подключение/переподключение — показывать полноэкранную заставку
   (спиннер + текстовый статус) вместо текущей модалки со списком устройств.
3. Автоматически восстанавливать соединение при потере связи.

Платформы: **Android и Desktop** (Browser не входит — Web Serial API требует явного
пользовательского жеста для выбора порта и не отдаёт имена устройств до этого жеста,
поэтому автоподключение там технически невозможно; Browser продолжает работать как
сейчас, только через ручную модалку).

Явное решение пользователя, зафиксированное в брейнсторминге:
- Сопоставление имени: `DisplayName.StartsWith("FluidNC", OrdinalIgnoreCase)`.
- Ретраи: 5 попыток, паузы между ними 1/2/4/8/8 секунд, затем — сдаться и показать
  обычную ручную модалку.
- Заставка минимальная: спиннер + текст, без логотипа и списка шагов.
- Автоподключение запускается сразу при старте приложения.
- Если связь пропадает во время воспроизведения программы — существующий механизм
  «остановить и сообщить» (`PlaybackState.Faulted`) не меняется, только соответствует
  увеличенному числу попыток восстановления.

## Часть 1 — Быстрый внутренний реконнект остаётся без изменений

`DeviceSession.OnTransportDisconnected` ([DeviceSession.cs:129-185](../../../ArctZ/Services/Device/DeviceSession.cs#L129-L185))
уже реализует цикл переподключения к тому же `deviceId` через `IReconnectPolicy`, по умолчанию
собранный `DeviceSessionFactory` как `FixedDelayReconnectPolicy(3, 200мс)`
([DeviceSessionFactory.cs:38-41](../../../ArctZ/Services/Device/DeviceSessionFactory.cs#L38-L41)).

**Изначально в этой спеке планировалось заменить эту политику на 5×(1/2/4/8/8с) — отменено.**
`ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs` (`LinkLoss_DuringPlayback_...`,
`PlayWhileReconnecting_IsIgnored_...`, ещё один на строке 385) ждут перехода в `PlaybackState.Faulted`
не дольше `TimeSpan.FromSeconds(3)` — именно столько занимает исчерпание текущих 3×200мс. При
задержках 1/2/4/8/8с (суммарно 23с) эти тесты падали бы по таймауту. Правило проекта
«Verify signal timing against source before designing around it» — сначала перечитать код
эмиттера, а не переспроектировать вслепую — здесь применилось буквально.

Итоговое решение: быстрый внутренний реконнект `DeviceSession` остаётся ровно таким, какой
есть (3 попытки, 200мс) — он ловит короткие обрывы связи бесшумно и быстро. График
5×(1/2/4/8/8с), который пользователь утвердил в брейнсторминге, применяется целиком в Части 2 —
к новому оркестратору поиска и подключения по имени, который подхватывает восстановление связи
ровно там, где заканчивается быстрый внутренний реконнект (см. «Перезапуск после исчерпания» в
Части 2). Итоговое поведение для пользователя не меняется: связь либо восстанавливается быстро
(короткий обрыв), либо — если устройство не отвечает дольше — идёт более терпеливый поиск с
полным пересканированием на протяжении ~23 с, и только затем ручная модалка.

Поведение при исчерпании быстрого реконнекта не меняется: `ConnectionState.Disconnected` +
`LastError = "Reconnect failed after {N} attempts"`. Именно этот переход уже ловит
`ProgramViewModel.ApplySessionConnectionState` ([ProgramViewModel.cs:879-896](../../../ArctZ/ViewModels/ProgramViewModel.cs#L879-L896))
и переводит выполняющуюся программу в `Faulted` — это и есть требуемое «остановить и
сообщить», менять не нужно. Тот же переход теперь дополнительно запускает оркестратор
Части 2 (в `ConnectionViewModel`, не в `DeviceSession` — `DeviceSession` не знает о
поиске по имени).

`ExponentialBackoffReconnectPolicy` (новый класс, нужен для Части 2):

```csharp
public sealed class ExponentialBackoffReconnectPolicy : IReconnectPolicy
{
    public static readonly IReadOnlyList<TimeSpan> DefaultDelays = new[]
    {
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(8),
    };

    public ExponentialBackoffReconnectPolicy(IReadOnlyList<TimeSpan> delays) { ... }

    public int MaxAttempts => _delays.Count;

    public Task WaitBeforeRetryAsync(int attemptNumber, CancellationToken ct = default) =>
        Task.Delay(_delays[attemptNumber - 1], ct);
}
```

Не регистрируется в DI как замена `FixedDelayReconnectPolicy` — используется только как default
для нового параметра конструктора `ConnectionViewModel` (Часть 2).

## Часть 2 — Оркестратор автоподключения по имени (новое)

Новая логика в `ConnectionViewModel`, но **не запускается из конструктора** — иначе
~50 существующих тестов, создающих VM напрямую (`new ConnectionViewModel(...)`), получат
незапрошенную фоновую активность (сеть/BT-разрешения) при каждом создании VM. Вместо этого:

```csharp
public async Task AutoConnectAsync(CancellationToken ct = default)
```

вызывается один раз явно из `App.axaml.cs` → `OnFrameworkInitializationCompleted`, как
fire-and-forget (`_ = viewModel.Connection.AutoConnectAsync();`) после создания `MainWindow`/`MainView`.

### Зависимости конструктора

Новый параметр `IReconnectPolicy autoConnectRetryPolicy` — отдельный экземпляр той же
`ExponentialBackoffReconnectPolicy` (тот же график 1/2/4/8/8с, DI регистрирует его как
именованный/второй singleton через фабрику в `AddArctZCore()`). Раздельный от
`DeviceSession`'ского — это про разные события (поиск+подключение против переподключения
к известному id), но использует общий тип/график, чтобы не плодить сущности. В тестах
подставляется быстрый `FixedDelayReconnectPolicy(5, TimeSpan.FromMilliseconds(1))`, как уже
делает `DeviceSessionReconnectTests`.

### Алгоритм

```
if (IsRealDeviceUnsupported) return; // Browser без Web Serial — сразу ручной режим

AutoConnectPhase = Searching;
for (attempt = 1..autoConnectRetryPolicy.MaxAttempts):
    if (cancelled) return;
    AutoConnectAttempt = attempt;

    endpoint = await FindFluidNcEndpointAsync(ct);   // см. ниже
    if (endpoint is not null):
        SelectedEndpoint = endpoint;
        AutoConnectPhase = Connecting;
        await ConnectAsync();                        // существующий приватный метод
        if (Session?.ConnectionState == Connected):
            AutoConnectPhase = Idle;
            return;

    if (attempt < MaxAttempts):
        AutoConnectPhase = WaitingRetry;
        await autoConnectRetryPolicy.WaitBeforeRetryAsync(attempt, ct);

AutoConnectPhase = GivenUp;
EndpointError ??= "Устройство FluidNC не найдено.";
```

`FindFluidNcEndpointAsync`:
1. `await RefreshEndpointsAsync()` (существующий приватный метод — уже умеет мержить
   список, сохранять выбор, писать `EndpointError` при сбое).
2. Если среди `AvailableEndpoints` есть `Kind == RealDevice` c `FluidNcDeviceName.Matches(DisplayName)`
   — вернуть первый такой.
3. Иначе, если `IsDiscoverySupported` — подписаться на `_endpointProvider.Discover()`,
   так же добавляя найденные в `AvailableEndpoints` (переиспользуя `OnDeviceDiscovered`),
   но с ранним выходом по первому совпадению по имени и общим ограничением по времени
   скана (константа, например 10 c — скан на Android и так сам завершается по
   `ActionDiscoveryFinished`, ограничение нужно как страховка от зависания). Вернуть первое
   совпадение или `null`, если скан закончился/истёк без совпадений.

### Сопоставление имени (отдельный тестируемый юнит)

`ArctZ/Services/Device/FluidNcDeviceName.cs`:

```csharp
public static class FluidNcDeviceName
{
    public static bool Matches(string? name) =>
        name is not null && name.StartsWith("FluidNC", StringComparison.OrdinalIgnoreCase);
}
```

### Отмена и приоритет ручных действий

- `ConnectCommand` и `DisconnectCommand` в начале отменяют текущий `AutoConnectAsync` через
  хранимый `CancellationTokenSource` (аналогично уже существующему `_scanSubscription`
  паттерну) — ручное действие пользователя не должно гоняться наперегонки с фоновым циклом.
- `DisconnectCommand` дополнительно выставляет флаг `_autoConnectSuppressed = true`:
  после явного отключения оркестратор сам не перезапускается. Флаг снимается при следующем
  успешном `ConnectAsync` (ручном или автоматическом).
- Триггер повторного автозапуска после того, как `DeviceSession` исчерпал свои 3 быстрые
  попытки переподключения к тому же `deviceId` (Часть 1, без изменений) и сессия ушла в
  `Disconnected`: подписка на `Session`-переходы в `Disconnected` (та же `Observable.FromEvent` +
  `.Switch()`-идиома, что уже используется для `ConnectionStateChanged`/`AlarmTriggered` в
  конструкторе), которая, если `!_autoConnectSuppressed`, снова вызывает `AutoConnectAsync()` —
  это и есть «восстановить связь», уже включающее пересканирование на случай, если у устройства
  сменился MAC/COM-порт.

## Часть 3 — Заставка (UI)

Новые свойства `ConnectionViewModel`:

```csharp
public bool IsAutoConnectSplashVisible =>
    AutoConnectPhase is AutoConnectPhase.Searching or AutoConnectPhase.Connecting or AutoConnectPhase.WaitingRetry
    || ConnectionState == ConnectionState.Reconnecting;

public string AutoConnectStatusText => ConnectionState == ConnectionState.Reconnecting
    ? "Переподключение…" // быстрый внутренний реконнект DeviceSession — Часть 1, без разбивки на попытки
    : AutoConnectPhase switch
    {
        AutoConnectPhase.Searching => "Поиск FluidNC…",
        AutoConnectPhase.Connecting => "Подключение…",
        AutoConnectPhase.WaitingRetry => $"Попытка {AutoConnectAttempt} из {AutoConnectMaxAttempts} не удалась, повтор…",
        _ => "",
    };
```

`IsConnectionModalVisible` (ручная модалка со списком) меняет смысл:

```csharp
public bool IsConnectionModalVisible =>
    !IsAutoConnectSplashVisible && (Session is null || ConnectionState != ConnectionState.Connected);
```

То есть ручной список показывается только когда заставка не активна: автоподключение
сдалось (`AutoConnectPhase.GivenUp`), либо реальное устройство не поддерживается
платформой (Browser), либо пользователь явно отключился (`_autoConnectSuppressed`, тогда
`AutoConnectPhase` не переходит в активные фазы вовсе).

Симметрично меняется `IsAlarmModalVisible`, чтобы заставка не рисовалась одновременно с
модалкой аварии (тот же класс бага, что уже был исправлен между ручной модалкой и аварией —
см. комментарий у `IsAlarmModalVisible` в текущем коде,
[ConnectionViewModel.cs:81-92](../../../ArctZ/ViewModels/ConnectionViewModel.cs#L81-L92)):

```csharp
public bool IsAlarmModalVisible =>
    LastAlarmCode is not null && !IsConnectionModalVisible && !IsAutoConnectSplashVisible;
```

**Осознанно ломаемый контракт**: сейчас во время `Reconnecting` показывается именно ручная
модалка со списком (тесты `UnsolicitedDisconnect_TransitionsToReconnectingAndShowsModal`,
`ConnectionViewModelTests.cs:349`) — это и есть то, что нужно заменить на заставку по этой
задаче. Эти тесты переписываются на проверку `IsAutoConnectSplashVisible`.

### Разметка (`MainView.axaml`)

Новый `Border` (полноэкранный скрим, тот же `HudScrimBrush`) с `IsVisible="{Binding
Connection.IsAutoConnectSplashVisible}"`, размещённый перед существующей модалкой
подключения (порядок в `Grid` — последний элемент рисуется поверх, поэтому заставка должна
идти позже блока с `IsConnectionModalVisible`, либо оба блока взаимоисключающие по
биндингу и порядок не важен — они физически не видны одновременно благодаря
`IsAutoConnectSplashVisible` в определении `IsConnectionModalVisible`). Содержимое:
`ProgressBar IsIndeterminate="True"` (без внешних зависимостей — встроенный контрол
Avalonia) + `TextBlock Text="{Binding Connection.AutoConnectStatusText}"`, по центру экрана.

## Часть 4 — Desktop: провайдер эндпоинтов с реальными именами

Сейчас `SingleRealDeviceEndpointProvider` (используется Desktop и Browser) отдаёт один
фиктивный эндпоинт `("real", "Устройство", true)` — имени устройства там нет, сопоставление
по «FluidNC» невозможно.

Новый `ArctZ.Desktop/DesktopBluetoothEndpointProvider.cs`, регистрируется в
`ArctZ.Desktop/Program.cs` после `AddArctZCore()` (та же схема, что Android):

- `SupportsDiscovery => false` — поиск/спаривание новых устройств на Desktop делает
  Windows Bluetooth Settings, не приложение; это уже так и есть сейчас.
- `GetKnownEndpointsAsync` — через `System.Management` (`Win32_PnPEntity`, `Win32_SerialPort`
  или парсинг `Name`/`Caption` вида `"Стандартная последовательная связь через Bluetooth
  (COM5)"`/`"FluidNC (COM5)"`) сопоставляет COM-порт со спаренным Bluetooth-устройством и
  его именем. Точный WMI-запрос/парсинг — предмет реализации и проверки на этой машине
  (среда уже содержит спаренный FluidNC по данным памяти проекта); при неудаче парсинга —
  порт всё равно попадает в список с именем порта вместо имени устройства (не роняет список
  целиком), просто не пройдёт `FluidNcDeviceName.Matches` и не будет выбран автоматически.
- `PairAsync` → всегда `true` (спаривание пользователь делает вне приложения, как сейчас).
- `Discover()` → `Observable.Empty` (как в `SingleRealDeviceEndpointProvider`).

Новая зависимость пакета `System.Management` — версия фиксируется в
`Directory.Packages.props`, добавляется в `ArctZ.Desktop.csproj`.

**Не тестируется юнит-тестами** (WMI недоступен из `ArctZ.Tests`, `net10.0`, без Windows-специфики
в чистом виде) — проверяется вручную на устройстве по правилу «Тестирование UI» из CLAUDE.md.

## Затронутые тесты

- `ArctZ.Tests/Services/Device/FixedDelayReconnectPolicyTests.cs` — по аналогии добавляется
  `ExponentialBackoffReconnectPolicyTests` (график задержек, `MaxAttempts`, `DefaultDelays`).
  `DeviceSessionFactory` и `DeviceSession`/`IDeviceSession` не меняются (Часть 1).
- `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs`:
  - `UnsolicitedDisconnect_TransitionsToReconnectingAndShowsModal` и тест на 349-й строке —
    переписать на `IsAutoConnectSplashVisible` вместо `IsConnectionModalVisible`.
  - Новые: `AutoConnectAsync` находит и подключает FluidNC-эндпоинт с первой попытки;
    игнорирует не-FluidNC эндпоинты; повторяет попытки с инжектированной быстрой политикой
    и сдаётся после исчерпания, оставляя `IsConnectionModalVisible == true`; отменяется
    ручным `ConnectCommand`/`DisconnectCommand`; не запускается повторно после явного
    `DisconnectCommand`, пока не будет успешного подключения.
  - Новый файл `FluidNcDeviceNameTests.cs` — регистронезависимость, префиксное
    совпадение, отсутствие совпадения для случайной подстроки.
- Существующие тесты, создающие VM напрямую и не вызывающие `AutoConnectAsync`, не
  затрагиваются — фоновый цикл никогда не стартует без явного вызова.

## Проверка на устройстве

По правилу проекта («Тестирование UI», CLAUDE.md): собрать и запустить Desktop-голову
(`dotnet run --project ArctZ.Desktop`), для Android — попросить пользователя собрать и
установить APK. После запуска — по отдельному вопросу через `AskUserQuestion` на каждый пункт:

1. При старте приложения показывается заставка с текстом поиска, а не список устройств.
2. Приложение находит уже включённый и спаренный FluidNC и подключается без участия
   пользователя.
3. Если FluidNC выключен — после 5 попыток (~23 с) появляется обычная ручная модалка со
   списком устройств.
4. При обрыве связи во время работы показывается заставка «Переподключение…», связь
   восстанавливается сама при возврате устройства в сеть.
5. Явное «Отключить» не запускает автоподключение заново само по себе — нужен ручной
   клик «Подключить» (или перезапуск приложения).
6. Обрыв связи во время выполнения программы останавливает её с сообщением об ошибке
   (без изменений — используется существующий механизм `Faulted`).
