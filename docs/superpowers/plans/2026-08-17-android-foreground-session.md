# Android Foreground Session Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Пока Android-приложение связано со станком, держать в шторке постоянное уведомление с состоянием и кнопками Пауза/Продолжить и Стоп, а при закрытии приложения через меню недавних — останавливать станок без диалогов.

**Architecture:** Логика, содержащая решения (что показывать, когда показывать, что делать при принудительном закрытии), живёт в ядре `ArctZ` и покрыта тестами; `ArctZ.Android` получает foreground-сервис, который только вызывает Android API. Связь между ними — интерфейс `IBackgroundSessionHost` с no-op реализацией по умолчанию, которую Android-голова подменяет своей. `ProgramViewModel` зарегистрирован в DI синглтоном, а сервис работает в том же процессе, поэтому получает ровно тот экземпляр, с которым работает UI.

**Tech Stack:** .NET 10, Avalonia, CommunityToolkit.Mvvm, ReactiveUI, xUnit, .NET for Android (`net10.0-android`).

**Spec:** `docs/superpowers/specs/2026-08-17-android-foreground-session-design.md`

## Global Constraints

- Целевой TFM Android-головы — `net10.0-android`, `SupportedOSPlatformVersion` = **23**. Любой вызов API выше 23 обязан быть под `OperatingSystem.IsAndroidVersionAtLeast(N)`, иначе компилятор выдаст CA1416 и приложение упадёт на старых устройствах.
- `ArctZ.Tests` **не может** ссылаться на `net10.0-android`. Всё, что попало в `ArctZ.Android`, тестами не покрывается — поэтому там не должно остаться ни одного решения, только вызовы Android API.
- Тесты гоняются командой `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ИмяКласса"`; полный прогон — без `--filter`. На момент старта плана эталон — **397 проходящих тестов**.
- Сборку APK, установку на устройство и запуск выполняет **пользователь**, а не агент (правило из `CLAUDE.md`). Задача 9 — это запрос к пользователю, а не команда сборки.
- Никаких новых NuGet-пакетов. Всё нужное есть в базовом Android SDK.
- Тексты, видимые пользователю, — на русском языке.
- Разрешение `POST_NOTIFICATIONS` запрашивается **только** на API 33+; на более старых версиях оно не существует и запрос вернёт отказ.
- Кнопка «Стоп» в уведомлении повторяет поведение кнопки «Стоп» в приложении (`StopCommand`: feed hold + сброс очереди), а **не** полную последовательность выхода.

---

## Файловая структура

**Создаются (ядро, покрыто тестами):**

| Файл | Ответственность |
|---|---|
| `ArctZ/Services/App/BackgroundSessionState.cs` | Неизменяемое описание того, что показывать в уведомлении |
| `ArctZ/Services/App/BackgroundSessionProjector.cs` | Чистая проекция состояния ViewModel → `BackgroundSessionState` |
| `ArctZ/Services/App/IBackgroundSessionHost.cs` | Seam к платформе + `NullBackgroundSessionHost` |
| `ArctZ/Services/App/BackgroundSessionCoordinator.cs` | Подписка на ViewModel, вызовы хоста |

**Изменяются (ядро):**

| Файл | Что меняется |
|---|---|
| `ArctZ/ViewModels/ProgramViewModel.cs` | `ShutdownAsync` получает параметр `confirmIfRunning` |
| `ArctZ/Services/Device/ServiceCollectionExtensions.cs` | Регистрация хоста и координатора |
| `ArctZ/App.axaml.cs` | Подъём координатора при старте |

**Создаются (Android, без тестов):**

| Файл | Ответственность |
|---|---|
| `ArctZ.Android/Resources/drawable/ic_notification.xml` | Монохромная иконка уведомления |
| `ArctZ.Android/MachineSessionService.cs` | Foreground-сервис: уведомление, кнопки, `OnTaskRemoved` |
| `ArctZ.Android/AndroidBackgroundSessionHost.cs` | Реализация seam: поднимает/останавливает сервис |

**Изменяются (Android):**

| Файл | Что меняется |
|---|---|
| `ArctZ.Android/Properties/AndroidManifest.xml` | Три новых разрешения |
| `ArctZ.Android/MainActivity.cs` | `LaunchMode = SingleTask` |
| `ArctZ.Android/Application.cs` | Подмена регистрации `IBackgroundSessionHost` |
| `ArctZ.Android/AndroidBluetoothTransport.cs` | Запрос `POST_NOTIFICATIONS` вместе с Bluetooth-разрешением |

**Тесты:**

| Файл | Что покрывает |
|---|---|
| `ArctZ.Tests/Services/App/FakeBackgroundSessionHost.cs` | Дублёр хоста |
| `ArctZ.Tests/Services/App/BackgroundSessionProjectorTests.cs` | Проекция |
| `ArctZ.Tests/Services/App/BackgroundSessionCoordinatorTests.cs` | Подписки и вызовы хоста |
| `ArctZ.Tests/ViewModels/ProgramViewModelShutdownTests.cs` | Выход без диалога (дополняется) |

---

### Task 1: Состояние фонового сеанса и его проекция

**Files:**
- Create: `ArctZ/Services/App/BackgroundSessionState.cs`
- Create: `ArctZ/Services/App/BackgroundSessionProjector.cs`
- Test: `ArctZ.Tests/Services/App/BackgroundSessionProjectorTests.cs`

**Interfaces:**
- Consumes: `ArctZ.ViewModels.PlaybackState` (enum, значения `Idle`, `Running`, `Paused`, `Stopped`, `Completed`, `Faulted`).
- Produces: `ArctZ.Services.App.BackgroundSessionState` (record struct с полями `Title`, `Status`, `CanPause`, `CanResume`, `CanStop`) и `ArctZ.Services.App.BackgroundSessionProjector.Project(PlaybackState playback, string statusLabel, string? programName)`.

