# Автоподключение к FluidNC по имени + заставка — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** При старте приложения (и после потери связи) автоматически находить устройство с
именем, начинающимся на «FluidNC», подключаться к нему без участия пользователя, и на время
поиска/подключения/переподключения показывать полноэкранную заставку вместо текущей модалки
со списком устройств. Ручная модалка остаётся резервным путём, когда автоматика не справилась.

**Architecture:** Новый оркестратор `AutoConnectAsync` в `ConnectionViewModel` — цикл до 5
попыток (график задержек 1/2/4/8/8 с) «найти по имени → подключиться», запускаемый явно из
`App.axaml.cs` при старте, а не из конструктора VM (иначе фоновая активность стартовала бы в
каждом из ~50 существующих тестов, создающих VM напрямую). Существующий быстрый внутренний
реконнект `DeviceSession` (3×200мс, к тому же `deviceId`) не меняется — он ловит короткие
обрывы; когда он сдаётся, за дело снова берётся оркестратор (полное пересканирование). Новые
вычисляемые свойства `IsAutoConnectSplashVisible`/`AutoConnectStatusText` управляют новой
заставкой в `MainView.axaml`, вытесняя существующую модалку со списком, пока автоматика активна.
На Desktop добавляется провайдер эндпоинтов с реальными именами устройств через WMI (сейчас там
единственный синтетический эндпоинт без имени — сопоставление по «FluidNC» было бы невозможно).

**Tech Stack:** .NET 10 / Avalonia / ReactiveUI (`ReactiveUI.SourceGenerators` `[Reactive]`,
`WhenAnyValue`, `System.Reactive.Linq`), xUnit, `System.Management` (WMI, Desktop-only).

**Spec:** [docs/superpowers/specs/2026-08-15-auto-connect-fluidnc-design.md](../specs/2026-08-15-auto-connect-fluidnc-design.md)

## Global Constraints

- Платформы: Android и Desktop. Browser не подключается автоматически (Web Serial требует
  явного жеста пользователя) — остаётся на ручной модалке без изменений.
- Сопоставление имени: `DisplayName.StartsWith("FluidNC", StringComparison.OrdinalIgnoreCase)`.
- Оркестратор автоподключения: 5 попыток, паузы между ними 1/2/4/8/8 секунд (`ExponentialBackoffReconnectPolicy.DefaultDelays`), затем — сдаться и показать ручную модалку.
- Заставка минимальная: `ProgressBar IsIndeterminate="True"` + текстовый статус, без логотипа/списка шагов.
- Автоподключение запускается один раз, явно, из `App.axaml.cs` → `OnFrameworkInitializationCompleted` — **никогда** из конструктора `ConnectionViewModel`.
- Быстрый внутренний реконнект `DeviceSession` (3 попытки, 200мс, к тому же `deviceId`) **не меняется** — трогать `DeviceSession`/`IDeviceSession`/`DeviceSessionFactory` в этом плане не нужно.
- Явное «Отключить» подавляет автоперезапуск до следующего успешного подключения (ручного или автоматического).
- Обрыв связи во время выполнения программы — существующий механизм `PlaybackState.Faulted` в `ProgramViewModel` не меняется.

---

## File Structure

Новые файлы:
- `ArctZ/Services/Device/FluidNcDeviceName.cs` — чистая функция сопоставления имени.
- `ArctZ/Services/Device/ExponentialBackoffReconnectPolicy.cs` — `IReconnectPolicy` с графиком задержек.
- `ArctZ/ViewModels/AutoConnectPhase.cs` — фазы оркестратора.
- `ArctZ.Desktop/DesktopBluetoothEndpointProvider.cs` — WMI-провайдер эндпоинтов для Desktop.
- `ArctZ.Tests/Services/Device/FluidNcDeviceNameTests.cs`
- `ArctZ.Tests/Services/Device/ExponentialBackoffReconnectPolicyTests.cs`

Изменяемые файлы:
- `ArctZ/ViewModels/ConnectionViewModel.cs` — основная логика (несколько задач подряд).
- `ArctZ/Views/MainView.axaml` — заставка автоподключения.
- `ArctZ/App.axaml.cs` — запуск `AutoConnectAsync()` при старте.
- `ArctZ.Desktop/Program.cs` — регистрация `DesktopBluetoothEndpointProvider`.
- `ArctZ.Desktop/ArctZ.Desktop.csproj`, `Directory.Packages.props` — пакет `System.Management`.
- `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs` — переписанные + новые тесты.

`DeviceSession.cs`, `IDeviceSession.cs`, `DeviceSessionFactory.cs`, `IReconnectPolicy.cs`,
`FixedDelayReconnectPolicy.cs` — **не трогаем** (см. Global Constraints).

---

### Task 1: `FluidNcDeviceName` — сопоставление имени устройства

**Files:**
- Create: `ArctZ/Services/Device/FluidNcDeviceName.cs`
- Test: `ArctZ.Tests/Services/Device/FluidNcDeviceNameTests.cs`

**Interfaces:**
- Produces: `public static class FluidNcDeviceName { public static bool Matches(string? name); }`

- [ ] **Step 1: Write the failing test**

```csharp
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class FluidNcDeviceNameTests
{
    [Theory]
    [InlineData("FluidNC")]
    [InlineData("FluidNC-1234")]
    [InlineData("fluidnc_jib")]
    [InlineData("FLUIDNC")]
    public void Matches_NameStartingWithFluidNcCaseInsensitive_ReturnsTrue(string name)
    {
        Assert.True(FluidNcDeviceName.Matches(name));
    }

    [Theory]
    [InlineData("Jib FluidNC")]
    [InlineData("Some Other Device")]
    [InlineData("")]
    [InlineData(null)]
    public void Matches_NameNotStartingWithFluidNc_ReturnsFalse(string? name)
    {
        Assert.False(FluidNcDeviceName.Matches(name));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~FluidNcDeviceNameTests"`
Expected: FAIL to build — `FluidNcDeviceName` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System;

namespace ArctZ.Services.Device;

