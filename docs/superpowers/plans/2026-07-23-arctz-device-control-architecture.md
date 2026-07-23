# ArctZ Device Control Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the device-control layer for ArctZ (command model → serialization → send-policy pipeline, `DeviceSession` orchestrator, connection/status state, MVVM composition) on top of the existing `VirtualJoystick`/`MainViewModel` skeleton, wired via DI across all four platform heads.

**Architecture:** Three independent layers for commands (data model `IDeviceCommand` → `ICommandSerializer` → three send-policy channels: realtime/queue/jog-throttled), coordinated by `DeviceSession`, which is the sole facade ViewModels talk to. `ConnectionViewModel` composes into `MainViewModel` via the existing `ViewLocator` convention. `Microsoft.Extensions.DependencyInjection` wires platform-specific `IDeviceTransport` implementations (Desktop/Android real, iOS/Browser stub) into the shared core.

**Tech Stack:** .NET 10, Avalonia UI 12, `CommunityToolkit.Mvvm` 8.4, `Microsoft.Extensions.DependencyInjection`, xUnit (new `ArctZ.Tests` project).

**Spec:** `docs/superpowers/specs/2026-07-23-arctz-app-architecture-design.md`

## Global Constraints

- Target framework `net10.0` everywhere (mobile heads use `net10.0-android`/iOS equivalents); `Nullable` enabled; `LangVersion` latest.
- Avalonia compiled bindings are on by default — every new `.axaml` view declares `x:DataType`.
- ViewModels use `CommunityToolkit.Mvvm` code-gen (`[ObservableProperty]`, `[RelayCommand]`), not hand-written properties/commands.
- Package versions are centrally managed in `Directory.Packages.props` — add new `PackageVersion` entries there, `PackageReference` (no version) in individual `.csproj` files.
- Jog commands (`$J=`) bypass `ICommandQueue`'s ack-wait entirely — sent directly to `IDeviceTransport`, because throttling already bounds their rate and waiting on `ok` would make live control jerky.
- iOS and Browser heads get `NotSupportedDeviceTransport` for now (classic Bluetooth SPP has no public API on either platform) — `IDeviceSession`/`ConnectionViewModel` must treat this the same as any other connect failure, not a crash.
- Command handling is split into three independent concerns: data model (`IDeviceCommand` records), text serialization (`ICommandSerializer`), and send policy (`IRealtimeCommandChannel` / `ICommandQueue` / `IJogScheduler`) — do not merge them back into one class.

---

## Task 1: Scaffold `ArctZ.Tests` project

**Files:**
- Create: `ArctZ.Tests/ArctZ.Tests.csproj`
- Create: `ArctZ.Tests/GlobalUsings.cs`
- Modify: `ArctZ.slnx`
- Modify: `Directory.Packages.props`
- Modify: `ArctZ/ArctZ.csproj`