- [ ] **Step 1: Написать падающий тест**

Создать `ArctZ.Tests/Services/App/BackgroundSessionProjectorTests.cs`:

```csharp
using ArctZ.Services.App;
using ArctZ.ViewModels;

namespace ArctZ.Tests.Services.App;

public class BackgroundSessionProjectorTests
{
    [Fact]
    public void Project_WhileRunning_OffersPauseAndStop()
    {
        var state = BackgroundSessionProjector.Project(PlaybackState.Running, "Выполнение", "Панорама цеха");

        Assert.Equal("Панорама цеха", state.Title);
        Assert.Equal("Выполнение", state.Status);
        Assert.True(state.CanPause);
        Assert.False(state.CanResume);
        Assert.True(state.CanStop);
    }

    [Fact]
    public void Project_WhilePaused_OffersResumeAndStop()
    {
        var state = BackgroundSessionProjector.Project(PlaybackState.Paused, "Пауза", "Панорама цеха");

        Assert.False(state.CanPause);
        Assert.True(state.CanResume);
        Assert.True(state.CanStop);
    }

    [Theory]
    [InlineData(PlaybackState.Idle)]
    [InlineData(PlaybackState.Stopped)]
    [InlineData(PlaybackState.Completed)]
    [InlineData(PlaybackState.Faulted)]
    public void Project_WhenNoProgramIsInFlight_OffersNoButtons(PlaybackState playback)
    {
        var state = BackgroundSessionProjector.Project(playback, "Ожидание", "Панорама цеха");

        Assert.False(state.CanPause);
        Assert.False(state.CanResume);
        Assert.False(state.CanStop);
    }

    /// <summary>Программа может быть не сохранена и не названа — в шторке всё равно должно
    /// стоять узнаваемое имя приложения, а не пустая строка.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Project_WithoutAProgramName_FallsBackToTheAppName(string? programName)
    {
        var state = BackgroundSessionProjector.Project(PlaybackState.Idle, "Ожидание", programName);

        Assert.Equal("ArctZ", state.Title);
    }
}
```

- [ ] **Step 2: Прогнать тест и убедиться, что он падает**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~BackgroundSessionProjectorTests" --nologo -v q`
Expected: ошибка компиляции CS0246 — тип `BackgroundSessionState`/`BackgroundSessionProjector` не найден.

- [ ] **Step 3: Написать минимальную реализацию**

`ArctZ/Services/App/BackgroundSessionState.cs`:

```csharp
namespace ArctZ.Services.App;

/// <summary>
/// Что показывать в постоянном уведомлении фонового сеанса. Платформа получает уже готовые
/// строки и флаги: решение о том, какая кнопка уместна, принимается в ядре и покрыто тестами.
/// </summary>
public readonly record struct BackgroundSessionState(
    string Title,
    string Status,
    bool CanPause,
    bool CanResume,
    bool CanStop);
```

`ArctZ/Services/App/BackgroundSessionProjector.cs`:

```csharp
using ArctZ.ViewModels;

namespace ArctZ.Services.App;

public static class BackgroundSessionProjector
{
    /// <summary>Заголовок, когда у программы ещё нет имени.</summary>
    public const string AppName = "ArctZ";

    public static BackgroundSessionState Project(PlaybackState playback, string statusLabel, string? programName) =>
        new(
            Title: string.IsNullOrWhiteSpace(programName) ? AppName : programName,
            Status: statusLabel,
            CanPause: playback == PlaybackState.Running,
            CanResume: playback == PlaybackState.Paused,
            CanStop: playback is PlaybackState.Running or PlaybackState.Paused);
}
```

- [ ] **Step 4: Прогнать тест и убедиться, что он проходит**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~BackgroundSessionProjectorTests" --nologo -v q`
Expected: PASS, 9 тестов.

- [ ] **Step 5: Коммит**

```bash
git add ArctZ/Services/App/BackgroundSessionState.cs ArctZ/Services/App/BackgroundSessionProjector.cs ArctZ.Tests/Services/App/BackgroundSessionProjectorTests.cs
git commit -m "feat: project playback state into background session state"
```

---

### Task 2: Seam к платформе

**Files:**
- Create: `ArctZ/Services/App/IBackgroundSessionHost.cs`
- Test: покрывается в задаче 3 через `FakeBackgroundSessionHost` (отдельного теста у no-op реализации нет — она по определению ничего не делает).

**Interfaces:**
- Consumes: `BackgroundSessionState` из задачи 1.
- Produces: `ArctZ.Services.App.IBackgroundSessionHost` с методами `void Update(BackgroundSessionState state)` и `void Stop()`, плюс `ArctZ.Services.App.NullBackgroundSessionHost`.

- [ ] **Step 1: Написать реализацию**

Тест на этом шаге не пишется намеренно: интерфейс без реализации нечем проверить, а `NullBackgroundSessionHost` не имеет наблюдаемого поведения. Первое поведение появляется в задаче 3, и там оно покрыто тестами.

`ArctZ/Services/App/IBackgroundSessionHost.cs`:

```csharp
namespace ArctZ.Services.App;

/// <summary>
/// Платформенный «фоновый сеанс»: на Android — постоянное уведомление с кнопками управления,
/// которое заодно удерживает процесс живым достаточно долго, чтобы остановить станок при
/// закрытии приложения. На остальных платформах ничего подобного нет — там работает
/// <see cref="NullBackgroundSessionHost"/>.
/// </summary>
public interface IBackgroundSessionHost
{
    /// <summary>Показать или обновить сеанс. Идемпотентно: вызывается на каждое изменение
    /// состояния, в том числе когда сеанс уже показан.</summary>
    void Update(BackgroundSessionState state);

    /// <summary>Убрать сеанс. Идемпотентно: вызывается и когда сеанса нет.</summary>
    void Stop();
}

public sealed class NullBackgroundSessionHost : IBackgroundSessionHost
{
    public void Update(BackgroundSessionState state)
    {
    }

    public void Stop()
    {
    }
}
```

