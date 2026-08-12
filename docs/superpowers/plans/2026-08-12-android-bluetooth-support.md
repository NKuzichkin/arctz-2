# Полноценная работа ArctZ на Android — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Заменить заглушку `NotSupportedDeviceTransport` на Android реальным Bluetooth Classic (RFCOMM/SPP) транспортом к FluidNC, дать пользователю выбирать устройство (спаренные + поиск + спаривание из приложения) и довести проект до устанавливаемого по USB APK.

**Architecture:** Новая абстракция `IDeviceEndpointProvider` в ядре (`ArctZ/Services/Device`) отделяет «откуда берутся доступные устройства» от `IDeviceTransport` («как с ними говорить»). `ConnectionViewModel` получает провайдер как четвёртую DI-зависимость и строит список эндпоинтов асинхронно. Desktop/Browser продолжают получать поведение по умолчанию (`SingleRealDeviceEndpointProvider` — один эндпоинт «Устройство», без поиска), Android регистрирует `AndroidBluetoothEndpointProvider` + `AndroidBluetoothTransport`, оба поверх `Android.Bluetooth.BluetoothAdapter`. Разбор входящего байтового потока в строки вынесен в чистый класс `LineAssembler` (ядро), чтобы быть покрытым юнит-тестами — сам транспорт живёт в `net10.0-android` и тестами не виден.

**Tech Stack:** .NET 10, Avalonia (compiled bindings), ReactiveUI + Zafiro.UI.Commands, xUnit, Android.Bluetooth (Mono.Android, без новых NuGet-пакетов).

## Global Constraints

- BT SPP UUID для RFCOMM-сокета: `00001101-0000-1000-8000-00805F9B34FB` (см. `docs/superpowers/specs/2026-08-12-android-bluetooth-support-design.md`).
- Манифест: `BLUETOOTH`/`BLUETOOTH_ADMIN`/`ACCESS_FINE_LOCATION` с `android:maxSdkVersion="30"`, плюс безусловные `BLUETOOTH_CONNECT` и `BLUETOOTH_SCAN` (`android:usesPermissionFlags="neverForLocation"`).
- `ApplicationId` → `com.arctz.app`, `Label` активности → `ArctZ`.
- Любой транспорт, вызывающий `ConnectAsync` повторно без `DisconnectAsync` (реконнект-цикл `DeviceSession`), должен сам закрыть предыдущее соединение в начале `ConnectAsync` — не полагаться на внешний вызов `DisconnectAsync`.
- Каждая новая `ReactiveCommand`-команда в `ConnectionViewModel` регистрируется через `Track(...)` (см. `ReactiveViewModelBase.Track`), иначе необработанное исключение команды роняет процесс.
- `LineAssembler`: разделители `\n`/`\r\n`, пустые строки отбрасываются, максимальная длина строки 4096 байт (строка длиннее лимита отбрасывается целиком, буфер не растёт бесконечно).
- Таймаут ожидания результата спаривания (`PairAsync`) — 60 секунд.
- Никаких новых NuGet-пакетов для Android-кода: разрешения запрашиваются через встроенные `Context.CheckSelfPermission`/`Activity.RequestPermissions`/`Activity.OnRequestPermissionsResult`, без `Xamarin.AndroidX.Core`.
- Android-специфичный код (`ArctZ.Android/*`) не покрывается юнит-тестами (`ArctZ.Tests` — `net10.0`, не видит `net10.0-android`) — верифицируется исключительно сборкой и запуском на устройстве.
- UI-изменения (Task 6) проверяются по обязательному правилу проекта: собрать → запустить → попросить пользователя проверить → задать отдельный вопрос через `AskUserQuestion` на каждое изменённое поведение.

---

### Task 1: `LineAssembler` — сборка строк из байтового потока

**Files:**
- Create: `ArctZ/Services/Device/LineAssembler.cs`
- Test: `ArctZ.Tests/Services/Device/LineAssemblerTests.cs`

**Interfaces:**
- Produces: `public sealed class LineAssembler { public IReadOnlyList<string> Append(byte[] data, int count); }` — используется `AndroidBluetoothTransport` (Task 8) для превращения чтений из `InputStream` в строки.

- [ ] **Step 1: Написать падающий тест**

```csharp
using System.Text;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class LineAssemblerTests
{
    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Append_SingleChunkWithNewline_ReturnsOneLine()
    {
        var assembler = new LineAssembler();

        var lines = assembler.Append(Bytes("ok\n"), Bytes("ok\n").Length);

        Assert.Equal(new[] { "ok" }, lines);
    }

    [Fact]
    public void Append_SplitAcrossTwoChunks_ReassemblesLineOnSecondChunk()
    {
        var assembler = new LineAssembler();

        var first = assembler.Append(Bytes("o"), 1);
        var second = assembler.Append(Bytes("k\n"), 2);

        Assert.Empty(first);
        Assert.Equal(new[] { "ok" }, second);
    }

    [Fact]
    public void Append_CarriageReturnBeforeNewline_IsStripped()
    {
        var assembler = new LineAssembler();

        var lines = assembler.Append(Bytes("ok\r\n"), Bytes("ok\r\n").Length);

        Assert.Equal(new[] { "ok" }, lines);
    }

    [Fact]
    public void Append_MultipleLinesInOneChunk_ReturnsAllOfThem()
    {
        var assembler = new LineAssembler();
        var data = Bytes("ok\nok\n");

        var lines = assembler.Append(data, data.Length);

        Assert.Equal(new[] { "ok", "ok" }, lines);
    }

    [Fact]
    public void Append_EmptyLines_AreDropped()
    {
        var assembler = new LineAssembler();
        var data = Bytes("\n\nok\n");

        var lines = assembler.Append(data, data.Length);

        Assert.Equal(new[] { "ok" }, lines);
    }

    [Fact]
    public void Append_LineLongerThanLimit_IsDroppedEntirely()
    {
        var assembler = new LineAssembler();
        var overlong = Bytes(new string('A', 5000));

        var duringOverlong = assembler.Append(overlong, overlong.Length);
        var afterNewline = assembler.Append(Bytes("\n"), 1);

        Assert.Empty(duringOverlong);
        Assert.Empty(afterNewline);
    }

    [Fact]
    public void Append_LineWithinLimitAfterAnOverlongOne_IsStillAssembledCorrectly()
    {
        var assembler = new LineAssembler();
        var overlong = Bytes(new string('A', 5000));
        assembler.Append(overlong, overlong.Length);
        assembler.Append(Bytes("\n"), 1);

        var lines = assembler.Append(Bytes("ok\n"), 3);

        Assert.Equal(new[] { "ok" }, lines);
    }
}
```

- [ ] **Step 2: Запустить тесты и убедиться, что они не компилируются/падают**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~LineAssemblerTests"`
Expected: FAIL (тип `LineAssembler` не существует).

- [ ] **Step 3: Реализовать `LineAssembler`**

```csharp
using System.Text;

namespace ArctZ.Services.Device;

/// <summary>
/// Собирает завершённые строки из последовательных чтений байтового потока
/// (RFCOMM/serial). Разделитель — '\n', ведущий '\r' перед ним отбрасывается.
/// Не зависит от Android/платформы — так этот разбор можно покрыть тестами,
/// хотя единственный сейчас потребитель (AndroidBluetoothTransport) живёт
/// в net10.0-android и из ArctZ.Tests не виден.
/// </summary>
public sealed class LineAssembler
{
    private const int MaxLineLength = 4096;

    private readonly StringBuilder _buffer = new();
    private bool _droppingOverlongLine;

    public IReadOnlyList<string> Append(byte[] data, int count)
    {
        var lines = new List<string>();

        for (var i = 0; i < count; i++)
        {
            var b = data[i];
            if (b == (byte)'\n')
            {
                if (!_droppingOverlongLine)
                {
                    var line = _buffer.ToString().TrimEnd('\r');
                    if (line.Length > 0)
                    {
                        lines.Add(line);
                    }
                }

                _buffer.Clear();
                _droppingOverlongLine = false;
                continue;
            }

            if (_droppingOverlongLine)
            {
                continue;
            }

            if (_buffer.Length >= MaxLineLength)
            {
                _droppingOverlongLine = true;
                _buffer.Clear();
                continue;
            }

            _buffer.Append((char)b);
        }

        return lines;
    }
}
```

Добавить `using System.Collections.Generic;` в начало файла, если целевой `ImplicitUsings` в `ArctZ.csproj` их не включает — проверить по факту компиляции на Step 4.