/// <summary>Матчинг имени устройства для автоподключения: устройства FluidNC отдают своё
/// имя с префиксом "FluidNC" (например "FluidNC-1234"). Регистр не учитывается.</summary>
public static class FluidNcDeviceName
{
    public static bool Matches(string? name) =>
        name is not null && name.StartsWith("FluidNC", StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~FluidNcDeviceNameTests"`
Expected: PASS (8 тестов: 4 true-кейса + 4 false-кейса)

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Services/Device/FluidNcDeviceName.cs ArctZ.Tests/Services/Device/FluidNcDeviceNameTests.cs
git commit -m "feat: add FluidNC device name matcher"
```

---

### Task 2: `ExponentialBackoffReconnectPolicy`

**Files:**
- Create: `ArctZ/Services/Device/ExponentialBackoffReconnectPolicy.cs`
- Test: `ArctZ.Tests/Services/Device/ExponentialBackoffReconnectPolicyTests.cs`

**Interfaces:**
- Consumes: `ArctZ.Services.Device.IReconnectPolicy` (existing — `int MaxAttempts { get; }`, `Task WaitBeforeRetryAsync(int attemptNumber, CancellationToken ct = default)`).
- Produces: `public sealed class ExponentialBackoffReconnectPolicy : IReconnectPolicy`, constructor `(IReadOnlyList<TimeSpan> delays)`, static `IReadOnlyList<TimeSpan> DefaultDelays` (5 entries: 1s,2s,4s,8s,8s). Used by Task 6 as the default for `ConnectionViewModel`'s new constructor parameter.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class ExponentialBackoffReconnectPolicyTests
{
    [Fact]
    public void MaxAttempts_EqualsNumberOfDelays()
    {
        var policy = new ExponentialBackoffReconnectPolicy(new[]
        {
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4),
        });

        Assert.Equal(3, policy.MaxAttempts);
    }

    [Fact]
    public async Task WaitBeforeRetryAsync_UsesDelayForGivenAttempt()
    {
        var policy = new ExponentialBackoffReconnectPolicy(new[]
        {
            TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(60),
        });

        var stopwatch = Stopwatch.StartNew();
        await policy.WaitBeforeRetryAsync(attemptNumber: 2);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds >= 50, $"Expected >= 50ms, was {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Constructor_EmptyDelays_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ExponentialBackoffReconnectPolicy(Array.Empty<TimeSpan>()));
    }

    [Fact]
    public void DefaultDelays_Has5EntriesEndingAt8Seconds()
    {
        Assert.Equal(5, ExponentialBackoffReconnectPolicy.DefaultDelays.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), ExponentialBackoffReconnectPolicy.DefaultDelays[0]);
        Assert.Equal(TimeSpan.FromSeconds(2), ExponentialBackoffReconnectPolicy.DefaultDelays[1]);
        Assert.Equal(TimeSpan.FromSeconds(4), ExponentialBackoffReconnectPolicy.DefaultDelays[2]);
        Assert.Equal(TimeSpan.FromSeconds(8), ExponentialBackoffReconnectPolicy.DefaultDelays[3]);
        Assert.Equal(TimeSpan.FromSeconds(8), ExponentialBackoffReconnectPolicy.DefaultDelays[4]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ExponentialBackoffReconnectPolicyTests"`
Expected: FAIL to build — type does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

/// <summary>Reconnect policy with a caller-supplied, per-attempt delay schedule (e.g. 1/2/4/8/8s).
/// MaxAttempts is simply the schedule's length — attempt N waits delays[N-1]. Used by
/// ConnectionViewModel's name-search auto-connect orchestrator (see AutoConnectAsync); the
/// existing fast DeviceSession-internal reconnect-to-known-id loop is untouched by this class.</summary>
public sealed class ExponentialBackoffReconnectPolicy : IReconnectPolicy
{
    /// <summary>Production default: 5 attempts, 1/2/4/8/8 seconds apart.</summary>
    public static readonly IReadOnlyList<TimeSpan> DefaultDelays = new[]
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(8),
    };

    private readonly IReadOnlyList<TimeSpan> _delays;

    public ExponentialBackoffReconnectPolicy(IReadOnlyList<TimeSpan> delays)
    {
        if (delays.Count == 0)
        {
            throw new ArgumentException("At least one delay is required.", nameof(delays));
        }

        _delays = delays;
    }

    public int MaxAttempts => _delays.Count;

    public Task WaitBeforeRetryAsync(int attemptNumber, CancellationToken cancellationToken = default) =>
        Task.Delay(_delays[attemptNumber - 1], cancellationToken);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ExponentialBackoffReconnectPolicyTests"`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Services/Device/ExponentialBackoffReconnectPolicy.cs ArctZ.Tests/Services/Device/ExponentialBackoffReconnectPolicyTests.cs
git commit -m "feat: add exponential backoff reconnect policy for auto-connect"
```

---

### Task 3: `AutoConnectPhase` enum + `ConnectionViewModel` constructor/reactive scaffolding

**Files:**
- Create: `ArctZ/ViewModels/AutoConnectPhase.cs`
- Modify: `ArctZ/ViewModels/ConnectionViewModel.cs`
- Test: `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs`

**Interfaces:**
- Consumes: `ExponentialBackoffReconnectPolicy` (Task 2), `IReconnectPolicy` (existing).
- Produces: `public enum AutoConnectPhase { Idle, Searching, Connecting, WaitingRetry, GivenUp }`;
  new `ConnectionViewModel` constructor parameter `IReconnectPolicy? autoConnectRetryPolicy = null`
  (existing 4-arg call sites keep compiling unchanged); reactive properties `AutoConnectPhase`,
  `AutoConnectAttempt` (int); computed `AutoConnectMaxAttempts` (int). Consumed by Task 5
  (`AutoConnectAsync`) and Task 7 (splash computed properties).

**Files touched in `ConnectionViewModel.cs` for this task** (exact anchors from the current file):
- Constructor parameter list at [ConnectionViewModel.cs:125-129](../../../ArctZ/ViewModels/ConnectionViewModel.cs#L125-L129).
- New fields near the existing `[Reactive]` block at [ConnectionViewModel.cs:30-55](../../../ArctZ/ViewModels/ConnectionViewModel.cs#L30-L55).

- [ ] **Step 1: Write the failing test**

```csharp
// In ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs, add:

[Fact]
public async Task Constructor_DefaultAutoConnectRetryPolicy_HasFiveMaxAttempts()
{
    var vm = await CreateVmAsync(new FakeDeviceTransport());

    Assert.Equal(5, vm.AutoConnectMaxAttempts);
    Assert.Equal(AutoConnectPhase.Idle, vm.AutoConnectPhase);
    Assert.Equal(0, vm.AutoConnectAttempt);
}

[Fact]
public async Task Constructor_CustomAutoConnectRetryPolicy_IsUsedInsteadOfDefault()
{
    var customPolicy = new FixedDelayReconnectPolicy(maxAttempts: 2, delay: TimeSpan.FromMilliseconds(1));
    var vm = await CreateVmAsync(new FakeDeviceTransport(), autoConnectRetryPolicy: customPolicy);

    Assert.Equal(2, vm.AutoConnectMaxAttempts);
}
```

Also extend the test helper right above these tests:

```csharp
private static async Task<ConnectionViewModel> CreateVmAsync(
    IDeviceTransport realTransport,
    IDeviceTransport? demoTransport = null,
    IDeviceEndpointProvider? endpointProvider = null,
    IReconnectPolicy? autoConnectRetryPolicy = null)
{
    var vm = new ConnectionViewModel(
        realTransport,
        () => demoTransport ?? new FakeDeviceTransport(),
        new DeviceSessionFactory(MachineLimits.Default),
        endpointProvider ?? DefaultEndpointProvider(),
        autoConnectRetryPolicy);
    await vm.RefreshEndpointsCommand.Execute();
    return vm;
}
```

(This replaces the existing 3-parameter `CreateVmAsync` at [ConnectionViewModelTests.cs:18-30](../../../ArctZ/../ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs#L18-L30) — every existing call site keeps compiling because the new parameter is optional and appended last.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ConnectionViewModelTests"`
Expected: FAIL to build — `AutoConnectMaxAttempts`/`AutoConnectPhase`/`AutoConnectAttempt` don't exist, constructor has no 5th parameter.

- [ ] **Step 3: Write minimal implementation**

Create `ArctZ/ViewModels/AutoConnectPhase.cs`:

```csharp
namespace ArctZ.ViewModels;

/// <summary>Progress of ConnectionViewModel.AutoConnectAsync's find-and-connect loop.
/// Drives IsAutoConnectSplashVisible/AutoConnectStatusText (see Task 7).</summary>
public enum AutoConnectPhase
{
    Idle,
    Searching,
    Connecting,
    WaitingRetry,
    GivenUp
}
```

In `ArctZ/ViewModels/ConnectionViewModel.cs`, add fields next to the existing `[Reactive]` block (after `isScanning` at line 55):

```csharp
    [Reactive] private AutoConnectPhase autoConnectPhase = AutoConnectPhase.Idle;
    [Reactive] private int autoConnectAttempt;

    private readonly IReconnectPolicy _autoConnectRetryPolicy;
    private CancellationTokenSource? _autoConnectCts;
    private bool _autoConnectSuppressed;

    public int AutoConnectMaxAttempts => _autoConnectRetryPolicy.MaxAttempts;
```

Add `using System.Threading;` to the top of the file (needed for `CancellationTokenSource`; not currently imported — only `System.Threading.Tasks` is).

Update the constructor signature and body:

```csharp
    public ConnectionViewModel(
        IDeviceTransport realTransport,
        Func<IDeviceTransport> createDemoTransport,
        IDeviceSessionFactory sessionFactory,
        IDeviceEndpointProvider endpointProvider,
        IReconnectPolicy? autoConnectRetryPolicy = null)
    {
        _realTransport = realTransport;
        _createDemoTransport = createDemoTransport;
        _sessionFactory = sessionFactory;
        _endpointProvider = endpointProvider;
        _autoConnectRetryPolicy = autoConnectRetryPolicy ?? new ExponentialBackoffReconnectPolicy(ExponentialBackoffReconnectPolicy.DefaultDelays);
```

(the rest of the constructor body is unchanged for this task).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ConnectionViewModelTests"`
Expected: PASS, including all pre-existing `ConnectionViewModelTests` (constructor change is additive/optional, so nothing else should regress).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/ViewModels/AutoConnectPhase.cs ArctZ/ViewModels/ConnectionViewModel.cs ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs
git commit -m "feat: add auto-connect retry policy and phase scaffolding to ConnectionViewModel"
```

---

### Task 4: Manual command wrappers + auto-connect suppression

**Files:**
- Modify: `ArctZ/ViewModels/ConnectionViewModel.cs`
- Test: `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs`

**Interfaces:**
- Consumes: `_autoConnectCts`, `_autoConnectSuppressed` fields (Task 3).
- Produces: `private Task ManualConnectAsync()`, `private async Task ManualDisconnectAsync()` — these become the delegates for `ConnectCommand`/`DisconnectCommand` instead of `ConnectAsync`/`DisconnectAsync` directly. `ConnectAsync()`/`DisconnectAsync()` keep their existing signatures and are still called directly by `AutoConnectAsync` in Task 5 — this task doesn't touch their bodies.

This task establishes the rule: **any manual user action cancels whatever auto-connect loop is
in flight**, and an explicit disconnect must not let the auto-restart subscription (Task 6)
immediately reconnect on the user's behalf.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task ManualDisconnect_SuppressesAutoConnectUntilNextSuccessfulConnect()
{
    var realTransport = new FakeDeviceTransport();
    var vm = await CreateVmAsync(realTransport);
    await vm.ConnectCommand.Execute();

    await vm.DisconnectCommand.Execute();

    // A fire-and-forget AutoConnectAsync() call would flip AutoConnectPhase away from Idle
    // (at minimum to Searching) before its first await — asserting it stays Idle proves no
    // auto-connect loop was started by the explicit disconnect.
    Assert.Equal(AutoConnectPhase.Idle, vm.AutoConnectPhase);
    Assert.Null(vm.Session);

    // Manual reconnect clears the suppression: a subsequent involuntary loss should be free to
    // auto-restart again. This is exercised end-to-end in Task 6; here we only prove the
    // manual connect path still works after a manual disconnect.
    await vm.ConnectCommand.Execute();
    Assert.NotNull(vm.Session);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ManualDisconnect_SuppressesAutoConnect"`
Expected: PASS already by accident (no auto-connect loop exists yet to violate the assertion) —
this is expected; the test earns its keep once Task 6 exists. Confirm it compiles and passes now,
then move on — it becomes a real regression guard once the restart subscription lands.

- [ ] **Step 3: Write minimal implementation**

In `ArctZ/ViewModels/ConnectionViewModel.cs`, change the two command registrations:

```csharp
        ConnectCommand = Track(ReactiveCommand.CreateFromTask(ManualConnectAsync, canConnect)
            .Enhance(text: "Подключить", name: "ConnectCommand"));
        DisconnectCommand = Track(ReactiveCommand.CreateFromTask(ManualDisconnectAsync, notPlaybackLocked)
            .Enhance(text: "Отключить", name: "DisconnectCommand"));
```

(replacing the current `ConnectAsync`/`DisconnectAsync` references at
[ConnectionViewModel.cs:149-152](../../../ArctZ/ViewModels/ConnectionViewModel.cs#L149-L152)).

Add the two new wrapper methods right before the existing `private async Task ConnectAsync()`
method:

```csharp
    /// <summary>Entry point for the "Подключить" button. Cancels any in-flight AutoConnectAsync
    /// loop first — a manual choice must never race a background auto-connect attempt — and
    /// clears the suppression flag so a later involuntary disconnect is free to auto-restart.</summary>
    private Task ManualConnectAsync()
    {
        _autoConnectCts?.Cancel();
        _autoConnectSuppressed = false;
        return ConnectAsync();
    }

    /// <summary>Entry point for the "Отключить" button. Cancels any in-flight AutoConnectAsync
    /// loop and suppresses auto-restart (Task 6's subscription) until the next successful
    /// connect — the user turned it off on purpose, auto-connect must not turn it back on.</summary>
    private async Task ManualDisconnectAsync()
    {
        _autoConnectCts?.Cancel();
        _autoConnectSuppressed = true;
        await DisconnectAsync();
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ConnectionViewModelTests"`
Expected: PASS — full existing suite in this file stays green (ConnectCommand/DisconnectCommand
behavior is unchanged from the caller's perspective, only the delegate wrapping changed).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/ViewModels/ConnectionViewModel.cs ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs
git commit -m "feat: route manual connect/disconnect through auto-connect-aware wrappers"
```

---

### Task 5: `AutoConnectAsync` — the find-and-connect loop

**Files:**
- Modify: `ArctZ/ViewModels/ConnectionViewModel.cs`
- Test: `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs`

**Interfaces:**
- Consumes: `FluidNcDeviceName.Matches` (Task 1), `_autoConnectRetryPolicy`/`AutoConnectPhase`/`AutoConnectAttempt` (Task 3), `ManualConnectAsync`-established suppression fields (Task 4), existing private `ConnectAsync()`/`RefreshEndpointsAsync()`/`OnDeviceDiscovered(DeviceEndpointInfo)`/`IsDiscoverySupported`/`IsRealDeviceUnsupported`/`AvailableEndpoints`.
- Produces: `public async Task AutoConnectAsync(CancellationToken cancellationToken = default)` — the method `App.axaml.cs` (Task 8) calls at startup and the restart subscription (Task 6) calls again after a give-up.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task AutoConnectAsync_FindsFluidNcEndpointAmongKnown_ConnectsToIt()
{
    var realTransport = new FakeDeviceTransport();
    var provider = new FakeDeviceEndpointProvider
    {
        KnownEndpoints =
        {
            new DeviceEndpointInfo("other", "Some Other Device", true),
            new DeviceEndpointInfo("fluid1", "FluidNC-1234", true),
        },
    };
    var vm = await CreateVmAsync(realTransport, endpointProvider: provider);

    await vm.AutoConnectAsync();

    Assert.NotNull(vm.Session);
    Assert.Equal(ConnectionState.Connected, vm.Session!.ConnectionState);
    Assert.Equal("fluid1", vm.SelectedEndpoint!.Id);
    Assert.Equal(AutoConnectPhase.Idle, vm.AutoConnectPhase);
}

[Fact]
public async Task AutoConnectAsync_NoFluidNcAnywhere_GivesUpAfterConfiguredAttemptsAndShowsManualModal()
{
    var realTransport = new FakeDeviceTransport();
    var provider = new FakeDeviceEndpointProvider
    {
        SupportsDiscovery = false,
        KnownEndpoints = { new DeviceEndpointInfo("other", "Some Other Device", true) },
    };
    var fastPolicy = new FixedDelayReconnectPolicy(maxAttempts: 2, delay: TimeSpan.FromMilliseconds(1));
    var vm = await CreateVmAsync(realTransport, endpointProvider: provider, autoConnectRetryPolicy: fastPolicy);

    await vm.AutoConnectAsync();

    Assert.Null(vm.Session);
    Assert.Equal(AutoConnectPhase.GivenUp, vm.AutoConnectPhase);
    Assert.True(vm.IsConnectionModalVisible);
    Assert.Equal("Устройство FluidNC не найдено.", vm.EndpointError);
}

[Fact]
public async Task AutoConnectAsync_NoKnownMatch_FindsFluidNcViaDiscoveryScan()
{
    var realTransport = new FakeDeviceTransport();
    var provider = new FakeDeviceEndpointProvider
    {
        SupportsDiscovery = true,
        KnownEndpoints = { new DeviceEndpointInfo("other", "Some Other Device", true) },
    };
    var vm = await CreateVmAsync(realTransport, endpointProvider: provider);

    var autoConnectTask = vm.AutoConnectAsync();
    provider.DiscoverySubject.OnNext(new DeviceEndpointInfo("found1", "FluidNC-ABCD", false));
    provider.DiscoverySubject.OnCompleted();
    await autoConnectTask;

    Assert.NotNull(vm.Session);
    Assert.Equal("found1", vm.SelectedEndpoint!.Id);
    Assert.Contains("found1", provider.PairedIds); // was IsPaired: false — must pair before connecting
}

[Fact]
public async Task AutoConnectAsync_RealDeviceUnsupported_ReturnsImmediatelyWithoutTryingToConnect()
{
    var realTransport = new FakeDeviceTransport { IsSupported = false };
    var vm = await CreateVmAsync(realTransport);

    await vm.AutoConnectAsync();

    Assert.Equal(AutoConnectPhase.Idle, vm.AutoConnectPhase);
    Assert.Null(vm.Session);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ConnectionViewModelTests"`
Expected: FAIL to build — `AutoConnectAsync` does not exist yet.

- [ ] **Step 3: Write minimal implementation**

`System.Reactive.Linq` is already imported (existing file header). Add
`using System.Reactive.Threading.Tasks;` to the top of the file as well — it's required for the
`.ToTask(CancellationToken)` extension on `IObservable<T>` used below (a different namespace
from the `System.Reactive.Linq` operators like `.Timeout`/`.Catch`, which are already available).
Then add, near the top of the class, alongside the other `private const` fields:

```csharp
    private static readonly TimeSpan AutoConnectScanWindow = TimeSpan.FromSeconds(10);
```

Add the two new methods, placed after `DisconnectAsync()` (the existing private core method):

```csharp
    /// <summary>Finds a FluidNC-named device and connects to it, retrying with the configured
    /// backoff schedule up to _autoConnectRetryPolicy.MaxAttempts times before giving up and
    /// leaving the manual connection modal (IsConnectionModalVisible) as the fallback. Safe to
    /// call multiple times — a new call cancels whatever call is currently in flight. Never
    /// called from the constructor (see Global Constraints) — App.axaml.cs calls it once at
    /// startup, and the restart subscription in the constructor (Task 6) calls it again after
    /// DeviceSession's own fast reconnect-to-known-id loop gives up.</summary>
    public async Task AutoConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsRealDeviceUnsupported)
        {
            return;
        }

        _autoConnectCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _autoConnectCts = cts;
        var token = cts.Token;

        try
        {
            for (var attempt = 1; attempt <= _autoConnectRetryPolicy.MaxAttempts; attempt++)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                AutoConnectAttempt = attempt;
                AutoConnectPhase = AutoConnectPhase.Searching;

                var endpoint = await FindFluidNcEndpointAsync(token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (endpoint is not null)
                {
                    SelectedEndpoint = endpoint;
                    AutoConnectPhase = AutoConnectPhase.Connecting;
                    await ConnectAsync();

                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    if (Session?.ConnectionState == ConnectionState.Connected)
                    {
                        AutoConnectPhase = AutoConnectPhase.Idle;
                        _autoConnectSuppressed = false;
                        return;
                    }
                }

                if (attempt < _autoConnectRetryPolicy.MaxAttempts)
                {
                    AutoConnectPhase = AutoConnectPhase.WaitingRetry;
                    await _autoConnectRetryPolicy.WaitBeforeRetryAsync(attempt, token);
                }
            }

            AutoConnectPhase = AutoConnectPhase.GivenUp;
            EndpointError ??= "Устройство FluidNC не найдено.";
        }
        catch (OperationCanceledException)
        {
            // Superseded by ManualConnectAsync/ManualDisconnectAsync (Task 4) or app shutdown —
            // leave whatever state that action already set; nothing to clean up here.
        }
        finally
        {
            if (ReferenceEquals(_autoConnectCts, cts))
            {
                _autoConnectCts = null;
            }
        }
    }

    /// <summary>Looks for a FluidNC-named endpoint: first among already-known endpoints
    /// (RefreshEndpointsAsync), then — if the platform supports it — via a bounded discovery
    /// scan. Every discovered endpoint (matching or not) is still merged into AvailableEndpoints
    /// via OnDeviceDiscovered, exactly like the manual ScanCommand does, so a user who takes over
    /// manually after a give-up sees the same list a manual scan would have produced.</summary>
    private async Task<ConnectionEndpoint?> FindFluidNcEndpointAsync(CancellationToken cancellationToken)
    {
        await RefreshEndpointsAsync();

        var known = AvailableEndpoints.FirstOrDefault(e =>
            e.Kind == ConnectionEndpointKind.RealDevice && FluidNcDeviceName.Matches(e.DisplayName));
        if (known is not null || !IsDiscoverySupported)
        {
            return known;
        }

        var match = await _endpointProvider.Discover()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Do(OnDeviceDiscovered)
            .Where(info => FluidNcDeviceName.Matches(info.Name))
            .Take(1)
            .Select(info => (DeviceEndpointInfo?)info)
            .Timeout(AutoConnectScanWindow, Observable.Return((DeviceEndpointInfo?)null))
            .Catch(Observable.Return((DeviceEndpointInfo?)null))
            .FirstOrDefaultAsync()
            .ToTask(cancellationToken);

        return match is null ? null : AvailableEndpoints.FirstOrDefault(e => e.Id == match.Id);
    }
```

Note: `AutoConnectAsync`'s loop calls the existing private `ConnectAsync()` (not
`ManualConnectAsync()`) directly — it must not cancel its own in-flight `_autoConnectCts`,
which is exactly what `ManualConnectAsync()` would do.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ConnectionViewModelTests"`
Expected: PASS — all 4 new tests plus the full existing suite in this file.

- [ ] **Step 5: Commit**

```bash
git add ArctZ/ViewModels/ConnectionViewModel.cs ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs
git commit -m "feat: implement AutoConnectAsync find-and-connect orchestrator"
```

---

### Task 6: Auto-restart after the fast internal reconnect gives up

**Files:**
- Modify: `ArctZ/ViewModels/ConnectionViewModel.cs`
- Test: `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs`

**Interfaces:**
- Consumes: `AutoConnectAsync()` (Task 5), `_autoConnectSuppressed` (Task 4).
- Produces: no new public members — this task wires an existing constructor subscription to call `AutoConnectAsync()` when the assigned session's own internal reconnect loop is exhausted.

This is the "restore connection on loss" requirement: `DeviceSession`'s existing fast
reconnect-to-known-id loop (3×200ms, untouched — see Global Constraints) already transitions
`ConnectionState` to `Disconnected` when it gives up, while `ConnectionViewModel.Session` is
still assigned (not yet nulled — only `ManualDisconnectAsync` nulls it). This task hooks exactly
that transition.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task ExhaustedFastReconnect_AutomaticallyRestartsAutoConnect_AndFindsFluidNcAgain()
{
    var realTransport = new FakeDeviceTransport();
    var provider = new FakeDeviceEndpointProvider
    {
        SupportsDiscovery = false,
        KnownEndpoints = { new DeviceEndpointInfo("fluid1", "FluidNC-1234", true) },
    };
    var vm = await CreateVmAsync(realTransport, endpointProvider: provider);
    vm.SelectedEndpoint = vm.AvailableEndpoints.Single(e => e.Id == "fluid1");
    await vm.ConnectCommand.Execute();
    var firstSession = vm.Session;

    // DeviceSessionFactory's unchanged internal reconnect policy is exactly 3 attempts (200ms
    // apart, see DeviceSessionFactory.cs). Failing exactly 3 upcoming connects exhausts it
    // deterministically without racing a wall-clock delay to flip the flag mid-loop, and leaves
    // ConnectFailuresRemaining at 0 so the orchestrator's subsequent attempt (this task's
    // restart) succeeds on its first try.
    realTransport.ConnectFailuresRemaining = 3;
    realTransport.SimulateDisconnect();
    Assert.Equal(ConnectionState.Reconnecting, firstSession!.ConnectionState);

    await WaitUntilAsync(() => vm.Session is not null && vm.Session.ConnectionState == ConnectionState.Connected, TimeSpan.FromSeconds(3));

    Assert.NotNull(vm.Session);
    Assert.Equal(ConnectionState.Connected, vm.Session!.ConnectionState);
}

[Fact]
public async Task GivenUpAutoConnectRestart_DoesNotFireAfterExplicitManualDisconnect()
{
    var realTransport = new FakeDeviceTransport();
    var vm = await CreateVmAsync(realTransport);
    await vm.ConnectCommand.Execute();

    await vm.DisconnectCommand.Execute();

    // DeviceSession.DisconnectAsync() fires ConnectionStateChanged(Disconnected) on the
    // torn-down session synchronously (DeviceSession.cs:78-90) — the same event the restart
    // subscription listens for. It must stay suppressed: Session stays null.
    Assert.Equal(AutoConnectPhase.Idle, vm.AutoConnectPhase);
    Assert.Null(vm.Session);
    Assert.True(vm.IsConnectionModalVisible);
}
```

`WaitUntilAsync` does not exist yet in this test file — check whether
`ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs` already has a reusable one (it's used
there per the earlier grep of this codebase); if it's private to that class, add an equivalent
private helper to `ConnectionViewModelTests.cs`:

```csharp
private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (!condition())
    {
        if (DateTime.UtcNow > deadline)
        {
            throw new TimeoutException("Condition not met within timeout.");
        }

        await Task.Delay(10);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ConnectionViewModelTests"`
Expected: `ExhaustedFastReconnect_...` FAILs (times out — nothing restarts `AutoConnectAsync`
yet). `GivenUpAutoConnectRestart_...` passes vacuously (no restart subscription exists to
violate it) — that's fine, it becomes a real guard once Step 3 lands.

- [ ] **Step 3: Write minimal implementation**

Modify the **first** `this.WhenAnyValue(x => x.Session)...` subscription block in the
constructor ([ConnectionViewModel.cs:174-191](../../../ArctZ/ViewModels/ConnectionViewModel.cs#L174-L191)) — the one that mirrors `ConnectionStateChanged`/`LastError`. Change only its
final `.Subscribe(...)` body:

```csharp
        this.WhenAnyValue(x => x.Session)
            .Do(s =>
            {
                ConnectionState = s?.ConnectionState ?? ConnectionState.Disconnected;
                LastError = s?.LastError;
                LastAlarmCode = null;
            })
            .Select(s => s is null
                ? Observable.Empty<Unit>()
                : Observable.FromEvent(h => s.ConnectionStateChanged += h, h => s.ConnectionStateChanged -= h)
                    .ObserveOn(RxSchedulers.MainThreadScheduler))
            .Switch()
            .Subscribe(_ =>
            {
                ConnectionState = Session?.ConnectionState ?? ConnectionState.Disconnected;
                LastError = Session?.LastError;

                // DeviceSession's own fast reconnect-to-known-id loop (3x200ms, unchanged)
                // exhausted on its own — Session is still assigned (only ManualDisconnectAsync
                // nulls it), its ConnectionState just flipped to Disconnected. Hand off to the
                // name-search auto-connect orchestrator unless the user explicitly disconnected.
                if (ConnectionState == ConnectionState.Disconnected && !_autoConnectSuppressed)
                {
                    _ = AutoConnectAsync();
                }
            })
            .DisposeWith(Disposables);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ConnectionViewModelTests"`
Expected: PASS — both new tests, and the full existing suite (in particular
`UnsolicitedDisconnect_TransitionsToReconnectingAndShowsModal` and
`UnsolicitedDisconnect_DuringAlarm_ConnectionModalWinsOverAlarmModal`, which only assert the
immediate `Reconnecting` transition and don't wait for exhaustion, so they stay green even
though a real exhaustion would now also trigger `AutoConnectAsync()` in the background — Task 7
rewrites these two specifically for the splash requirement).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/ViewModels/ConnectionViewModel.cs ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs
git commit -m "feat: auto-restart AutoConnectAsync after the fast reconnect loop gives up"
```

---

### Task 7: Splash visibility/status + redefine the manual-modal and alarm-modal conditions

**Files:**
- Modify: `ArctZ/ViewModels/ConnectionViewModel.cs`
- Modify (rewrite 2 tests): `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs`

**Interfaces:**
- Consumes: `AutoConnectPhase`/`AutoConnectAttempt`/`AutoConnectMaxAttempts` (Task 3), `ConnectionState` (existing).
- Produces: `public bool IsAutoConnectSplashVisible`, `public string AutoConnectStatusText` — consumed by `MainView.axaml` in Task 9. Redefines existing `IsConnectionModalVisible`/`IsAlarmModalVisible` getters (same names, same public contract shape, different logic).

- [ ] **Step 1: Write the failing test**

Replace the two existing tests that assert the old "Reconnecting shows the manual list modal"
contract — this is a deliberate, spec-approved behavior change (see the design doc's "Часть 3").

Replace `UnsolicitedDisconnect_TransitionsToReconnectingAndShowsModal` at
[ConnectionViewModelTests.cs:168-187](../../../ArctZ/../ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs#L168-L187) with:

```csharp
[Fact]
public async Task UnsolicitedDisconnect_TransitionsToReconnectingAndShowsSplashInsteadOfModal()
{
    var realTransport = new FakeDeviceTransport();
    var vm = await CreateVmAsync(realTransport);
    await vm.ConnectCommand.Execute();

    realTransport.ConnectFailuresRemaining = 10;
    realTransport.SimulateDisconnect();

    Assert.Equal(ConnectionState.Reconnecting, vm.ConnectionState);
    Assert.True(vm.IsAutoConnectSplashVisible);
    Assert.False(vm.IsConnectionModalVisible);
    Assert.Equal("Переподключение…", vm.AutoConnectStatusText);
}
```

Replace `UnsolicitedDisconnect_DuringAlarm_ConnectionModalWinsOverAlarmModal` at
[ConnectionViewModelTests.cs:320-352](../../../ArctZ/../ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs#L320-L352) with:

```csharp
[Fact]
public async Task UnsolicitedDisconnect_DuringAlarm_SplashWinsOverAlarmModal()
{
    // Regression test (updated for the auto-connect splash — see the connection-modal-vs-alarm
    // regression this replaces, above): a routine transport-level link drop during an active
    // alarm must not paint the alarm modal over the ONLY working recovery UI. Previously that
    // was the manual connection modal; now it's the auto-connect splash.
    var realTransport = new FakeDeviceTransport();
    var vm = await CreateVmAsync(realTransport);
    await vm.ConnectCommand.Execute();

    realTransport.SimulateReceivedLine("ALARM:1");
    Assert.True(vm.IsAlarmModalVisible);
    Assert.False(vm.IsConnectionModalVisible);
    Assert.False(vm.IsAutoConnectSplashVisible);
    Assert.True(vm.IsAnyModalVisible);

    realTransport.ConnectFailuresRemaining = 10;
    realTransport.SimulateDisconnect();

    Assert.Equal(ConnectionState.Reconnecting, vm.ConnectionState);
    Assert.True(vm.IsAutoConnectSplashVisible);
    Assert.False(vm.IsConnectionModalVisible);
    Assert.False(vm.IsAlarmModalVisible);
    Assert.True(vm.IsAnyModalVisible);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ConnectionViewModelTests"`
Expected: FAIL to build (`IsAutoConnectSplashVisible`/`AutoConnectStatusText` don't exist yet),
then — once it builds — the two rewritten tests FAIL on assertions (old getters still show the
modal during Reconnecting).

- [ ] **Step 3: Write minimal implementation**

Replace the existing `IsConnectionModalVisible`/`IsAlarmModalVisible` properties at
[ConnectionViewModel.cs:79-94](../../../ArctZ/ViewModels/ConnectionViewModel.cs#L79-L94):

```csharp
    public bool IsAutoConnectSplashVisible =>
        AutoConnectPhase is AutoConnectPhase.Searching or AutoConnectPhase.Connecting or AutoConnectPhase.WaitingRetry
        || ConnectionState == ConnectionState.Reconnecting;

    public string AutoConnectStatusText => ConnectionState == ConnectionState.Reconnecting
        ? "Переподключение…" // DeviceSession's fast internal reconnect (Part 1) — no attempt count exposed
        : AutoConnectPhase switch
        {
            AutoConnectPhase.Searching => "Поиск FluidNC…",
            AutoConnectPhase.Connecting => "Подключение…",
            AutoConnectPhase.WaitingRetry => $"Попытка {AutoConnectAttempt} из {AutoConnectMaxAttempts} не удалась, повтор…",
            _ => "",
        };

    public bool IsConnectionModalVisible =>
        !IsAutoConnectSplashVisible && (Session is null || ConnectionState != ConnectionState.Connected);

    // Авария (LastAlarmCode) блокирует основной экран отдельной модалкой; обычная ошибка
    // соединения (LastError) остаётся баннером внутри ConnectionView — см. HasError/ErrorMessage.
    // Приоритет: заставка автоподключения > ручная модалка соединения > модалка аварии — авария
    // не должна перекрывать единственный работающий путь восстановления связи, будь то заставка
    // (идёт автоматика) или ручной список (автоматика сдалась).
    public bool IsAlarmModalVisible =>
        LastAlarmCode is not null && !IsConnectionModalVisible && !IsAutoConnectSplashVisible;

    public bool IsAnyModalVisible => IsAutoConnectSplashVisible || IsConnectionModalVisible || IsAlarmModalVisible;
```

Extend the existing property-change-notification subscription
([ConnectionViewModel.cs:222-235](../../../ArctZ/ViewModels/ConnectionViewModel.cs#L222-L235)) so
`IsAutoConnectSplashVisible`/`AutoConnectStatusText` also get re-raised — add a new, separate
subscription right after it (keeps the existing well-commented block's diff minimal):

```csharp
        this.WhenAnyValue(x => x.AutoConnectPhase, x => x.AutoConnectAttempt,
                (_, _) => Unit.Default)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(IsAutoConnectSplashVisible));
                this.RaisePropertyChanged(nameof(AutoConnectStatusText));
                this.RaisePropertyChanged(nameof(IsConnectionModalVisible));
                this.RaisePropertyChanged(nameof(IsAlarmModalVisible));
                this.RaisePropertyChanged(nameof(IsAnyModalVisible));
            })
            .DisposeWith(Disposables);
```

(`ConnectionState` changes already re-raise these same names via the existing block at
line 222-235 — this new block only needs to cover the two properties that block doesn't already
watch.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ConnectionViewModelTests"`
Expected: PASS — full file, including `IsConnectionModalVisible_TracksSessionLifecycle` and
`ConnectCommand_TransportThrows_ResetsSessionAndReenablesRetry` (both unaffected: neither ever
reaches `ConnectionState.Reconnecting`, so `IsAutoConnectSplashVisible` stays false and
`IsConnectionModalVisible` behaves exactly as before for them).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/ViewModels/ConnectionViewModel.cs ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs
git commit -m "feat: add auto-connect splash visibility, redefine modal priority"
```

---

### Task 8: `App.axaml.cs` — start auto-connect at launch

**Files:**
- Modify: `ArctZ/App.axaml.cs`

**Interfaces:**
- Consumes: `ProgramViewModel.Connection` (existing public property), `ConnectionViewModel.AutoConnectAsync()` (Task 5).

No automated test — this is composition-root wiring exercised by the manual verification in
Task 11. (A unit test would need to spin up the full Avalonia app lifecycle, which
`ArctZ.Tests` doesn't do; the existing `ArctZ.Tests.Screenshots` project does something close
but only for rendering, not this wiring — not worth the machinery for a one-line call.)

- [ ] **Step 1: Modify `OnFrameworkInitializationCompleted`**

In `ArctZ/App.axaml.cs`, change:

```csharp
        public override void OnFrameworkInitializationCompleted()
        {
            var viewModel = Services!.GetRequiredService<ProgramViewModel>();
            _ = viewModel.RefreshLibraryCommand.ExecuteAsync(null);
```

to:

```csharp
        public override void OnFrameworkInitializationCompleted()
        {
            var viewModel = Services!.GetRequiredService<ProgramViewModel>();
            _ = viewModel.RefreshLibraryCommand.ExecuteAsync(null);
            _ = viewModel.Connection.AutoConnectAsync();
```

(fire-and-forget, same idiom already used one line above for `RefreshLibraryCommand` —
`AutoConnectAsync` never throws out of its own `try`/`catch` for cancellation, and any
connect-attempt failure is captured in `EndpointError` rather than an unobserved exception).

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add ArctZ/App.axaml.cs
git commit -m "feat: start FluidNC auto-connect at app launch"
```

---

### Task 9: Splash overlay in `MainView.axaml`

**Files:**
- Modify: `ArctZ/Views/MainView.axaml`

**Interfaces:**
- Consumes: `Connection.IsAutoConnectSplashVisible`, `Connection.AutoConnectStatusText` (Task 7).

No unit test (XAML rendering isn't exercised by `ArctZ.Tests`); verified via
`ArctZ.Tests.Screenshots` regeneration (Step 2 below) and the manual on-device check in Task 11.

- [ ] **Step 1: Add the splash `Border`**

In `ArctZ/Views/MainView.axaml`, insert a new `Border` immediately before the existing
connection-modal `Border` at [MainView.axaml:575](../../../ArctZ/Views/MainView.axaml#L575):

```xml
        <Border IsVisible="{Binding Connection.IsAutoConnectSplashVisible}" Background="{StaticResource HudScrimBrush}">
            <StackPanel x:DataType="vm:ConnectionViewModel" DataContext="{Binding Connection}"
                        Spacing="14" HorizontalAlignment="Center" VerticalAlignment="Center" Width="280">
                <ProgressBar IsIndeterminate="True" HorizontalAlignment="Stretch" />
                <TextBlock Text="{Binding AutoConnectStatusText}" HorizontalAlignment="Center"
                           TextWrapping="Wrap" TextAlignment="Center" />
            </StackPanel>
        </Border>

        <Border IsVisible="{Binding Connection.IsConnectionModalVisible}" Background="{StaticResource HudScrimBrush}">
```

(the second line above is the existing line 575, shown only to anchor the insertion point —
don't duplicate it).

- [ ] **Step 2: Regenerate the screenshot gallery to catch XAML errors early**

Run: `dotnet test ArctZ.Tests.Screenshots/ArctZ.Tests.Screenshots.csproj`
Expected: PASS — this renders every screen headlessly, including `MainView`, so a XAML binding
typo (wrong `x:DataType`, missing resource, etc.) fails loudly here instead of only at runtime.
The new splash won't visibly appear in any of the 11 generated screenshots (none of the existing
screens are captured mid-auto-connect), but a XAML parse/binding error would still fail the run.

- [ ] **Step 3: Commit**

```bash
git add ArctZ/Views/MainView.axaml screenshots/
git commit -m "feat: add auto-connect splash overlay to MainView"
```

---

### Task 10: Desktop endpoint provider with real device names (WMI)

**Files:**
- Create: `ArctZ.Desktop/DesktopBluetoothEndpointProvider.cs`
- Modify: `ArctZ.Desktop/Program.cs`
- Modify: `ArctZ.Desktop/ArctZ.Desktop.csproj`
- Modify: `Directory.Packages.props`

**Interfaces:**
- Consumes: `IDeviceEndpointProvider` (existing interface, `ArctZ.Services.Device`), `DeviceEndpointInfo` (existing record).
- Produces: `ArctZ.Desktop.DesktopBluetoothEndpointProvider : IDeviceEndpointProvider`, registered in Desktop's DI container so `ConnectionViewModel`'s `FindFluidNcEndpointAsync` (Task 5) has real device names to match on Desktop.

Not unit-testable (WMI is Windows-only and unavailable from `ArctZ.Tests`, `net10.0`) — verified
manually on this machine in Task 11, per CLAUDE.md's "Тестирование UI" rule.

- [ ] **Step 1: Add the `System.Management` package**

In `Directory.Packages.props`, add a line next to the existing `System.IO.Ports` entry:

```xml
    <PackageVersion Include="System.Management" Version="9.0.0" />
```

In `ArctZ.Desktop/ArctZ.Desktop.csproj`, add to the existing `<ItemGroup>` with the other
`PackageReference` entries:

```xml
    <PackageReference Include="System.Management" />
```

- [ ] **Step 2: Write the provider**

Create `ArctZ.Desktop/DesktopBluetoothEndpointProvider.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Management;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Desktop;

/// <summary>
/// IDeviceEndpointProvider for Desktop: enumerates already-paired Bluetooth SPP COM ports
/// through WMI, so ConnectionViewModel's name-based auto-connect (FluidNcDeviceName.Matches)
/// has a real device name to match against. Replaces the default SingleRealDeviceEndpointProvider
/// (registered by AddArctZCore()), which only exposed a single synthetic "Устройство" endpoint
/// with no real name — auto-connect by name was impossible on Desktop before this. Pairing new
/// devices is left to Windows Bluetooth Settings, same as SingleRealDeviceEndpointProvider before it.
/// </summary>
public sealed class DesktopBluetoothEndpointProvider : IDeviceEndpointProvider
{
    // Win32_PnPEntity.Name for a Bluetooth SPP COM port looks like "FluidNC (COM5)" — the
    // friendly (paired) device name followed by the assigned port in parentheses.
    private static readonly Regex ComPortNamePattern = new(@"^(?<friendly>.+)\s\((?<port>COM\d+)\)$", RegexOptions.Compiled);

    public bool SupportsDiscovery => false;

    public Task<IReadOnlyList<DeviceEndpointInfo>> GetKnownEndpointsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DeviceEndpointInfo>();

#pragma warning disable CA1416 // WMI is Windows-only; ArctZ.Desktop only ships on Windows.
        using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
        using var devices = searcher.Get();
        foreach (var device in devices)
        {
            using (device)
            {
                if (device["Name"] is not string name)
                {
                    continue;
                }

                var match = ComPortNamePattern.Match(name);
                if (!match.Success)
                {
                    continue;
                }

                result.Add(new DeviceEndpointInfo(match.Groups["port"].Value, match.Groups["friendly"].Value.Trim(), true));
            }
        }
#pragma warning restore CA1416

        return Task.FromResult<IReadOnlyList<DeviceEndpointInfo>>(result);
    }

    public IObservable<DeviceEndpointInfo> Discover() => Observable.Empty<DeviceEndpointInfo>();

    public Task<bool> PairAsync(string deviceId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}
```

- [ ] **Step 3: Register it in `ArctZ.Desktop/Program.cs`**

Change:

```csharp
            var services = new ServiceCollection();
            services.AddArctZCore();
            services.AddSingleton<IDeviceTransport, DesktopSerialTransport>();
```

to:

```csharp
            var services = new ServiceCollection();
            services.AddArctZCore();
            services.AddSingleton<IDeviceTransport, DesktopSerialTransport>();
            services.AddSingleton<IDeviceEndpointProvider, DesktopBluetoothEndpointProvider>();
```

(registering after `AddArctZCore()` so it wins over the default `SingleRealDeviceEndpointProvider`
— same pattern already used by `ArctZ.Android/Application.cs` for `AndroidBluetoothEndpointProvider`).

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: Build succeeds. If NuGet fails to resolve `System.Management` version `9.0.0`, check
the actual latest stable version available for this SDK and adjust the `PackageVersion` in
`Directory.Packages.props` accordingly, then rebuild.

- [ ] **Step 5: Commit**

```bash
git add ArctZ.Desktop/DesktopBluetoothEndpointProvider.cs ArctZ.Desktop/Program.cs ArctZ.Desktop/ArctZ.Desktop.csproj Directory.Packages.props
git commit -m "feat: enumerate paired Bluetooth COM ports with real names on Desktop"
```

---

### Task 11: Build, run, and verify on real devices

**Files:** none (verification only).

This is the mandatory verification step from CLAUDE.md's "Тестирование UI" — the only way to
call this feature done. Do not skip or substitute with more unit tests.

- [ ] **Step 1: Run the full test suite once, all together**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS, 0 failures — confirms nothing from Tasks 1-7 regressed anything else in the
suite when run together (not just per-file, matching CLAUDE.md's async-dialog-hang caution:
if this doesn't come back in a normal amount of time, treat it as a hang, not a slow test).

- [ ] **Step 2: Build and run the Desktop head**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`, then run the produced executable (or
`dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj`) so the app is actually running, not
just built.

- [ ] **Step 3: Ask the user to prepare Android**

Per CLAUDE.md's Android carve-out: use `AskUserQuestion` to ask the user to build and install
the latest APK on their device (`dotnet build ArctZ.Android/ArctZ.Android.csproj -t:Install`
with the phone connected over USB) and confirm readiness, rather than doing it automatically.

- [ ] **Step 4: Ask the user to verify, one question per behavior**

Use `AskUserQuestion`, one question per point (not one combined "does it work?"):

1. При старте приложения (Desktop и Android) сразу показывается заставка с текстом поиска, а
   не список устройств.
2. Приложение находит уже включённый и спаренный FluidNC и подключается без участия
   пользователя (и на Desktop, и на Android).
3. Если FluidNC выключен/недоступен — через некоторое время появляется обычная ручная модалка
   со списком устройств, а не бесконечная заставка.
4. При обрыве связи во время работы показывается заставка «Переподключение…», связь
   восстанавливается сама при возврате устройства в сеть.
5. Явное «Отключить» не запускает автоподключение заново само по себе — нужен ручной клик
   «Подключить» (или перезапуск приложения).
6. Обрыв связи во время выполнения программы останавливает её с сообщением об ошибке (без
   изменений в этой задаче — существующий механизм `Faulted`).

Record the user's answers faithfully; if any point fails, treat it as a bug to fix (with its own
systematic-debugging pass) before considering this plan complete — do not mark the plan done
with a known-failing verification point.