- [ ] **Step 2: Убедиться, что решение собирается**

Run: `dotnet build ArctZ/ArctZ.csproj --nologo -v q`
Expected: `Сборка успешно завершена`, 0 ошибок.

- [ ] **Step 3: Коммит**

```bash
git add ArctZ/Services/App/IBackgroundSessionHost.cs
git commit -m "feat: add the background session host seam"
```

---

### Task 3: Координатор фонового сеанса

**Files:**
- Create: `ArctZ/Services/App/BackgroundSessionCoordinator.cs`
- Create: `ArctZ.Tests/Services/App/FakeBackgroundSessionHost.cs`
- Test: `ArctZ.Tests/Services/App/BackgroundSessionCoordinatorTests.cs`

**Interfaces:**
- Consumes: `IBackgroundSessionHost` (задача 2), `BackgroundSessionProjector.Project` (задача 1), `ArctZ.ViewModels.ProgramViewModel` (свойства `PlaybackState`, `StatusLabel`, `ProgramName`, `Connection`), `ArctZ.ViewModels.ConnectionViewModel` (свойства `ConnectionState`, `Session`).
- Produces: `ArctZ.Services.App.BackgroundSessionCoordinator` — публичный конструктор `(ProgramViewModel program, IBackgroundSessionHost host)` и `void Dispose()`.

- [ ] **Step 1: Написать дублёр хоста**

Создать `ArctZ.Tests/Services/App/FakeBackgroundSessionHost.cs`:

```csharp
using System.Collections.Generic;
using ArctZ.Services.App;

namespace ArctZ.Tests.Services.App;

public sealed class FakeBackgroundSessionHost : IBackgroundSessionHost
{
    public List<BackgroundSessionState> Updates { get; } = new();

    public int StopCallCount { get; private set; }

    public BackgroundSessionState? LastUpdate => Updates.Count == 0 ? null : Updates[^1];

    public void Update(BackgroundSessionState state) => Updates.Add(state);

    public void Stop() => StopCallCount++;
}
```

- [ ] **Step 2: Написать падающий тест**

Создать `ArctZ.Tests/Services/App/BackgroundSessionCoordinatorTests.cs`:

```csharp
using ArctZ.Services.App;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.Device;
using ArctZ.Tests.Services.Program;
using ArctZ.ViewModels;

namespace ArctZ.Tests.Services.App;

public class BackgroundSessionCoordinatorTests
{
    private readonly FakeBackgroundSessionHost _host = new();
    private readonly ProgramViewModel _program;

    public BackgroundSessionCoordinatorTests()
    {
        var connection = new ConnectionViewModel(
            new FakeDeviceTransport(),
            () => new FakeDeviceTransport(),
            new DeviceSessionFactory(MachineLimits.Default),
            new SingleRealDeviceEndpointProvider());
        _program = new ProgramViewModel(
            connection,
            new FakeProgramStorage(),
            new TrajectoryCompiler(),
            new FakeAppExitService());
    }

    private BackgroundSessionCoordinator CreateCoordinator() => new(_program, _host);

    private void Connect() => _program.Connection.Session = new FakeDeviceSession();

    [Fact]
    public void WhileDisconnected_NothingIsShown()
    {
        using var coordinator = CreateCoordinator();

        Assert.Empty(_host.Updates);
    }

    [Fact]
    public void OnConnect_TheSessionIsShown()
    {
        using var coordinator = CreateCoordinator();

        Connect();

        Assert.NotNull(_host.LastUpdate);
        Assert.False(_host.LastUpdate!.Value.CanStop);
    }

    [Fact]
    public void WhenPlaybackStarts_TheSessionOffersPauseAndStop()
    {
        using var coordinator = CreateCoordinator();
        Connect();

        _program.PlaybackState = PlaybackState.Running;

        Assert.True(_host.LastUpdate!.Value.CanPause);
        Assert.True(_host.LastUpdate.Value.CanStop);
    }

    [Fact]
    public void WhenTheProgramIsRenamed_TheSessionTitleFollows()
    {
        using var coordinator = CreateCoordinator();
        Connect();

        _program.ProgramName = "Проезд по цеху";

        Assert.Equal("Проезд по цеху", _host.LastUpdate!.Value.Title);
    }

    [Fact]
    public void OnDisconnect_TheSessionIsStopped()
    {
        using var coordinator = CreateCoordinator();
        Connect();

        _program.Connection.Session = null;

        Assert.Equal(1, _host.StopCallCount);
    }

    /// <summary>Разрыв связи при уже убранном сеансе не должен снова дёргать платформу:
    /// каждый вызов Stop() на Android — это обращение к системному сервису.</summary>
    [Fact]
    public void OnDisconnect_WhenNothingWasShown_TheHostIsLeftAlone()
    {
        using var coordinator = CreateCoordinator();

        _program.Connection.Session = null;

        Assert.Equal(0, _host.StopCallCount);
    }

    [Fact]
    public void AfterDispose_ViewModelChangesAreIgnored()
    {
        var coordinator = CreateCoordinator();
        Connect();
        var updatesBeforeDispose = _host.Updates.Count;

        coordinator.Dispose();
        _program.PlaybackState = PlaybackState.Running;

        Assert.Equal(updatesBeforeDispose, _host.Updates.Count);
    }
}
```

- [ ] **Step 3: Прогнать тест и убедиться, что он падает**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~BackgroundSessionCoordinatorTests" --nologo -v q`
Expected: ошибка компиляции CS0246 — тип `BackgroundSessionCoordinator` не найден.