- [ ] **Step 4: Запустить тесты и убедиться, что они проходят**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~LineAssemblerTests"`
Expected: PASS (7 тестов).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Services/Device/LineAssembler.cs ArctZ.Tests/Services/Device/LineAssemblerTests.cs
git commit -m "feat: add LineAssembler for byte-stream line framing"
```

---

### Task 2: `IDeviceEndpointProvider` + `SingleRealDeviceEndpointProvider` + DI-регистрация

**Files:**
- Create: `ArctZ/Services/Device/DeviceEndpointInfo.cs`
- Create: `ArctZ/Services/Device/IDeviceEndpointProvider.cs`
- Create: `ArctZ/Services/Device/SingleRealDeviceEndpointProvider.cs`
- Modify: `ArctZ/Services/Device/ServiceCollectionExtensions.cs`
- Test: `ArctZ.Tests/Services/Device/ServiceCollectionExtensionsTests.cs` (добавить один тест)

**Interfaces:**
- Produces: `public sealed record DeviceEndpointInfo(string Id, string Name, bool IsPaired)`; `public interface IDeviceEndpointProvider { bool SupportsDiscovery { get; } Task<IReadOnlyList<DeviceEndpointInfo>> GetKnownEndpointsAsync(CancellationToken ct = default); IObservable<DeviceEndpointInfo> Discover(); Task<bool> PairAsync(string deviceId, CancellationToken ct = default); }`. Используется `ConnectionViewModel` (Task 3) и Android-реализацией (Task 9).
- Consumes: ничего нового.

- [ ] **Step 1: Написать падающий тест DI-регистрации**

Добавить в конец `ArctZ.Tests/Services/Device/ServiceCollectionExtensionsTests.cs` (перед закрывающей `}` класса):

```csharp

    [Fact]
    public void AddArctZCore_RegistersDefaultDeviceEndpointProvider()
    {
        using var provider = BuildProvider();

        var endpointProvider = provider.GetRequiredService<IDeviceEndpointProvider>();

        Assert.IsType<SingleRealDeviceEndpointProvider>(endpointProvider);
    }
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~AddArctZCore_RegistersDefaultDeviceEndpointProvider"`
Expected: FAIL (тип `IDeviceEndpointProvider`/`SingleRealDeviceEndpointProvider` не существует).

- [ ] **Step 3: Создать `DeviceEndpointInfo.cs`**

```csharp
namespace ArctZ.Services.Device;

/// <summary>Одно устройство, о котором знает IDeviceEndpointProvider — уже известное или найденное сканом.</summary>
public sealed record DeviceEndpointInfo(string Id, string Name, bool IsPaired);
```

- [ ] **Step 4: Создать `IDeviceEndpointProvider.cs`**

```csharp
namespace ArctZ.Services.Device;

/// <summary>
/// Источник доступных устройств для ConnectionViewModel. Desktop/Browser получают
/// SingleRealDeviceEndpointProvider (один эндпоинт, без поиска); Android — свою
/// реализацию поверх BluetoothAdapter (см. ArctZ.Android.AndroidBluetoothEndpointProvider).
/// </summary>
public interface IDeviceEndpointProvider
{
    /// <summary>Умеет ли платформа искать новые устройства в эфире и спаривать их.</summary>
    bool SupportsDiscovery { get; }

    /// <summary>Уже известные (спаренные) устройства.</summary>
    Task<IReadOnlyList<DeviceEndpointInfo>> GetKnownEndpointsAsync(CancellationToken cancellationToken = default);

    /// <summary>Поиск в эфире: подписка запускает скан, dispose или естественное завершение (OnCompleted) — его конец.</summary>
    IObservable<DeviceEndpointInfo> Discover();

    /// <summary>Спаривание устройства. true — устройство спарено к моменту возврата.</summary>
    Task<bool> PairAsync(string deviceId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Создать `SingleRealDeviceEndpointProvider.cs`**

```csharp
using System.Reactive.Linq;

namespace ArctZ.Services.Device;

/// <summary>
/// Провайдер по умолчанию для платформ без нескольких реальных устройств
/// (Desktop, Browser): один эндпоинт "real", без сканирования, без спаривания.
/// Сохраняет поведение, которое ConnectionViewModel имел до появления
/// IDeviceEndpointProvider — Android переопределяет эту регистрацию своей.
/// </summary>
public sealed class SingleRealDeviceEndpointProvider : IDeviceEndpointProvider
{
    public const string RealDeviceId = "real";

    public bool SupportsDiscovery => false;

    public Task<IReadOnlyList<DeviceEndpointInfo>> GetKnownEndpointsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DeviceEndpointInfo>>(new[] { new DeviceEndpointInfo(RealDeviceId, "Устройство", true) });

    public IObservable<DeviceEndpointInfo> Discover() => Observable.Empty<DeviceEndpointInfo>();

    public Task<bool> PairAsync(string deviceId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}
```

- [ ] **Step 6: Зарегистрировать в `AddArctZCore()`**

Modify `ArctZ/Services/Device/ServiceCollectionExtensions.cs:16-31`, добавить строку после `services.AddSingleton(MachineLimits.Default);`:

```csharp
        services.AddSingleton(MachineLimits.Default);
        services.AddSingleton<IDeviceEndpointProvider, SingleRealDeviceEndpointProvider>();
        services.AddSingleton<IDeviceSessionFactory, DeviceSessionFactory>();
```

- [ ] **Step 7: Запустить тесты и убедиться, что они проходят**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ServiceCollectionExtensionsTests"`
Expected: PASS (5 тестов, включая новый).

- [ ] **Step 8: Commit**

```bash
git add ArctZ/Services/Device/DeviceEndpointInfo.cs ArctZ/Services/Device/IDeviceEndpointProvider.cs ArctZ/Services/Device/SingleRealDeviceEndpointProvider.cs ArctZ/Services/Device/ServiceCollectionExtensions.cs ArctZ.Tests/Services/Device/ServiceCollectionExtensionsTests.cs
git commit -m "feat: add IDeviceEndpointProvider abstraction with single-real-device default"
```

---

### Task 3: `ConnectionViewModel` — список устройств из провайдера

**Files:**
- Modify: `ArctZ/ViewModels/ConnectionEndpoint.cs`
- Modify: `ArctZ/ViewModels/ConnectionViewModel.cs`
- Modify: `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs`
- Create: `ArctZ.Tests/Services/Device/FakeDeviceEndpointProvider.cs`
- Modify: `ArctZ.Tests.Screenshots/ScreenshotGalleryTests.cs` (конструирует `ConnectionViewModel` напрямую, в обход DI — сигнатура конструктора меняется)
- Modify: `ArctZ.Tests.Screenshots/ScreenCatalog.cs` («connection»-экран должен дождаться первого обновления списка перед тем, как его снимут)

**Interfaces:**
- Consumes: `IDeviceEndpointProvider` (Task 2), `DeviceEndpointInfo` (Task 2).
- Produces: `ConnectionViewModel` 4-параметровый конструктор `(IDeviceTransport, Func<IDeviceTransport>, IDeviceSessionFactory, IDeviceEndpointProvider)`; `IEnhancedCommand<Unit> RefreshEndpointsCommand`; `string? EndpointError`; `bool HasEndpointError`; `ConnectionEndpoint` теперь `record ConnectionEndpoint(string Id, string DisplayName, ConnectionEndpointKind Kind, bool IsPaired = true)` c `string? StatusLabel`. Используется Task 4/5 (Scan/Pairing) и Task 6 (XAML).

**Важно:** `ArctZ.Tests.Screenshots` — отдельный проект (не ссылается на `ArctZ.Tests`), и в `ScreenshotGalleryTests.cs:40-43` уже есть прямой вызов `new ConnectionViewModel(realTransport, () => demoTransport, new DeviceSessionFactory(MachineLimits.Default))` (3 параметра, без DI). Смена сигнатуры конструктора в Step 6 без правки этого файла сломает сборку `ArctZ.Tests.Screenshots.csproj` — это отдельный шаг ниже (Step 8), не часть общего набора `ArctZ.Tests`.

- [ ] **Step 1: Создать тестовый дубль `FakeDeviceEndpointProvider`**

```csharp
using System.Reactive.Subjects;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public sealed class FakeDeviceEndpointProvider : IDeviceEndpointProvider
{
    public bool SupportsDiscovery { get; set; } = true;
    public List<DeviceEndpointInfo> KnownEndpoints { get; set; } = new();
    public Exception? GetKnownEndpointsException { get; set; }
    public Exception? PairException { get; set; }
    public bool PairResult { get; set; } = true;
    public List<string> PairedIds { get; } = new();
    public Subject<DeviceEndpointInfo> DiscoverySubject { get; } = new();

    public Task<IReadOnlyList<DeviceEndpointInfo>> GetKnownEndpointsAsync(CancellationToken cancellationToken = default)
    {
        if (GetKnownEndpointsException is not null)
        {
            throw GetKnownEndpointsException;
        }

        return Task.FromResult<IReadOnlyList<DeviceEndpointInfo>>(KnownEndpoints);
    }

    public IObservable<DeviceEndpointInfo> Discover() => DiscoverySubject;

    public Task<bool> PairAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (PairException is not null)
        {
            throw PairException;
        }

        if (PairResult)
        {
            PairedIds.Add(deviceId);
        }

        return Task.FromResult(PairResult);
    }
}
```

- [ ] **Step 2: Обновить `ConnectionEndpoint.cs`**

Replace `ArctZ/ViewModels/ConnectionEndpoint.cs` целиком:

```csharp
namespace ArctZ.ViewModels;

public enum ConnectionEndpointKind
{
    RealDevice,
    Demo
}

public sealed record ConnectionEndpoint(string Id, string DisplayName, ConnectionEndpointKind Kind, bool IsPaired = true)
{
    public string? StatusLabel => Kind switch
    {
        ConnectionEndpointKind.RealDevice => IsPaired ? "спарено" : "не спарено",
        _ => null,
    };
}
```

- [ ] **Step 3: Переписать тестовый helper и существующие тесты под асинхронный конструктор**

В `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs` заменить блок `using`-ов и helper (строки 1-14):

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Simulation;
using ArctZ.Tests.Services.Device;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class ConnectionViewModelTests
{
    private static FakeDeviceEndpointProvider DefaultEndpointProvider() => new()
    {
        KnownEndpoints = { new DeviceEndpointInfo("real", "Устройство", true) },
    };

    private static async Task<ConnectionViewModel> CreateVmAsync(
        IDeviceTransport realTransport,
        IDeviceTransport? demoTransport = null,
        IDeviceEndpointProvider? endpointProvider = null)
    {
        var vm = new ConnectionViewModel(
            realTransport,
            () => demoTransport ?? new FakeDeviceTransport(),
            new DeviceSessionFactory(MachineLimits.Default),
            endpointProvider ?? DefaultEndpointProvider());
        await vm.RefreshEndpointsCommand.Execute();
        return vm;
    }
```

Затем во всём остальном файле:
- Каждый вызов `CreateVm(...)` заменить на `await CreateVmAsync(...)`.
- Каждый `[Fact] public void Method()`, который вызывает `CreateVm`, сделать `[Fact] public async Task Method()`.

Конкретно эти четыре метода меняют сигнатуру с `void` на `async Task` (остальные уже `async Task`):
- `Constructor_RealTransportSupported_ListsRealAndDemoAndDoesNotFlagUnsupported`
- `Constructor_RealTransportUnsupported_OnlyListsDemoAndFlagsUnsupported`
- `ToggleGCodeLogCommand_TogglesIsGCodeLogOpen`
- `ToggleMockSettingsCommand_TogglesIsMockSettingsOpen`

Пример (первый метод, полностью):

```csharp
    [Fact]
    public async Task Constructor_RealTransportSupported_ListsRealAndDemoAndDoesNotFlagUnsupported()
    {
        var vm = await CreateVmAsync(new FakeDeviceTransport());

        Assert.Equal(2, vm.AvailableEndpoints.Count);
        Assert.Contains(vm.AvailableEndpoints, e => e.Kind == ConnectionEndpointKind.RealDevice);
        Assert.Contains(vm.AvailableEndpoints, e => e.Kind == ConnectionEndpointKind.Demo);
        Assert.Equal(ConnectionEndpointKind.RealDevice, vm.SelectedEndpoint!.Kind);
        Assert.False(vm.IsRealDeviceUnsupported);
    }

    [Fact]
    public async Task Constructor_RealTransportUnsupported_OnlyListsDemoAndFlagsUnsupported()
    {
        var realTransport = new FakeDeviceTransport { IsSupported = false };
        var vm = await CreateVmAsync(realTransport);

        Assert.Single(vm.AvailableEndpoints);
        Assert.Equal(ConnectionEndpointKind.Demo, vm.AvailableEndpoints[0].Kind);
        Assert.Equal(ConnectionEndpointKind.Demo, vm.SelectedEndpoint!.Kind);
        Assert.True(vm.IsRealDeviceUnsupported);
    }
```

Все остальные `async Task`-тесты меняются механически: `var vm = CreateVm(...)` → `var vm = await CreateVmAsync(...)`. Пройтись по каждому вызову в файле (после Step 3 их 16 штук — строки исходного файла 19, 32, 44, 58, 69, 82, 99, 115, 129, 155, 176, 205, 217, 231, 249, 263→CreateVmAsync тоже, 276, 317, 340→CreateVmAsync, 357, 371, 387 в нумерации ДО этой правки) и `ToggleGCodeLogCommand_TogglesIsGCodeLogOpen`/`ToggleMockSettingsCommand_TogglesIsMockSettingsOpen` дополнительно получают `async Task` вместо `void`, например:

```csharp
    [Fact]
    public async Task ToggleGCodeLogCommand_TogglesIsGCodeLogOpen()
    {
        var vm = await CreateVmAsync(new FakeDeviceTransport());
        Assert.False(vm.IsGCodeLogOpen);

        vm.ToggleGCodeLogCommand.Execute(null);
        Assert.True(vm.IsGCodeLogOpen);

        vm.ToggleGCodeLogCommand.Execute(null);
        Assert.False(vm.IsGCodeLogOpen);
    }

    [Fact]
    public async Task ToggleMockSettingsCommand_TogglesIsMockSettingsOpen()
    {
        var vm = await CreateVmAsync(new FakeDeviceTransport());
        Assert.False(vm.IsMockSettingsOpen);

        vm.ToggleMockSettingsCommand.Execute(null);
        Assert.True(vm.IsMockSettingsOpen);

        vm.ToggleMockSettingsCommand.Execute(null);
        Assert.False(vm.IsMockSettingsOpen);
    }
```

- [ ] **Step 4: Добавить новые тесты списка/переупорядочивания/ошибки в конец файла (перед закрывающей `}` класса)**

```csharp

