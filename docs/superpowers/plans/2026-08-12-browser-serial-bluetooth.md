# Bluetooth в браузере через Web Serial API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Заменить `NotSupportedDeviceTransport` в `ArctZ.Browser` на реальный транспорт поверх Web Serial API (`navigator.serial`), который обращается к тому же виртуальному COM-порту, что уже спарен по Bluetooth Classic SPP с FluidNC — так же, как `DesktopSerialTransport` делает на Desktop через `System.IO.Ports.SerialPort`.

**Architecture:** Новый JS-модуль `wwwroot/serial.js` реализует пикер/чтение/запись поверх `navigator.serial`. `SerialInterop.cs` — тонкий мост `[JSImport]`/`[JSExport]` (net10-browser WASM interop, встроенный, без Blazor). `BrowserSerialTransport.cs` реализует `IDeviceTransport` поверх этого моста и подключается в DI вместо `NotSupportedDeviceTransport`. Новый флаг `IDeviceTransport.IsSupported` (default `true` через C# default interface member) даёт `ConnectionViewModel` знать, что реальное устройство недоступно в этом браузере, и сразу — при старте, а не только при попытке подключения — скрыть пункт «Устройство» и показать баннер.

**Tech Stack:** .NET 10 (`net10.0-browser` TFM), `System.Runtime.InteropServices.JavaScript` (`[JSImport]`/`[JSExport]`), Web Serial API, Avalonia (Browser head), CommunityToolkit.Mvvm/ReactiveUI (`ConnectionViewModel`), xUnit (`ArctZ.Tests`).

## Global Constraints

- Область действия — только `ArctZ.Browser`. Desktop/Android/iOS не меняются (кроме того, что `IDeviceTransport` получает новый default-член, который они наследуют без изменений).
- Baud rate 115200, разделитель строк `\n` — как на Desktop (`DesktopSerialTransport.cs:44`), протокол FluidNC не меняется.
- `deviceId`, передаваемый в `ConnectAsync`, в браузере не используется (выбор порта идёт через нативный пикер/сохранённые permissions, а не по строковому идентификатору).
- Реконнект без пикера: `DeviceSession` вызывает `ConnectAsync` повторно без нового клика пользователя — реализация обязана переиспользовать уже выданный порт (`getPorts()`), а не звать `requestPort()` при каждой попытке.
- Единственный допустимый способ финальной проверки — реальный запуск в браузере (`dotnet run --project ArctZ.Browser/ArctZ.Browser.csproj`) с `AskUserQuestion` по каждой проверяемой функции, согласно правилу «Тестирование UI» в `CLAUDE.md`.

---

## Task 1: `IDeviceTransport.IsSupported` + `ConnectionViewModel` фильтрует список endpoint'ов

**Files:**
- Modify: `ArctZ/Services/Device/IDeviceTransport.cs`
- Modify: `ArctZ/ViewModels/ConnectionViewModel.cs:96-100` (поле `AvailableEndpoints`), `ArctZ/ViewModels/ConnectionViewModel.cs:120` (конструктор)
- Modify: `ArctZ.Tests/Services/Device/FakeDeviceTransport.cs`
- Test: `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs`

**Interfaces:**
- Produces: `IDeviceTransport.IsSupported` (`bool`, default interface member `=> true`) — платформенные реализации, которым не нужно "неподдерживается", ничего не меняют.
- Produces: `ConnectionViewModel.IsRealDeviceUnsupported` (`bool`, `get`-only) — используется Task 2 в XAML-биндинге.
- Consumes: существующий `ConnectionViewModel` конструктор `(IDeviceTransport realTransport, Func<IDeviceTransport> createDemoTransport, IDeviceSessionFactory sessionFactory)` — не меняется по сигнатуре.

- [ ] **Step 1: Написать падающие тесты**

В `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs` заменить существующий тест `Constructor_DefaultsToFirstEndpointAndListsRealAndDemo` (строки 16-25) на версию с дополнительной проверкой и добавить новый тест сразу после него:

```csharp
[Fact]
public void Constructor_RealTransportSupported_ListsRealAndDemoAndDoesNotFlagUnsupported()
{
    var vm = CreateVm(new FakeDeviceTransport());

    Assert.Equal(2, vm.AvailableEndpoints.Count);
    Assert.Contains(vm.AvailableEndpoints, e => e.Kind == ConnectionEndpointKind.RealDevice);
    Assert.Contains(vm.AvailableEndpoints, e => e.Kind == ConnectionEndpointKind.Demo);
    Assert.Equal(ConnectionEndpointKind.RealDevice, vm.SelectedEndpoint!.Kind);
    Assert.False(vm.IsRealDeviceUnsupported);
}

[Fact]
public void Constructor_RealTransportUnsupported_OnlyListsDemoAndFlagsUnsupported()
{
    var realTransport = new FakeDeviceTransport { IsSupported = false };
    var vm = CreateVm(realTransport);

    Assert.Single(vm.AvailableEndpoints);
    Assert.Equal(ConnectionEndpointKind.Demo, vm.AvailableEndpoints[0].Kind);
    Assert.Equal(ConnectionEndpointKind.Demo, vm.SelectedEndpoint!.Kind);
    Assert.True(vm.IsRealDeviceUnsupported);
}
```

(Первый тест — переименованная версия старого `Constructor_DefaultsToFirstEndpointAndListsRealAndDemo`; удалить старый метод целиком, чтобы не дублировать проверку.)

- [ ] **Step 2: Запустить тесты и убедиться, что они падают**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ConnectionViewModelTests"`
Expected: FAIL — `FakeDeviceTransport` не содержит `IsSupported`, `ConnectionViewModel` не содержит `IsRealDeviceUnsupported` (ошибки компиляции).

- [ ] **Step 3: Добавить `IsSupported` в `IDeviceTransport`**

В `ArctZ/Services/Device/IDeviceTransport.cs`, сразу после `bool IsConnected { get; }` (строка 10):

```csharp
    /// <summary>
    /// Whether this transport can actually run in the current environment (e.g. false in a
    /// browser without Web Serial support). Platforms that are always usable don't override this.
    /// </summary>
    bool IsSupported => true;
```

- [ ] **Step 4: Добавить `IsSupported` в `FakeDeviceTransport`**

В `ArctZ.Tests/Services/Device/FakeDeviceTransport.cs`, сразу после `public bool IsConnected { get; private set; }` (строка 13):

```csharp
    public bool IsSupported { get; set; } = true;
```

- [ ] **Step 5: Обновить `ConnectionViewModel`**

В `ArctZ/ViewModels/ConnectionViewModel.cs` заменить блок (строки 96-100):

```csharp
    public ObservableCollection<ConnectionEndpoint> AvailableEndpoints { get; } = new()
    {
        new ConnectionEndpoint("real", "Устройство", ConnectionEndpointKind.RealDevice),
        new ConnectionEndpoint("demo", "Демо", ConnectionEndpointKind.Demo),
    };
```

на:

```csharp
    public ObservableCollection<ConnectionEndpoint> AvailableEndpoints { get; } = new();

    public bool IsRealDeviceUnsupported => !_realTransport.IsSupported;
```

и в конструкторе заменить строку `SelectedEndpoint = AvailableEndpoints[0];` (строка 120) на:

```csharp
        if (_realTransport.IsSupported)
        {
            AvailableEndpoints.Add(new ConnectionEndpoint("real", "Устройство", ConnectionEndpointKind.RealDevice));
        }

        AvailableEndpoints.Add(new ConnectionEndpoint("demo", "Демо", ConnectionEndpointKind.Demo));
        SelectedEndpoint = AvailableEndpoints[0];
```

- [ ] **Step 6: Запустить тесты и убедиться, что они проходят**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ConnectionViewModelTests"`
Expected: PASS, все тесты класса зелёные (включая ранее существовавшие — новый порядок добавления в `AvailableEndpoints` не должен ломать `ConnectCommand_RealDeviceSelected_ConnectsUsingRealTransport` и другие, т.к. `Id`/`Kind` элементов не изменились).

- [ ] **Step 7: Прогнать весь тестовый проект**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS, без регрессий в других классах.

- [ ] **Step 8: Commit**

```bash
git add ArctZ/Services/Device/IDeviceTransport.cs ArctZ/ViewModels/ConnectionViewModel.cs ArctZ.Tests/Services/Device/FakeDeviceTransport.cs ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs
git commit -m "feat: add IDeviceTransport.IsSupported and hide real endpoint when unsupported"
```

---

## Task 2: Баннер «браузер не поддерживается» в модалке подключения

**Files:**
- Modify: `ArctZ/Views/MainView.axaml:575-600` (модалка подключения)

**Interfaces:**
- Consumes: `ConnectionViewModel.IsRealDeviceUnsupported` (из Task 1).

- [ ] **Step 1: Добавить баннер в XAML**

В `ArctZ/Views/MainView.axaml`, внутри `<StackPanel Spacing="14">` модалки подключения (начинается на строке 580), сразу после `<TextBlock Classes="section-heading" Text="ПОДКЛЮЧЕНИЕ" />` (строка 581) и перед блоком статуса (строка 582), добавить:

```xml
                    <Border IsVisible="{Binding IsRealDeviceUnsupported}"
                            Background="{StaticResource HudWarningDimBrush}"
                            BorderBrush="{StaticResource HudWarningBrush}" BorderThickness="1" Padding="10,6">
                        <TextBlock TextWrapping="Wrap" Foreground="{StaticResource HudWarningBrush}"
                                   Text="Web Serial API не поддерживается этим браузером — используйте Chrome или Edge. Доступен только режим «Демо»." />
                    </Border>
```

(Стили `HudWarningDimBrush`/`HudWarningBrush` уже используются для баннера ошибки в `ArctZ/Views/ConnectionView.axaml:21-24` — переиспользуем ту же палитру.)

Пункт «Устройство» в `ComboBox` ничего дополнительно фильтровать не нужно — `AvailableEndpoints` уже не содержит его при `IsRealDeviceUnsupported == true` (Task 1).

- [ ] **Step 2: Собрать core-проект, чтобы убедиться, что XAML компилируется**

Run: `dotnet build ArctZ/ArctZ.csproj`
Expected: Build succeeded, без ошибок компилируемых биндингов (compiled bindings упадут на этапе сборки, если имя свойства не совпадает).

- [ ] **Step 3: Commit**

```bash
git add "ArctZ/Views/MainView.axaml"
git commit -m "feat: show unsupported-browser banner in connection modal"
```

(Визуальная проверка баннера — в Task 7, вместе с остальными live-проверками в браузере.)

---

## Task 3: `serial.js` — JS-модуль поверх Web Serial API

**Files:**
- Create: `ArctZ.Browser/wwwroot/serial.js`

**Interfaces:**
- Produces: экспортируемые функции модуля `serial.js` — `isSupported()`, `requestPort()`, `reopenSavedPort()`, `write(bytes)`, `closePort()` — вызываются из `SerialInterop.cs` (Task 4) через `[JSImport(..., "serial.js")]`.
- Consumes: `globalThis.__arctzSerialExports.ArctZ.Browser.SerialInterop.OnLineReceived(line)` / `.OnDisconnected()` — статические `[JSExport]`-методы, которые появятся в Task 4 и будут проброшены в `globalThis` в Task 6 (`main.js`). До Task 6 эти вызовы будут падать в рантайме браузера — это ожидаемо, весь стек проверяется целиком в Task 7.

- [ ] **Step 1: Создать `ArctZ.Browser/wwwroot/serial.js`**

```javascript
let port = null;
let writer = null;
let readLoopAbort = false;

function csharpExports() {
    return globalThis.__arctzSerialExports.ArctZ.Browser.SerialInterop;
}

export function isSupported() {
    return "serial" in navigator;
}

async function openAndStartReading(selectedPort) {
    await selectedPort.open({ baudRate: 115200 });
    port = selectedPort;
    writer = port.writable.getWriter();
    readLoopAbort = false;
    startReadLoop();
}

export async function requestPort() {
    const selected = await navigator.serial.requestPort();
    await openAndStartReading(selected);
    return true;
}

export async function reopenSavedPort() {
    const ports = await navigator.serial.getPorts();
    if (ports.length === 0) {
        return false;
    }

    await openAndStartReading(ports[0]);
    return true;
}

export async function write(bytes) {
    if (!writer) {
        return;
    }

    await writer.write(new Uint8Array(bytes));
}

export async function closePort() {
    readLoopAbort = true;

    if (writer) {
        try { await writer.close(); } catch { }
        writer = null;
    }

    if (port) {
        try { await port.close(); } catch { }
        port = null;
    }
}

async function startReadLoop() {
    const activePort = port;
    const decoder = new TextDecoderStream();
    activePort.readable.pipeTo(decoder.writable).catch(() => { });
    const reader = decoder.readable.getReader();
    let buffer = "";

    try {
        while (!readLoopAbort) {
            const { value, done } = await reader.read();
            if (done) {
                break;
            }

            buffer += value;
            let newlineIndex;
            while ((newlineIndex = buffer.indexOf("\n")) >= 0) {
                const line = buffer.slice(0, newlineIndex).replace(/\r$/, "");
                buffer = buffer.slice(newlineIndex + 1);
                csharpExports().OnLineReceived(line);
            }
        }
    } catch {
        // Falls through to the disconnect notification below — a read error
        // (cable/BT drop) and a clean stream close are handled the same way.
    }

    if (!readLoopAbort) {
        csharpExports().OnDisconnected();
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add ArctZ.Browser/wwwroot/serial.js
git commit -m "feat: add Web Serial API JS module for browser Bluetooth transport"
```

---

## Task 4: `SerialInterop.cs` — мост `[JSImport]`/`[JSExport]`

**Files:**
- Create: `ArctZ.Browser/SerialInterop.cs`

**Interfaces:**
- Consumes: функции `serial.js` (Task 3) — `isSupported`, `requestPort`, `reopenSavedPort`, `write`, `closePort`.
- Produces: `SerialInterop.InitializeAsync()` (`Task`) — должна быть awaited в `Program.cs` (Task 6) до первого использования любого другого члена класса. `SerialInterop.IsSupported()` (`bool`), `SerialInterop.RequestPortAsync()` (`Task<bool>`), `SerialInterop.ReopenSavedPortAsync()` (`Task<bool>`), `SerialInterop.WriteAsync(byte[])` (`Task`), `SerialInterop.ClosePortAsync()` (`Task`) — используются `BrowserSerialTransport` (Task 5).
- Produces: статические `[JSExport]` методы `OnLineReceived(string)` / `OnDisconnected()`, вызываемые из `serial.js`; делегируют в `BrowserSerialTransport.RaiseLineReceived`/`RaiseDisconnected` (Task 5 их определит — на момент написания этого файла они ещё не существуют, компиляция всего проекта пройдёт только после Task 5; это ожидаемо, см. шаг 2).

- [ ] **Step 1: Создать `ArctZ.Browser/SerialInterop.cs`**

```csharp
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace ArctZ.Browser;

/// <summary>
/// Thin JS-interop bridge to wwwroot/serial.js (Web Serial API). ConnectAsync semantics
/// (reuse-saved-port-first, request-port-as-fallback) live in BrowserSerialTransport, not here.
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class SerialInterop
{
    private const string ModuleName = "serial.js";

    /// <summary>Must be awaited once, before any other member of this class is used.</summary>
    public static async Task InitializeAsync()
    {
        await JSHost.ImportAsync(ModuleName, "./serial.js");
    }

    [JSImport("isSupported", ModuleName)]
    internal static partial bool IsSupported();

    [JSImport("requestPort", ModuleName)]
    internal static partial Task<bool> RequestPortAsync();

    [JSImport("reopenSavedPort", ModuleName)]
    internal static partial Task<bool> ReopenSavedPortAsync();

    [JSImport("write", ModuleName)]
    internal static partial Task WriteAsync(byte[] bytes);

    [JSImport("closePort", ModuleName)]
    internal static partial Task ClosePortAsync();

    [JSExport]
    internal static void OnLineReceived(string line) => BrowserSerialTransport.RaiseLineReceived(line);

    [JSExport]
    internal static void OnDisconnected() => BrowserSerialTransport.RaiseDisconnected();
}
```

- [ ] **Step 2: Ничего не собирать отдельно**

Этот файл ссылается на `BrowserSerialTransport`, которого ещё нет — `ArctZ.Browser` не соберётся до Task 5. Это ожидаемо и не нарушает пошаговость: следующий таск создаёт недостающий тип. Не коммитить отдельно проверку сборки на этом шаге.

- [ ] **Step 3: Commit**

```bash
git add ArctZ.Browser/SerialInterop.cs
git commit -m "feat: add SerialInterop JS bridge for Web Serial API"
```

---

## Task 5: `BrowserSerialTransport.cs` — реализация `IDeviceTransport`

**Files:**
- Create: `ArctZ.Browser/BrowserSerialTransport.cs`
- Delete: `ArctZ.Browser/NotSupportedDeviceTransport.cs` (заменяется этим классом; проверено, что используется только в `ArctZ.Browser/Program.cs`, который правится в Task 6)

**Interfaces:**
- Consumes: `SerialInterop` (Task 4) — все члены.
- Consumes: `IDeviceTransport` (`ArctZ/Services/Device/IDeviceTransport.cs`) — интерфейс, который реализует этот класс.
- Produces: `BrowserSerialTransport` (публичный класс, реализует `IDeviceTransport`), `BrowserSerialTransport.RaiseLineReceived(string)` / `RaiseDisconnected()` (`internal static`, вызываются из `SerialInterop`'s `[JSExport]` методов). Используется в Task 6 (`Program.cs` регистрация DI).

- [ ] **Step 1: Создать `ArctZ.Browser/BrowserSerialTransport.cs`**

```csharp
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Browser;

/// <summary>
/// Real transport for the browser head: navigator.serial reaches the same OS-level
/// virtual COM port that a paired Bluetooth Classic SPP FluidNC device exposes on
/// Desktop (see DesktopSerialTransport) - just through JS interop instead of
/// System.IO.Ports. `deviceId` passed to ConnectAsync is unused: Web Serial has no
/// stable string port identifier, selection happens through the browser's picker
/// and its remembered per-origin permissions instead.
/// </summary>
public sealed class BrowserSerialTransport : IDeviceTransport
{
    private static BrowserSerialTransport? _active;

    public BrowserSerialTransport()
    {
        _active = this;
        IsSupported = SerialInterop.IsSupported();
    }

    public bool IsSupported { get; }

    public bool IsConnected { get; private set; }

    public event Action<string>? LineReceived;

    public event Action? Disconnected;

    public async Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        // DeviceSession's reconnect loop calls ConnectAsync again without a user
        // gesture, so re-showing the picker would silently fail (browsers require
        // a gesture for requestPort()). Reusing the already-granted port via
        // reopenSavedPort() covers both the first connect after a previous grant
        // and every automatic reconnect after that.
        var reopened = await SerialInterop.ReopenSavedPortAsync();
        if (!reopened)
        {
            await SerialInterop.RequestPortAsync();
        }

        IsConnected = true;
    }

    public async Task DisconnectAsync()
    {
        await SerialInterop.ClosePortAsync();
        IsConnected = false;
    }

    public Task SendLineAsync(string line, CancellationToken cancellationToken = default) =>
        SerialInterop.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"));

    public Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default) =>
        SerialInterop.WriteAsync(new[] { value });

    internal static void RaiseLineReceived(string line) => _active?.OnLineReceived(line);

    internal static void RaiseDisconnected() => _active?.OnDisconnected();

    private void OnLineReceived(string line) => LineReceived?.Invoke(line);

    private void OnDisconnected()
    {
        IsConnected = false;
        Disconnected?.Invoke();
    }
}
```

- [ ] **Step 2: Удалить `ArctZ.Browser/NotSupportedDeviceTransport.cs`**

Файл удаляется целиком — заменяется классом выше. Он ещё используется в `ArctZ.Browser/Program.cs`, который правится в следующем таске, так что между Task 5 и Task 6 `ArctZ.Browser` временно не соберётся — это ожидаемо, оба таска коммитятся раздельно, но сборка проверяется только в конце Task 6.

- [ ] **Step 3: Commit**

```bash
git add ArctZ.Browser/BrowserSerialTransport.cs
git rm ArctZ.Browser/NotSupportedDeviceTransport.cs
git commit -m "feat: add BrowserSerialTransport implementing IDeviceTransport over Web Serial"
```

---

## Task 6: Подключить interop и транспорт в `ArctZ.Browser`

**Files:**
- Modify: `ArctZ.Browser/Program.cs`
- Modify: `ArctZ.Browser/wwwroot/main.js`

**Interfaces:**
- Consumes: `SerialInterop.InitializeAsync()` (Task 4), `BrowserSerialTransport` (Task 5).

- [ ] **Step 1: Обновить `ArctZ.Browser/Program.cs`**

Заменить содержимое файла (было: `services.AddSingleton<IDeviceTransport, NotSupportedDeviceTransport>();`, без await interop-инициализации) на:

```csharp
using ArctZ;
using ArctZ.Browser;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using Avalonia;
using Avalonia.Browser;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI.Avalonia;
using System.Runtime.Versioning;
using System.Threading.Tasks;

internal sealed partial class Program
{
    private static async Task Main(string[] args)
    {
        await SerialInterop.InitializeAsync();

        var services = new ServiceCollection();
        services.AddArctZCore();
        services.AddSingleton<IDeviceTransport, BrowserSerialTransport>();
        services.AddSingleton<IProgramStorage, InMemoryProgramStorage>();
        App.Services = services.BuildServiceProvider();

        await BuildAvaloniaApp()
            .WithInterFont()
            .UseReactiveUI(b => b.WithAvalonia())
#if DEBUG
            .WithDeveloperTools()
#endif
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
```

(Единственные изменения по сути: `await SerialInterop.InitializeAsync();` первой строкой — модуль `serial.js` должен быть импортирован до того, как DI создаст `BrowserSerialTransport` и вызовет `SerialInterop.IsSupported()` в его конструкторе; `NotSupportedDeviceTransport` → `BrowserSerialTransport`; `return BuildAvaloniaApp()...` → `await BuildAvaloniaApp()...`, т.к. метод больше не может просто вернуть `Task` последней строкой, если после него ничего не осталось — на деле оба варианта компилируются, здесь `await` для единообразия с новым `await` выше.)

- [ ] **Step 2: Обновить `ArctZ.Browser/wwwroot/main.js`**

Заменить содержимое файла на:

```javascript
import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();

// serial.js (imported from C# via JSHost.ImportAsync in SerialInterop) calls back
// into .NET through this global — it has no other way to reach the assembly's
// [JSExport] methods since it isn't itself loaded through the dotnet runtime's
// module resolution.
globalThis.__arctzSerialExports = await dotnetRuntime.getAssemblyExports(config.mainAssemblyName);

await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
```

- [ ] **Step 3: Собрать `ArctZ.Browser`**

Run: `dotnet build ArctZ.Browser/ArctZ.Browser.csproj`
Expected: Build succeeded. Если JSImport/JSExport source generator ругается на сигнатуры — проверить, что все `[JSImport]`/`[JSExport]` методы в `SerialInterop.cs` `static partial`, а класс `static partial`.

- [ ] **Step 4: Прогнать полный тестовый набор (регрессия)**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS — `ArctZ.Browser` не участвует в `ArctZ.Tests`, это просто финальная проверка, что ядро не сломано.

- [ ] **Step 5: Commit**

```bash
git add ArctZ.Browser/Program.cs "ArctZ.Browser/wwwroot/main.js"
git commit -m "feat: wire BrowserSerialTransport and Web Serial JS interop into ArctZ.Browser"
```

---

## Task 7: Живая проверка в браузере

**Files:** нет изменений кода — только запуск и проверка согласно правилу «Тестирование UI» в `CLAUDE.md`.

- [ ] **Step 1: Собрать и запустить**

Run: `dotnet run --project ArctZ.Browser/ArctZ.Browser.csproj`

Дождаться, пока приложение реально поднимется (не просто соберётся) и откроется в браузере (Chrome или Edge — Web Serial недоступен в Firefox/Safari).

- [ ] **Step 2: Попросить пользователя проверить функции**

Попросить пользователя самостоятельно проверить в запущенном приложении (Chrome/Edge, с реальным FluidNC-устройством, уже спаренным по Bluetooth на уровне ОС так, чтобы для него существовал виртуальный COM-порт):

1. Модалка подключения открыта при старте, пункт «Устройство» присутствует и не заблокирован (в поддерживаемом браузере баннер про Web Serial не показывается).
2. Клик «Подключить» с endpoint'ом «Устройство» показывает нативный браузерный пикер выбора порта.
3. После выбора порта в пикере устройство подключается (модалка закрывается, статус — «Подключено»).
4. G-code/джойстик-команды реально уходят на устройство (движение джойстика/панели откликается на физическом устройстве).
5. Разрыв связи (например, физически выключить/отключить BT на устройстве) переводит статус в `Reconnecting`/`Disconnected`, модалка подключения появляется снова, и повторное автоматическое подключение (или повторный клик «Подключить») проходит **без нового показа пикера** (порт переиспользуется через `getPorts()`).
6. Явное «Отключить», затем повторное «Подключить» с «Устройство» — тоже без нового пикера (тот же спаренный порт).
7. Если есть доступ к Firefox или Safari: при открытии приложения в них сразу виден баннер «Web Serial API не поддерживается…», а пункт «Устройство» отсутствует в списке (доступно только «Демо»).

- [ ] **Step 3: Задать вопросы через `AskUserQuestion`**

По каждому пункту 1-7 из Step 2 — отдельный вопрос через `AskUserQuestion` (не один общий «всё ок?»), подтверждающий, что конкретное поведение работает как задумано. При отрицательном ответе на любой пункт — вернуться к соответствующему таску (1-6) и исправить, затем повторить Step 1-3.

- [ ] **Step 4: Финальный коммит (если по итогам живой проверки были правки)**

Если Step 3 выявил и потребовал правок — закоммитить их отдельным коммитом с понятным сообщением о том, что именно исправлено по результатам живой проверки.