- [ ] **Step 4: Написать минимальную реализацию**

Создать `ArctZ/Services/App/BackgroundSessionCoordinator.cs`:

```csharp
using System;
using System.ComponentModel;
using ArctZ.Services.Device;
using ArctZ.ViewModels;

namespace ArctZ.Services.App;

/// <summary>
/// Держит платформенный фоновый сеанс в согласии с состоянием приложения: показывает его, пока
/// есть связь со станком, и убирает, когда связи не стало. Один экземпляр на приложение,
/// создаётся при старте — см. App.OnFrameworkInitializationCompleted.
/// </summary>
public sealed class BackgroundSessionCoordinator : IDisposable
{
    private readonly ProgramViewModel _program;
    private readonly IBackgroundSessionHost _host;
    private bool _shown;

    public BackgroundSessionCoordinator(ProgramViewModel program, IBackgroundSessionHost host)
    {
        _program = program;
        _host = host;

        _program.PropertyChanged += OnProgramPropertyChanged;
        _program.Connection.PropertyChanged += OnConnectionPropertyChanged;

        Refresh();
    }

    public void Dispose()
    {
        _program.PropertyChanged -= OnProgramPropertyChanged;
        _program.Connection.PropertyChanged -= OnConnectionPropertyChanged;
    }

    private void OnProgramPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProgramViewModel.PlaybackState)
            or nameof(ProgramViewModel.StatusLabel)
            or nameof(ProgramViewModel.ProgramName))
        {
            Refresh();
        }
    }

    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Session, а не ConnectionState: при разрыве связи ConnectionState успевает уйти в
        // Reconnecting, и сеанс должен пережить переподключение — станок в этот момент никуда
        // не делся. Убирать его нужно только когда сессии не стало совсем.
        if (e.PropertyName is nameof(ConnectionViewModel.Session)
            or nameof(ConnectionViewModel.ConnectionState)
            or nameof(ConnectionViewModel.DeviceStatus))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        if (_program.Connection.Session is null)
        {
            if (_shown)
            {
                _shown = false;
                _host.Stop();
            }

            return;
        }

        _shown = true;
        _host.Update(BackgroundSessionProjector.Project(
            _program.PlaybackState,
            _program.StatusLabel,
            _program.ProgramName));
    }
}
```

- [ ] **Step 5: Прогнать тест и убедиться, что он проходит**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~BackgroundSessionCoordinatorTests" --nologo -v q`
Expected: PASS, 7 тестов.

- [ ] **Step 6: Прогнать весь набор тестов**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --nologo -v q`
Expected: PASS, ≥404 теста, ни одного падения.

- [ ] **Step 7: Коммит**

```bash
git add ArctZ/Services/App/BackgroundSessionCoordinator.cs ArctZ.Tests/Services/App/FakeBackgroundSessionHost.cs ArctZ.Tests/Services/App/BackgroundSessionCoordinatorTests.cs
git commit -m "feat: keep the background session in step with the app state"
```

---

### Task 4: Выход без диалога

**Files:**
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs` (метод `ShutdownAsync`)
- Test: `ArctZ.Tests/ViewModels/ProgramViewModelShutdownTests.cs` (дополняется)

**Interfaces:**
- Produces: `ProgramViewModel.ShutdownAsync(bool confirmIfRunning = true)` — существующие вызовы (`ExitAsync`, `MainWindow`) продолжают работать без изменений за счёт значения по умолчанию.

- [ ] **Step 1: Написать падающий тест**

Добавить в конец класса `ProgramViewModelShutdownTests` (файл `ArctZ.Tests/ViewModels/ProgramViewModelShutdownTests.cs`), перед закрывающей скобкой класса:

```csharp
    /// <summary>Принудительное закрытие приложения (смахивание из недавних на Android) не
    /// может ничего спросить у пользователя: показывать некому и некогда.</summary>
    [Fact]
    public async Task ShutdownAsync_WithoutConfirmation_StopsARunningProgramWithoutAskingAnything()
    {
        var vm = CreateViewModel(out _, out var session);
        vm.PlaybackState = PlaybackState.Running;

        var stopped = await vm.ShutdownAsync(confirmIfRunning: false);

        Assert.True(stopped);
        Assert.Null(vm.PendingConfirmation);
        Assert.Equal(1, session.StopAndDrainCallCount);
        Assert.Equal(PlaybackState.Stopped, vm.PlaybackState);
    }
```

- [ ] **Step 2: Прогнать тест и убедиться, что он падает**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelShutdownTests" --nologo -v q`
Expected: ошибка компиляции CS1739/CS1501 — у `ShutdownAsync` нет параметра `confirmIfRunning`.

- [ ] **Step 3: Написать минимальную реализацию**

В `ArctZ/ViewModels/ProgramViewModel.cs` заменить сигнатуру и условие подтверждения. Было:

```csharp
    public async Task<bool> ShutdownAsync()
    {
        IsSideMenuOpen = false;

        if (IsProgramLocked)
        {
```

Стало:

```csharp
    /// <param name="confirmIfRunning">False на пути принудительного закрытия приложения
    /// (смахивание из недавних на Android): спросить там некого, а станок остановить
    /// обязательно.</param>
    public async Task<bool> ShutdownAsync(bool confirmIfRunning = true)
    {
        IsSideMenuOpen = false;

        if (confirmIfRunning && IsProgramLocked)
        {
```

Остальное тело метода не меняется.

- [ ] **Step 4: Прогнать тест и убедиться, что он проходит**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ProgramViewModelShutdownTests" --nologo -v q`
Expected: PASS, 7 тестов.

- [ ] **Step 5: Коммит**