    [Fact]
    public async Task AvailableEndpoints_MultipleKnownDevices_RealDevicesListedBeforeDemo()
    {
        var provider = new FakeDeviceEndpointProvider
        {
            KnownEndpoints =
            {
                new DeviceEndpointInfo("aa:bb", "FluidNC-1", true),
                new DeviceEndpointInfo("cc:dd", "FluidNC-2", true),
            },
        };
        var vm = await CreateVmAsync(new FakeDeviceTransport(), endpointProvider: provider);

        Assert.Equal(3, vm.AvailableEndpoints.Count);
        Assert.Equal("aa:bb", vm.AvailableEndpoints[0].Id);
        Assert.Equal("cc:dd", vm.AvailableEndpoints[1].Id);
        Assert.Equal(ConnectionEndpointKind.Demo, vm.AvailableEndpoints[2].Kind);
    }

    [Fact]
    public async Task RefreshEndpointsCommand_PreservesManuallySelectedEndpointById()
    {
        var provider = new FakeDeviceEndpointProvider
        {
            KnownEndpoints = { new DeviceEndpointInfo("aa:bb", "FluidNC-1", true) },
        };
        var vm = await CreateVmAsync(new FakeDeviceTransport(), endpointProvider: provider);
        vm.SelectedEndpoint = vm.AvailableEndpoints.Single(e => e.Kind == ConnectionEndpointKind.Demo);

        await vm.RefreshEndpointsCommand.Execute();

        Assert.Equal(ConnectionEndpointKind.Demo, vm.SelectedEndpoint!.Kind);
    }