**Interfaces:**
- Produces: an `ArctZ.Tests` project that compiles, references `ArctZ`, and can run via `dotnet test`. `ArctZ.csproj` exposes its internals to `ArctZ.Tests` via `InternalsVisibleTo` (needed later for `ConnectionViewModel`'s test-only constructor in Task 15).

- [ ] **Step 1: Add test package versions to `Directory.Packages.props`**

Add inside the existing `<ItemGroup>` (after the `CommunityToolkit.Mvvm` entry):

```xml
        <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />

        <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
        <PackageVersion Include="xunit" Version="2.9.2" />
        <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
```

- [ ] **Step 2: Create `ArctZ.Tests/ArctZ.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ArctZ\ArctZ.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create `ArctZ.Tests/GlobalUsings.cs`**

```csharp
global using Xunit;
```

- [ ] **Step 4: Add `InternalsVisibleTo` to `ArctZ/ArctZ.csproj`**

Add a new `ItemGroup` before the closing `</Project>` tag:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="ArctZ.Tests" />
  </ItemGroup>
```

- [ ] **Step 5: Register the new project in `ArctZ.slnx`**

Add before the closing `</Solution>` tag:

```xml
  <Project Path="ArctZ.Tests/ArctZ.Tests.csproj" />
```

- [ ] **Step 6: Verify the empty test project builds and runs**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: build succeeds, "No test summaries" or 0 tests run, no errors.

- [ ] **Step 7: Commit**

```bash
git add ArctZ.slnx Directory.Packages.props ArctZ/ArctZ.csproj ArctZ.Tests/ArctZ.Tests.csproj ArctZ.Tests/GlobalUsings.cs
git commit -m "test: scaffold ArctZ.Tests project"
```

---

## Task 2: Command model types

**Files:**
- Create: `ArctZ/Services/Device/Commands/IDeviceCommand.cs`
- Create: `ArctZ/Services/Device/JoystickState.cs`

**Interfaces:**
- Produces: `IDeviceCommand` marker interface; `AxisDeltas(double X, double Y)`; `JogCommand(AxisDeltas Deltas, double Feed) : IDeviceCommand`; `GCodeLineCommand(string Line) : IDeviceCommand`; `RealtimeCommand(byte Value) : IDeviceCommand` with static members `StatusQuery`, `FeedHold`, `CycleStartResume`, `JogCancel`; `JoystickState(double X, double Y, double Force)`.

This task is pure data types with no behavior, so there is nothing to drive with a failing test — the "test" is that the types compile and are usable in later tasks' tests.

- [ ] **Step 1: Create `ArctZ/Services/Device/Commands/IDeviceCommand.cs`**

```csharp
namespace ArctZ.Services.Device.Commands;

public interface IDeviceCommand
{
}

public readonly record struct AxisDeltas(double X, double Y);

/// <summary>
/// Live jog move built from the joystick each throttle tick. Axis mapping
/// (which joystick axis drives which physical jib axis) is provisional
/// pending docs/hardware/mechanics.md.
/// </summary>
public sealed record JogCommand(AxisDeltas Deltas, double Feed) : IDeviceCommand;

/// <summary>A single queued G-code or $-settings line (e.g. "$H", "G28").</summary>
public sealed record GCodeLineCommand(string Line) : IDeviceCommand;

/// <summary>A single-byte realtime command sent immediately, outside the ack queue.</summary>
public sealed record RealtimeCommand(byte Value) : IDeviceCommand
{
    public static readonly RealtimeCommand StatusQuery = new((byte)'?');
    public static readonly RealtimeCommand FeedHold = new((byte)'!');
    public static readonly RealtimeCommand CycleStartResume = new((byte)'~');
    public static readonly RealtimeCommand JogCancel = new(0x85);
}
```

- [ ] **Step 2: Create `ArctZ/Services/Device/JoystickState.cs`**

```csharp
namespace ArctZ.Services.Device;

/// <summary>
/// Device-layer snapshot of joystick input, decoupled from
/// Components.VirtualJoystick.JoystickEventArgs so Services/Device does
/// not depend on a UI control's event type.
/// </summary>
public readonly record struct JoystickState(double X, double Y, double Force);
```

- [ ] **Step 3: Verify the solution builds**

Run: `dotnet build ArctZ/ArctZ.csproj`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add ArctZ/Services/Device/Commands/IDeviceCommand.cs ArctZ/Services/Device/JoystickState.cs
git commit -m "feat: add device command model types"
```

---

## Task 3: `ICommandSerializer` / `FluidNcCommandSerializer`

**Files:**
- Create: `ArctZ/Services/Device/ICommandSerializer.cs`
- Create: `ArctZ/Services/Device/FluidNcCommandSerializer.cs`
- Test: `ArctZ.Tests/Services/Device/FluidNcCommandSerializerTests.cs`

**Interfaces:**
- Consumes: `IDeviceCommand`, `JogCommand`, `GCodeLineCommand`, `RealtimeCommand`, `AxisDeltas` (Task 2).
- Produces: `ICommandSerializer.Serialize(IDeviceCommand) : string`; `FluidNcCommandSerializer : ICommandSerializer`.

- [ ] **Step 1: Write the failing tests**

```csharp
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Tests.Services.Device;

public class FluidNcCommandSerializerTests
{
    private readonly FluidNcCommandSerializer _serializer = new();

    [Fact]
    public void Serialize_JogCommand_ProducesRelativeJogLine()
    {
        var command = new JogCommand(new AxisDeltas(10, -5), 500);

        var result = _serializer.Serialize(command);

        Assert.Equal("$J=G91 G21 X10 Y-5 F500", result);
    }

    [Fact]
    public void Serialize_GCodeLineCommand_ReturnsLineUnchanged()
    {
        var command = new GCodeLineCommand("$H");

        var result = _serializer.Serialize(command);

        Assert.Equal("$H", result);
    }

    [Fact]
    public void Serialize_RealtimeCommand_ReturnsSingleCharacterString()
    {
        var result = _serializer.Serialize(RealtimeCommand.JogCancel);

        Assert.Equal(((char)0x85).ToString(), result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter FluidNcCommandSerializerTests`
Expected: FAIL — `FluidNcCommandSerializer` does not exist.

- [ ] **Step 3: Create `ArctZ/Services/Device/ICommandSerializer.cs`**

```csharp
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public interface ICommandSerializer
{
    string Serialize(IDeviceCommand command);
}
```

- [ ] **Step 4: Create `ArctZ/Services/Device/FluidNcCommandSerializer.cs`**

```csharp
using System;
using System.Globalization;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public sealed class FluidNcCommandSerializer : ICommandSerializer
{
    public string Serialize(IDeviceCommand command) => command switch
    {
        JogCommand jog => SerializeJog(jog),
        GCodeLineCommand line => line.Line,
        RealtimeCommand realtime => ((char)realtime.Value).ToString(),
        _ => throw new NotSupportedException($"Unknown command type: {command.GetType()}")
    };

    private static string SerializeJog(JogCommand jog)
    {
        var x = jog.Deltas.X.ToString("0.###", CultureInfo.InvariantCulture);
        var y = jog.Deltas.Y.ToString("0.###", CultureInfo.InvariantCulture);
        var feed = jog.Feed.ToString("0.###", CultureInfo.InvariantCulture);
        return $"$J=G91 G21 X{x} Y{y} F{feed}";
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter FluidNcCommandSerializerTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Services/Device/ICommandSerializer.cs ArctZ/Services/Device/FluidNcCommandSerializer.cs ArctZ.Tests/Services/Device/FluidNcCommandSerializerTests.cs
git commit -m "feat: add FluidNC command serializer"
```

---

## Task 4: `IDeviceTransport` + `FakeDeviceTransport` test double

**Files:**
- Create: `ArctZ/Services/Device/IDeviceTransport.cs`
- Create: `ArctZ.Tests/Services/Device/FakeDeviceTransport.cs`

**Interfaces:**
- Produces: `IDeviceTransport` (`IsConnected`, `LineReceived`, `Disconnected`, `ConnectAsync`, `DisconnectAsync`, `SendLineAsync`, `SendRawByteAsync`); `FakeDeviceTransport : IDeviceTransport` — shared test double used by Tasks 5, 6, 10, 11, 12, 13, 14.

`FakeDeviceTransport` is infrastructure for other tests, not behavior under test itself — no TDD cycle here, just build it and verify it compiles.

- [ ] **Step 1: Create `ArctZ/Services/Device/IDeviceTransport.cs`**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

/// <summary>Byte-stream abstraction over whatever the platform gives us for the paired FluidNC device (BT SPP COM port, RFCOMM socket, ...).</summary>
public interface IDeviceTransport
{
    bool IsConnected { get; }

    /// <summary>Raised for every line the device sends, newline already stripped.</summary>
    event Action<string>? LineReceived;

    /// <summary>Raised when the underlying link drops, whether requested or not.</summary>
    event Action? Disconnected;

    Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default);

    Task DisconnectAsync();

    /// <summary>Sends a newline-terminated G-code/$-command line.</summary>
    Task SendLineAsync(string line, CancellationToken cancellationToken = default);

    /// <summary>Sends a single realtime byte with no line terminator.</summary>
    Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Create `ArctZ.Tests/Services/Device/FakeDeviceTransport.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public sealed class FakeDeviceTransport : IDeviceTransport
{
    public List<string> SentLines { get; } = new();
    public List<byte> SentRawBytes { get; } = new();
    public bool IsConnected { get; private set; }

    /// <summary>Number of upcoming ConnectAsync calls that should throw before one succeeds — used to simulate flaky reconnects.</summary>
    public int ConnectFailuresRemaining { get; set; }

    public event Action<string>? LineReceived;
    public event Action? Disconnected;

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (ConnectFailuresRemaining > 0)
        {
            ConnectFailuresRemaining--;
            throw new InvalidOperationException("Simulated connect failure");
        }

        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        SentLines.Add(line);
        return Task.CompletedTask;
    }

    public Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default)
    {
        SentRawBytes.Add(value);
        return Task.CompletedTask;
    }

    public void SimulateReceivedLine(string line) => LineReceived?.Invoke(line);

    public void SimulateDisconnect()
    {
        IsConnected = false;
        Disconnected?.Invoke();
    }
}
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build ArctZ.Tests/ArctZ.Tests.csproj`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add ArctZ/Services/Device/IDeviceTransport.cs ArctZ.Tests/Services/Device/FakeDeviceTransport.cs
git commit -m "feat: add IDeviceTransport and FakeDeviceTransport test double"
```

---

## Task 5: `IRealtimeCommandChannel` / `RealtimeCommandChannel`

**Files:**
- Create: `ArctZ/Services/Device/IRealtimeCommandChannel.cs`
- Create: `ArctZ/Services/Device/RealtimeCommandChannel.cs`
- Test: `ArctZ.Tests/Services/Device/RealtimeCommandChannelTests.cs`

**Interfaces:**
- Consumes: `IDeviceTransport` (Task 4), `RealtimeCommand` (Task 2), `FakeDeviceTransport` (Task 4).
- Produces: `IRealtimeCommandChannel.SendAsync(RealtimeCommand, CancellationToken)`; `RealtimeCommandChannel : IRealtimeCommandChannel`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;
using ArctZ.Tests.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class RealtimeCommandChannelTests
{
    [Fact]
    public async Task SendAsync_SendsRawByteThroughTransport()
    {
        var transport = new FakeDeviceTransport();
        var channel = new RealtimeCommandChannel(transport);

        await channel.SendAsync(RealtimeCommand.JogCancel);

        Assert.Single(transport.SentRawBytes);
        Assert.Equal((byte)0x85, transport.SentRawBytes[0]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter RealtimeCommandChannelTests`
Expected: FAIL — `RealtimeCommandChannel` does not exist.

- [ ] **Step 3: Create `ArctZ/Services/Device/IRealtimeCommandChannel.cs`**

```csharp
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public interface IRealtimeCommandChannel
{
    Task SendAsync(RealtimeCommand command, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Create `ArctZ/Services/Device/RealtimeCommandChannel.cs`**

```csharp
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public sealed class RealtimeCommandChannel : IRealtimeCommandChannel
{
    private readonly IDeviceTransport _transport;

    public RealtimeCommandChannel(IDeviceTransport transport)
    {
        _transport = transport;
    }

    public Task SendAsync(RealtimeCommand command, CancellationToken cancellationToken = default) =>
        _transport.SendRawByteAsync(command.Value, cancellationToken);
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter RealtimeCommandChannelTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Services/Device/IRealtimeCommandChannel.cs ArctZ/Services/Device/RealtimeCommandChannel.cs ArctZ.Tests/Services/Device/RealtimeCommandChannelTests.cs
git commit -m "feat: add realtime command channel"
```

---

## Task 6: `ICommandQueue` / `CommandQueue`

**Files:**
- Create: `ArctZ/Services/Device/ICommandQueue.cs`
- Create: `ArctZ/Services/Device/CommandQueue.cs`
- Test: `ArctZ.Tests/Services/Device/CommandQueueTests.cs`

**Interfaces:**
- Consumes: `IDeviceTransport`, `FakeDeviceTransport` (Task 4), `ICommandSerializer`, `FluidNcCommandSerializer` (Task 3), `GCodeLineCommand` (Task 2).
- Produces: `CommandOutcome` enum (`Acknowledged`, `Rejected`); `CommandResult(CommandOutcome Outcome, int? ErrorCode)`; `ICommandQueue` (`CommandCompleted` event, `EnqueueAsync`, `HandleOk`, `HandleError`); `CommandQueue : ICommandQueue` — `CommandCompleted` and `EnqueueAsync`'s returned `Task<CommandResult>` are relied on by `DeviceSession` (Task 12).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;
using ArctZ.Tests.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class CommandQueueTests
{
    private readonly FakeDeviceTransport _transport = new();
    private readonly CommandQueue _queue;

    public CommandQueueTests()
    {
        _queue = new CommandQueue(_transport, new FluidNcCommandSerializer());
    }

    [Fact]
    public void EnqueueAsync_SendsFirstCommandImmediately()
    {
        _ = _queue.EnqueueAsync(new GCodeLineCommand("$H"));

        Assert.Equal(new[] { "$H" }, _transport.SentLines);
    }

    [Fact]
    public void EnqueueAsync_SecondCommandWaitsForFirstAck()
    {
        _ = _queue.EnqueueAsync(new GCodeLineCommand("$H"));
        _ = _queue.EnqueueAsync(new GCodeLineCommand("G28"));

        Assert.Equal(new[] { "$H" }, _transport.SentLines);

        _queue.HandleOk();

        Assert.Equal(new[] { "$H", "G28" }, _transport.SentLines);
    }

    [Fact]
    public async Task HandleOk_CompletesPendingTaskAsAcknowledged()
    {
        var resultTask = _queue.EnqueueAsync(new GCodeLineCommand("$H"));

        _queue.HandleOk();
        var result = await resultTask;

        Assert.Equal(CommandOutcome.Acknowledged, result.Outcome);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task HandleError_CompletesPendingTaskAsRejectedAndContinuesQueue()
    {
        var firstResult = _queue.EnqueueAsync(new GCodeLineCommand("$H"));
        _ = _queue.EnqueueAsync(new GCodeLineCommand("G28"));

        _queue.HandleError(9);
        var result = await firstResult;

        Assert.Equal(CommandOutcome.Rejected, result.Outcome);
        Assert.Equal(9, result.ErrorCode);
        Assert.Equal(new[] { "$H", "G28" }, _transport.SentLines);
    }

    [Fact]
    public void HandleError_RaisesCommandCompletedWithRejectedResult()
    {
        GCodeLineCommand? completedCommand = null;
        CommandResult? completedResult = null;
        _queue.CommandCompleted += (command, result) =>
        {
            completedCommand = command;
            completedResult = result;
        };

        _ = _queue.EnqueueAsync(new GCodeLineCommand("$H"));
        _queue.HandleError(9);

        Assert.Equal("$H", completedCommand!.Line);
        Assert.Equal(CommandOutcome.Rejected, completedResult!.Value.Outcome);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter CommandQueueTests`
Expected: FAIL — `CommandQueue` does not exist.

- [ ] **Step 3: Create `ArctZ/Services/Device/ICommandQueue.cs`**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public enum CommandOutcome
{
    Acknowledged,
    Rejected
}

public readonly record struct CommandResult(CommandOutcome Outcome, int? ErrorCode);

public interface ICommandQueue
{
    event Action<GCodeLineCommand, CommandResult>? CommandCompleted;

    Task<CommandResult> EnqueueAsync(GCodeLineCommand command, CancellationToken cancellationToken = default);

    /// <summary>Call when the transport receives a plain "ok" line.</summary>
    void HandleOk();

    /// <summary>Call when the transport receives an "error:N" line.</summary>
    void HandleError(int code);
}
```

- [ ] **Step 4: Create `ArctZ/Services/Device/CommandQueue.cs`**

```csharp
using System;
using System.Collections.Generic;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public sealed class CommandQueue : ICommandQueue
{
    private readonly IDeviceTransport _transport;
    private readonly ICommandSerializer _serializer;
    private readonly object _lock = new();
    private readonly Queue<(GCodeLineCommand Command, TaskCompletionSource<CommandResult> Completion)> _pending = new();
    private (GCodeLineCommand Command, TaskCompletionSource<CommandResult> Completion)? _inFlight;

    public event Action<GCodeLineCommand, CommandResult>? CommandCompleted;

    public CommandQueue(IDeviceTransport transport, ICommandSerializer serializer)
    {
        _transport = transport;
        _serializer = serializer;
    }

    public Task<CommandResult> EnqueueAsync(GCodeLineCommand command, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _pending.Enqueue((command, completion));
            TrySendNextLocked();
        }

        return completion.Task;
    }

    public void HandleOk() => CompleteInFlight(new CommandResult(CommandOutcome.Acknowledged, null));

    public void HandleError(int code) => CompleteInFlight(new CommandResult(CommandOutcome.Rejected, code));

    private void CompleteInFlight(CommandResult result)
    {
        (GCodeLineCommand Command, TaskCompletionSource<CommandResult> Completion)? completed;
        lock (_lock)
        {
            if (_inFlight is null)
            {
                return;
            }

            completed = _inFlight;
            _inFlight = null;
            TrySendNextLocked();
        }

        completed.Value.Completion.SetResult(result);
        CommandCompleted?.Invoke(completed.Value.Command, result);
    }

    private void TrySendNextLocked()
    {
        if (_inFlight is not null || _pending.Count == 0)
        {
            return;
        }

        _inFlight = _pending.Dequeue();
        var text = _serializer.Serialize(_inFlight.Value.Command);
        _ = _transport.SendLineAsync(text);
    }
}
```

Add `using System.Threading.Tasks;` to the top of the file alongside the other usings.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter CommandQueueTests`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Services/Device/ICommandQueue.cs ArctZ/Services/Device/CommandQueue.cs ArctZ.Tests/Services/Device/CommandQueueTests.cs
git commit -m "feat: add ack-based command queue"
```

---

## Task 7: `IStatusParser` / `FluidNcStatusParser`

**Files:**
- Create: `ArctZ/Services/Device/DeviceStatus.cs`
- Create: `ArctZ/Services/Device/FluidNcLine.cs`
- Create: `ArctZ/Services/Device/IStatusParser.cs`
- Create: `ArctZ/Services/Device/FluidNcStatusParser.cs`
- Test: `ArctZ.Tests/Services/Device/FluidNcStatusParserTests.cs`

**Interfaces:**
- Produces: `MachineState` enum (`Idle`, `Run`, `Jog`, `Hold`, `Home`, `Alarm`, `Unknown`); `DeviceStatus(MachineState State, double WPosX, double WPosY, double WPosZ)`; `FluidNcLine` abstract record with `StatusReportLine(DeviceStatus Status)`, `OkLine`, `ErrorLine(int Code)`, `AlarmLine(int Code)`, `UnrecognizedLine(string Raw)`; `IStatusParser.Parse(string) : FluidNcLine`; `FluidNcStatusParser : IStatusParser` — all relied on by `DeviceSession` (Task 12).

- [ ] **Step 1: Write the failing tests**

```csharp
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class FluidNcStatusParserTests
{
    private readonly FluidNcStatusParser _parser = new();

    [Fact]
    public void Parse_StatusReportLine_ExtractsStateAndWorkPosition()
    {
        var result = _parser.Parse("<Idle|WPos:0.000,-80.000,-10.540|Bf:15,128|FS:0,0|Ov:100,100,100>");

        var report = Assert.IsType<StatusReportLine>(result);
        Assert.Equal(MachineState.Idle, report.Status.State);
        Assert.Equal(0.000, report.Status.WPosX);
        Assert.Equal(-80.000, report.Status.WPosY);
        Assert.Equal(-10.540, report.Status.WPosZ);
    }

    [Fact]
    public void Parse_Ok_ReturnsOkLine()
    {
        Assert.IsType<OkLine>(_parser.Parse("ok"));
    }

    [Fact]
    public void Parse_Error_ReturnsErrorLineWithCode()
    {
        var result = Assert.IsType<ErrorLine>(_parser.Parse("error:9"));
        Assert.Equal(9, result.Code);
    }

    [Fact]
    public void Parse_Alarm_ReturnsAlarmLineWithCode()
    {
        var result = Assert.IsType<AlarmLine>(_parser.Parse("ALARM:1"));
        Assert.Equal(1, result.Code);
    }

    [Fact]
    public void Parse_UnknownText_ReturnsUnrecognizedLine()
    {
        var result = Assert.IsType<UnrecognizedLine>(_parser.Parse("garbage"));
        Assert.Equal("garbage", result.Raw);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter FluidNcStatusParserTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Create `ArctZ/Services/Device/DeviceStatus.cs`**

```csharp
namespace ArctZ.Services.Device;

public enum MachineState
{
    Idle,
    Run,
    Jog,
    Hold,
    Home,
    Alarm,
    Unknown
}

public readonly record struct DeviceStatus(MachineState State, double WPosX, double WPosY, double WPosZ);
```

- [ ] **Step 4: Create `ArctZ/Services/Device/FluidNcLine.cs`**

```csharp
namespace ArctZ.Services.Device;

public abstract record FluidNcLine;

public sealed record StatusReportLine(DeviceStatus Status) : FluidNcLine;

public sealed record OkLine : FluidNcLine;

public sealed record ErrorLine(int Code) : FluidNcLine;

public sealed record AlarmLine(int Code) : FluidNcLine;

public sealed record UnrecognizedLine(string Raw) : FluidNcLine;
```

- [ ] **Step 5: Create `ArctZ/Services/Device/IStatusParser.cs`**

```csharp
namespace ArctZ.Services.Device;

public interface IStatusParser
{
    FluidNcLine Parse(string rawLine);
}
```

- [ ] **Step 6: Create `ArctZ/Services/Device/FluidNcStatusParser.cs`**

```csharp
using System;
using System.Globalization;
using System.Linq;

namespace ArctZ.Services.Device;

public sealed class FluidNcStatusParser : IStatusParser
{
    public FluidNcLine Parse(string rawLine)
    {
        var line = rawLine.Trim();

        if (line.Length == 0)
        {
            return new UnrecognizedLine(rawLine);
        }

        if (line == "ok")
        {
            return new OkLine();
        }

        if (line.StartsWith("error:", StringComparison.Ordinal) &&
            int.TryParse(line.AsSpan(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out var errorCode))
        {
            return new ErrorLine(errorCode);
        }

        if (line.StartsWith("ALARM:", StringComparison.Ordinal) &&
            int.TryParse(line.AsSpan(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out var alarmCode))
        {
            return new AlarmLine(alarmCode);
        }

        if (line.StartsWith('<') && line.EndsWith('>'))
        {
            return ParseStatusReport(line[1..^1], rawLine);
        }

        return new UnrecognizedLine(rawLine);
    }

    private static FluidNcLine ParseStatusReport(string body, string rawLine)
    {
        var fields = body.Split('|');
        if (fields.Length == 0)
        {
            return new UnrecognizedLine(rawLine);
        }

        var state = Enum.TryParse<MachineState>(fields[0], ignoreCase: true, out var parsedState)
            ? parsedState
            : MachineState.Unknown;

        double x = 0, y = 0, z = 0;
        var wPosField = fields.Skip(1).FirstOrDefault(f => f.StartsWith("WPos:", StringComparison.Ordinal));
        if (wPosField is not null)
        {
            var coords = wPosField["WPos:".Length..].Split(',');
            if (coords.Length == 3)
            {
                x = double.Parse(coords[0], CultureInfo.InvariantCulture);
                y = double.Parse(coords[1], CultureInfo.InvariantCulture);
                z = double.Parse(coords[2], CultureInfo.InvariantCulture);
            }
        }

        return new StatusReportLine(new DeviceStatus(state, x, y, z));
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter FluidNcStatusParserTests`
Expected: PASS (5 tests).

- [ ] **Step 8: Commit**

```bash
git add ArctZ/Services/Device/DeviceStatus.cs ArctZ/Services/Device/FluidNcLine.cs ArctZ/Services/Device/IStatusParser.cs ArctZ/Services/Device/FluidNcStatusParser.cs ArctZ.Tests/Services/Device/FluidNcStatusParserTests.cs
git commit -m "feat: add FluidNC status line parser"
```

---

## Task 8: `IJogCommandFactory` / `JogCommandFactory`

**Files:**
- Create: `ArctZ/Services/Device/IJogCommandFactory.cs`
- Create: `ArctZ/Services/Device/JogCommandFactory.cs`
- Test: `ArctZ.Tests/Services/Device/JogCommandFactoryTests.cs`

**Interfaces:**
- Consumes: `JoystickState`, `JogCommand`, `AxisDeltas` (Task 2).
- Produces: `IJogCommandFactory.Create(JoystickState) : JogCommand`; `JogCommandFactory : IJogCommandFactory` — used by `JogScheduler` (Task 10).

- [ ] **Step 1: Write the failing tests**

```csharp
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class JogCommandFactoryTests
{
    private readonly JogCommandFactory _factory = new(maxStepMm: 5.0, maxFeedMmPerMin: 1000.0);

    [Fact]
    public void Create_FullDeflection_ScalesToMaxStepAndFeed()
    {
        var command = _factory.Create(new JoystickState(X: 1, Y: 0, Force: 1));

        Assert.Equal(5.0, command.Deltas.X);
        Assert.Equal(0.0, command.Deltas.Y);
        Assert.Equal(1000.0, command.Feed);
    }

    [Fact]
    public void Create_NegativeAxis_ProducesNegativeDelta()
    {
        var command = _factory.Create(new JoystickState(X: 0, Y: -0.5, Force: 0.5));

        Assert.Equal(-2.5, command.Deltas.Y);
    }

    [Fact]
    public void Create_ZeroForce_ClampsFeedToMinimumOfOne()
    {
        var command = _factory.Create(new JoystickState(X: 0, Y: 0, Force: 0));

        Assert.Equal(1.0, command.Feed);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter JogCommandFactoryTests`
Expected: FAIL — `JogCommandFactory` does not exist.

- [ ] **Step 3: Create `ArctZ/Services/Device/IJogCommandFactory.cs`**

```csharp
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public interface IJogCommandFactory
{
    JogCommand Create(JoystickState state);
}
```

- [ ] **Step 4: Create `ArctZ/Services/Device/JogCommandFactory.cs`**

```csharp
using System;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

/// <summary>
/// Maps raw joystick input to a JogCommand. The X→axis / Y→axis assignment
/// here is a provisional placeholder — the real mapping depends on which
/// physical jib axis (boom lift, pan, ...) each joystick axis should drive,
/// which is still open in docs/hardware/mechanics.md.
/// </summary>
public sealed class JogCommandFactory : IJogCommandFactory
{
    private readonly double _maxStepMm;
    private readonly double _maxFeedMmPerMin;

    public JogCommandFactory(double maxStepMm = 5.0, double maxFeedMmPerMin = 1000.0)
    {
        _maxStepMm = maxStepMm;
        _maxFeedMmPerMin = maxFeedMmPerMin;
    }

    public JogCommand Create(JoystickState state)
    {
        var deltas = new AxisDeltas(state.X * _maxStepMm, state.Y * _maxStepMm);
        var feed = Math.Max(1.0, state.Force * _maxFeedMmPerMin);
        return new JogCommand(deltas, feed);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter JogCommandFactoryTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Services/Device/IJogCommandFactory.cs ArctZ/Services/Device/JogCommandFactory.cs ArctZ.Tests/Services/Device/JogCommandFactoryTests.cs
git commit -m "feat: add joystick-to-jog-command factory"
```

---

## Task 9: `IPeriodicTimer` + `SystemPeriodicTimer` + `ManualPeriodicTimer` test double

**Files:**
- Create: `ArctZ/Services/Device/IPeriodicTimer.cs`
- Create: `ArctZ/Services/Device/SystemPeriodicTimer.cs`
- Create: `ArctZ.Tests/Services/Device/ManualPeriodicTimer.cs`

**Interfaces:**
- Produces: `IPeriodicTimer` (`Elapsed` event, `Start(TimeSpan)`, `Stop()`); `SystemPeriodicTimer : IPeriodicTimer, IDisposable` (production, backed by `System.Threading.Timer`); `ManualPeriodicTimer : IPeriodicTimer` (test double with `IsRunning`, `LastInterval`, `RaiseElapsed()`) — used by `JogScheduler` (Task 10) and `StatusPoller` (Task 11).

Infrastructure task — no independent behavior to TDD beyond what Tasks 10/11 already exercise through it.

- [ ] **Step 1: Create `ArctZ/Services/Device/IPeriodicTimer.cs`**

```csharp
using System;

namespace ArctZ.Services.Device;

public interface IPeriodicTimer
{
    event Action? Elapsed;

    void Start(TimeSpan interval);

    void Stop();
}
```

- [ ] **Step 2: Create `ArctZ/Services/Device/SystemPeriodicTimer.cs`**

```csharp
using System;
using System.Threading;

namespace ArctZ.Services.Device;

public sealed class SystemPeriodicTimer : IPeriodicTimer, IDisposable
{
    private readonly Timer _timer;

    public event Action? Elapsed;

    public SystemPeriodicTimer()
    {
        _timer = new Timer(_ => Elapsed?.Invoke(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start(TimeSpan interval) => _timer.Change(interval, interval);

    public void Stop() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    public void Dispose() => _timer.Dispose();
}
```

- [ ] **Step 3: Create `ArctZ.Tests/Services/Device/ManualPeriodicTimer.cs`**

```csharp
using System;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public sealed class ManualPeriodicTimer : IPeriodicTimer
{
    public bool IsRunning { get; private set; }
    public TimeSpan? LastInterval { get; private set; }

    public event Action? Elapsed;

    public void Start(TimeSpan interval)
    {
        IsRunning = true;
        LastInterval = interval;
    }

    public void Stop() => IsRunning = false;

    public void RaiseElapsed() => Elapsed?.Invoke();
}
```

- [ ] **Step 4: Verify the solution builds**

Run: `dotnet build ArctZ.slnx`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Services/Device/IPeriodicTimer.cs ArctZ/Services/Device/SystemPeriodicTimer.cs ArctZ.Tests/Services/Device/ManualPeriodicTimer.cs
git commit -m "feat: add periodic timer abstraction"
```

---

## Task 10: `IJogScheduler` / `JogScheduler`

**Files:**
- Create: `ArctZ/Services/Device/IJogScheduler.cs`
- Create: `ArctZ/Services/Device/JogScheduler.cs`
- Test: `ArctZ.Tests/Services/Device/JogSchedulerTests.cs`

**Interfaces:**
- Consumes: `IJogCommandFactory` (Task 8), `ICommandSerializer` (Task 3), `IDeviceTransport`/`FakeDeviceTransport` (Task 4), `IRealtimeCommandChannel`/`RealtimeCommandChannel` (Task 5), `IPeriodicTimer`/`ManualPeriodicTimer` (Task 9), `JoystickState` (Task 2).
- Produces: `IJogScheduler` (`IsActive`, `Start()`, `UpdateState(JoystickState)`, `Stop()`); `JogScheduler : IJogScheduler` — used by `DeviceSession` (Task 12).

- [ ] **Step 1: Write the failing tests**

```csharp
using ArctZ.Services.Device;
using ArctZ.Tests.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class JogSchedulerTests
{
    private readonly FakeDeviceTransport _transport = new();
    private readonly ManualPeriodicTimer _timer = new();
    private readonly JogScheduler _scheduler;

    public JogSchedulerTests()
    {
        _scheduler = new JogScheduler(
            new JogCommandFactory(maxStepMm: 5.0, maxFeedMmPerMin: 1000.0),
            new FluidNcCommandSerializer(),
            _transport,
            new RealtimeCommandChannel(_transport),
            _timer,
            TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void Start_StartsTimerAtConfiguredInterval()
    {
        _scheduler.Start();

        Assert.True(_scheduler.IsActive);
        Assert.True(_timer.IsRunning);
        Assert.Equal(TimeSpan.FromMilliseconds(100), _timer.LastInterval);
    }

    [Fact]
    public void Tick_WithNoState_SendsNothing()
    {
        _scheduler.Start();

        _timer.RaiseElapsed();

        Assert.Empty(_transport.SentLines);
    }

    [Fact]
    public void Tick_WithState_SendsSerializedJogLine()
    {
        _scheduler.Start();
        _scheduler.UpdateState(new JoystickState(X: 1, Y: 0, Force: 1));

        _timer.RaiseElapsed();

        Assert.Equal(new[] { "$J=G91 G21 X5 Y0 F1000" }, _transport.SentLines);
    }

    [Fact]
    public void Stop_StopsTimerAndSendsJogCancel()
    {
        _scheduler.Start();
        _scheduler.UpdateState(new JoystickState(X: 1, Y: 0, Force: 1));

        _scheduler.Stop();

        Assert.False(_scheduler.IsActive);
        Assert.False(_timer.IsRunning);
        Assert.Equal(new byte[] { 0x85 }, _transport.SentRawBytes);
    }

    [Fact]
    public void Tick_AfterStop_SendsNothing()
    {
        _scheduler.Start();
        _scheduler.UpdateState(new JoystickState(X: 1, Y: 0, Force: 1));
        _scheduler.Stop();

        _timer.RaiseElapsed();

        Assert.Empty(_transport.SentLines);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter JogSchedulerTests`
Expected: FAIL — `JogScheduler` does not exist.

- [ ] **Step 3: Create `ArctZ/Services/Device/IJogScheduler.cs`**

```csharp
namespace ArctZ.Services.Device;

public interface IJogScheduler
{
    bool IsActive { get; }

    void Start();

    void UpdateState(JoystickState state);

    void Stop();
}
```

- [ ] **Step 4: Create `ArctZ/Services/Device/JogScheduler.cs`**

```csharp
using System;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public sealed class JogScheduler : IJogScheduler
{
    private readonly IJogCommandFactory _commandFactory;
    private readonly ICommandSerializer _serializer;
    private readonly IDeviceTransport _transport;
    private readonly IRealtimeCommandChannel _realtimeChannel;
    private readonly IPeriodicTimer _timer;
    private readonly TimeSpan _interval;
    private JoystickState? _latestState;

    public JogScheduler(
        IJogCommandFactory commandFactory,
        ICommandSerializer serializer,
        IDeviceTransport transport,
        IRealtimeCommandChannel realtimeChannel,
        IPeriodicTimer timer,
        TimeSpan interval)
    {
        _commandFactory = commandFactory;
        _serializer = serializer;
        _transport = transport;
        _realtimeChannel = realtimeChannel;
        _timer = timer;
        _interval = interval;
        _timer.Elapsed += OnElapsed;
    }

    public bool IsActive { get; private set; }

    public void Start()
    {
        IsActive = true;
        _timer.Start(_interval);
    }

    public void UpdateState(JoystickState state) => _latestState = state;

    public void Stop()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        _timer.Stop();
        _latestState = null;
        _ = _realtimeChannel.SendAsync(RealtimeCommand.JogCancel);
    }

    private void OnElapsed()
    {
        if (!IsActive || _latestState is null)
        {
            return;
        }

        var command = _commandFactory.Create(_latestState.Value);
        var text = _serializer.Serialize(command);
        _ = _transport.SendLineAsync(text);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter JogSchedulerTests`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Services/Device/IJogScheduler.cs ArctZ/Services/Device/JogScheduler.cs ArctZ.Tests/Services/Device/JogSchedulerTests.cs
git commit -m "feat: add throttled jog scheduler"
```

---

## Task 11: `IStatusPoller` / `StatusPoller`

**Files:**
- Create: `ArctZ/Services/Device/IStatusPoller.cs`
- Create: `ArctZ/Services/Device/StatusPoller.cs`
- Test: `ArctZ.Tests/Services/Device/StatusPollerTests.cs`

**Interfaces:**
- Consumes: `IRealtimeCommandChannel`/`RealtimeCommandChannel` (Task 5), `IPeriodicTimer`/`ManualPeriodicTimer` (Task 9), `FakeDeviceTransport` (Task 4).
- Produces: `IStatusPoller` (`Start()`, `Stop()`); `StatusPoller : IStatusPoller` — used by `DeviceSession` (Task 12).

- [ ] **Step 1: Write the failing tests**

```csharp
using ArctZ.Services.Device;
using ArctZ.Tests.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class StatusPollerTests
{
    private readonly FakeDeviceTransport _transport = new();
    private readonly ManualPeriodicTimer _timer = new();
    private readonly StatusPoller _poller;

    public StatusPollerTests()
    {
        _poller = new StatusPoller(new RealtimeCommandChannel(_transport), _timer, TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public void Start_StartsTimerAtConfiguredInterval()
    {
        _poller.Start();

        Assert.True(_timer.IsRunning);
        Assert.Equal(TimeSpan.FromMilliseconds(250), _timer.LastInterval);
    }

    [Fact]
    public void Tick_SendsStatusQueryByte()
    {
        _poller.Start();

        _timer.RaiseElapsed();

        Assert.Equal(new byte[] { (byte)'?' }, _transport.SentRawBytes);
    }

    [Fact]
    public void Stop_StopsTimer()
    {
        _poller.Start();

        _poller.Stop();

        Assert.False(_timer.IsRunning);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter StatusPollerTests`
Expected: FAIL — `StatusPoller` does not exist.

- [ ] **Step 3: Create `ArctZ/Services/Device/IStatusPoller.cs`**

```csharp
namespace ArctZ.Services.Device;

public interface IStatusPoller
{
    void Start();

    void Stop();
}
```

- [ ] **Step 4: Create `ArctZ/Services/Device/StatusPoller.cs`**

```csharp
using System;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public sealed class StatusPoller : IStatusPoller
{
    private readonly IRealtimeCommandChannel _realtimeChannel;
    private readonly IPeriodicTimer _timer;
    private readonly TimeSpan _interval;

    public StatusPoller(IRealtimeCommandChannel realtimeChannel, IPeriodicTimer timer, TimeSpan interval)
    {
        _realtimeChannel = realtimeChannel;
        _timer = timer;
        _interval = interval;
        _timer.Elapsed += OnElapsed;
    }

    public void Start() => _timer.Start(_interval);

    public void Stop() => _timer.Stop();

    private void OnElapsed() => _ = _realtimeChannel.SendAsync(RealtimeCommand.StatusQuery);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter StatusPollerTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Services/Device/IStatusPoller.cs ArctZ/Services/Device/StatusPoller.cs ArctZ.Tests/Services/Device/StatusPollerTests.cs
git commit -m "feat: add periodic status poller"
```

---

## Task 12: `IDeviceSession` / `DeviceSession` — core (connect, routing, jog delegation)

**Files:**
- Create: `ArctZ/Services/Device/ConnectionState.cs`
- Create: `ArctZ/Services/Device/CommandRejectedEventArgs.cs`
- Create: `ArctZ/Services/Device/IDeviceSession.cs`
- Create: `ArctZ/Services/Device/DeviceSession.cs`
- Test: `ArctZ.Tests/Services/Device/DeviceSessionTests.cs`

**Interfaces:**
- Consumes: `IDeviceTransport`/`FakeDeviceTransport` (Task 4), `ICommandQueue`/`CommandQueue` (Task 6), `IStatusParser`/`FluidNcStatusParser` (Task 7), `IJogScheduler` (Task 10), `IStatusPoller` (Task 11), `JoystickState` (Task 2).
- Produces: `ConnectionState` enum (`Disconnected`, `Connecting`, `Connected`, `Reconnecting`); `CommandRejectedEventArgs(GCodeLineCommand Command, int ErrorCode)`; `IDeviceSession` (`ConnectionState`, `DeviceStatus`, `ConnectionStateChanged`, `DeviceStatusChanged`, `CommandRejected`, `AlarmTriggered` events; `ConnectAsync`, `DisconnectAsync`, `BeginJog`, `UpdateJog`, `EndJog`, `SendGCodeAsync`, `HomeAsync`, `ResetAlarmAsync`); `DeviceSession : IDeviceSession` — used by `ConnectionViewModel`/`MainViewModel` (Tasks 15, 16) and extended with reconnect in Task 13.

This task covers everything except the reconnect-with-backoff behavior, which is Task 13.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;
using ArctZ.Tests.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class DeviceSessionTests
{
    private readonly FakeDeviceTransport _transport = new();
    private readonly ManualPeriodicTimer _jogTimer = new();
    private readonly ManualPeriodicTimer _pollTimer = new();
    private readonly DeviceSession _session;

    public DeviceSessionTests()
    {
        var serializer = new FluidNcCommandSerializer();
        var realtimeChannel = new RealtimeCommandChannel(_transport);
        var commandQueue = new CommandQueue(_transport, serializer);
        var jogScheduler = new JogScheduler(
            new JogCommandFactory(), serializer, _transport, realtimeChannel, _jogTimer, TimeSpan.FromMilliseconds(100));
        var statusPoller = new StatusPoller(realtimeChannel, _pollTimer, TimeSpan.FromMilliseconds(250));

        _session = new DeviceSession(_transport, commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller);
    }

    [Fact]
    public async Task ConnectAsync_TransitionsThroughConnectingToConnected()
    {
        var states = new List<ConnectionState>();
        _session.ConnectionStateChanged += () => states.Add(_session.ConnectionState);

        await _session.ConnectAsync("COM5");

        Assert.Equal(new[] { ConnectionState.Connecting, ConnectionState.Connected }, states);
        Assert.True(_transport.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_StartsStatusPolling()
    {
        await _session.ConnectAsync("COM5");

        Assert.True(_pollTimer.IsRunning);
    }

    [Fact]
    public async Task OnStatusReportLine_UpdatesDeviceStatusAndRaisesEvent()
    {
        await _session.ConnectAsync("COM5");
        var raised = false;
        _session.DeviceStatusChanged += () => raised = true;

        _transport.SimulateReceivedLine("<Idle|WPos:0.000,-80.000,-10.540|FS:0,0>");

        Assert.True(raised);
        Assert.Equal(MachineState.Idle, _session.DeviceStatus!.Value.State);
    }

    [Fact]
    public async Task OnErrorLine_RaisesCommandRejected()
    {
        await _session.ConnectAsync("COM5");
        CommandRejectedEventArgs? rejected = null;
        _session.CommandRejected += args => rejected = args;

        _ = _session.SendGCodeAsync("G0 X1000");
        _transport.SimulateReceivedLine("error:9");

        Assert.NotNull(rejected);
        Assert.Equal("G0 X1000", rejected!.Command.Line);
        Assert.Equal(9, rejected.ErrorCode);
    }

    [Fact]
    public async Task OnAlarmLine_RaisesAlarmTriggered()
    {
        await _session.ConnectAsync("COM5");
        int? alarmCode = null;
        _session.AlarmTriggered += code => alarmCode = code;

        _transport.SimulateReceivedLine("ALARM:1");

        Assert.Equal(1, alarmCode);
    }

    [Fact]
    public async Task BeginUpdateEndJog_DelegatesToJogScheduler()
    {
        await _session.ConnectAsync("COM5");

        _session.BeginJog();
        _session.UpdateJog(new JoystickState(1, 0, 1));
        _jogTimer.RaiseElapsed();

        Assert.Contains(_transport.SentLines, line => line.StartsWith("$J=", StringComparison.Ordinal));

        _session.EndJog();

        Assert.Contains((byte)0x85, _transport.SentRawBytes);
    }

    [Fact]
    public async Task HomeAsync_EnqueuesHomingCommand()
    {
        await _session.ConnectAsync("COM5");

        _ = _session.HomeAsync();

        Assert.Contains("$H", _transport.SentLines);
    }

    [Fact]
    public async Task ResetAlarmAsync_EnqueuesAlarmResetCommand()
    {
        await _session.ConnectAsync("COM5");

        _ = _session.ResetAlarmAsync();

        Assert.Contains("$X", _transport.SentLines);
    }

    [Fact]
    public async Task DisconnectAsync_StopsPollingAndTransitionsToDisconnected()
    {
        await _session.ConnectAsync("COM5");

        await _session.DisconnectAsync();

        Assert.Equal(ConnectionState.Disconnected, _session.ConnectionState);
        Assert.False(_pollTimer.IsRunning);
        Assert.False(_transport.IsConnected);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter DeviceSessionTests`
Expected: FAIL — `DeviceSession` does not exist.

- [ ] **Step 3: Create `ArctZ/Services/Device/ConnectionState.cs`**

```csharp
namespace ArctZ.Services.Device;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting
}
```

- [ ] **Step 4: Create `ArctZ/Services/Device/CommandRejectedEventArgs.cs`**

```csharp
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public sealed record CommandRejectedEventArgs(GCodeLineCommand Command, int ErrorCode);
```

- [ ] **Step 5: Create `ArctZ/Services/Device/IDeviceSession.cs`**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

public interface IDeviceSession
{
    ConnectionState ConnectionState { get; }

    DeviceStatus? DeviceStatus { get; }

    event Action? ConnectionStateChanged;

    event Action? DeviceStatusChanged;

    event Action<CommandRejectedEventArgs>? CommandRejected;

    event Action<int>? AlarmTriggered;

    Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default);

    Task DisconnectAsync();

    void BeginJog();

    void UpdateJog(JoystickState state);

    void EndJog();

    Task<CommandResult> SendGCodeAsync(string line, CancellationToken cancellationToken = default);

    Task<CommandResult> HomeAsync(CancellationToken cancellationToken = default);

    Task<CommandResult> ResetAlarmAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 6: Create `ArctZ/Services/Device/DeviceSession.cs`**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public sealed class DeviceSession : IDeviceSession
{
    private readonly IDeviceTransport _transport;
    private readonly ICommandQueue _commandQueue;
    private readonly IStatusParser _statusParser;
    private readonly IJogScheduler _jogScheduler;
    private readonly IStatusPoller _statusPoller;

    public DeviceSession(
        IDeviceTransport transport,
        ICommandQueue commandQueue,
        IStatusParser statusParser,
        IJogScheduler jogScheduler,
        IStatusPoller statusPoller)
    {
        _transport = transport;
        _commandQueue = commandQueue;
        _statusParser = statusParser;
        _jogScheduler = jogScheduler;
        _statusPoller = statusPoller;

        _commandQueue.CommandCompleted += OnCommandCompleted;
    }

    public ConnectionState ConnectionState { get; private set; } = ConnectionState.Disconnected;

    public DeviceStatus? DeviceStatus { get; private set; }

    public event Action? ConnectionStateChanged;

    public event Action? DeviceStatusChanged;

    public event Action<CommandRejectedEventArgs>? CommandRejected;

    public event Action<int>? AlarmTriggered;

    public async Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        SetConnectionState(ConnectionState.Connecting);

        _transport.LineReceived += OnLineReceived;

        await _transport.ConnectAsync(deviceId, cancellationToken).ConfigureAwait(false);

        SetConnectionState(ConnectionState.Connected);
        _statusPoller.Start();
    }

    public async Task DisconnectAsync()
    {
        _statusPoller.Stop();
        _jogScheduler.Stop();

        await _transport.DisconnectAsync().ConfigureAwait(false);
        _transport.LineReceived -= OnLineReceived;

        SetConnectionState(ConnectionState.Disconnected);
    }

    public void BeginJog() => _jogScheduler.Start();

    public void UpdateJog(JoystickState state) => _jogScheduler.UpdateState(state);

    public void EndJog() => _jogScheduler.Stop();

    public Task<CommandResult> SendGCodeAsync(string line, CancellationToken cancellationToken = default) =>
        _commandQueue.EnqueueAsync(new GCodeLineCommand(line), cancellationToken);

    public Task<CommandResult> HomeAsync(CancellationToken cancellationToken = default) =>
        _commandQueue.EnqueueAsync(new GCodeLineCommand("$H"), cancellationToken);

    public Task<CommandResult> ResetAlarmAsync(CancellationToken cancellationToken = default) =>
        _commandQueue.EnqueueAsync(new GCodeLineCommand("$X"), cancellationToken);

    private void SetConnectionState(ConnectionState state)
    {
        ConnectionState = state;
        ConnectionStateChanged?.Invoke();
    }

    private void OnCommandCompleted(GCodeLineCommand command, CommandResult result)
    {
        if (result is { Outcome: CommandOutcome.Rejected, ErrorCode: { } code })
        {
            CommandRejected?.Invoke(new CommandRejectedEventArgs(command, code));
        }
    }

    private void OnLineReceived(string rawLine)
    {
        switch (_statusParser.Parse(rawLine))
        {
            case OkLine:
                _commandQueue.HandleOk();
                break;
            case ErrorLine error:
                _commandQueue.HandleError(error.Code);
                break;
            case AlarmLine alarm:
                AlarmTriggered?.Invoke(alarm.Code);
                break;
            case StatusReportLine report:
                DeviceStatus = report.Status;
                DeviceStatusChanged?.Invoke();
                break;
            case UnrecognizedLine:
                break;
        }
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter DeviceSessionTests`
Expected: PASS (8 tests).

- [ ] **Step 8: Commit**

```bash
git add ArctZ/Services/Device/ConnectionState.cs ArctZ/Services/Device/CommandRejectedEventArgs.cs ArctZ/Services/Device/IDeviceSession.cs ArctZ/Services/Device/DeviceSession.cs ArctZ.Tests/Services/Device/DeviceSessionTests.cs
git commit -m "feat: add DeviceSession orchestrator"
```

---

## Task 13: `DeviceSession` reconnect with backoff

**Files:**
- Create: `ArctZ/Services/Device/IReconnectPolicy.cs`
- Create: `ArctZ/Services/Device/FixedDelayReconnectPolicy.cs`
- Modify: `ArctZ/Services/Device/DeviceSession.cs`
- Test: `ArctZ.Tests/Services/Device/DeviceSessionReconnectTests.cs`

**Interfaces:**
- Consumes: `DeviceSession`, `FakeDeviceTransport` (extended in Task 4 with `ConnectFailuresRemaining`/`SimulateDisconnect`).
- Produces: `IReconnectPolicy.WaitBeforeRetryAsync(int attemptNumber, CancellationToken) : Task`; `FixedDelayReconnectPolicy : IReconnectPolicy`; `DeviceSession` gains an `IReconnectPolicy` constructor parameter and reconnect-on-unexpected-disconnect behavior.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Tests.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class DeviceSessionReconnectTests
{
    private readonly FakeDeviceTransport _transport = new();
    private readonly DeviceSession _session;

    public DeviceSessionReconnectTests()
    {
        var serializer = new FluidNcCommandSerializer();
        var realtimeChannel = new RealtimeCommandChannel(_transport);
        var commandQueue = new CommandQueue(_transport, serializer);
        var jogScheduler = new JogScheduler(
            new JogCommandFactory(), serializer, _transport, realtimeChannel, new ManualPeriodicTimer(), TimeSpan.FromMilliseconds(100));
        var statusPoller = new StatusPoller(realtimeChannel, new ManualPeriodicTimer(), TimeSpan.FromMilliseconds(250));

        _session = new DeviceSession(
            _transport, commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller,
            new FixedDelayReconnectPolicy(TimeSpan.Zero));
    }

    [Fact]
    public async Task UnexpectedDisconnect_TransitionsToReconnectingThenBackToConnected()
    {
        await _session.ConnectAsync("COM5");
        var states = new List<ConnectionState>();
        _session.ConnectionStateChanged += () => states.Add(_session.ConnectionState);

        _transport.SimulateDisconnect();
        await WaitUntilAsync(() => _session.ConnectionState == ConnectionState.Connected);

        Assert.Contains(ConnectionState.Reconnecting, states);
        Assert.Equal(ConnectionState.Connected, _session.ConnectionState);
    }

    [Fact]
    public async Task UnexpectedDisconnect_RetriesUntilTransportAcceptsConnection()
    {
        await _session.ConnectAsync("COM5");
        _transport.ConnectFailuresRemaining = 2;

        _transport.SimulateDisconnect();
        await WaitUntilAsync(() => _session.ConnectionState == ConnectionState.Connected);

        Assert.Equal(0, _transport.ConnectFailuresRemaining);
    }

    [Fact]
    public async Task ManualDisconnectAsync_DoesNotTriggerReconnect()
    {
        await _session.ConnectAsync("COM5");

        await _session.DisconnectAsync();
        await Task.Delay(50);

        Assert.Equal(ConnectionState.Disconnected, _session.ConnectionState);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, cts.Token);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter DeviceSessionReconnectTests`
Expected: FAIL — `IReconnectPolicy`/constructor overload do not exist.

- [ ] **Step 3: Create `ArctZ/Services/Device/IReconnectPolicy.cs`**

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

public interface IReconnectPolicy
{
    Task WaitBeforeRetryAsync(int attemptNumber, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Create `ArctZ/Services/Device/FixedDelayReconnectPolicy.cs`**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

public sealed class FixedDelayReconnectPolicy : IReconnectPolicy
{
    private readonly TimeSpan _delay;

    public FixedDelayReconnectPolicy(TimeSpan delay)
    {
        _delay = delay;
    }

    public Task WaitBeforeRetryAsync(int attemptNumber, CancellationToken cancellationToken) =>
        Task.Delay(_delay, cancellationToken);
}
```

- [ ] **Step 5: Modify `ArctZ/Services/Device/DeviceSession.cs`**

Add a field, extend the constructor, track the last device id and a manual-disconnect flag, and subscribe to `Disconnected`:

```csharp
    private readonly IReconnectPolicy _reconnectPolicy;
    private string? _lastDeviceId;
    private bool _manualDisconnect;
    private CancellationTokenSource? _reconnectCts;

    public DeviceSession(
        IDeviceTransport transport,
        ICommandQueue commandQueue,
        IStatusParser statusParser,
        IJogScheduler jogScheduler,
        IStatusPoller statusPoller,
        IReconnectPolicy reconnectPolicy)
    {
        _transport = transport;
        _commandQueue = commandQueue;
        _statusParser = statusParser;
        _jogScheduler = jogScheduler;
        _statusPoller = statusPoller;
        _reconnectPolicy = reconnectPolicy;

        _commandQueue.CommandCompleted += OnCommandCompleted;
        _transport.Disconnected += OnTransportDisconnected;
    }
```

Update `ConnectAsync` to remember the device id and cancel any in-flight reconnect loop, and update `DisconnectAsync` to mark the disconnect as manual:

```csharp
    public async Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        _lastDeviceId = deviceId;
        _reconnectCts?.Cancel();
        SetConnectionState(ConnectionState.Connecting);

        _transport.LineReceived += OnLineReceived;

        await _transport.ConnectAsync(deviceId, cancellationToken).ConfigureAwait(false);

        SetConnectionState(ConnectionState.Connected);
        _statusPoller.Start();
    }

    public async Task DisconnectAsync()
    {
        _manualDisconnect = true;
        _reconnectCts?.Cancel();

        _statusPoller.Stop();
        _jogScheduler.Stop();

        await _transport.DisconnectAsync().ConfigureAwait(false);
        _transport.LineReceived -= OnLineReceived;

        SetConnectionState(ConnectionState.Disconnected);
    }
```

Add the reconnect loop:

```csharp
    private async void OnTransportDisconnected()
    {
        if (_manualDisconnect)
        {
            _manualDisconnect = false;
            return;
        }

        _statusPoller.Stop();
        _jogScheduler.Stop();
        SetConnectionState(ConnectionState.Reconnecting);

        _reconnectCts = new CancellationTokenSource();
        var token = _reconnectCts.Token;
        var attempt = 0;

        while (!token.IsCancellationRequested)
        {
            attempt++;
            try
            {
                await _reconnectPolicy.WaitBeforeRetryAsync(attempt, token).ConfigureAwait(false);
                await _transport.ConnectAsync(_lastDeviceId!, token).ConfigureAwait(false);

                SetConnectionState(ConnectionState.Connected);
                _statusPoller.Start();
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // keep retrying until cancelled by a manual ConnectAsync/DisconnectAsync
            }
        }
    }
```

- [ ] **Step 6: Fix Task 12's `DeviceSessionTests` constructor call**

`DeviceSessionTests`'s constructor now needs a sixth argument. Add `new FixedDelayReconnectPolicy(TimeSpan.Zero)` after `statusPoller` in `ArctZ.Tests/Services/Device/DeviceSessionTests.cs`'s `_session = new DeviceSession(...)` call.

- [ ] **Step 7: Run all `DeviceSession*` tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~DeviceSession"`
Expected: PASS (8 + 3 tests).

- [ ] **Step 8: Commit**

```bash
git add ArctZ/Services/Device/IReconnectPolicy.cs ArctZ/Services/Device/FixedDelayReconnectPolicy.cs ArctZ/Services/Device/DeviceSession.cs ArctZ.Tests/Services/Device/DeviceSessionTests.cs ArctZ.Tests/Services/Device/DeviceSessionReconnectTests.cs
git commit -m "feat: add reconnect-with-backoff to DeviceSession"
```

---

## Task 14: `ServiceCollectionExtensions.AddArctZCore`

**Files:**
- Create: `ArctZ/Services/Device/ServiceCollectionExtensions.cs`
- Test: `ArctZ.Tests/Services/Device/ServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Consumes: every `Services/Device` type from Tasks 2–13; `FakeDeviceTransport` (Task 4).
- Produces: `AddArctZCore(this IServiceCollection) : IServiceCollection`, registering everything platform-agnostic. `IDeviceTransport` is intentionally NOT registered here — the platform heads (Tasks 19–21) supply it.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.Extensions.DependencyInjection;
using ArctZ.Services.Device;
using ArctZ.Tests.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddArctZCore_WithTransportRegistered_ResolvesDeviceSession()
    {
        var services = new ServiceCollection();
        services.AddArctZCore();
        services.AddSingleton<IDeviceTransport>(new FakeDeviceTransport());

        var provider = services.BuildServiceProvider();

        var session = provider.GetRequiredService<IDeviceSession>();

        Assert.NotNull(session);
        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter ServiceCollectionExtensionsTests`
Expected: FAIL — `AddArctZCore` does not exist. Also add `<PackageReference Include="Microsoft.Extensions.DependencyInjection" />` to `ArctZ.Tests/ArctZ.Tests.csproj`'s `ItemGroup` before running, since the test references it directly.

- [ ] **Step 3: Add the DI package reference to `ArctZ/ArctZ.csproj` and `ArctZ.Tests/ArctZ.Tests.csproj`**

In both `.csproj` files' existing `PackageReference` `ItemGroup`, add:

```xml
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
```

- [ ] **Step 4: Create `ArctZ/Services/Device/ServiceCollectionExtensions.cs`**

```csharp
using System;
using Microsoft.Extensions.DependencyInjection;

namespace ArctZ.Services.Device;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddArctZCore(this IServiceCollection services)
    {
        services.AddSingleton<ICommandSerializer, FluidNcCommandSerializer>();
        services.AddSingleton<IStatusParser, FluidNcStatusParser>();
        services.AddSingleton<IJogCommandFactory>(_ => new JogCommandFactory());
        services.AddSingleton<IReconnectPolicy>(_ => new FixedDelayReconnectPolicy(TimeSpan.FromSeconds(2)));

        services.AddSingleton<IRealtimeCommandChannel>(sp =>
            new RealtimeCommandChannel(sp.GetRequiredService<IDeviceTransport>()));

        services.AddSingleton<ICommandQueue>(sp =>
            new CommandQueue(sp.GetRequiredService<IDeviceTransport>(), sp.GetRequiredService<ICommandSerializer>()));

        services.AddSingleton<IJogScheduler>(sp => new JogScheduler(
            sp.GetRequiredService<IJogCommandFactory>(),
            sp.GetRequiredService<ICommandSerializer>(),
            sp.GetRequiredService<IDeviceTransport>(),
            sp.GetRequiredService<IRealtimeCommandChannel>(),
            new SystemPeriodicTimer(),
            TimeSpan.FromMilliseconds(100)));

        services.AddSingleton<IStatusPoller>(sp => new StatusPoller(
            sp.GetRequiredService<IRealtimeCommandChannel>(),
            new SystemPeriodicTimer(),
            TimeSpan.FromMilliseconds(250)));

        services.AddSingleton<IDeviceSession>(sp => new DeviceSession(
            sp.GetRequiredService<IDeviceTransport>(),
            sp.GetRequiredService<ICommandQueue>(),
            sp.GetRequiredService<IStatusParser>(),
            sp.GetRequiredService<IJogScheduler>(),
            sp.GetRequiredService<IStatusPoller>(),
            sp.GetRequiredService<IReconnectPolicy>()));

        return services;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter ServiceCollectionExtensionsTests`
Expected: PASS.

- [ ] **Step 6: Run the full test suite to confirm nothing regressed**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS, all tests from Tasks 3–14 green.

- [ ] **Step 7: Commit**

```bash
git add ArctZ/ArctZ.csproj ArctZ.Tests/ArctZ.Tests.csproj ArctZ/Services/Device/ServiceCollectionExtensions.cs ArctZ.Tests/Services/Device/ServiceCollectionExtensionsTests.cs
git commit -m "feat: add AddArctZCore DI registration"
```

---

## Task 15: `ConnectionViewModel`

**Files:**
- Create: `ArctZ/Services/Device/DesignTimeDeviceSession.cs`
- Create: `ArctZ/ViewModels/ConnectionViewModel.cs`
- Test: `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs`

**Interfaces:**
- Consumes: `IDeviceSession` (Task 12/13), `ViewModelBase` (existing), `FakeDeviceTransport` and friends for building a real `DeviceSession` in tests.
- Produces: `DesignTimeDeviceSession : IDeviceSession` (no-op, for XAML previewers only); `ConnectionViewModel : ViewModelBase` with `[ObservableProperty]` `ConnectionState`, `StatusText`, `LastErrorMessage`, a parameterless design-time constructor, and `[RelayCommand]` `ConnectAsync(string deviceId)`, `DisconnectAsync()`, `HomeAsync()`, `ResetAlarmAsync()` — used by `MainViewModel` (Task 16, which also needs `DesignTimeDeviceSession` for its own design-time constructor) and `ConnectionView` (Task 17).

Because `IDeviceSession` events can fire from a background thread (the transport's read loop), property updates are marshalled through an injectable `Action<Action>` dispatcher — defaults to `Dispatcher.UIThread.Post` in the public constructor, and is a synchronous pass-through in the `internal` test constructor exposed via `InternalsVisibleTo` (Task 1).

- [ ] **Step 1: Write the failing tests**

```csharp
using ArctZ.Services.Device;
using ArctZ.Tests.Services.Device;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class ConnectionViewModelTests
{
    private static (ConnectionViewModel ViewModel, FakeDeviceTransport Transport) CreateViewModel()
    {
        var transport = new FakeDeviceTransport();
        var serializer = new FluidNcCommandSerializer();
        var realtimeChannel = new RealtimeCommandChannel(transport);
        var commandQueue = new CommandQueue(transport, serializer);
        var jogScheduler = new JogScheduler(
            new JogCommandFactory(), serializer, transport, realtimeChannel, new ManualPeriodicTimer(), TimeSpan.FromMilliseconds(100));
        var statusPoller = new StatusPoller(realtimeChannel, new ManualPeriodicTimer(), TimeSpan.FromMilliseconds(250));
        var session = new DeviceSession(
            transport, commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller,
            new FixedDelayReconnectPolicy(TimeSpan.Zero));

        var viewModel = new ConnectionViewModel(session, dispatch: action => action());
        return (viewModel, transport);
    }

    [Fact]
    public async Task ConnectCommand_ConnectsAndUpdatesConnectionState()
    {
        var (viewModel, _) = CreateViewModel();

        await viewModel.ConnectCommand.ExecuteAsync("COM5");

        Assert.Equal(ConnectionState.Connected, viewModel.ConnectionState);
    }

    [Fact]
    public async Task DisconnectCommand_DisconnectsAndUpdatesConnectionState()
    {
        var (viewModel, _) = CreateViewModel();
        await viewModel.ConnectCommand.ExecuteAsync("COM5");

        await viewModel.DisconnectCommand.ExecuteAsync(null);

        Assert.Equal(ConnectionState.Disconnected, viewModel.ConnectionState);
    }

    [Fact]
    public async Task HomeCommand_SendsHomingLine()
    {
        var (viewModel, transport) = CreateViewModel();
        await viewModel.ConnectCommand.ExecuteAsync("COM5");

        _ = viewModel.HomeCommand.ExecuteAsync(null);

        Assert.Contains("$H", transport.SentLines);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter ConnectionViewModelTests`
Expected: FAIL — `ConnectionViewModel` does not exist.

- [ ] **Step 3: Create `ArctZ/Services/Device/DesignTimeDeviceSession.cs`**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

/// <summary>No-op IDeviceSession used only for XAML designer Design.DataContext (ConnectionView, MainView).</summary>
public sealed class DesignTimeDeviceSession : IDeviceSession
{
    public ConnectionState ConnectionState => ConnectionState.Disconnected;
    public DeviceStatus? DeviceStatus => null;

    public event Action? ConnectionStateChanged { add { } remove { } }
    public event Action? DeviceStatusChanged { add { } remove { } }
    public event Action<CommandRejectedEventArgs>? CommandRejected { add { } remove { } }
    public event Action<int>? AlarmTriggered { add { } remove { } }

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DisconnectAsync() => Task.CompletedTask;
    public void BeginJog() { }
    public void UpdateJog(JoystickState state) { }
    public void EndJog() { }
    public Task<CommandResult> SendGCodeAsync(string line, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CommandResult(CommandOutcome.Acknowledged, null));
    public Task<CommandResult> HomeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new CommandResult(CommandOutcome.Acknowledged, null));
    public Task<CommandResult> ResetAlarmAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new CommandResult(CommandOutcome.Acknowledged, null));
}
```

- [ ] **Step 4: Create `ArctZ/ViewModels/ConnectionViewModel.cs`**

```csharp
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ArctZ.Services.Device;
using Avalonia.Threading;

namespace ArctZ.ViewModels
{
    public partial class ConnectionViewModel : ViewModelBase
    {
        private readonly IDeviceSession _deviceSession;
        private readonly Action<Action> _dispatch;

        [ObservableProperty]
        private ConnectionState _connectionState = ConnectionState.Disconnected;

        [ObservableProperty]
        private string _statusText = "Не подключено";

        [ObservableProperty]
        private string? _lastErrorMessage;

        /// <summary>Design-time only constructor for &lt;Design.DataContext&gt;.</summary>
        public ConnectionViewModel()
            : this(new DesignTimeDeviceSession(), action => action())
        {
        }

        public ConnectionViewModel(IDeviceSession deviceSession)
            : this(deviceSession, action => Dispatcher.UIThread.Post(action))
        {
        }

        internal ConnectionViewModel(IDeviceSession deviceSession, Action<Action> dispatch)
        {
            _deviceSession = deviceSession;
            _dispatch = dispatch;

            _deviceSession.ConnectionStateChanged += OnConnectionStateChanged;
            _deviceSession.CommandRejected += OnCommandRejected;
            _deviceSession.AlarmTriggered += OnAlarmTriggered;
        }

        [RelayCommand]
        private async Task ConnectAsync(string deviceId) => await _deviceSession.ConnectAsync(deviceId);

        [RelayCommand]
        private async Task DisconnectAsync() => await _deviceSession.DisconnectAsync();

        [RelayCommand]
        private async Task HomeAsync() => await _deviceSession.HomeAsync();

        [RelayCommand]
        private async Task ResetAlarmAsync() => await _deviceSession.ResetAlarmAsync();

        private void OnConnectionStateChanged() => _dispatch(() =>
        {
            ConnectionState = _deviceSession.ConnectionState;
            StatusText = ConnectionState switch
            {
                ConnectionState.Disconnected => "Не подключено",
                ConnectionState.Connecting => "Подключение...",
                ConnectionState.Connected => "Подключено",
                ConnectionState.Reconnecting => "Переподключение...",
                _ => ConnectionState.ToString()
            };
        });

        private void OnCommandRejected(CommandRejectedEventArgs args) => _dispatch(() =>
            LastErrorMessage = $"Отклонено: {args.Command.Line} (error:{args.ErrorCode})");

        private void OnAlarmTriggered(int code) => _dispatch(() =>
            LastErrorMessage = $"ALARM:{code}");
    }
}
```

Add `using System.Threading.Tasks;` alongside the other usings at the top of the file.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter ConnectionViewModelTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Services/Device/DesignTimeDeviceSession.cs ArctZ/ViewModels/ConnectionViewModel.cs ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs
git commit -m "feat: add ConnectionViewModel"
```

---

## Task 16: `MainViewModel` — composition + joystick-to-session wiring

**Files:**
- Modify: `ArctZ/ViewModels/MainViewModel.cs`
- Test: `ArctZ.Tests/ViewModels/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `ConnectionViewModel` and its parameterless design-time constructor, `DesignTimeDeviceSession` (Task 15), `IDeviceSession` (Task 12/13), `JoystickState` (Task 2).
- Produces: `MainViewModel.Connection : ConnectionViewModel` (no setter); a parameterless design-time constructor; `OnJoystickDown()`, `OnJoystickMove(double x, double y, double force)`, `OnJoystickUp()` methods calling `IDeviceSession.BeginJog/UpdateJog/EndJog` — consumed by `MainView` code-behind (Task 17).

`OnJoystickMove` takes primitive `double`s rather than `Components.VirtualJoystick.JoystickEventArgs` directly, keeping `ViewModels` decoupled from `Components` — the code-behind (Task 17) is what reads `JoystickEventArgs` and calls this with its fields.

- [ ] **Step 1: Write the failing tests**

```csharp
using ArctZ.Services.Device;
using ArctZ.Tests.Services.Device;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class MainViewModelTests
{
    private static (MainViewModel ViewModel, FakeDeviceTransport Transport, ManualPeriodicTimer JogTimer) CreateViewModel()
    {
        var transport = new FakeDeviceTransport();
        var serializer = new FluidNcCommandSerializer();
        var realtimeChannel = new RealtimeCommandChannel(transport);
        var commandQueue = new CommandQueue(transport, serializer);
        var jogTimer = new ManualPeriodicTimer();
        var jogScheduler = new JogScheduler(
            new JogCommandFactory(), serializer, transport, realtimeChannel, jogTimer, TimeSpan.FromMilliseconds(100));
        var statusPoller = new StatusPoller(realtimeChannel, new ManualPeriodicTimer(), TimeSpan.FromMilliseconds(250));
        var session = new DeviceSession(
            transport, commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller,
            new FixedDelayReconnectPolicy(TimeSpan.Zero));

        var connectionViewModel = new ConnectionViewModel(session, dispatch: action => action());
        var viewModel = new MainViewModel(session, connectionViewModel);
        return (viewModel, transport, jogTimer);
    }

    [Fact]
    public void Connection_ExposesInjectedConnectionViewModel()
    {
        var (viewModel, _, _) = CreateViewModel();

        Assert.NotNull(viewModel.Connection);
    }

    [Fact]
    public async Task OnJoystickDownMoveUp_DrivesDeviceSessionJog()
    {
        var (viewModel, transport, jogTimer) = CreateViewModel();
        await viewModel.Connection.ConnectCommand.ExecuteAsync("COM5");

        viewModel.OnJoystickDown();
        viewModel.OnJoystickMove(1, 0, 1);
        jogTimer.RaiseElapsed();

        Assert.Contains(transport.SentLines, line => line.StartsWith("$J=", StringComparison.Ordinal));

        viewModel.OnJoystickUp();

        Assert.Contains((byte)0x85, transport.SentRawBytes);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter MainViewModelTests`
Expected: FAIL — `MainViewModel` has no such constructor/methods yet.

- [ ] **Step 3: Rewrite `ArctZ/ViewModels/MainViewModel.cs`**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using ArctZ.Services.Device;

namespace ArctZ.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        private readonly IDeviceSession _deviceSession;

        [ObservableProperty]
        private string _greeting = "Welcome to Avalonia!";

        [ObservableProperty]
        private double _joystickX;

        [ObservableProperty]
        private double _joystickY;

        [ObservableProperty]
        private double _joystickForce;

        [ObservableProperty]
        private double _joystickAngle;

        [ObservableProperty]
        private string _joystickDirection = "None";

        /// <summary>Design-time only constructor for &lt;Design.DataContext&gt; in MainView.axaml.</summary>
        public MainViewModel()
            : this(new DesignTimeDeviceSession(), new ConnectionViewModel())
        {
        }

        public MainViewModel(IDeviceSession deviceSession, ConnectionViewModel connection)
        {
            _deviceSession = deviceSession;
            Connection = connection;
        }

        public ConnectionViewModel Connection { get; }

        public void OnJoystickDown() => _deviceSession.BeginJog();

        public void OnJoystickMove(double x, double y, double force)
        {
            JoystickX = x;
            JoystickY = y;
            JoystickForce = force;
            _deviceSession.UpdateJog(new JoystickState(x, y, force));
        }

        public void OnJoystickUp() => _deviceSession.EndJog();
    }
}
```

Note: this drops the previous `JoystickAngle`/`JoystickDirection` assignment from `OnJoystickMove` because the method signature no longer receives `JoystickEventArgs` — Task 17's code-behind sets those two properties directly from the event args before calling `OnJoystickMove`, since they are UI-telemetry-only and don't feed `JoystickState`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter MainViewModelTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/ViewModels/MainViewModel.cs ArctZ.Tests/ViewModels/MainViewModelTests.cs
git commit -m "feat: wire MainViewModel to DeviceSession and ConnectionViewModel"
```

---

## Task 17: Views — `ConnectionView` + `MainView` updates

**Files:**
- Create: `ArctZ/Views/ConnectionView.axaml`
- Create: `ArctZ/Views/ConnectionView.axaml.cs`
- Modify: `ArctZ/Views/MainView.axaml`
- Modify: `ArctZ/Views/MainView.axaml.cs`

**Interfaces:**
- Consumes: `ConnectionViewModel` (Task 15), `MainViewModel.OnJoystickDown/Move/Up` (Task 16).
- Produces: `ConnectionView` (auto-resolved by the existing `ViewLocator` from `ConnectionViewModel`); updated `MainView` composing it in.

Views aren't covered by `ArctZ.Tests` (no Avalonia headless test host in this plan) — verification is build success plus a manual check via the `run` skill, consistent with `CLAUDE.md`'s guidance to verify UI changes by running the app.

- [ ] **Step 1: Create `ArctZ/Views/ConnectionView.axaml`**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:vm="using:ArctZ.ViewModels"
             mc:Ignorable="d" d:DesignWidth="800" d:DesignHeight="80"
             x:Class="ArctZ.Views.ConnectionView"
             x:DataType="vm:ConnectionViewModel">
    <Design.DataContext>
        <vm:ConnectionViewModel />
    </Design.DataContext>
    <StackPanel Orientation="Horizontal" Spacing="10" Margin="10">
        <TextBlock Text="{Binding StatusText}" VerticalAlignment="Center" />
        <Button Content="Подключить" Command="{Binding ConnectCommand}" CommandParameter="COM5" />
        <Button Content="Отключить" Command="{Binding DisconnectCommand}" />
        <Button Content="Home" Command="{Binding HomeCommand}" />
        <Button Content="Сброс аварии" Command="{Binding ResetAlarmCommand}" />
        <TextBlock Text="{Binding LastErrorMessage}" Foreground="Red" VerticalAlignment="Center" />
    </StackPanel>
</UserControl>
```

- [ ] **Step 2: Create `ArctZ/Views/ConnectionView.axaml.cs`**

```csharp
using Avalonia.Controls;

namespace ArctZ.Views
{
    public partial class ConnectionView : UserControl
    {
        public ConnectionView()
        {
            InitializeComponent();
        }
    }
}
```

`<Design.DataContext><vm:ConnectionViewModel /></Design.DataContext>` relies on the parameterless `ConnectionViewModel()` constructor and `DesignTimeDeviceSession` already added in Task 15 — nothing new to write here. Likewise, `MainView.axaml`'s existing `<Design.DataContext><vm:MainViewModel /></Design.DataContext>` (Step 3 below) keeps working because of the parameterless `MainViewModel()` constructor added in Task 16.

- [ ] **Step 3: Modify `ArctZ/Views/MainView.axaml`**

Replace the `Grid.RowDefinitions` block and the top `StackPanel` to add a third row hosting `ConnectionView` above the telemetry text, and add the `ContentControl`:

```xml
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <ContentControl Grid.Row="0" Content="{Binding Connection}" />

        <StackPanel Grid.Row="1" Margin="10">
            <TextBlock Text="{Binding Greeting}" HorizontalAlignment="Center" VerticalAlignment="Center" Margin="0,0,0,10"/>
            <TextBlock HorizontalAlignment="Center">
                <Run Text="X: " />
                <Run Text="{Binding JoystickX, StringFormat={}{0:F1}}" />
                <Run Text=" Y: " />
                <Run Text="{Binding JoystickY, StringFormat={}{0:F1}}" />
            </TextBlock>
            <TextBlock HorizontalAlignment="Center">
                <Run Text="Force: " />
                <Run Text="{Binding JoystickForce, StringFormat={}{0:F2}}" />
                <Run Text=" Angle: " />
                <Run Text="{Binding JoystickAngle, StringFormat={}{0:F1}}" />
                <Run Text=" Dir: " />
                <Run Text="{Binding JoystickDirection}" />
            </TextBlock>
        </StackPanel>

        <Grid Grid.Row="2">
            <js:VirtualJoystick Radius="80"
                                Mode="Fixed"
                                Shape="Circle"
                                Lock="None"
                                Threshold="0.1"
                                HorizontalAlignment="Center"
                                VerticalAlignment="Center"
                                JoystickDown="OnJoystickDown"
                                JoystickMove="OnJoystickMove"
                                JoystickUp="OnJoystickUp" />
        </Grid>
    </Grid>
```

- [ ] **Step 4: Modify `ArctZ/Views/MainView.axaml.cs`**

```csharp
using ArctZ.Components.VirtualJoystick;
using ArctZ.ViewModels;
using Avalonia.Controls;

namespace ArctZ.Views
{
    public partial class MainView : UserControl
    {
        public MainView()
        {
            InitializeComponent();
        }

        private void OnJoystickDown(object? sender, JoystickEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.OnJoystickDown();
            }
        }

        private void OnJoystickMove(object? sender, JoystickEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.JoystickAngle = e.AngleDeg;
                vm.JoystickDirection = e.Direction.ToString();
                vm.OnJoystickMove(e.Position.X, e.Position.Y, e.Force);
            }
        }

        private void OnJoystickUp(object? sender, JoystickEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.OnJoystickUp();
            }
        }
    }
}
```

- [ ] **Step 5: Verify the solution builds**

Run: `dotnet build ArctZ/ArctZ.csproj`
Expected: build succeeds (compiled-binding/`x:DataType` errors would show here if the XAML is wrong).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Views/ConnectionView.axaml ArctZ/Views/ConnectionView.axaml.cs ArctZ/Views/MainView.axaml ArctZ/Views/MainView.axaml.cs
git commit -m "feat: compose ConnectionView into MainView, wire joystick events to DeviceSession"
```

---

## Task 18: `App.axaml.cs` — resolve ViewModels via DI

**Files:**
- Modify: `ArctZ/App.axaml.cs`

**Interfaces:**
- Consumes: `MainViewModel` (Task 16), `ServiceCollectionExtensions.AddArctZCore` (Task 14).
- Produces: `App.Services` static property, populated by each platform head (Tasks 19–21) before Avalonia starts.

Not independently unit-testable (it's the composition root) — verified by the platform-head tasks that follow, each of which must build and run.

- [ ] **Step 1: Modify `ArctZ/App.axaml.cs`**

```csharp
using ArctZ.ViewModels;
using ArctZ.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;

namespace ArctZ
{
    public partial class App : Application
    {
        /// <summary>Set by each platform head before BuildAvaloniaApp().Start... is called.</summary>
        public static IServiceProvider? Services { get; set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            var services = Services ?? throw new InvalidOperationException(
                "App.Services must be set by the platform head before Avalonia starts.");

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = services.GetRequiredService<MainViewModel>()
                };
            }
            else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
            {
                singleViewFactoryApplicationLifetime.MainViewFactory = () => new MainView
                {
                    DataContext = services.GetRequiredService<MainViewModel>()
                };
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                singleViewPlatform.MainView = new MainView
                {
                    DataContext = services.GetRequiredService<MainViewModel>()
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
```

- [ ] **Step 2: Verify the core project builds**

Run: `dotnet build ArctZ/ArctZ.csproj`
Expected: build succeeds. (The Desktop/Android/iOS/Browser heads will fail to build/run at this point until Tasks 19–21 set `App.Services` — that's expected and fixed by the next tasks.)

- [ ] **Step 3: Commit**

```bash
git add ArctZ/App.axaml.cs
git commit -m "feat: resolve MainViewModel via DI in App"
```

---

## Task 19: Desktop head — real transport + DI wiring

**Files:**
- Create: `ArctZ.Desktop/DesktopSerialBluetoothTransport.cs`
- Modify: `ArctZ.Desktop/Program.cs`
- Modify: `ArctZ.Desktop/ArctZ.Desktop.csproj`

**Interfaces:**
- Consumes: `IDeviceTransport` (Task 4), `ServiceCollectionExtensions.AddArctZCore` (Task 14), `App.Services` (Task 18).
- Produces: `DesktopSerialBluetoothTransport : IDeviceTransport`, registered as the Desktop head's `IDeviceTransport`.

Not unit-testable without a real paired COM port — verification is build success now; functional verification needs a paired FluidNC device and is out of this plan's automated-test scope (matches the spec, which never lists transport implementations under `ArctZ.Tests`).

- [ ] **Step 1: Add the DI package reference to `ArctZ.Desktop/ArctZ.Desktop.csproj`**

In the existing `PackageReference` `ItemGroup`, add:

```xml
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
```

- [ ] **Step 2: Create `ArctZ.Desktop/DesktopSerialBluetoothTransport.cs`**

```csharp
using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Desktop;

/// <summary>
/// A paired classic-Bluetooth-SPP FluidNC device shows up to Windows as a
/// normal COM port, so this wraps System.IO.Ports.SerialPort. deviceId is
/// the COM port name, e.g. "COM5".
/// </summary>
public sealed class DesktopSerialBluetoothTransport : IDeviceTransport, IDisposable
{
    private SerialPort? _port;

    public bool IsConnected => _port?.IsOpen ?? false;

    public event Action<string>? LineReceived;
    public event Action? Disconnected;

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var port = new SerialPort(deviceId, baudRate: 115200)
        {
            NewLine = "\n"
        };
        port.DataReceived += OnDataReceived;
        port.ErrorReceived += (_, _) => Disconnected?.Invoke();
        port.Open();
        _port = port;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        if (_port is not null)
        {
            _port.DataReceived -= OnDataReceived;
            _port.Close();
            _port.Dispose();
            _port = null;
        }

        return Task.CompletedTask;
    }

    public Task SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        _port?.WriteLine(line);
        return Task.CompletedTask;
    }

    public Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default)
    {
        _port?.Write(new[] { value }, 0, 1);
        return Task.CompletedTask;
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_port is not { IsOpen: true } port)
        {
            return;
        }

        try
        {
            var line = port.ReadLine();
            LineReceived?.Invoke(line);
        }
        catch (Exception) when (_port is not { IsOpen: true })
        {
            Disconnected?.Invoke();
        }
    }

    public void Dispose() => _port?.Dispose();
}
```

- [ ] **Step 3: Modify `ArctZ.Desktop/Program.cs`**

```csharp
using ArctZ.Services.Device;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace ArctZ.Desktop
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            var services = new ServiceCollection();
            services.AddArctZCore();
            services.AddSingleton<IDeviceTransport, DesktopSerialBluetoothTransport>();
            App.Services = services.BuildServiceProvider();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();
    }
}
```

- [ ] **Step 4: Verify the Desktop head builds and runs**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: build succeeds.

Run: `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj` (manual check, not part of automated tests)
Expected: window opens, shows the joystick and the new connection bar with "Не подключено" and Connect/Disconnect/Home/Сброс аварии buttons.

- [ ] **Step 5: Commit**

```bash
git add ArctZ.Desktop/DesktopSerialBluetoothTransport.cs ArctZ.Desktop/Program.cs ArctZ.Desktop/ArctZ.Desktop.csproj
git commit -m "feat: add Desktop serial Bluetooth transport and DI wiring"
```

---

## Task 20: Android head — real transport + DI wiring

**Files:**
- Create: `ArctZ.Android/AndroidBluetoothSocketTransport.cs`
- Modify: `ArctZ.Android/Application.cs`
- Modify: `ArctZ.Android/ArctZ.Android.csproj`
- Modify: `ArctZ.Android/Properties/AndroidManifest.xml`

**Interfaces:**
- Consumes: `IDeviceTransport` (Task 4), `ServiceCollectionExtensions.AddArctZCore` (Task 14), `App.Services` (Task 18).
- Produces: `AndroidBluetoothSocketTransport : IDeviceTransport`, registered as the Android head's `IDeviceTransport`.

Not unit-testable without a real paired device/emulated Bluetooth stack — same scope note as Task 19.

- [ ] **Step 1: Add the DI package reference to `ArctZ.Android/ArctZ.Android.csproj`**

In the existing `PackageReference` `ItemGroup`, add:

```xml
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
```

- [ ] **Step 2: Add Bluetooth permissions to `ArctZ.Android/Properties/AndroidManifest.xml`**

Add inside the `<manifest>` element, alongside any existing `<uses-permission>` entries (add the element if none exist yet):

```xml
    <uses-permission android:name="android.permission.BLUETOOTH_CONNECT" />
    <uses-permission android:name="android.permission.BLUETOOTH_SCAN" />
```

- [ ] **Step 3: Create `ArctZ.Android/AndroidBluetoothSocketTransport.cs`**

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.Bluetooth;
using Java.Util;
using ArctZ.Services.Device;

namespace ArctZ.Android;

/// <summary>
/// deviceId is the paired device's MAC address (e.g. "00:11:22:33:44:55").
/// Uses the standard Serial Port Profile UUID that FluidNC's Bluetooth
/// Classic stack exposes.
/// </summary>
public sealed class AndroidBluetoothSocketTransport : IDeviceTransport, IDisposable
{
    private static readonly UUID SppUuid = UUID.FromString("00001101-0000-1000-8000-00805F9B34FB")!;

    private BluetoothSocket? _socket;
    private StreamReader? _reader;
    private Stream? _outputStream;
    private CancellationTokenSource? _readLoopCts;

    public bool IsConnected => _socket?.IsConnected ?? false;

    public event Action<string>? LineReceived;
    public event Action? Disconnected;

    public async Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var adapter = BluetoothAdapter.DefaultAdapter
            ?? throw new InvalidOperationException("No Bluetooth adapter available on this device.");
        var device = adapter.GetRemoteDevice(deviceId);
        var socket = device.CreateRfcommSocketToServiceRecord(SppUuid)
            ?? throw new InvalidOperationException("Could not create an RFCOMM socket for the paired device.");

        await Task.Run(() => socket.Connect(), cancellationToken).ConfigureAwait(false);

        _socket = socket;
        _outputStream = socket.OutputStream;
        _reader = new StreamReader(socket.InputStream!);

        _readLoopCts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoopAsync(_readLoopCts.Token), CancellationToken.None);
    }

    public Task DisconnectAsync()
    {
        _readLoopCts?.Cancel();
        _socket?.Close();
        _socket?.Dispose();
        _socket = null;
        return Task.CompletedTask;
    }

    public async Task SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        if (_outputStream is null)
        {
            return;
        }

        var bytes = System.Text.Encoding.ASCII.GetBytes(line + "\n");
        await _outputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default)
    {
        if (_outputStream is null)
        {
            return;
        }

        await _outputStream.WriteAsync(new[] { value }.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _reader is not null)
            {
                var line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                LineReceived?.Invoke(line);
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // fall through to Disconnected below
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            Disconnected?.Invoke();
        }
    }

    public void Dispose() => _socket?.Dispose();
}
```

- [ ] **Step 4: Modify `ArctZ.Android/Application.cs`**

```csharp
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using ArctZ.Services.Device;
using Microsoft.Extensions.DependencyInjection;

namespace ArctZ.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            var services = new ServiceCollection();
            services.AddArctZCore();
            services.AddSingleton<IDeviceTransport, AndroidBluetoothSocketTransport>();
            App.Services = services.BuildServiceProvider();

            return base.CustomizeAppBuilder(builder)
                .WithInterFont();
        }
    }
}
```

- [ ] **Step 5: Verify the Android head builds**

Run: `dotnet build ArctZ.Android/ArctZ.Android.csproj`
Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add ArctZ.Android/AndroidBluetoothSocketTransport.cs ArctZ.Android/Application.cs ArctZ.Android/ArctZ.Android.csproj ArctZ.Android/Properties/AndroidManifest.xml
git commit -m "feat: add Android Bluetooth socket transport and DI wiring"
```

---

## Task 21: iOS + Browser heads — stub transport + DI wiring

**Files:**
- Create: `ArctZ/Services/Device/NotSupportedDeviceTransport.cs`
- Modify: `ArctZ.iOS/AppDelegate.cs`
- Modify: `ArctZ.iOS/ArctZ.iOS.csproj`
- Modify: `ArctZ.Browser/Program.cs`
- Modify: `ArctZ.Browser/ArctZ.Browser.csproj`

**Interfaces:**
- Consumes: `IDeviceTransport` (Task 4), `ServiceCollectionExtensions.AddArctZCore` (Task 14), `App.Services` (Task 18).
- Produces: `NotSupportedDeviceTransport : IDeviceTransport` (shared, lives in core since it's platform-agnostic by construction), registered by both the iOS and Browser heads. `ConnectAsync` throws `PlatformNotSupportedException`, which `ConnectionViewModel.ConnectAsync`'s `[RelayCommand]`-generated method surfaces the normal way `CommunityToolkit.Mvvm` surfaces exceptions from async relay commands — no special-casing needed in `ConnectionViewModel`.

- [ ] **Step 1: Create `ArctZ/Services/Device/NotSupportedDeviceTransport.cs`**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

/// <summary>
/// Placeholder for platforms with no public classic-Bluetooth-SPP API
/// (iOS CoreBluetooth and the Browser's Web Bluetooth API are both
/// BLE-only). Swap the DI registration for a real transport once a
/// BLE bridge or alternative firmware is decided — nothing above
/// IDeviceTransport needs to change.
/// </summary>
public sealed class NotSupportedDeviceTransport : IDeviceTransport
{
    public bool IsConnected => false;

    public event Action<string>? LineReceived { add { } remove { } }
    public event Action? Disconnected { add { } remove { } }

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default) =>
        throw new PlatformNotSupportedException(
            "Классический Bluetooth SPP недоступен на этой платформе (только BLE через публичный API).");

    public Task DisconnectAsync() => Task.CompletedTask;

    public Task SendLineAsync(string line, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
```

- [ ] **Step 2: Add the DI package reference to `ArctZ.iOS/ArctZ.iOS.csproj` and `ArctZ.Browser/ArctZ.Browser.csproj`**

In each existing `PackageReference` `ItemGroup` (create one in `ArctZ.Browser.csproj` if it has none), add:

```xml
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
```

- [ ] **Step 3: Modify `ArctZ.iOS/AppDelegate.cs`**

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.iOS;
using Avalonia.Media;
using Foundation;
using UIKit;
using ArctZ.Services.Device;
using Microsoft.Extensions.DependencyInjection;

namespace ArctZ.iOS
{
    [Register("AppDelegate")]
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
    public partial class AppDelegate : AvaloniaAppDelegate<App>
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
    {
        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            var services = new ServiceCollection();
            services.AddArctZCore();
            services.AddSingleton<IDeviceTransport, NotSupportedDeviceTransport>();
            App.Services = services.BuildServiceProvider();

            return base.CustomizeAppBuilder(builder)
                .WithInterFont();
        }
    }
}
```

- [ ] **Step 4: Modify `ArctZ.Browser/Program.cs`**

```csharp
using ArctZ;
using ArctZ.Services.Device;
using Avalonia;
using Avalonia.Browser;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.Versioning;
using System.Threading.Tasks;

internal sealed partial class Program
{
    private static Task Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddArctZCore();
        services.AddSingleton<IDeviceTransport, NotSupportedDeviceTransport>();
        App.Services = services.BuildServiceProvider();

        return BuildAvaloniaApp()
            .WithInterFont()
#if DEBUG
            .WithDeveloperTools()
#endif
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
```

- [ ] **Step 5: Verify both heads build**

Run: `dotnet build ArctZ.iOS/ArctZ.iOS.csproj -r iossimulator-x64`
Expected: build succeeds.

Run: `dotnet build ArctZ.Browser/ArctZ.Browser.csproj`
Expected: build succeeds.

- [ ] **Step 6: Run the full test suite one more time to confirm nothing anywhere regressed**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS, every test from Tasks 3–16 green.

- [ ] **Step 7: Commit**

```bash
git add ArctZ/Services/Device/NotSupportedDeviceTransport.cs ArctZ.iOS/AppDelegate.cs ArctZ.iOS/ArctZ.iOS.csproj ArctZ.Browser/Program.cs ArctZ.Browser/ArctZ.Browser.csproj
git commit -m "feat: add NotSupportedDeviceTransport stub for iOS/Browser heads"
```