```bash
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/ViewModels/ProgramViewModelShutdownTests.cs
git commit -m "feat: allow shutting down without asking for confirmation"
```

---

### Task 5: Регистрация в DI и подъём при старте

**Files:**
- Modify: `ArctZ/Services/Device/ServiceCollectionExtensions.cs:17-35`
- Modify: `ArctZ/App.axaml.cs:38-42`
- Test: `ArctZ.Tests/Services/Device/ServiceCollectionExtensionsTests.cs` (дополняется)

**Interfaces:**
- Produces: `IBackgroundSessionHost` и `BackgroundSessionCoordinator` доступны из `App.Services`; Android-голова переопределяет регистрацию хоста после вызова `AddArctZCore()`.

- [ ] **Step 1: Написать падающий тест**

Открыть `ArctZ.Tests/Services/Device/ServiceCollectionExtensionsTests.cs`, посмотреть, как устроены существующие тесты (они строят `ServiceCollection`, вызывают `AddArctZCore()` и резолвят типы), и добавить в том же стиле:

```csharp
    [Fact]
    public void AddArctZCore_RegistersANoOpBackgroundSessionHostByDefault()
    {
        var services = new ServiceCollection();
        services.AddArctZCore();
        services.AddSingleton<IProgramStorage>(new FakeProgramStorage());
        services.AddSingleton<IDeviceTransport>(new FakeDeviceTransport());

        using var provider = services.BuildServiceProvider();

        Assert.IsType<NullBackgroundSessionHost>(provider.GetRequiredService<IBackgroundSessionHost>());
    }

    /// <summary>Голова платформы регистрирует свой хост после AddArctZCore() — последняя
    /// регистрация обязана победить, иначе на Android остался бы no-op.</summary>
    [Fact]
    public void AddArctZCore_LetsAPlatformHeadReplaceTheBackgroundSessionHost()
    {
        var services = new ServiceCollection();
        services.AddArctZCore();
        services.AddSingleton<IProgramStorage>(new FakeProgramStorage());
        services.AddSingleton<IDeviceTransport>(new FakeDeviceTransport());
        services.AddSingleton<IBackgroundSessionHost>(new FakeBackgroundSessionHost());

        using var provider = services.BuildServiceProvider();

        Assert.IsType<FakeBackgroundSessionHost>(provider.GetRequiredService<IBackgroundSessionHost>());
    }

    [Fact]
    public void AddArctZCore_RegistersTheBackgroundSessionCoordinator()
    {
        var services = new ServiceCollection();
        services.AddArctZCore();
        services.AddSingleton<IProgramStorage>(new FakeProgramStorage());
        services.AddSingleton<IDeviceTransport>(new FakeDeviceTransport());

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<BackgroundSessionCoordinator>());
    }
```

Дописать в начало файла недостающие `using ArctZ.Services.App;` и `using ArctZ.Tests.Services.App;`. Если существующие тесты в этом файле собирают провайдер иначе (например, через общий хелпер) — использовать тот же способ, а не вводить новый.

- [ ] **Step 2: Прогнать тест и убедиться, что он падает**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ServiceCollectionExtensionsTests" --nologo -v q`
Expected: FAIL — `InvalidOperationException: No service for type 'ArctZ.Services.App.IBackgroundSessionHost' has been registered`.

- [ ] **Step 3: Написать минимальную реализацию**

В `ArctZ/Services/Device/ServiceCollectionExtensions.cs` добавить `using ArctZ.Services.App;` (если его там ещё нет) и вставить две регистрации перед `return services;`:

```csharp
        // Обычный AddSingleton, а не TryAddSingleton: голова платформы (Android) регистрирует
        // свой хост после этого вызова, а при резолве одиночного сервиса выигрывает последняя
        // регистрация. TryAdd оставил бы на Android no-op.
        services.AddSingleton<IBackgroundSessionHost, NullBackgroundSessionHost>();
        services.AddSingleton<BackgroundSessionCoordinator>();
```

- [ ] **Step 4: Прогнать тест и убедиться, что он проходит**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~ServiceCollectionExtensionsTests" --nologo -v q`
Expected: PASS.

- [ ] **Step 5: Поднять координатор при старте приложения**

В `ArctZ/App.axaml.cs`, в методе `OnFrameworkInitializationCompleted`, сразу после строки `var viewModel = Services!.GetRequiredService<ProgramViewModel>();` добавить:

```csharp
            // Резолвится ради самого факта создания: конструктор подписывается на ViewModel и
            // дальше живёт столько же, сколько контейнер. Без этой строки на Android не появится
            // ни уведомления, ни остановки станка при закрытии из недавних.
            _ = Services.GetRequiredService<BackgroundSessionCoordinator>();
```

Добавить в шапку файла `using ArctZ.Services.App;`.

- [ ] **Step 6: Собрать ядро и Desktop**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj --nologo -v q`
Expected: `Сборка успешно завершена`, 0 ошибок.

- [ ] **Step 7: Прогнать весь набор тестов**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --nologo -v q`
Expected: PASS, ни одного падения.

- [ ] **Step 8: Коммит**

```bash
git add ArctZ/Services/Device/ServiceCollectionExtensions.cs ArctZ/App.axaml.cs ArctZ.Tests/Services/Device/ServiceCollectionExtensionsTests.cs
git commit -m "feat: register and start the background session coordinator"
```

---

### Task 6: Манифест, разрешения и режим запуска активности

**Files:**
- Modify: `ArctZ.Android/Properties/AndroidManifest.xml:3-9`
- Modify: `ArctZ.Android/MainActivity.cs:10-15`
- Modify: `ArctZ.Android/AndroidBluetoothTransport.cs` (метод, вызывающий `ConnectPermissions()`)
- Create: `ArctZ.Android/Resources/drawable/ic_notification.xml`