    [Fact]
    public async Task RefreshEndpointsCommand_ProviderThrows_SetsEndpointErrorAndKeepsExistingList()
    {
        var provider = new FakeDeviceEndpointProvider
        {
            KnownEndpoints = { new DeviceEndpointInfo("aa:bb", "FluidNC-1", true) },
        };
        var vm = await CreateVmAsync(new FakeDeviceTransport(), endpointProvider: provider);
        Assert.Equal(2, vm.AvailableEndpoints.Count);

        provider.GetKnownEndpointsException = new InvalidOperationException("Нет разрешения на Bluetooth");
        await vm.RefreshEndpointsCommand.Execute();

        Assert.Equal("Нет разрешения на Bluetooth", vm.EndpointError);
        Assert.True(vm.HasEndpointError);
        Assert.Equal(2, vm.AvailableEndpoints.Count);
    }
```

- [ ] **Step 5: Запустить тесты и убедиться, что они падают на отсутствующем API**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ConnectionViewModelTests"`
Expected: FAIL на компиляции (нет `RefreshEndpointsCommand`/`EndpointError`/4-параметрового конструктора).

- [ ] **Step 6: Реализовать изменения в `ConnectionViewModel.cs`**

Modify `ArctZ/ViewModels/ConnectionViewModel.cs:14-24` (поля):

```csharp
public partial class ConnectionViewModel : ReactiveViewModelBase
{
    private readonly IDeviceTransport _realTransport;
    private readonly Func<IDeviceTransport> _createDemoTransport;
    private readonly IDeviceSessionFactory _sessionFactory;
    private readonly IDeviceEndpointProvider _endpointProvider;
    private static readonly ConnectionEndpoint DemoEndpoint = new("demo", "Демо", ConnectionEndpointKind.Demo);
    private IDisposable? _sentGCodeSubscription;
    private IMockDeviceControl? _currentMockControl;
    private const int MaxSentGCodeLines = 200;
    private const int MockErrorCode = 9;
    private const int MockAlarmCode = 1;
```

Modify `ArctZ/ViewModels/ConnectionViewModel.cs:57-60` (после `[Reactive] private bool isPlaybackLocked;`), добавить:

```csharp
    [Reactive] private string? endpointError;

    public bool HasEndpointError => !string.IsNullOrEmpty(EndpointError);

    public bool IsDiscoverySupported => _realTransport.IsSupported && _endpointProvider.SupportsDiscovery;
```

Modify сигнатуру `public IEnhancedCommand<Unit> ResetAlarmCommand { get; }` block (строки 102-108), добавить после:

```csharp
    public IEnhancedCommand<Unit> RefreshEndpointsCommand { get; }
```

Modify конструктор (строки 110-125) — заменить целиком блок от сигнатуры до конца заполнения `AvailableEndpoints`/`SelectedEndpoint`:

```csharp
    public ConnectionViewModel(
        IDeviceTransport realTransport,
        Func<IDeviceTransport> createDemoTransport,
        IDeviceSessionFactory sessionFactory,
        IDeviceEndpointProvider endpointProvider)
    {
        _realTransport = realTransport;
        _createDemoTransport = createDemoTransport;
        _sessionFactory = sessionFactory;
        _endpointProvider = endpointProvider;

        AvailableEndpoints.Add(DemoEndpoint);
        SelectedEndpoint = _realTransport.IsSupported ? null : DemoEndpoint;
```

Modify блок команд (строки 137-150) — после `ResetAlarmCommand = ...`, добавить:

```csharp
        RefreshEndpointsCommand = Track(ReactiveCommand.CreateFromTask(RefreshEndpointsAsync)
            .Enhance(text: "Обновить список", name: "RefreshEndpointsCommand"));
```

В конце конструктора, после существующей подписки `this.WhenAnyValue(x => x.MockResponseDelayMs)...DisposeWith(Disposables);` (строки 220-222), добавить запуск начального обновления списка:

```csharp

        RefreshEndpointsCommand.Execute().Subscribe().DisposeWith(Disposables);
    }
```

(Закрывающая скобка конструктора — она уже была на строке 223, просто убедиться, что после вставки строка `RefreshEndpointsCommand.Execute().Subscribe().DisposeWith(Disposables);` идёт последней инструкцией внутри тела конструктора.)

Modify объединённый блок ре-рейза `PropertyChanged` (строки 206-218) — добавить `EndpointError` в отслеживаемые зависимости:

```csharp
        this.WhenAnyValue(x => x.Session, x => x.ConnectionState, x => x.DeviceStatus, x => x.LastError, x => x.LastAlarmCode, x => x.EndpointError,
                (s, cs, ds, le, ac, ee) => (s, cs, ds, le, ac, ee))
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(IsConnectionModalVisible));
                this.RaisePropertyChanged(nameof(IsAlarmModalVisible));
                this.RaisePropertyChanged(nameof(IsAnyModalVisible));
                this.RaisePropertyChanged(nameof(ConnectionStateLabel));
                this.RaisePropertyChanged(nameof(PositionLabel));
                this.RaisePropertyChanged(nameof(HasError));
                this.RaisePropertyChanged(nameof(ErrorMessage));
                this.RaisePropertyChanged(nameof(HasEndpointError));
            })
            .DisposeWith(Disposables);
```

Add новый приватный метод `RefreshEndpointsAsync` в конец класса, перед последним `}` (после существующего `ResetAlarmAsync`, строки 309-319):

```csharp

    private async Task RefreshEndpointsAsync()
    {
        if (!_realTransport.IsSupported)
        {
            return;
        }

        var previousSelectedId = SelectedEndpoint?.Id;

        try
        {
            var known = await _endpointProvider.GetKnownEndpointsAsync();
            EndpointError = null;

            var realEndpoints = known
                .Select(info => new ConnectionEndpoint(info.Id, info.Name, ConnectionEndpointKind.RealDevice, info.IsPaired))
                .ToList();

            AvailableEndpoints.Clear();
            foreach (var endpoint in realEndpoints)
            {
                AvailableEndpoints.Add(endpoint);
            }

            AvailableEndpoints.Add(DemoEndpoint);

            SelectedEndpoint =
                AvailableEndpoints.FirstOrDefault(e => e.Id == previousSelectedId) ??
                realEndpoints.FirstOrDefault() ??
                DemoEndpoint;
        }
        catch (Exception ex)
        {
            EndpointError = ex.Message;
        }
    }
```

Проверить, что в начале файла есть `using System.Linq;` (уже есть — используется для `Single`/др. в остальном коде проекта; если в этом файле его нет, добавить).

- [ ] **Step 7: Запустить тесты и убедиться, что они проходят**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ConnectionViewModelTests"`
Expected: PASS (все тесты, включая 3 новых).

- [ ] **Step 8: Прогнать весь набор тестов, чтобы убедиться, что ничего не сломано**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS.

- [ ] **Step 9: Починить прямой (в обход DI) вызов конструктора в `ArctZ.Tests.Screenshots`**

`ArctZ.Tests.Screenshots` — отдельный проект (ссылается только на `ArctZ.csproj`, не на `ArctZ.Tests`), и его собственный `ScreenshotGalleryTests.cs` строит `ConnectionViewModel` напрямую (не через `AddArctZCore()`), поэтому смена сигнатуры конструктора в Step 6 ломает его сборку.

Modify `ArctZ.Tests.Screenshots/ScreenshotGalleryTests.cs:40-43`:

```csharp
        var connection = new ConnectionViewModel(
            realTransport,
            () => demoTransport,
            new DeviceSessionFactory(MachineLimits.Default),
            new SingleRealDeviceEndpointProvider());
```

(`SingleRealDeviceEndpointProvider` — из `ArctZ.Services.Device`, `using` для этого namespace в файле уже есть, строка 9. Даёт тот же единственный эндпоинт «Устройство» + «Демо», что показывалось на скриншотах и раньше.)

- [ ] **Step 10: Дождаться первого обновления списка перед тем, как снимается самый первый скриншот («connection»)**

Список эндпоинтов теперь заполняется асинхронно (Task 3, Step 6: `RefreshEndpointsCommand.Execute().Subscribe()` в конструкторе, fire-and-forget). `ScreenshotGalleryTests.cs:57` вызывает `Dispatcher.UIThread.RunJobs()` один раз перед циклом захвата экранов, что в большинстве случаев успевает продвинуть уже запущенную команду — но полагаться на это неявно не стоит: явное ожидание в `Setup` детерминирует результат независимо от того, на каком шедулере в итоге выполняется команда.

Modify `ArctZ.Tests.Screenshots/ScreenCatalog.cs:35-39` (первый `ScreenDefinition`, "connection"):

```csharp
        new ScreenDefinition(
            "connection",
            "Модалка подключения",
            Setup: vm => vm.Connection.RefreshEndpointsCommand.Execute().ToTask(),
            Teardown: _ => Task.CompletedTask),
```

(`.ToTask()` — уже используемый в этом файле идиом для `IObservable<Unit>` → `Task`, см. `ToggleGCodeLogCommand`/`ToggleMockSettingsCommand` ниже в этом же файле; `using System.Reactive.Threading.Tasks;` уже есть, строка 4.)

- [ ] **Step 11: Прогнать галерею скриншотов и обновить сами файлы**

Run: `dotnet test ArctZ.Tests.Screenshots/ArctZ.Tests.Screenshots.csproj`
Expected: PASS; `screenshots/01-connection.png` (и, возможно, соседние) перезаписываются — состав списка на модалке подключения не изменился (по-прежнему «Устройство» + «Демо»), так что визуальной регрессии здесь быть не должно, но файлы физически перезаписываются тестом при каждом запуске (это штатное поведение генератора, см. `docs/README.md`).

- [ ] **Step 12: Commit**

```bash
git add ArctZ/ViewModels/ConnectionEndpoint.cs ArctZ/ViewModels/ConnectionViewModel.cs ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs ArctZ.Tests/Services/Device/FakeDeviceEndpointProvider.cs ArctZ.Tests.Screenshots/ScreenshotGalleryTests.cs ArctZ.Tests.Screenshots/ScreenCatalog.cs screenshots/
git commit -m "feat: build ConnectionViewModel's endpoint list from IDeviceEndpointProvider"
```

---

### Task 4: `ConnectionViewModel` — поиск устройств (`ScanCommand`)

**Files:**
- Modify: `ArctZ/ViewModels/ConnectionViewModel.cs`
- Modify: `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs`

**Interfaces:**
- Consumes: `IDeviceEndpointProvider.Discover()`, `IDeviceEndpointProvider.SupportsDiscovery` (Task 2); `FakeDeviceEndpointProvider.DiscoverySubject` (Task 3).
- Produces: `IEnhancedCommand<Unit> ScanCommand`; `bool IsScanning`. Используется Task 6 (XAML).

- [ ] **Step 1: Написать падающие тесты**

Добавить в конец `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs`:

```csharp

    [Fact]
    public async Task ScanCommand_DeviceFound_AddsItBeforeDemoWithoutDuplicates()
    {
        var provider = new FakeDeviceEndpointProvider();
        var vm = await CreateVmAsync(new FakeDeviceTransport(), endpointProvider: provider);
        Assert.Single(vm.AvailableEndpoints);

        vm.ScanCommand.Execute(null);
        Assert.True(vm.IsScanning);
        provider.DiscoverySubject.OnNext(new DeviceEndpointInfo("aa:bb", "FluidNC-1", false));
        provider.DiscoverySubject.OnNext(new DeviceEndpointInfo("aa:bb", "FluidNC-1", false));
        provider.DiscoverySubject.OnCompleted();

        Assert.False(vm.IsScanning);
        Assert.Equal(2, vm.AvailableEndpoints.Count);
        Assert.Equal("aa:bb", vm.AvailableEndpoints[0].Id);
        Assert.Equal(ConnectionEndpointKind.Demo, vm.AvailableEndpoints[1].Kind);
    }

    [Fact]
    public async Task ScanCommand_InvokedWhileScanning_StopsTheScan()
    {
        var provider = new FakeDeviceEndpointProvider();
        var vm = await CreateVmAsync(new FakeDeviceTransport(), endpointProvider: provider);

        vm.ScanCommand.Execute(null);
        Assert.True(vm.IsScanning);

        vm.ScanCommand.Execute(null);

        Assert.False(vm.IsScanning);
        Assert.False(provider.DiscoverySubject.HasObservers);
    }
```

- [ ] **Step 2: Запустить тесты и убедиться, что они падают**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ScanCommand"`
Expected: FAIL на компиляции (`ScanCommand`/`IsScanning` не существуют).

- [ ] **Step 3: Реализовать `ScanCommand`**

Modify `ArctZ/ViewModels/ConnectionViewModel.cs` — добавить поле рядом с `_sentGCodeSubscription` (строка ~19):

```csharp
    private IDisposable? _sentGCodeSubscription;
    private IDisposable? _scanSubscription;
```

Add reactive-свойство рядом с `endpointError` (после блока из Task 3):

```csharp
    [Reactive] private bool isScanning;
```

Add команду в секцию публичных команд, после `RefreshEndpointsCommand`:

```csharp
    public IEnhancedCommand<Unit> ScanCommand { get; }
```

Modify конструктор — после `RefreshEndpointsCommand = Track(...)`, добавить:

```csharp
        ScanCommand = Track(ReactiveCommand.Create(ToggleScan)
            .Enhance(text: "Поиск", name: "ScanCommand"));
```

Add методы `ToggleScan`/`OnDeviceDiscovered` в конец класса (после `RefreshEndpointsAsync`):

```csharp

    private void ToggleScan()
    {
        if (_scanSubscription is not null)
        {
            _scanSubscription.Dispose();
            _scanSubscription = null;
            IsScanning = false;
            return;
        }

        IsScanning = true;
        EndpointError = null;

        _scanSubscription = _endpointProvider.Discover()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(
                OnDeviceDiscovered,
                ex =>
                {
                    EndpointError = ex.Message;
                    IsScanning = false;
                    _scanSubscription = null;
                },
                () =>
                {
                    IsScanning = false;
                    _scanSubscription = null;
                });
    }

    private void OnDeviceDiscovered(DeviceEndpointInfo info)
    {
        if (AvailableEndpoints.Any(e => e.Id == info.Id))
        {
            return;
        }

        var demoIndex = AvailableEndpoints.IndexOf(DemoEndpoint);
        var insertAt = demoIndex >= 0 ? demoIndex : AvailableEndpoints.Count;
        AvailableEndpoints.Insert(insertAt, new ConnectionEndpoint(info.Id, info.Name, ConnectionEndpointKind.RealDevice, info.IsPaired));
    }
```

Add `Dispose` override в конец класса (после `OnDeviceDiscovered`):

```csharp

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scanSubscription?.Dispose();
        }

        base.Dispose(disposing);
    }
```

- [ ] **Step 4: Запустить тесты и убедиться, что они проходят**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ConnectionViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Прогнать весь набор тестов**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ArctZ/ViewModels/ConnectionViewModel.cs ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs
git commit -m "feat: add ScanCommand for discovering new Bluetooth endpoints"
```

---

### Task 5: `ConnectionViewModel` — спаривание перед подключением

**Files:**
- Modify: `ArctZ/ViewModels/ConnectionViewModel.cs`
- Modify: `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs`

**Interfaces:**
- Consumes: `IDeviceEndpointProvider.PairAsync` (Task 2).
- Produces: `ConnectAsync` теперь спаривает неспаренный `RealDevice`-эндпоинт перед созданием сессии.

- [ ] **Step 1: Написать падающие тесты**

Добавить в конец `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs`:

```csharp