**Interfaces:**
- Produces: манифест с разрешениями `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_CONNECTED_DEVICE`, `POST_NOTIFICATIONS`; `Resource.Drawable.ic_notification`; `MainActivity` в режиме `SingleTask`.

- [ ] **Step 1: Добавить разрешения в манифест**

В `ArctZ.Android/Properties/AndroidManifest.xml` добавить три строки после существующих `uses-permission`:

```xml
	<uses-permission android:name="android.permission.FOREGROUND_SERVICE" />
	<uses-permission android:name="android.permission.FOREGROUND_SERVICE_CONNECTED_DEVICE" />
	<uses-permission android:name="android.permission.POST_NOTIFICATIONS" />
```

- [ ] **Step 2: Перевести активность в SingleTask**

В `ArctZ.Android/MainActivity.cs` в атрибут `[Activity(...)]` добавить `LaunchMode = LaunchMode.SingleTask,` (например, сразу после `MainLauncher = true,`) и `using Android.Content.PM;` уже присутствует — `LaunchMode` живёт именно в нём.

Без этого тап по уведомлению поднимет вторую копию активности поверх живой, и в приложении окажется два экрана с одним и тем же состоянием.

- [ ] **Step 3: Создать монохромную иконку уведомления**

Создать `ArctZ.Android/Resources/drawable/ic_notification.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<!-- Малую иконку уведомления Android с API 21 рисует силуэтом: цветной Icon.png
     превратился бы в белый квадрат. Здесь простой векторный знак. -->
<vector xmlns:android="http://schemas.android.com/apk/res/android"
    android:width="24dp"
    android:height="24dp"
    android:viewportWidth="24"
    android:viewportHeight="24">
    <path
        android:fillColor="#FFFFFFFF"
        android:pathData="M12,2L2,7v10l10,5 10,-5V7L12,2zM12,4.3l7,3.5v8.4l-7,3.5 -7,-3.5V7.8l7,-3.5z" />
    <path
        android:fillColor="#FFFFFFFF"
        android:pathData="M12,8a4,4 0 1,0 0,8 4,4 0 1,0 0,-8z" />
</vector>
```

- [ ] **Step 4: Запрашивать разрешение на уведомления вместе с Bluetooth**

Заменить приватный хелпер `ConnectPermissions()` в `ArctZ.Android/AndroidBluetoothTransport.cs:155-158` так, чтобы на API 33+ он дополнительно возвращал `POST_NOTIFICATIONS`:

```csharp
    // POST_NOTIFICATIONS идёт вместе с разрешением на Bluetooth: подключение — единственный
    // момент, когда пользователь заведомо смотрит на экран, а уведомление нужно ровно с этого
    // момента. Отказ ничего не ломает: сервис работает, просто уведомления не видно.
    private static string[] ConnectPermissions()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            return new[] { "android.permission.BLUETOOTH" };
        }

        return OperatingSystem.IsAndroidVersionAtLeast(33)
            ? new[] { "android.permission.BLUETOOTH_CONNECT", "android.permission.POST_NOTIFICATIONS" }
            : new[] { "android.permission.BLUETOOTH_CONNECT" };
    }
```

Одноимённый хелпер в `AndroidBluetoothEndpointProvider.cs:200-203` **не трогать**: он обслуживает перечисление и сопряжение устройств, то есть срабатывает раньше — до того, как сеанс со станком вообще возможен. Разрешение на уведомления уместно просить ровно в момент подключения.

- [ ] **Step 5: Собрать Android-голову**

Run: `dotnet build ArctZ.Android/ArctZ.Android.csproj --nologo -v q`
Expected: `Сборка успешно завершена`, 0 ошибок. Если сборка падает на `Resource.Drawable.ic_notification` — значит, файл иконки положен не в `Resources/drawable/`; проверить путь.

- [ ] **Step 6: Коммит**

```bash
git add ArctZ.Android/Properties/AndroidManifest.xml ArctZ.Android/MainActivity.cs ArctZ.Android/Resources/drawable/ic_notification.xml ArctZ.Android/AndroidBluetoothTransport.cs
git commit -m "feat: declare foreground service permissions and notification icon"
```

---

### Task 7: Foreground-сервис

**Files:**
- Create: `ArctZ.Android/MachineSessionService.cs`

**Interfaces:**
- Consumes: `BackgroundSessionState` (задача 1), `ProgramViewModel.PauseCommand`/`PlayCommand`/`StopCommand`, `ProgramViewModel.ShutdownAsync(bool)` (задача 4), `ArctZ.App.Services`.
- Produces: `ArctZ.Android.MachineSessionService` со статическим свойством `public static BackgroundSessionState CurrentState { get; set; }` и константами `ActionShow`, `ActionPause`, `ActionResume`, `ActionStop`, используемыми в задаче 8.

Тестов у этой задачи нет: код целиком состоит из вызовов Android API, а `ArctZ.Tests` не может ссылаться на `net10.0-android`. Единственная проверка — сборка и живой прогон на устройстве (задача 9).

- [ ] **Step 1: Написать сервис**

Создать `ArctZ.Android/MachineSessionService.cs`:

```csharp
using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using ArctZ.Services.App;
using ArctZ.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ArctZ.Android;

/// <summary>
/// Постоянное уведомление сеанса со станком плюс — и это главное — живой процесс на время,
/// которого хватает остановить станок при закрытии приложения из недавних. Без запущенного
/// сервиса Android не вызывает OnTaskRemoved и убивает процесс молча, оставив станок
/// доигрывать содержимое буфера прошивки.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public class MachineSessionService : Service
{
    public const string ActionShow = "com.arctz.app.action.SHOW";
    public const string ActionPause = "com.arctz.app.action.PAUSE";
    public const string ActionResume = "com.arctz.app.action.RESUME";
    public const string ActionStop = "com.arctz.app.action.STOP";

    private const string ChannelId = "arctz.session";
    private const int NotificationId = 1;

    /// <summary>Последнее состояние, отданное ядром. Пишется из AndroidBackgroundSessionHost
    /// перед тем, как поднять сервис, и читается здесь при построении уведомления.</summary>
    public static BackgroundSessionState CurrentState { get; set; } =
        new(BackgroundSessionProjector.AppName, "Ожидание", false, false, false);

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        EnsureChannel();
        StartInForeground();

        var program = ArctZ.App.Services?.GetService<ProgramViewModel>();
        switch (intent?.Action)
        {
            case ActionPause:
                program?.PauseCommand.Execute(null);
                break;
            case ActionResume:
                program?.PlayCommand.Execute(null);
                break;
            case ActionStop:
                program?.StopCommand.Execute(null);
                break;
        }

        // NotSticky: перезапускать сервис после убийства процесса бессмысленно — вместе с
        // процессом исчезли и ViewModel, и связь со станком, управлять нечем.
        return StartCommandResult.NotSticky;
    }

    /// <summary>Приложение смахнули из недавних. Активности уже нет, спрашивать некого —
    /// останавливаем станок молча и только потом отпускаем процесс.</summary>
    public override void OnTaskRemoved(Intent? rootIntent)
    {
        base.OnTaskRemoved(rootIntent);

        var program = ArctZ.App.Services?.GetService<ProgramViewModel>();
        if (program is null)
        {
            StopSession();
            return;
        }

        _ = StopMachineThenSessionAsync(program);
    }

    private async Task StopMachineThenSessionAsync(ProgramViewModel program)
    {
        try
        {
            await program.ShutdownAsync(confirmIfRunning: false);
        }
        catch (Exception)
        {
            // Связь могла оборваться вместе с закрытием приложения. Остановить станок в этом
            // случае уже нечем, но процесс отпустить надо в любом случае — иначе сервис
            // останется висеть в шторке навсегда.
        }
        finally
        {
            StopSession();
        }
    }

    private void StopSession()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(24))
        {
            StopForeground(StopForegroundFlags.Remove);
        }
        else
        {
#pragma warning disable CA1422 // до API 24 другой перегрузки нет
            StopForeground(removeNotification: true);
#pragma warning restore CA1422
        }

        StopSelf();
    }

    private void StartInForeground()
    {
        var notification = BuildNotification();

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeConnectedDevice);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }
    }

    private void EnsureChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        if (manager is null || manager.GetNotificationChannel(ChannelId) is not null)
        {
            return;
        }

        // Low: уведомление висит всё время работы со станком, звук и всплытие тут были бы
        // наказанием.
        var channel = new NotificationChannel(ChannelId, "Сеанс со станком", NotificationImportance.Low)
        {
            Description = "Состояние связи со станком и управление выполнением программы",
        };
        manager.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification()
    {
        var state = CurrentState;

        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, ChannelId)
            : new Notification.Builder(this);

        builder
            .SetContentTitle(state.Title)
            .SetContentText(state.Status)
            .SetSmallIcon(Resource.Drawable.ic_notification)
            .SetOngoing(true)
            .SetContentIntent(OpenAppIntent());

        if (state.CanPause)
        {
            builder.AddAction(BuildAction(global::Android.Resource.Drawable.IcMediaPause, "Пауза", ActionPause));
        }

        if (state.CanResume)
        {
            builder.AddAction(BuildAction(global::Android.Resource.Drawable.IcMediaPlay, "Продолжить", ActionResume));
        }

        if (state.CanStop)
        {
            builder.AddAction(BuildAction(global::Android.Resource.Drawable.IcMenuCloseClearCancel, "Стоп", ActionStop));
        }

        return builder.Build();
    }

    private Notification.Action BuildAction(int icon, string title, string action)
    {
        var intent = new Intent(this, typeof(MachineSessionService)).SetAction(action);
        var pending = PendingIntent.GetService(this, action.GetHashCode(), intent, PendingIntentFlags())!;

        return new Notification.Action.Builder(icon, title, pending).Build();
    }

    private PendingIntent OpenAppIntent()
    {
        var intent = new Intent(this, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.NewTask);

        return PendingIntent.GetActivity(this, 0, intent, PendingIntentFlags())!;
    }

    // Immutable обязателен с API 31; UpdateCurrent нужен, чтобы кнопки не залипали на первом
    // созданном интенте.
    private static PendingIntentFlags PendingIntentFlags() =>
        OperatingSystem.IsAndroidVersionAtLeast(31)
            ? global::Android.App.PendingIntentFlags.UpdateCurrent | global::Android.App.PendingIntentFlags.Immutable
            : global::Android.App.PendingIntentFlags.UpdateCurrent;
}
```

- [ ] **Step 2: Собрать Android-голову**

Run: `dotnet build ArctZ.Android/ArctZ.Android.csproj --nologo -v q`
Expected: `Сборка успешно завершена`, 0 ошибок и 0 предупреждений CA1416 (устаревшие/новые API вне версионных гейтов). Если появилось CA1416 — добавить недостающий `OperatingSystem.IsAndroidVersionAtLeast`, а не подавлять предупреждение.

- [ ] **Step 3: Коммит**

```bash
git add ArctZ.Android/MachineSessionService.cs
git commit -m "feat: add the Android foreground session service"
```

---

### Task 8: Android-реализация seam

**Files:**
- Create: `ArctZ.Android/AndroidBackgroundSessionHost.cs`
- Modify: `ArctZ.Android/Application.cs:22-30`