    [Fact]
    public async Task ConnectCommand_UnpairedRealDeviceSelected_PairsBeforeConnecting()
    {
        var provider = new FakeDeviceEndpointProvider
        {
            KnownEndpoints = { new DeviceEndpointInfo("aa:bb", "FluidNC-1", false) },
        };
        var realTransport = new FakeDeviceTransport();
        var vm = await CreateVmAsync(realTransport, endpointProvider: provider);

        await vm.ConnectCommand.Execute();

        Assert.Equal(new[] { "aa:bb" }, provider.PairedIds);
        Assert.True(realTransport.IsConnected);
        Assert.True(vm.SelectedEndpoint!.IsPaired);
    }

    [Fact]
    public async Task ConnectCommand_PairingFails_DoesNotCreateSessionAndSetsEndpointError()
    {
        var provider = new FakeDeviceEndpointProvider
        {
            KnownEndpoints = { new DeviceEndpointInfo("aa:bb", "FluidNC-1", false) },
            PairResult = false,
        };
        var realTransport = new FakeDeviceTransport();
        var vm = await CreateVmAsync(realTransport, endpointProvider: provider);

        await vm.ConnectCommand.Execute();

        Assert.Null(vm.Session);
        Assert.False(realTransport.IsConnected);
        Assert.False(string.IsNullOrEmpty(vm.EndpointError));
    }

    [Fact]
    public async Task ConnectCommand_PairingThrows_DoesNotCreateSessionAndSurfacesTheError()
    {
        var provider = new FakeDeviceEndpointProvider
        {
            KnownEndpoints = { new DeviceEndpointInfo("aa:bb", "FluidNC-1", false) },
            PairException = new InvalidOperationException("Нет разрешения на Bluetooth"),
        };
        var vm = await CreateVmAsync(new FakeDeviceTransport(), endpointProvider: provider);

        await vm.ConnectCommand.Execute();

        Assert.Null(vm.Session);
        Assert.Equal("Нет разрешения на Bluetooth", vm.EndpointError);
    }

    [Fact]
    public async Task ConnectCommand_AlreadyPairedRealDevice_DoesNotCallPairAsync()
    {
        var provider = new FakeDeviceEndpointProvider
        {
            KnownEndpoints = { new DeviceEndpointInfo("aa:bb", "FluidNC-1", true) },
        };
        var vm = await CreateVmAsync(new FakeDeviceTransport(), endpointProvider: provider);

        await vm.ConnectCommand.Execute();

        Assert.Empty(provider.PairedIds);
    }
```

- [ ] **Step 2: Запустить тесты и убедиться, что они падают**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~PairingFails|PairingThrows|PairsBeforeConnecting|DoesNotCallPairAsync"`
Expected: FAIL (`PairedIds` пуст там, где ожидается спаривание — логика ещё не реализована).

- [ ] **Step 3: Реализовать спаривание в `ConnectAsync`**

Modify `ArctZ/ViewModels/ConnectionViewModel.cs:225-230` (начало `ConnectAsync`) — вставить блок сразу после проверки `if (SelectedEndpoint is null) { return; }`:

```csharp
    private async Task ConnectAsync()
    {
        if (SelectedEndpoint is null)
        {
            return;
        }

        if (SelectedEndpoint.Kind == ConnectionEndpointKind.RealDevice && !SelectedEndpoint.IsPaired)
        {
            EndpointError = null;
            bool paired;
            try
            {
                paired = await _endpointProvider.PairAsync(SelectedEndpoint.Id);
            }
            catch (Exception ex)
            {
                EndpointError = ex.Message;
                return;
            }

            if (!paired)
            {
                EndpointError = "Не удалось спарить устройство.";
                return;
            }

            var pairedIndex = AvailableEndpoints.IndexOf(SelectedEndpoint);
            var pairedEndpoint = SelectedEndpoint with { IsPaired = true };
            if (pairedIndex >= 0)
            {
                AvailableEndpoints[pairedIndex] = pairedEndpoint;
            }

            SelectedEndpoint = pairedEndpoint;
        }

        // ... остальное тело метода (Session is not null / innerTransport / ... ) без изменений
```

(Дальше — существующее тело `ConnectAsync` от `if (Session is not null)` и до конца, без изменений.)

- [ ] **Step 4: Запустить тесты и убедиться, что они проходят**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ConnectionViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Прогнать весь набор тестов проекта**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ArctZ/ViewModels/ConnectionViewModel.cs ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs
git commit -m "feat: pair unbonded real-device endpoints before connecting"
```

---

### Task 6: Модалка подключения — список устройств, поиск, ошибка

**Files:**
- Modify: `ArctZ/Views/MainView.axaml:575-606`

**Interfaces:**
- Consumes: `ConnectionViewModel.AvailableEndpoints`, `.SelectedEndpoint`, `.IsDiscoverySupported`, `.ScanCommand`, `.IsScanning`, `.HasEndpointError`, `.EndpointError` (Task 3-5); `ConnectionEndpoint.DisplayName`/`.StatusLabel` (Task 3).
- Produces: обновлённая модалка подключения — визуальная проверка на Desktop и (после Task 7-9) на Android.

- [ ] **Step 1: Заменить блок модалки подключения**

Modify `ArctZ/Views/MainView.axaml:575-606` — заменить блок целиком:

```xml
        <Border IsVisible="{Binding Connection.IsConnectionModalVisible}" Background="{StaticResource HudScrimBrush}">
            <Border x:DataType="vm:ConnectionViewModel" DataContext="{Binding Connection}"
                    Width="360" Background="{StaticResource HudPanelElevatedBrush}"
                    BorderBrush="{StaticResource HudBorderStrongBrush}" BorderThickness="1"
                    Padding="20" HorizontalAlignment="Center" VerticalAlignment="Center">
                <StackPanel Spacing="14">
                    <TextBlock Classes="section-heading" Text="ПОДКЛЮЧЕНИЕ" />
                    <Border IsVisible="{Binding IsRealDeviceUnsupported}"
                            Background="{StaticResource HudWarningDimBrush}"
                            BorderBrush="{StaticResource HudWarningBrush}" BorderThickness="1" Padding="10,6">
                        <TextBlock TextWrapping="Wrap" Foreground="{StaticResource HudWarningBrush}"
                                   Text="Web Serial API не поддерживается этим браузером. Доступен только режим «Демо»." />
                    </Border>
                    <Grid ColumnDefinitions="*,Auto" VerticalAlignment="Center">
                        <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
                            <Ellipse Width="8" Height="8" VerticalAlignment="Center"
                                     Fill="{Binding ConnectionState, Converter={StaticResource StateToBrush}}" />
                            <TextBlock VerticalAlignment="Center"
                                       Text="{Binding ConnectionStateLabel}" />
                        </StackPanel>
                        <Button Grid.Column="1" IsVisible="{Binding IsDiscoverySupported}" Command="{Binding ScanCommand}">
                            <StackPanel Orientation="Horizontal" Spacing="6">
                                <materialIcons:MaterialIcon Kind="BluetoothSearching" Width="16" Height="16" VerticalAlignment="Center" />
                                <TextBlock Text="Поиск" IsVisible="{Binding !IsScanning}" VerticalAlignment="Center" />
                                <TextBlock Text="Стоп" IsVisible="{Binding IsScanning}" VerticalAlignment="Center" />
                            </StackPanel>
                        </Button>
                    </Grid>
                    <ListBox ItemsSource="{Binding AvailableEndpoints}"
                             SelectedItem="{Binding SelectedEndpoint}"
                             MaxHeight="180">
                        <ListBox.ItemTemplate>
                            <DataTemplate x:DataType="vm:ConnectionEndpoint">
                                <Grid ColumnDefinitions="*,Auto" ColumnSpacing="8">
                                    <TextBlock Grid.Column="0" Text="{Binding DisplayName}" VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="1" Text="{Binding StatusLabel}" VerticalAlignment="Center"
                                               FontSize="12" Foreground="{StaticResource HudTextSecondaryBrush}" />
                                </Grid>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                    <Border IsVisible="{Binding HasEndpointError}"
                            Background="{StaticResource HudWarningDimBrush}"
                            BorderBrush="{StaticResource HudWarningBrush}" BorderThickness="1" Padding="10,6">
                        <TextBlock TextWrapping="Wrap" Foreground="{StaticResource HudWarningBrush}"
                                   Text="{Binding EndpointError}" />
                    </Border>
                    <Button Classes="primary" Command="{Binding ConnectCommand}" HorizontalAlignment="Stretch">
                        <StackPanel Orientation="Horizontal" Spacing="8">
                            <materialIcons:MaterialIcon Kind="Bluetooth" Width="16" Height="16" VerticalAlignment="Center" />
                            <TextBlock Text="Подключить" VerticalAlignment="Center" />
                        </StackPanel>
                    </Button>
                </StackPanel>
            </Border>
        </Border>
```

- [ ] **Step 2: Собрать Desktop-голову и убедиться, что XAML компилируется**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: сборка без ошибок (compiled bindings проверяются на этапе сборки — `vm:ConnectionEndpoint`, `StatusLabel`, `IsDiscoverySupported` и т.д. должны резолвиться).

- [ ] **Step 3: Прогнать полный набор тестов**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS (изменение чисто визуальное, логика не тронута).

- [ ] **Step 4: Перегенерировать галерею скриншотов**

Run: `dotnet test ArctZ.Tests.Screenshots/ArctZ.Tests.Screenshots.csproj`
Expected: PASS; `screenshots/01-connection.png` (и, возможно, другие) обновятся под новый вид модалки.

- [ ] **Step 5: Запустить Desktop-приложение и проверить вручную**

Run: `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj`

Попросить пользователя проверить экран подключения, затем задать через `AskUserQuestion` отдельные вопросы (не один общий):
1. Список устройств («Устройство» + «Демо») отображается и выбирается корректно?
2. Кнопка «Поиск» на Desktop скрыта (ожидаемо, `IsDiscoverySupported=false` для `SingleRealDeviceEndpointProvider`)?
3. Подключение через «Демо» по-прежнему работает как раньше?

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Views/MainView.axaml screenshots/
git commit -m "feat: replace connection-endpoint ComboBox with a searchable device list"
```

---

### Task 7: Android-манифест и упаковка приложения

**Files:**
- Modify: `ArctZ.Android/Properties/AndroidManifest.xml`
- Modify: `ArctZ.Android/ArctZ.Android.csproj`

**Interfaces:**
- Consumes: ничего.
- Produces: устанавливаемый APK с именем `ArctZ`, `ApplicationId com.arctz.app`, объявленными Bluetooth-разрешениями (пока не используемыми — транспорт всё ещё `NotSupportedDeviceTransport`).

- [ ] **Step 1: Обновить `AndroidManifest.xml`**

Replace `ArctZ.Android/Properties/AndroidManifest.xml` целиком:

```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android" android:installLocation="auto">
	<uses-permission android:name="android.permission.INTERNET" />
	<uses-permission android:name="android.permission.BLUETOOTH" android:maxSdkVersion="30" />
	<uses-permission android:name="android.permission.BLUETOOTH_ADMIN" android:maxSdkVersion="30" />
	<uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" android:maxSdkVersion="30" />
	<uses-permission android:name="android.permission.BLUETOOTH_CONNECT" />
	<uses-permission android:name="android.permission.BLUETOOTH_SCAN" android:usesPermissionFlags="neverForLocation" />
	<application android:label="ArctZ" android:icon="@drawable/Icon" />
</manifest>
```

- [ ] **Step 2: Переименовать `ApplicationId`**

Modify `ArctZ.Android/ArctZ.Android.csproj:8` — заменить строку:

```xml
    <ApplicationId>com.CompanyName.ArctZ</ApplicationId>
```

на:

```xml
    <ApplicationId>com.arctz.app</ApplicationId>
```

- [ ] **Step 3: Обновить `Label` в `MainActivity.cs`**

Modify `ArctZ.Android/MainActivity.cs:9` — заменить:

```csharp
        Label = "ArctZ.Android",
```

на:

```csharp
        Label = "ArctZ",
```

- [ ] **Step 4: Собрать Android-голову**

Run: `dotnet build ArctZ.Android/ArctZ.Android.csproj`
Expected: сборка без ошибок.

- [ ] **Step 5: Установить и запустить на подключённом по USB телефоне**

Требование: телефон подключён по USB, включена отладка по USB (Настройки → Для разработчиков → Отладка по USB).

Run: `dotnet build ArctZ.Android/ArctZ.Android.csproj -t:Install`

Запустить приложение на телефоне вручную (значок «ArctZ» на экране приложений). При необходимости логов: `adb logcat -s ArctZ:V mono-stdout:V DOTNET:V`.

Попросить пользователя проверить и задать через `AskUserQuestion`:
1. Приложение установилось под именем «ArctZ» и запускается без падения?
2. Экран подключения виден, режим «Демо» по-прежнему работает (кнопки джойстика, «Пуск»/«Пауза»/«Стоп»)?

(Полноценная адаптация раскладки под маленький экран — вне объёма этой задачи; проверяем именно работоспособность, не полировку вёрстки.)

- [ ] **Step 6: Commit**

```bash
git add ArctZ.Android/Properties/AndroidManifest.xml ArctZ.Android/ArctZ.Android.csproj ArctZ.Android/MainActivity.cs
git commit -m "chore: rename Android app id/label and declare Bluetooth permissions"
```

---

### Task 8: `AndroidPermissions` + `AndroidBluetoothTransport`

**Files:**
- Modify: `ArctZ.Android/MainActivity.cs`
- Create: `ArctZ.Android/AndroidPermissions.cs`
- Create: `ArctZ.Android/AndroidBluetoothTransport.cs`
- Delete: `ArctZ.Android/NotSupportedDeviceTransport.cs`
- Modify: `ArctZ.Android/Application.cs`

**Interfaces:**
- Consumes: `LineAssembler` (Task 1).
- Produces: `AndroidPermissions` — `Task<bool> RequestAsync(string[] permissions)`; `AndroidBluetoothTransport : IDeviceTransport` — реальный RFCOMM-транспорт. Используется Task 9 (`AndroidBluetoothEndpointProvider` тоже зависит от `AndroidPermissions`).

- [ ] **Step 1: Расширить `MainActivity.cs` — статическая ссылка и запрос разрешений**

Replace `ArctZ.Android/MainActivity.cs` целиком:

```csharp
using System.Threading.Tasks;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;

namespace ArctZ.Android
{
    [Activity(
        Label = "ArctZ",
        Theme = "@style/MyTheme.NoActionBar",
        Icon = "@drawable/icon",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
    public class MainActivity : AvaloniaMainActivity
    {
        private const int BluetoothPermissionRequestCode = 5001;

        public static MainActivity? Instance { get; private set; }

        private TaskCompletionSource<bool>? _permissionRequestCompletion;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Instance = this;
        }

        protected override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            base.OnDestroy();
        }

        public Task<bool> RequestPermissionsAsync(string[] permissions)
        {
            _permissionRequestCompletion = new TaskCompletionSource<bool>();
            RequestPermissions(permissions, BluetoothPermissionRequestCode);
            return _permissionRequestCompletion.Task;
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            if (requestCode != BluetoothPermissionRequestCode)
            {
                return;
            }

            var granted = grantResults.Length > 0;
            foreach (var result in grantResults)
            {
                if (result != Permission.Granted)
                {
                    granted = false;
                }
            }

            _permissionRequestCompletion?.TrySetResult(granted);
            _permissionRequestCompletion = null;
        }
    }
}
```

(Если сигнатура `OnRequestPermissionsResult` в установленной версии биндингов помечает параметры `permissions`/`grantResults` как nullable-массивы — поправить сигнатуру по подсказке компилятора, сохранив логику.)

- [ ] **Step 2: Создать `AndroidPermissions.cs`**

```csharp
using System.Threading.Tasks;
using Android.Content.PM;

namespace ArctZ.Android;

/// <summary>
/// Тонкая обёртка над CheckSelfPermission/RequestPermissions без AndroidX —
/// в csproj нет AndroidX.Core, а обе нужные операции есть в базовом Context/Activity.
/// </summary>
public sealed class AndroidPermissions
{
    public Task<bool> RequestAsync(string[] permissions)
    {
        var context = global::Android.App.Application.Context;
        var missing = System.Array.FindAll(permissions, p => context.CheckSelfPermission(p) != Permission.Granted);

        if (missing.Length == 0)
        {
            return Task.FromResult(true);
        }

        var activity = MainActivity.Instance;
        return activity is null
            ? Task.FromResult(false)
            : activity.RequestPermissionsAsync(missing);
    }
}
```

- [ ] **Step 3: Удалить заглушку транспорта**

Run: `git rm ArctZ.Android/NotSupportedDeviceTransport.cs`

- [ ] **Step 4: Создать `AndroidBluetoothTransport.cs`**

```csharp
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android.Bluetooth;
using ArctZ.Services.Device;
using Java.Util;

namespace ArctZ.Android;

/// <summary>
/// Реальный транспорт для Android: Bluetooth Classic RFCOMM/SPP к FluidNC
/// (только ESP32 WROOM/WROVER отдают BT-SPP; BLE тут не подходит).
/// `deviceId`, передаваемый в ConnectAsync, — MAC-адрес устройства.
/// </summary>
public sealed class AndroidBluetoothTransport : IDeviceTransport
{
    private static readonly UUID SppUuid = UUID.FromString("00001101-0000-1000-8000-00805F9B34FB")!;
    private const int ReadBufferSize = 1024;

    private readonly AndroidPermissions _permissions;
    private readonly LineAssembler _lineAssembler = new();
    private BluetoothSocket? _socket;
    private CancellationTokenSource? _readLoopCts;

    public AndroidBluetoothTransport(AndroidPermissions permissions)
    {
        _permissions = permissions;
    }

    public bool IsSupported => BluetoothAdapter.DefaultAdapter is not null;

    public bool IsConnected => _socket?.IsConnected ?? false;

    public event Action<string>? LineReceived;

    public event Action? Disconnected;

    public async Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        // DeviceSession вызывает ConnectAsync повторно в цикле переподключения без
        // промежуточного DisconnectAsync — тот же приём, что в DesktopSerialTransport.
        CloseSocket();

        var granted = await _permissions.RequestAsync(ConnectPermissions()).ConfigureAwait(false);
        if (!granted)
        {
            throw new InvalidOperationException("Нет разрешения на подключение по Bluetooth.");
        }

        var adapter = BluetoothAdapter.DefaultAdapter
            ?? throw new InvalidOperationException("Bluetooth недоступен на этом устройстве.");

        adapter.CancelDiscovery();

        var device = adapter.GetRemoteDevice(deviceId)
            ?? throw new InvalidOperationException("Устройство не найдено.");

        var socket = device.CreateRfcommSocketToServiceRecord(SppUuid)
            ?? throw new InvalidOperationException("Не удалось создать соединение с устройством.");

        await Task.Run(() => socket.Connect(), cancellationToken).ConfigureAwait(false);

        _socket = socket;
        var cts = new CancellationTokenSource();
        _readLoopCts = cts;
        _ = Task.Factory.StartNew(
            () => ReadLoop(socket, cts.Token),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public Task DisconnectAsync()
    {
        CloseSocket();
        return Task.CompletedTask;
    }

    public async Task SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        var socket = _socket;
        if (socket?.OutputStream is null)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        await Task.Run(() =>
        {
            socket.OutputStream.Write(bytes, 0, bytes.Length);
            socket.OutputStream.Flush();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default)
    {
        var socket = _socket;
        if (socket?.OutputStream is null)
        {
            return;
        }

        await Task.Run(() =>
        {
            socket.OutputStream.Write(new[] { value }, 0, 1);
            socket.OutputStream.Flush();
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string[] ConnectPermissions() =>
        OperatingSystem.IsAndroidVersionAtLeast(31)
            ? new[] { "android.permission.BLUETOOTH_CONNECT" }
            : new[] { "android.permission.BLUETOOTH" };

    private void ReadLoop(BluetoothSocket socket, CancellationToken cancellationToken)
    {
        var stream = socket.InputStream;
        if (stream is null)
        {
            RaiseDisconnected();
            return;
        }

        var buffer = new byte[ReadBufferSize];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                foreach (var line in _lineAssembler.Append(buffer, read))
                {
                    LineReceived?.Invoke(line);
                }
            }
        }
        catch (Java.IO.IOException)
        {
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            RaiseDisconnected();
        }
    }

    private void RaiseDisconnected()
    {
        CloseSocket();
        Disconnected?.Invoke();
    }

    private void CloseSocket()
    {
        _readLoopCts?.Cancel();
        _readLoopCts = null;

        var socket = _socket;
        _socket = null;
        if (socket is null)
        {
            return;
        }

        try
        {
            socket.Close();
        }
        catch (Java.IO.IOException)
        {
        }
    }
}
```

- [ ] **Step 5: Зарегистрировать реальный транспорт в `Application.cs`**

Modify `ArctZ.Android/Application.cs:20-32`:

```csharp
        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            var services = new ServiceCollection();
            services.AddArctZCore();
            var permissions = new AndroidPermissions();
            services.AddSingleton(permissions);
            services.AddSingleton<IDeviceTransport>(new AndroidBluetoothTransport(permissions));
            services.AddSingleton<IProgramStorage>(_ => new JsonFileProgramStorage(
                Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "ArctZ", "Programs")));
            App.Services = services.BuildServiceProvider();

            return base.CustomizeAppBuilder(builder)
                .UseReactiveUI(b => b.WithAvalonia())
                .WithInterFont();
        }
```

- [ ] **Step 6: Собрать Android-голову**

Run: `dotnet build ArctZ.Android/ArctZ.Android.csproj`
Expected: сборка без ошибок. Если конкретные имена Android API (например, точное имя перегрузки/свойства в установленной версии `Mono.Android`) отличаются — поправить по подсказке компилятора, сохраняя структуру (guard-в-начале-ConnectAsync, блокирующий read-loop на выделенном `LongRunning`-таске, закрытие сокета для прерывания `Read()`).

- [ ] **Step 7: Прогнать полный набор тестов ядра (транспорт Android тестами не покрыт, но регрессий в ядре быть не должно)**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add ArctZ.Android/MainActivity.cs ArctZ.Android/AndroidPermissions.cs ArctZ.Android/AndroidBluetoothTransport.cs ArctZ.Android/Application.cs
git rm ArctZ.Android/NotSupportedDeviceTransport.cs
git commit -m "feat: implement real Android Bluetooth SPP transport"
```

---

### Task 9: `AndroidBluetoothEndpointProvider`

**Files:**
- Create: `ArctZ.Android/AndroidBluetoothEndpointProvider.cs`
- Modify: `ArctZ.Android/Application.cs`

**Interfaces:**
- Consumes: `AndroidPermissions` (Task 8), `IDeviceEndpointProvider`/`DeviceEndpointInfo` (Task 2).
- Produces: `AndroidBluetoothEndpointProvider : IDeviceEndpointProvider` — список спаренных устройств, поиск в эфире, спаривание.

- [ ] **Step 1: Создать `AndroidBluetoothEndpointProvider.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.Bluetooth;
using Android.Content;
using ArctZ.Services.Device;

namespace ArctZ.Android;

/// <summary>
/// IDeviceEndpointProvider поверх BluetoothAdapter: спаренные устройства через
/// BondedDevices, поиск через ACTION_FOUND/ACTION_DISCOVERY_FINISHED, спаривание
/// через CreateBond + ACTION_BOND_STATE_CHANGED.
/// </summary>
public sealed class AndroidBluetoothEndpointProvider : IDeviceEndpointProvider
{
    private static readonly TimeSpan PairTimeout = TimeSpan.FromSeconds(60);