**Interfaces:**
- Consumes: `IBackgroundSessionHost` (задача 2), `MachineSessionService.CurrentState` и `MachineSessionService.ActionShow` (задача 7).
- Produces: `ArctZ.Android.AndroidBackgroundSessionHost`, зарегистрированный как `IBackgroundSessionHost` в контейнере Android-головы.

- [ ] **Step 1: Написать реализацию хоста**

Создать `ArctZ.Android/AndroidBackgroundSessionHost.cs`:

```csharp
using System;
using Android.Content;
using ArctZ.Services.App;

namespace ArctZ.Android;

/// <summary>
/// Поднимает и обновляет <see cref="MachineSessionService"/>. Обновление — это тот же запуск
/// сервиса с ActionShow: сервис на каждый старт перестраивает уведомление из
/// <see cref="MachineSessionService.CurrentState"/>, так что отдельный путь обновления не нужен.
/// </summary>
public sealed class AndroidBackgroundSessionHost : IBackgroundSessionHost
{
    public void Update(BackgroundSessionState state)
    {
        MachineSessionService.CurrentState = state;

        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(MachineSessionService)).SetAction(MachineSessionService.ActionShow);

        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                context.StartForegroundService(intent);
            }
            else
            {
                context.StartService(intent);
            }
        }
        catch (Exception)
        {
            // С API 31 запуск foreground-сервиса из фона запрещён и бросает
            // ForegroundServiceStartNotAllowedException. Первый Update приходит на подключение,
            // то есть из активного приложения, поэтому в норме сюда не попадаем; но обновление
            // состояния не должно ронять приложение из-за уведомления.
        }
    }

    public void Stop()
    {
        var context = global::Android.App.Application.Context;
        context.StopService(new Intent(context, typeof(MachineSessionService)));
    }
}
```

- [ ] **Step 2: Зарегистрировать хост в контейнере головы**

В `ArctZ.Android/Application.cs`, в методе `CustomizeAppBuilder`, после строки `services.AddArctZCore();` и рядом с остальными платформенными регистрациями добавить:

```csharp
            services.AddSingleton<IBackgroundSessionHost, AndroidBackgroundSessionHost>();
```

Добавить в шапку файла `using ArctZ.Services.App;`.

Регистрация идёт **после** `AddArctZCore()` намеренно: при резолве одиночного сервиса побеждает последняя регистрация, поэтому она вытесняет `NullBackgroundSessionHost` из ядра. Ровно это поведение закреплено тестом `AddArctZCore_LetsAPlatformHeadReplaceTheBackgroundSessionHost` из задачи 5.

- [ ] **Step 3: Собрать Android-голову**

Run: `dotnet build ArctZ.Android/ArctZ.Android.csproj --nologo -v q`
Expected: `Сборка успешно завершена`, 0 ошибок.

- [ ] **Step 4: Прогнать весь набор тестов**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --nologo -v q`
Expected: PASS, ни одного падения (Android-голова тестами не покрыта, но изменения в ядре из задач 1–5 обязаны остаться зелёными).

- [ ] **Step 5: Коммит**

```bash
git add ArctZ.Android/AndroidBackgroundSessionHost.cs ArctZ.Android/Application.cs
git commit -m "feat: drive the foreground session from the Android head"
```

---

### Task 9: Живая проверка на устройстве

**Files:** нет. Это проверка, а не изменение кода.

**Interfaces:** нет.

По правилу из `CLAUDE.md` сборку APK, установку на устройство и запуск выполняет **пользователь**. Агент не собирает и не деплоит APK самостоятельно.

- [ ] **Step 1: Попросить пользователя собрать и установить актуальную сборку**

Через `AskUserQuestion` попросить собрать APK, установить его на устройство и подтвердить готовность к проверке.

- [ ] **Step 2: Попросить пользователя проверить сценарии**

Список для проверки:

1. Подключиться к станку — в шторке появилось уведомление со статусом.
2. Запустить программу — в уведомлении появились кнопки «Пауза» и «Стоп», статус сменился на «Выполнение».
3. Нажать «Пауза» в шторке — станок встал, кнопка сменилась на «Продолжить».
4. Нажать «Продолжить» в шторке — выполнение возобновилось.
5. Нажать «Стоп» в шторке — программа остановлена, кнопки исчезли.
6. Тап по телу уведомления — открылось приложение, ровно одна копия экрана.
7. Отключиться от станка кнопкой «Отключить» — уведомление исчезло.
8. **Смахнуть приложение из недавних во время выполнения программы** — станок остановился, уведомление исчезло.
9. **Смахнуть из недавних во время джога** — станок остановился.

- [ ] **Step 3: Задать поточечные вопросы**

Через `AskUserQuestion` задать отдельный вопрос по каждому изменённому поведению (появление уведомления, каждая из трёх кнопок, тап по уведомлению, исчезновение при отключении, остановка при смахивании во время программы, остановка при смахивании во время джога) — по одному вопросу на сценарий, а не один общий «выглядит нормально?».

- [ ] **Step 4: Коммит правок по итогам проверки**

Если проверка вскрыла дефекты — чинить их по TDD там, где дефект в ядре, и точечными правками в Android-голове, после чего повторить шаги 1–3.

---

## Итог

После задачи 9 приложение на Android: держит уведомление всё время, пока есть связь со станком; позволяет управлять выполнением из шторки; останавливает станок полной последовательностью (`0x85` → сброс очереди → `!` → ожидание остановки → `0x18`) при закрытии из недавних. Desktop, Browser и iOS работают ровно как раньше — у них зарегистрирован `NullBackgroundSessionHost`.

Ограничение, которое остаётся: «Закрыть принудительно» в настройках приложения, агрессивный менеджер питания (Xiaomi, Huawei, Samsung) и падение процесса убивают процесс мгновенно — послать команды в этот момент невозможно. Это ограничение Android, из приложения не обходится.