    private readonly AndroidPermissions _permissions;

    public AndroidBluetoothEndpointProvider(AndroidPermissions permissions)
    {
        _permissions = permissions;
    }

    public bool SupportsDiscovery => true;

    public async Task<IReadOnlyList<DeviceEndpointInfo>> GetKnownEndpointsAsync(CancellationToken cancellationToken = default)
    {
        var granted = await _permissions.RequestAsync(ConnectPermissions()).ConfigureAwait(false);
        if (!granted)
        {
            throw new InvalidOperationException("Нет разрешения на использование Bluetooth.");
        }

        var adapter = BluetoothAdapter.DefaultAdapter
            ?? throw new InvalidOperationException("Bluetooth недоступен на этом устройстве.");

        return adapter.BondedDevices?
            .Select(d => new DeviceEndpointInfo(d.Address!, d.Name ?? d.Address!, true))
            .ToList<DeviceEndpointInfo>()
            ?? new List<DeviceEndpointInfo>();
    }

    public IObservable<DeviceEndpointInfo> Discover() => Observable.Create<DeviceEndpointInfo>(observer =>
    {
        var adapter = BluetoothAdapter.DefaultAdapter;
        if (adapter is null)
        {
            observer.OnCompleted();
            return () => { };
        }

        var context = global::Android.App.Application.Context;
        var receiver = new DiscoveryReceiver(observer);
        var filter = new IntentFilter();
        filter.AddAction(BluetoothDevice.ActionFound!);
        filter.AddAction(BluetoothAdapter.ActionDiscoveryFinished!);

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            context.RegisterReceiver(receiver, filter, ReceiverFlags.NotExported);
        }
        else
        {
            context.RegisterReceiver(receiver, filter);
        }

        adapter.StartDiscovery();

        return () =>
        {
            adapter.CancelDiscovery();
            context.UnregisterReceiver(receiver);
        };
    });

    public async Task<bool> PairAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var granted = await _permissions.RequestAsync(ConnectPermissions()).ConfigureAwait(false);
        if (!granted)
        {
            throw new InvalidOperationException("Нет разрешения на использование Bluetooth.");
        }

        var adapter = BluetoothAdapter.DefaultAdapter
            ?? throw new InvalidOperationException("Bluetooth недоступен на этом устройстве.");

        var device = adapter.GetRemoteDevice(deviceId)
            ?? throw new InvalidOperationException("Устройство не найдено.");

        if (device.BondState == Bond.Bonded)
        {
            return true;
        }

        var tcs = new TaskCompletionSource<bool>();
        var context = global::Android.App.Application.Context;
        var receiver = new BondStateReceiver(deviceId, tcs);
        context.RegisterReceiver(receiver, new IntentFilter(BluetoothDevice.ActionBondStateChanged));

        try
        {
            device.CreateBond();
            using var timeoutCts = new CancellationTokenSource(PairTimeout);
            using var registration = timeoutCts.Token.Register(() => tcs.TrySetResult(false));
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            context.UnregisterReceiver(receiver);
        }
    }

    private static string[] ConnectPermissions() =>
        OperatingSystem.IsAndroidVersionAtLeast(31)
            ? new[] { "android.permission.BLUETOOTH_CONNECT" }
            : new[] { "android.permission.BLUETOOTH" };

    private sealed class DiscoveryReceiver : BroadcastReceiver
    {
        private readonly IObserver<DeviceEndpointInfo> _observer;

        public DiscoveryReceiver(IObserver<DeviceEndpointInfo> observer)
        {
            _observer = observer;
        }

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent is null)
            {
                return;
            }

            if (intent.Action == BluetoothAdapter.ActionDiscoveryFinished)
            {
                _observer.OnCompleted();
                return;
            }

            if (intent.Action != BluetoothDevice.ActionFound)
            {
                return;
            }

#pragma warning disable CA1422, CS0618
            var device = intent.GetParcelableExtra(BluetoothDevice.ExtraDevice) as BluetoothDevice;
#pragma warning restore CA1422, CS0618

            if (device?.Address is null)
            {
                return;
            }

            _observer.OnNext(new DeviceEndpointInfo(device.Address, device.Name ?? device.Address, device.BondState == Bond.Bonded));
        }
    }

    private sealed class BondStateReceiver : BroadcastReceiver
    {
        private readonly string _deviceId;
        private readonly TaskCompletionSource<bool> _completion;

        public BondStateReceiver(string deviceId, TaskCompletionSource<bool> completion)
        {
            _deviceId = deviceId;
            _completion = completion;
        }

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action != BluetoothDevice.ActionBondStateChanged)
            {
                return;
            }

#pragma warning disable CA1422, CS0618
            var device = intent.GetParcelableExtra(BluetoothDevice.ExtraDevice) as BluetoothDevice;
#pragma warning restore CA1422, CS0618

            if (device?.Address != _deviceId)
            {
                return;
            }

            var bondState = (Bond)intent.GetIntExtra(BluetoothDevice.ExtraBondState, (int)Bond.None);
            if (bondState == Bond.Bonded)
            {
                _completion.TrySetResult(true);
            }
            else if (bondState == Bond.None)
            {
                _completion.TrySetResult(false);
            }
        }
    }
}
```

Примечания для сборки (не догадки «что попробовать», а конкретные точки, где может понадобиться правка под установленную версию биндингов):
- Если `ReceiverFlags.NotExported` отсутствует в установленной версии `Mono.Android` — убрать ветку `OperatingSystem.IsAndroidVersionAtLeast(33)` и всегда звать двухаргументный `RegisterReceiver(receiver, filter)` (системные широковещательные intent'ы вроде `ACTION_FOUND` не требуют флага экспорта).
- `Intent.GetParcelableExtra(string)` помечен устаревшим начиная с API 33, но продолжает работать — предупреждение компилятора подавлено `#pragma warning disable`, ошибкой сборки не является.

- [ ] **Step 2: Зарегистрировать провайдер в `Application.cs`**

Modify `ArctZ.Android/Application.cs` — после `services.AddSingleton<IDeviceTransport>(new AndroidBluetoothTransport(permissions));` добавить:

```csharp
            services.AddSingleton<IDeviceEndpointProvider>(new AndroidBluetoothEndpointProvider(permissions));
```

(Регистрация происходит после `services.AddArctZCore()`, которая уже зарегистрировала `SingleRealDeviceEndpointProvider` — в Microsoft.Extensions.DependencyInjection побеждает последняя регистрация, так что Android получает свою реализацию.)

- [ ] **Step 3: Собрать Android-голову**

Run: `dotnet build ArctZ.Android/ArctZ.Android.csproj`
Expected: сборка без ошибок (см. примечания к Step 1 при расхождениях в API поверхности).

- [ ] **Step 4: Прогнать полный набор тестов ядра**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ArctZ.Android/AndroidBluetoothEndpointProvider.cs ArctZ.Android/Application.cs
git commit -m "feat: implement Android Bluetooth endpoint discovery and pairing"
```

---

### Task 10: Финальная проверка на устройстве

**Files:** нет (только сборка/установка и ручная проверка).

**Interfaces:** нет новых — сквозная проверка всего, что добавили Tasks 1-9.

- [ ] **Step 1: Собрать и установить финальную сборку**

Run: `dotnet build ArctZ.Android/ArctZ.Android.csproj -t:Install` (телефон подключён по USB, отладка включена).

- [ ] **Step 2: Запустить приложение на телефоне**

Открыть «ArctZ» на телефоне вручную. При проблемах — `adb logcat -s ArctZ:V mono-stdout:V DOTNET:V`.

- [ ] **Step 3: Попросить пользователя пройти чек-лист из спеки и задать вопросы через `AskUserQuestion`**

Согласно `docs/superpowers/specs/2026-08-12-android-bluetooth-support-design.md` (раздел «Проверка на устройстве»), задать по одному вопросу на каждый пункт — двумя вызовами `AskUserQuestion` (лимит инструмента — 4 вопроса за вызов), например 4+2:

Первый вызов (4 вопроса):
1. Приложение ставится и запускается, экран подключения виден?
2. Запрос разрешений Bluetooth появляется, и после согласия список спаренных устройств заполняется?
3. Кнопка «Поиск» находит устройства в эфире, статус «не спарено» виден?
4. Выбор неспаренного устройства запускает системное спаривание?

Второй вызов (2 вопроса):
5. Режим «Демо» работает как на Desktop (движение, программа, лог G-code)?
6. Попытка подключения к недоступному устройству даёт понятную ошибку, а не зависание?

Явно отметить в отчёте пользователю: реальный обмен G-code с физическим контроллером FluidNC не проверялся (железа нет) — эта часть остаётся не подтверждённой на реальном устройстве.

- [ ] **Step 4: Зафиксировать результат**

Если все пункты подтверждены — задача считается завершённой. Если пользователь сообщает о проблеме по конкретному пункту — завести её как заметку/todo для отдельного фикса, не пытаясь исправлять вслепую без доступа к логам/устройству в моменте.

- [ ] **Step 5: Итоговый commit (если по ходу проверки потребовались правки)**

Если Step 3-4 выявили и потребовали точечных фиксов — закоммитить их отдельно с понятным сообщением, ссылающимся на конкретный найденный симптом (например: `fix: request BLUETOOTH_SCAN before starting discovery on API 31+`). Если правок не было — этот шаг пропускается.
