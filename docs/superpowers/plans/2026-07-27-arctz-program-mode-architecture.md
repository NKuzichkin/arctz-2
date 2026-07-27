# ArctZ Program-Mode Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the buffer-aware device-control layer, the simulated-controller demo transport, and the waypoint-program domain (authoring + playback with segment progress) for ArctZ, wired into a single dual-mode MVVM screen across all four platform heads.

**Architecture:** Three layers. (1) `Services/Device` — command model → serialization → three send-policy channels (realtime / character-counting buffered queue / throttled jog), orchestrated by `DeviceSession`, talking to a swappable `IDeviceTransport` (real serial or `MockDeviceTransport`, a full simulated FluidNC). (2) `Services/Program` — `JibProgram` (waypoints + per-segment transition settings) compiled by `ITrajectoryCompiler` into G-code lines fed through the same buffered queue, persisted via `IProgramStorage`. (3) `ViewModels`/`Views` — one `ProgramViewModel` with an Authoring/Playback mode switch, two `VirtualJoystick` instances for the 4-axis machine.

**Tech Stack:** .NET 10, Avalonia UI 12, `CommunityToolkit.Mvvm` 8.4, `Microsoft.Extensions.DependencyInjection`, `System.IO.Ports` (Desktop transport), xUnit (`ArctZ.Tests`, new project).

**Spec:** `docs/superpowers/specs/2026-07-27-arctz-program-mode-architecture-design.md` (supersedes parts of `docs/superpowers/specs/2026-07-23-arctz-app-architecture-design.md` — see that spec's "Отличия" section for the exact diff).

## Global Constraints

- Target framework `net10.0` everywhere (mobile heads use `net10.0-android`/iOS equivalents); `Nullable` enabled; `LangVersion` latest.
- Avalonia compiled bindings are on by default — every new `.axaml` view declares `x:DataType`.
- ViewModels use `CommunityToolkit.Mvvm` code-gen (`[ObservableProperty]`, `[RelayCommand]`), not hand-written properties/commands.
- Package versions are centrally managed in `Directory.Packages.props` — add new `PackageVersion` entries there, `PackageReference` (no version) in individual `.csproj` files.
- All 4 machine axes (X, Y, Z, A) are **angular** (degrees), not linear — `F`/feed values are "units per minute" in the axis's own calibration, never named with "Mm" in code.
- Jog commands bypass the buffered queue's ack-wait entirely — sent directly to `IDeviceTransport` via `JogScheduler`, because throttling already bounds their rate and waiting on `ok` would make live control jerky.
- Realtime single-byte commands (`?`, `!`, `~`, `0x85`, overrides) always go through `IRealtimeCommandChannel`, never through the buffered command queue, never counted against its character budget.
- Lines starting with `$` (settings, `$H`, `$X`, ...) are "exclusive" in the buffered queue: never pipelined with other commands, sent only once the queue is fully drained and acked (EEPROM writes can corrupt if raced — see spec).
- `MachineLimits` defaults: X ∈ [-15, 65] (no wrap), Y unbounded (no wrap), Z ∈ [0, 360] (wraps), A ∈ [0, 360] (wraps). Not user-editable in this plan — a code-level default, explicitly deferred per spec.
- Android real Bluetooth (`BluetoothSocket`) and iOS/Browser transports are **out of scope for this plan** — no physical hardware exists yet to validate against. All four heads get `NotSupportedDeviceTransport` plus the always-available `MockDeviceTransport`; Desktop additionally gets a real `System.IO.Ports.SerialPort`-based transport since it's directly testable without hardware (COM port can be exercised against the mock or a virtual null-modem later).

## File Structure

```
ArctZ/Services/Device/
  MachinePose.cs                  — Task 2
  AxisLimits.cs                   — Task 2
  MachineLimits.cs                — Task 2
  Commands/IDeviceCommand.cs      — Task 3 (JogCommand, GCodeLineCommand, RealtimeCommand)
  ICommandSerializer.cs           — Task 4
  FluidNcCommandSerializer.cs     — Task 4
  IDeviceTransport.cs             — Task 5
  IRealtimeCommandChannel.cs      — Task 6
  RealtimeCommandChannel.cs       — Task 6
  IBufferAwareCommandQueue.cs     — Task 7
  BufferAwareCommandQueue.cs      — Task 7
  DeviceStatus.cs                 — Task 8
  FluidNcLine.cs                  — Task 8
  IStatusParser.cs                — Task 8
  FluidNcStatusParser.cs          — Task 8
  DualJoystickState.cs            — Task 9
  IJogCommandFactory.cs           — Task 9
  JogCommandFactory.cs            — Task 9
  IPeriodicTimer.cs               — Task 10
  SystemPeriodicTimer.cs          — Task 10
  IJogScheduler.cs                — Task 11
  JogScheduler.cs                 — Task 11
  IStatusPoller.cs                — Task 12
  StatusPoller.cs                 — Task 12
  IReconnectPolicy.cs             — Task 13
  FixedDelayReconnectPolicy.cs    — Task 13
  ConnectionState.cs              — Task 14
  CommandRejectedEventArgs.cs     — Task 14
  IDeviceSession.cs               — Task 14
  DeviceSession.cs                — Task 14, extended Task 15
  Simulation/MockDeviceTransport.cs — Task 16
  ServiceCollectionExtensions.cs  — Task 17

ArctZ/Services/Program/
  Waypoint.cs                     — Task 18
  EaseMode.cs                     — Task 18
  TransitionSettings.cs           — Task 18
  ProgramSegment.cs               — Task 18
  JibProgram.cs                   — Task 18
  CompiledStep.cs                 — Task 19
  ITrajectoryCompiler.cs          — Task 19
  TrajectoryCompiler.cs           — Task 19
  ProgramSummary.cs               — Task 20
  IProgramStorage.cs              — Task 20
  JsonFileProgramStorage.cs       — Task 20

ArctZ/ViewModels/
  ConnectionViewModel.cs          — Task 17
  ProgramMode.cs                  — Task 21
  ProgramViewModel.cs             — Task 21, extended Task 22

ArctZ/Views/
  ConnectionView.axaml(.cs)       — Task 17
  MainView.axaml(.cs)             — modified, Task 23

ArctZ.Desktop/DesktopSerialTransport.cs — Task 24
ArctZ.Android/NotSupportedDeviceTransport.cs — Task 24 (shared source or per-head copy, see Task 24)
ArctZ.iOS/NotSupportedDeviceTransport.cs     — Task 24
ArctZ.Browser/NotSupportedDeviceTransport.cs — Task 24
ArctZ.Desktop/App.axaml.cs, ArctZ.Android/..., ArctZ.iOS/..., ArctZ.Browser/... — modified, Task 24 (DI bootstrap)

ArctZ.Tests/ — new project (Task 1), plus one test file per production file above that has behavior to verify.
```

---

## Task 1: Scaffold `ArctZ.Tests` project

**Files:**
- Create: `ArctZ.Tests/ArctZ.Tests.csproj`
- Create: `ArctZ.Tests/GlobalUsings.cs`
- Modify: `ArctZ.slnx`
- Modify: `Directory.Packages.props`
- Modify: `ArctZ/ArctZ.csproj`

**Interfaces:**
- Produces: an `ArctZ.Tests` project that compiles, references `ArctZ`, and can run via `dotnet test`.

- [ ] **Step 1: Add test package versions to `Directory.Packages.props`**

Add inside the existing `<ItemGroup>`:

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
Expected: build succeeds, 0 tests run, no errors.

- [ ] **Step 7: Commit**

```bash
git add ArctZ.slnx Directory.Packages.props ArctZ/ArctZ.csproj ArctZ.Tests/ArctZ.Tests.csproj ArctZ.Tests/GlobalUsings.cs
git commit -m "test: scaffold ArctZ.Tests project"
```

---

## Task 2: Machine geometry — `MachinePose`, `AxisLimits`, `MachineLimits`

**Files:**
- Create: `ArctZ/Services/Device/MachinePose.cs`
- Create: `ArctZ/Services/Device/AxisLimits.cs`
- Create: `ArctZ/Services/Device/MachineLimits.cs`
- Test: `ArctZ.Tests/Services/Device/AxisLimitsTests.cs`
- Test: `ArctZ.Tests/Services/Device/MachineLimitsTests.cs`

**Interfaces:**
- Produces: `MachinePose(double X, double Y, double Z, double A)` with `MachinePose.Zero`; `AxisLimits(double? Min, double? Max, bool WrapsAt360)` with `Clamp(double) : double` and `ClampDelta(double currentValue, double delta) : double`; `MachineLimits` (`X`/`Y`/`Z`/`A` properties of type `AxisLimits`, `MachineLimits.Default`) with `Clamp(MachinePose) : MachinePose` and `ClampDelta(MachinePose current, MachinePose delta) : MachinePose` — used by `JogCommandFactory` (Task 9) and the Authoring-mode waypoint editor (Task 21).

All 4 axes are angular. `Clamp` operates on **absolute** positions (used for manual numeric entry of a waypoint). `ClampDelta` operates on a **relative jog increment** — it is a distinct operation because clamping a small delta against absolute bounds is a different computation: for a wrapping axis a delta always passes through unchanged (continuous rotation has no bound to hit); for a bounded axis (only `X` today) the prospective absolute target (`current + delta`) is clamped and the delta is recomputed from the clamped target, so jogging into a limit slows to a stop instead of jumping.

- [ ] **Step 1: Write the failing tests**

```csharp
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class AxisLimitsTests
{
    [Fact]
    public void Clamp_ValueWithinBounds_ReturnsUnchanged()
    {
        var limits = new AxisLimits(-15, 65, WrapsAt360: false);

        Assert.Equal(30, limits.Clamp(30));
    }

    [Fact]
    public void Clamp_ValueAboveMax_ReturnsMax()
    {
        var limits = new AxisLimits(-15, 65, WrapsAt360: false);

        Assert.Equal(65, limits.Clamp(90));
    }

    [Fact]
    public void Clamp_ValueBelowMin_ReturnsMin()
    {
        var limits = new AxisLimits(-15, 65, WrapsAt360: false);

        Assert.Equal(-15, limits.Clamp(-40));
    }

    [Fact]
    public void Clamp_NoBounds_ReturnsUnchanged()
    {
        var limits = new AxisLimits(null, null, WrapsAt360: false);

        Assert.Equal(12345, limits.Clamp(12345));
    }

    [Fact]
    public void Clamp_WrappingAxis_NormalizesIntoZeroTo360()
    {
        var limits = new AxisLimits(0, 360, WrapsAt360: true);

        Assert.Equal(10, limits.Clamp(370));
        Assert.Equal(350, limits.Clamp(-10));
    }

    [Fact]
    public void ClampDelta_WrappingAxis_PassesThroughUnchanged()
    {
        var limits = new AxisLimits(0, 360, WrapsAt360: true);

        Assert.Equal(5, limits.ClampDelta(currentValue: 359, delta: 5));
    }

    [Fact]
    public void ClampDelta_UnboundedAxis_PassesThroughUnchanged()
    {
        var limits = new AxisLimits(null, null, WrapsAt360: false);

        Assert.Equal(500, limits.ClampDelta(currentValue: 1_000_000, delta: 500));
    }

    [Fact]
    public void ClampDelta_BoundedAxis_WithinRange_PassesThroughUnchanged()
    {
        var limits = new AxisLimits(-15, 65, WrapsAt360: false);

        Assert.Equal(5, limits.ClampDelta(currentValue: 30, delta: 5));
    }

    [Fact]
    public void ClampDelta_BoundedAxis_WouldExceedMax_TruncatesToRemainingRoom()
    {
        var limits = new AxisLimits(-15, 65, WrapsAt360: false);

        Assert.Equal(2, limits.ClampDelta(currentValue: 63, delta: 5));
    }

    [Fact]
    public void ClampDelta_BoundedAxis_AlreadyAtMax_ReturnsZero()
    {
        var limits = new AxisLimits(-15, 65, WrapsAt360: false);

        Assert.Equal(0, limits.ClampDelta(currentValue: 65, delta: 5));
    }
}
```

```csharp
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class MachineLimitsTests
{
    [Fact]
    public void Default_MatchesDocumentedAxisRanges()
    {
        var limits = MachineLimits.Default;

        Assert.Equal(65, limits.X.Clamp(90));
        Assert.Equal(-15, limits.X.Clamp(-90));
        Assert.Equal(999, limits.Y.Clamp(999));
        Assert.Equal(10, limits.Z.Clamp(370));
        Assert.Equal(10, limits.A.Clamp(370));
    }

    [Fact]
    public void Clamp_AppliesPerAxis()
    {
        var limits = MachineLimits.Default;
        var pose = new MachinePose(X: 90, Y: 999, Z: 370, A: -10);

        var clamped = limits.Clamp(pose);

        Assert.Equal(new MachinePose(65, 999, 10, 350), clamped);
    }

    [Fact]
    public void ClampDelta_AppliesPerAxis()
    {
        var limits = MachineLimits.Default;
        var current = new MachinePose(X: 63, Y: 0, Z: 359, A: 0);
        var delta = new MachinePose(X: 5, Y: 5, Z: 5, A: 5);

        var clampedDelta = limits.ClampDelta(current, delta);

        Assert.Equal(new MachinePose(2, 5, 5, 5), clampedDelta);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "AxisLimitsTests|MachineLimitsTests"`
Expected: FAIL — `AxisLimits`/`MachineLimits`/`MachinePose` do not exist.

- [ ] **Step 3: Create `ArctZ/Services/Device/MachinePose.cs`**

```csharp
namespace ArctZ.Services.Device;

/// <summary>
/// A pose of all 4 machine axes. All 4 are angular (degrees) — X boom
/// lift, Y boom rotation, Z camera pan, A camera tilt — not linear.
/// </summary>
public readonly record struct MachinePose(double X, double Y, double Z, double A)
{
    public static readonly MachinePose Zero = new(0, 0, 0, 0);
}
```

- [ ] **Step 4: Create `ArctZ/Services/Device/AxisLimits.cs`**

```csharp
namespace ArctZ.Services.Device;

public readonly record struct AxisLimits(double? Min, double? Max, bool WrapsAt360)
{
    /// <summary>Clamps an absolute axis position.</summary>
    public double Clamp(double value)
    {
        if (WrapsAt360)
        {
            var wrapped = value % 360.0;
            return wrapped < 0 ? wrapped + 360.0 : wrapped;
        }

        if (Min is { } min && value < min)
        {
            return min;
        }

        if (Max is { } max && value > max)
        {
            return max;
        }

        return value;
    }

    /// <summary>
    /// Clamps a relative jog increment so that current+delta never exceeds
    /// bounds. Wrapping axes have no bound to hit, so the delta always
    /// passes through unchanged.
    /// </summary>
    public double ClampDelta(double currentValue, double delta)
    {
        if (WrapsAt360)
        {
            return delta;
        }

        var clampedTarget = Clamp(currentValue + delta);
        return clampedTarget - currentValue;
    }
}
```

- [ ] **Step 5: Create `ArctZ/Services/Device/MachineLimits.cs`**

```csharp
namespace ArctZ.Services.Device;

/// <summary>
/// Default axis ranges for the jib. Not user-editable in this version —
/// X in particular is expected to change as the boom mechanics are
/// finalized (see docs/hardware/mechanics.md).
/// </summary>
public sealed class MachineLimits
{
    public AxisLimits X { get; init; } = new(-15, 65, WrapsAt360: false);
    public AxisLimits Y { get; init; } = new(null, null, WrapsAt360: false);
    public AxisLimits Z { get; init; } = new(0, 360, WrapsAt360: true);
    public AxisLimits A { get; init; } = new(0, 360, WrapsAt360: true);

    public static readonly MachineLimits Default = new();

    public MachinePose Clamp(MachinePose pose) => new(
        X.Clamp(pose.X),
        Y.Clamp(pose.Y),
        Z.Clamp(pose.Z),
        A.Clamp(pose.A));

    public MachinePose ClampDelta(MachinePose current, MachinePose delta) => new(
        X.ClampDelta(current.X, delta.X),
        Y.ClampDelta(current.Y, delta.Y),
        Z.ClampDelta(current.Z, delta.Z),
        A.ClampDelta(current.A, delta.A));
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "AxisLimitsTests|MachineLimitsTests"`
Expected: PASS (12 tests).

- [ ] **Step 7: Commit**

```bash
git add ArctZ/Services/Device/MachinePose.cs ArctZ/Services/Device/AxisLimits.cs ArctZ/Services/Device/MachineLimits.cs ArctZ.Tests/Services/Device/AxisLimitsTests.cs ArctZ.Tests/Services/Device/MachineLimitsTests.cs
git commit -m "feat: add machine pose and axis-limit clamping"
```

---

## Task 3: Device command model

**Files:**
- Create: `ArctZ/Services/Device/Commands/IDeviceCommand.cs`

**Interfaces:**
- Consumes: `MachinePose` (Task 2).
- Produces: `IDeviceCommand` marker interface; `JogCommand(MachinePose Deltas, double Feed) : IDeviceCommand`; `GCodeLineCommand(string Line) : IDeviceCommand` with `bool IsExclusive` (true when `Line` starts with `$`); `RealtimeCommand(byte Value) : IDeviceCommand` with static `StatusQuery`, `FeedHold`, `CycleStartResume`, `JogCancel`.

Pure data types, nothing to drive with a failing test — the "test" is that they compile and are usable in later tasks.

- [ ] **Step 1: Create `ArctZ/Services/Device/Commands/IDeviceCommand.cs`**

```csharp
using System;

namespace ArctZ.Services.Device.Commands;

public interface IDeviceCommand
{
}

/// <summary>A relative jog move built from the joystick each throttle tick.</summary>
public sealed record JogCommand(MachinePose Deltas, double Feed) : IDeviceCommand;

/// <summary>A single queued G-code or $-settings line (e.g. "$H", "G28").</summary>
public sealed record GCodeLineCommand(string Line) : IDeviceCommand
{
    /// <summary>
    /// $-prefixed lines (settings, $H, $X, ...) touch EEPROM and must never
    /// be pipelined with other commands — see BufferAwareCommandQueue.
    /// </summary>
    public bool IsExclusive => Line.StartsWith("$", StringComparison.Ordinal);
}

/// <summary>A single-byte realtime command sent immediately, outside the buffered queue.</summary>
public sealed record RealtimeCommand(byte Value) : IDeviceCommand
{
    public static readonly RealtimeCommand StatusQuery = new((byte)'?');
    public static readonly RealtimeCommand FeedHold = new((byte)'!');
    public static readonly RealtimeCommand CycleStartResume = new((byte)'~');
    public static readonly RealtimeCommand JogCancel = new(0x85);
}
```

- [ ] **Step 2: Verify the solution builds**

Run: `dotnet build ArctZ/ArctZ.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add ArctZ/Services/Device/Commands/IDeviceCommand.cs
git commit -m "feat: add device command model"
```

---

## Task 4: `ICommandSerializer` / `FluidNcCommandSerializer`

**Files:**
- Create: `ArctZ/Services/Device/ICommandSerializer.cs`
- Create: `ArctZ/Services/Device/FluidNcCommandSerializer.cs`
- Test: `ArctZ.Tests/Services/Device/FluidNcCommandSerializerTests.cs`

**Interfaces:**
- Consumes: `IDeviceCommand`, `JogCommand`, `GCodeLineCommand`, `RealtimeCommand`, `MachinePose` (Tasks 2, 3).
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
    public void Serialize_JogCommand_ProducesRelativeJogLineWithAllFourAxes()
    {
        var command = new JogCommand(new MachinePose(X: 10, Y: -5, Z: 3, A: -2), Feed: 500);

        var result = _serializer.Serialize(command);

        Assert.Equal("$J=G91 G21 X10 Y-5 Z3 A-2 F500", result);
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
        var x = Format(jog.Deltas.X);
        var y = Format(jog.Deltas.Y);
        var z = Format(jog.Deltas.Z);
        var a = Format(jog.Deltas.A);
        var feed = Format(jog.Feed);
        return $"$J=G91 G21 X{x} Y{y} Z{z} A{a} F{feed}";
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
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

## Task 5: `IDeviceTransport` + `FakeDeviceTransport` test double

**Files:**
- Create: `ArctZ/Services/Device/IDeviceTransport.cs`
- Create: `ArctZ.Tests/Services/Device/FakeDeviceTransport.cs`

**Interfaces:**
- Produces: `IDeviceTransport` (`IsConnected`, `LineReceived`, `Disconnected`, `ConnectAsync`, `DisconnectAsync`, `SendLineAsync`, `SendRawByteAsync`); `FakeDeviceTransport : IDeviceTransport` — shared test double used by Tasks 7, 8, 11, 12, 14, 15, 16.

Infrastructure — no independent behavior to TDD, just build it and verify it compiles.

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

## Task 6: `IRealtimeCommandChannel` / `RealtimeCommandChannel`

**Files:**
- Create: `ArctZ/Services/Device/IRealtimeCommandChannel.cs`
- Create: `ArctZ/Services/Device/RealtimeCommandChannel.cs`
- Test: `ArctZ.Tests/Services/Device/RealtimeCommandChannelTests.cs`

**Interfaces:**
- Consumes: `IDeviceTransport` (Task 5), `RealtimeCommand` (Task 3), `FakeDeviceTransport` (Task 5).
- Produces: `IRealtimeCommandChannel.SendAsync(RealtimeCommand, CancellationToken)`; `RealtimeCommandChannel : IRealtimeCommandChannel`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;

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

## Task 7: `IBufferAwareCommandQueue` / `BufferAwareCommandQueue`

**Files:**
- Create: `ArctZ/Services/Device/IBufferAwareCommandQueue.cs`
- Create: `ArctZ/Services/Device/BufferAwareCommandQueue.cs`
- Test: `ArctZ.Tests/Services/Device/BufferAwareCommandQueueTests.cs`

**Interfaces:**
- Consumes: `IDeviceTransport`/`FakeDeviceTransport` (Task 5), `GCodeLineCommand` (Task 3).
- Produces: `CommandOutcome` enum (`Acknowledged`, `Rejected`, `Aborted`); `CommandResult(CommandOutcome Outcome, int? ErrorCode)`; `IBufferAwareCommandQueue` (`CommandCompleted` event, `EnqueueAsync`, `UpdateBufferCapacity`, `HandleOk`, `HandleError`); `BufferAwareCommandQueue : IBufferAwareCommandQueue` — relied on by `DeviceSession` (Task 14) and `TrajectoryCompiler`-driven playback (Task 22).

This replaces the naive "one command, wait for ok, send next" approach with
character-counting: it pipelines as many lines as fit in the controller's
RX buffer (tracked via `UpdateBufferCapacity`, called by `DeviceSession`
whenever a status report carries a `Bf:` reading — default 128 bytes until
the first one arrives) instead of stalling after every line. `$`-prefixed
lines are exclusive — sent only once every prior command has been
acknowledged, and nothing else is sent until they complete, because EEPROM
writes can corrupt under pipelining. On `error:N` the in-flight command
that failed is rejected and every command still waiting in the queue (not
yet sent to the transport) is aborted — but commands already sent ahead of
it are left alone; they'll resolve individually as their own `ok`/`error`
arrive, matching what the controller actually does with an already-full
RX buffer.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Tests.Services.Device;

public class BufferAwareCommandQueueTests
{
    [Fact]
    public void EnqueueAsync_MultipleShortLines_PipelinesWithoutWaitingForAck()
    {
        var transport = new FakeDeviceTransport();
        var queue = new BufferAwareCommandQueue(transport);

        _ = queue.EnqueueAsync(new GCodeLineCommand("G1 X1"));
        _ = queue.EnqueueAsync(new GCodeLineCommand("G1 X2"));
        _ = queue.EnqueueAsync(new GCodeLineCommand("G1 X3"));

        Assert.Equal(new[] { "G1 X1", "G1 X2", "G1 X3" }, transport.SentLines);
    }

    [Fact]
    public void EnqueueAsync_LinesExceedingCapacity_OnlySendsWhatFitsThenSendsRestAfterAck()
    {
        var transport = new FakeDeviceTransport();
        var queue = new BufferAwareCommandQueue(transport);
        queue.UpdateBufferCapacity(rxBytesAvailable: 10, plannerBlocksAvailable: 15);

        _ = queue.EnqueueAsync(new GCodeLineCommand("G1 X1"));
        _ = queue.EnqueueAsync(new GCodeLineCommand("G1 X2"));

        Assert.Equal(new[] { "G1 X1" }, transport.SentLines);

        queue.HandleOk();

        Assert.Equal(new[] { "G1 X1", "G1 X2" }, transport.SentLines);
    }

    [Fact]
    public async Task HandleOk_CompletesInFlightCommandAcknowledged()
    {
        var transport = new FakeDeviceTransport();
        var queue = new BufferAwareCommandQueue(transport);
        var resultTask = queue.EnqueueAsync(new GCodeLineCommand("G1 X1"));

        queue.HandleOk();
        var result = await resultTask;

        Assert.Equal(CommandOutcome.Acknowledged, result.Outcome);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task HandleError_RejectsInFlightAndAbortsPendingNotYetSent()
    {
        var transport = new FakeDeviceTransport();
        var queue = new BufferAwareCommandQueue(transport);
        queue.UpdateBufferCapacity(rxBytesAvailable: 10, plannerBlocksAvailable: 15);

        var taskA = queue.EnqueueAsync(new GCodeLineCommand("G1 X1"));
        var taskB = queue.EnqueueAsync(new GCodeLineCommand("G1 X2"));
        var taskC = queue.EnqueueAsync(new GCodeLineCommand("G1 X3"));

        Assert.Equal(new[] { "G1 X1" }, transport.SentLines);

        queue.HandleError(9);

        var resultA = await taskA;
        var resultB = await taskB;
        var resultC = await taskC;

        Assert.Equal(CommandOutcome.Rejected, resultA.Outcome);
        Assert.Equal(9, resultA.ErrorCode);
        Assert.Equal(CommandOutcome.Aborted, resultB.Outcome);
        Assert.Null(resultB.ErrorCode);
        Assert.Equal(CommandOutcome.Aborted, resultC.Outcome);
        Assert.Equal(new[] { "G1 X1" }, transport.SentLines);
    }

    [Fact]
    public void HandleError_RaisesCommandCompletedForEachAffectedCommand()
    {
        var transport = new FakeDeviceTransport();
        var queue = new BufferAwareCommandQueue(transport);
        queue.UpdateBufferCapacity(rxBytesAvailable: 10, plannerBlocksAvailable: 15);
        var completed = new List<(GCodeLineCommand Command, CommandResult Result)>();
        queue.CommandCompleted += (command, result) => completed.Add((command, result));

        _ = queue.EnqueueAsync(new GCodeLineCommand("G1 X1"));
        _ = queue.EnqueueAsync(new GCodeLineCommand("G1 X2"));

        queue.HandleError(9);

        Assert.Equal(2, completed.Count);
        Assert.Equal("G1 X1", completed[0].Command.Line);
        Assert.Equal(CommandOutcome.Rejected, completed[0].Result.Outcome);
        Assert.Equal("G1 X2", completed[1].Command.Line);
        Assert.Equal(CommandOutcome.Aborted, completed[1].Result.Outcome);
    }

    [Fact]
    public void Enqueue_ExclusiveDollarCommand_WaitsForQueueToDrainBeforeSending()
    {
        var transport = new FakeDeviceTransport();
        var queue = new BufferAwareCommandQueue(transport);

        _ = queue.EnqueueAsync(new GCodeLineCommand("G1 X1"));
        _ = queue.EnqueueAsync(new GCodeLineCommand("$H"));

        Assert.Equal(new[] { "G1 X1" }, transport.SentLines);

        queue.HandleOk();

        Assert.Equal(new[] { "G1 X1", "$H" }, transport.SentLines);
    }

    [Fact]
    public void Enqueue_NormalCommandAfterExclusiveInFlight_WaitsForExclusiveAck()
    {
        var transport = new FakeDeviceTransport();
        var queue = new BufferAwareCommandQueue(transport);

        _ = queue.EnqueueAsync(new GCodeLineCommand("$H"));
        _ = queue.EnqueueAsync(new GCodeLineCommand("G1 X1"));

        Assert.Equal(new[] { "$H" }, transport.SentLines);

        queue.HandleOk();

        Assert.Equal(new[] { "$H", "G1 X1" }, transport.SentLines);
    }

    [Fact]
    public void UpdateBufferCapacity_IncreasingCapacity_UnblocksPendingCommand()
    {
        var transport = new FakeDeviceTransport();
        var queue = new BufferAwareCommandQueue(transport);
        queue.UpdateBufferCapacity(rxBytesAvailable: 4, plannerBlocksAvailable: 15);

        _ = queue.EnqueueAsync(new GCodeLineCommand("G1 X1"));

        Assert.Empty(transport.SentLines);

        queue.UpdateBufferCapacity(rxBytesAvailable: 20, plannerBlocksAvailable: 15);

        Assert.Equal(new[] { "G1 X1" }, transport.SentLines);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter BufferAwareCommandQueueTests`
Expected: FAIL — `BufferAwareCommandQueue` does not exist.

- [ ] **Step 3: Create `ArctZ/Services/Device/IBufferAwareCommandQueue.cs`**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public enum CommandOutcome
{
    Acknowledged,
    Rejected,
    Aborted
}

public readonly record struct CommandResult(CommandOutcome Outcome, int? ErrorCode);

public interface IBufferAwareCommandQueue
{
    event Action<GCodeLineCommand, CommandResult>? CommandCompleted;

    Task<CommandResult> EnqueueAsync(GCodeLineCommand command, CancellationToken cancellationToken = default);

    /// <summary>Called whenever a status report carries a fresh Bf: reading.</summary>
    void UpdateBufferCapacity(int rxBytesAvailable, int plannerBlocksAvailable);

    /// <summary>Call when the transport receives a plain "ok" line.</summary>
    void HandleOk();

    /// <summary>Call when the transport receives an "error:N" line.</summary>
    void HandleError(int code);
}
```

- [ ] **Step 4: Create `ArctZ/Services/Device/BufferAwareCommandQueue.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public sealed class BufferAwareCommandQueue : IBufferAwareCommandQueue
{
    private const int DefaultRxBytesAvailable = 128;

    private readonly IDeviceTransport _transport;
    private readonly object _lock = new();
    private readonly Queue<Entry> _pending = new();
    private readonly Queue<Entry> _inFlight = new();

    private int _rxBytesAvailable = DefaultRxBytesAvailable;
    private int _inFlightCharCount;
    private bool _exclusiveInFlight;

    public BufferAwareCommandQueue(IDeviceTransport transport)
    {
        _transport = transport;
    }

    public event Action<GCodeLineCommand, CommandResult>? CommandCompleted;

    public Task<CommandResult> EnqueueAsync(GCodeLineCommand command, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _pending.Enqueue(new Entry(command, completion));
            Pump();
        }

        return completion.Task;
    }

    public void UpdateBufferCapacity(int rxBytesAvailable, int plannerBlocksAvailable)
    {
        lock (_lock)
        {
            _rxBytesAvailable = rxBytesAvailable;
            Pump();
        }
    }

    public void HandleOk() => Complete(new CommandResult(CommandOutcome.Acknowledged, null), abortPending: false);

    public void HandleError(int code) => Complete(new CommandResult(CommandOutcome.Rejected, code), abortPending: true);

    private void Complete(CommandResult inFlightResult, bool abortPending)
    {
        var toNotify = new List<(GCodeLineCommand Command, CommandResult Result)>();

        lock (_lock)
        {
            if (_inFlight.Count == 0)
            {
                return;
            }

            var resolved = _inFlight.Dequeue();
            _inFlightCharCount -= LineLength(resolved.Command);
            if (resolved.Command.IsExclusive)
            {
                _exclusiveInFlight = false;
            }

            resolved.Completion.SetResult(inFlightResult);
            toNotify.Add((resolved.Command, inFlightResult));

            if (abortPending)
            {
                while (_pending.Count > 0)
                {
                    var aborted = _pending.Dequeue();
                    var abortedResult = new CommandResult(CommandOutcome.Aborted, null);
                    aborted.Completion.SetResult(abortedResult);
                    toNotify.Add((aborted.Command, abortedResult));
                }
            }

            Pump();
        }

        foreach (var (command, result) in toNotify)
        {
            CommandCompleted?.Invoke(command, result);
        }
    }

    /// <summary>Caller must hold `_lock`.</summary>
    private void Pump()
    {
        while (_pending.Count > 0 && !_exclusiveInFlight)
        {
            var next = _pending.Peek();

            if (next.Command.IsExclusive)
            {
                if (_inFlight.Count > 0)
                {
                    break;
                }

                _pending.Dequeue();
                _inFlight.Enqueue(next);
                _inFlightCharCount += LineLength(next.Command);
                _exclusiveInFlight = true;
                _ = _transport.SendLineAsync(next.Command.Line);
                break;
            }

            var length = LineLength(next.Command);
            if (_inFlightCharCount + length > _rxBytesAvailable - 1)
            {
                break;
            }

            _pending.Dequeue();
            _inFlight.Enqueue(next);
            _inFlightCharCount += length;
            _ = _transport.SendLineAsync(next.Command.Line);
        }
    }

    private static int LineLength(GCodeLineCommand command) => command.Line.Length + 1;

    private readonly record struct Entry(GCodeLineCommand Command, TaskCompletionSource<CommandResult> Completion);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter BufferAwareCommandQueueTests`
Expected: PASS (8 tests).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Services/Device/IBufferAwareCommandQueue.cs ArctZ/Services/Device/BufferAwareCommandQueue.cs ArctZ.Tests/Services/Device/BufferAwareCommandQueueTests.cs
git commit -m "feat: add character-counting buffer-aware command queue"
```

---

## Task 8: `DeviceStatus` + `IStatusParser` / `FluidNcStatusParser`

**Files:**
- Create: `ArctZ/Services/Device/DeviceStatus.cs`
- Create: `ArctZ/Services/Device/FluidNcLine.cs`
- Create: `ArctZ/Services/Device/IStatusParser.cs`
- Create: `ArctZ/Services/Device/FluidNcStatusParser.cs`
- Test: `ArctZ.Tests/Services/Device/FluidNcStatusParserTests.cs`

**Interfaces:**
- Consumes: `MachinePose` (Task 2).
- Produces: `MachineState` enum (`Idle`, `Run`, `Jog`, `Hold`, `Home`, `Alarm`, `Unknown`); `DeviceStatus(MachineState State, MachinePose WPos, int? PlannerBlocksAvailable, int? RxBytesAvailable)`; `FluidNcLine` abstract record with `StatusReportLine(DeviceStatus Status)`, `OkLine`, `ErrorLine(int Code)`, `AlarmLine(int Code)`, `UnrecognizedLine(string Raw)`; `IStatusParser.Parse(string) : FluidNcLine`; `FluidNcStatusParser : IStatusParser` — all relied on by `DeviceSession` (Task 14), which feeds `PlannerBlocksAvailable`/`RxBytesAvailable` into `BufferAwareCommandQueue.UpdateBufferCapacity` (Task 7).

`DeviceStatus.WPos` consolidates what would otherwise be 4 separate
`WPosX`/`WPosY`/`WPosZ`/`WPosA` fields into one `MachinePose` — the status
report and the rest of the domain (`Waypoint.Pose` in Task 18) speak the
same type.

- [ ] **Step 1: Write the failing tests**

```csharp
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class FluidNcStatusParserTests
{
    private readonly FluidNcStatusParser _parser = new();

    [Fact]
    public void Parse_StatusReportLine_ExtractsStateWorkPositionAndBuffer()
    {
        var result = _parser.Parse("<Idle|WPos:0.000,-80.000,-10.540,45.000|Bf:15,128|FS:0,0|Ov:100,100,100>");

        var report = Assert.IsType<StatusReportLine>(result);
        Assert.Equal(MachineState.Idle, report.Status.State);
        Assert.Equal(new MachinePose(0.000, -80.000, -10.540, 45.000), report.Status.WPos);
        Assert.Equal(15, report.Status.PlannerBlocksAvailable);
        Assert.Equal(128, report.Status.RxBytesAvailable);
    }

    [Fact]
    public void Parse_StatusReportLine_MissingAxis_DefaultsToZero()
    {
        var result = _parser.Parse("<Run|WPos:1.000,2.000,3.000|FS:0,0>");

        var report = Assert.IsType<StatusReportLine>(result);
        Assert.Equal(new MachinePose(1.000, 2.000, 3.000, 0), report.Status.WPos);
    }

    [Fact]
    public void Parse_StatusReportLine_MissingBf_ReturnsNullBufferInfo()
    {
        var result = _parser.Parse("<Idle|WPos:0,0,0,0|FS:0,0>");

        var report = Assert.IsType<StatusReportLine>(result);
        Assert.Null(report.Status.PlannerBlocksAvailable);
        Assert.Null(report.Status.RxBytesAvailable);
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

public readonly record struct DeviceStatus(
    MachineState State,
    MachinePose WPos,
    int? PlannerBlocksAvailable,
    int? RxBytesAvailable);
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

        var pose = MachinePose.Zero;
        var wPosField = Array.Find(fields, f => f.StartsWith("WPos:", StringComparison.Ordinal));
        if (wPosField is not null)
        {
            var coords = wPosField["WPos:".Length..].Split(',');
            pose = new MachinePose(
                X: ParseCoordinate(coords, 0),
                Y: ParseCoordinate(coords, 1),
                Z: ParseCoordinate(coords, 2),
                A: ParseCoordinate(coords, 3));
        }

        int? plannerBlocksAvailable = null;
        int? rxBytesAvailable = null;
        var bfField = Array.Find(fields, f => f.StartsWith("Bf:", StringComparison.Ordinal));
        if (bfField is not null)
        {
            var parts = bfField["Bf:".Length..].Split(',');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var planner) &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rx))
            {
                plannerBlocksAvailable = planner;
                rxBytesAvailable = rx;
            }
        }

        return new StatusReportLine(new DeviceStatus(state, pose, plannerBlocksAvailable, rxBytesAvailable));
    }

    private static double ParseCoordinate(string[] coords, int index) =>
        index < coords.Length &&
        double.TryParse(coords[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter FluidNcStatusParserTests`
Expected: PASS (7 tests).

- [ ] **Step 8: Commit**

```bash
git add ArctZ/Services/Device/DeviceStatus.cs ArctZ/Services/Device/FluidNcLine.cs ArctZ/Services/Device/IStatusParser.cs ArctZ/Services/Device/FluidNcStatusParser.cs ArctZ.Tests/Services/Device/FluidNcStatusParserTests.cs
git commit -m "feat: add FluidNC status line parser"
```

---

## Task 9: `DualJoystickState` + `IJogCommandFactory` / `JogCommandFactory`

**Files:**
- Create: `ArctZ/Services/Device/DualJoystickState.cs`
- Create: `ArctZ/Services/Device/IJogCommandFactory.cs`
- Create: `ArctZ/Services/Device/JogCommandFactory.cs`
- Test: `ArctZ.Tests/Services/Device/JogCommandFactoryTests.cs`

**Interfaces:**
- Consumes: `MachinePose`, `MachineLimits` (Task 2), `JogCommand` (Task 3).
- Produces: `JoystickAxisInput(double X, double Y, double Force)`; `DualJoystickState(JoystickAxisInput Left, JoystickAxisInput Right)`; `IJogCommandFactory.Create(DualJoystickState, MachinePose currentPose) : JogCommand`; `JogCommandFactory : IJogCommandFactory` — used by `JogScheduler` (Task 11).

Left joystick: `X` → boom lift (machine `X`), `Y` → boom rotation (machine
`Y`). Right joystick: `X` → camera pan (machine `Z`), `Y` → camera tilt
(machine `A`). The factory needs `currentPose` (not just the joystick
state) because clamping a *relative* jog delta against the machine's
*absolute* limits requires knowing where the machine currently is — see
`AxisLimits.ClampDelta` (Task 2).

- [ ] **Step 1: Write the failing tests**

```csharp
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class JogCommandFactoryTests
{
    private readonly JogCommandFactory _factory =
        new(MachineLimits.Default, maxStepDegrees: 5.0, maxFeedUnitsPerMin: 1000.0);

    [Fact]
    public void Create_BothSticksNeutral_ZeroDeltasAndMinimumFeed()
    {
        var state = new DualJoystickState(new JoystickAxisInput(0, 0, 0), new JoystickAxisInput(0, 0, 0));

        var command = _factory.Create(state, MachinePose.Zero);

        Assert.Equal(MachinePose.Zero, command.Deltas);
        Assert.Equal(1.0, command.Feed);
    }

    [Fact]
    public void Create_LeftStickX_MapsToBoomLiftAxis()
    {
        var state = new DualJoystickState(new JoystickAxisInput(1, 0, 1), new JoystickAxisInput(0, 0, 0));

        var command = _factory.Create(state, MachinePose.Zero);

        Assert.Equal(5, command.Deltas.X);
        Assert.Equal(0, command.Deltas.Y);
        Assert.Equal(0, command.Deltas.Z);
        Assert.Equal(0, command.Deltas.A);
    }

    [Fact]
    public void Create_LeftStickY_MapsToBoomRotationAxis()
    {
        var state = new DualJoystickState(new JoystickAxisInput(0, 1, 1), new JoystickAxisInput(0, 0, 0));

        var command = _factory.Create(state, MachinePose.Zero);

        Assert.Equal(5, command.Deltas.Y);
    }

    [Fact]
    public void Create_RightStickX_MapsToCameraPanAxis()
    {
        var state = new DualJoystickState(new JoystickAxisInput(0, 0, 0), new JoystickAxisInput(1, 0, 1));

        var command = _factory.Create(state, MachinePose.Zero);

        Assert.Equal(5, command.Deltas.Z);
    }

    [Fact]
    public void Create_RightStickY_MapsToCameraTiltAxis()
    {
        var state = new DualJoystickState(new JoystickAxisInput(0, 0, 0), new JoystickAxisInput(0, 1, 1));

        var command = _factory.Create(state, MachinePose.Zero);

        Assert.Equal(5, command.Deltas.A);
    }

    [Fact]
    public void Create_NearUpperXLimit_ClampsDeltaToRemainingRoom()
    {
        var state = new DualJoystickState(new JoystickAxisInput(1, 0, 1), new JoystickAxisInput(0, 0, 0));
        var currentPose = new MachinePose(X: 63, Y: 0, Z: 0, A: 0);

        var command = _factory.Create(state, currentPose);

        Assert.Equal(2, command.Deltas.X);
    }

    [Fact]
    public void Create_WrappingAxisNearBoundary_DeltaPassesThroughUnclamped()
    {
        var state = new DualJoystickState(new JoystickAxisInput(0, 0, 0), new JoystickAxisInput(1, 0, 1));
        var currentPose = new MachinePose(X: 0, Y: 0, Z: 359, A: 0);

        var command = _factory.Create(state, currentPose);

        Assert.Equal(5, command.Deltas.Z);
    }

    [Fact]
    public void Create_FeedUsesLargerOfTheTwoStickForces()
    {
        var state = new DualJoystickState(new JoystickAxisInput(1, 0, 0.3), new JoystickAxisInput(0, 1, 0.8));

        var command = _factory.Create(state, MachinePose.Zero);

        Assert.Equal(800, command.Feed);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter JogCommandFactoryTests`
Expected: FAIL — `JogCommandFactory` does not exist.

- [ ] **Step 3: Create `ArctZ/Services/Device/DualJoystickState.cs`**

```csharp
namespace ArctZ.Services.Device;

public readonly record struct JoystickAxisInput(double X, double Y, double Force);

/// <summary>Combined snapshot of both physical joysticks driving the 4-axis machine.</summary>
public readonly record struct DualJoystickState(JoystickAxisInput Left, JoystickAxisInput Right);
```

- [ ] **Step 4: Create `ArctZ/Services/Device/IJogCommandFactory.cs`**

```csharp
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public interface IJogCommandFactory
{
    JogCommand Create(DualJoystickState state, MachinePose currentPose);
}
```

- [ ] **Step 5: Create `ArctZ/Services/Device/JogCommandFactory.cs`**

```csharp
using System;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

/// <summary>
/// Maps the two physical joysticks to a 4-axis JogCommand. Left stick:
/// X -> boom lift (machine X), Y -> boom rotation (machine Y). Right
/// stick: X -> camera pan (machine Z), Y -> camera tilt (machine A).
/// </summary>
public sealed class JogCommandFactory : IJogCommandFactory
{
    private readonly MachineLimits _limits;
    private readonly double _maxStepDegrees;
    private readonly double _maxFeedUnitsPerMin;

    public JogCommandFactory(MachineLimits limits, double maxStepDegrees = 5.0, double maxFeedUnitsPerMin = 1000.0)
    {
        _limits = limits;
        _maxStepDegrees = maxStepDegrees;
        _maxFeedUnitsPerMin = maxFeedUnitsPerMin;
    }

    public JogCommand Create(DualJoystickState state, MachinePose currentPose)
    {
        var rawDeltas = new MachinePose(
            X: state.Left.X * _maxStepDegrees,
            Y: state.Left.Y * _maxStepDegrees,
            Z: state.Right.X * _maxStepDegrees,
            A: state.Right.Y * _maxStepDegrees);

        var deltas = _limits.ClampDelta(currentPose, rawDeltas);

        var force = Math.Max(state.Left.Force, state.Right.Force);
        var feed = Math.Max(1.0, force * _maxFeedUnitsPerMin);

        return new JogCommand(deltas, feed);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter JogCommandFactoryTests`
Expected: PASS (8 tests).

- [ ] **Step 7: Commit**

```bash
git add ArctZ/Services/Device/DualJoystickState.cs ArctZ/Services/Device/IJogCommandFactory.cs ArctZ/Services/Device/JogCommandFactory.cs ArctZ.Tests/Services/Device/JogCommandFactoryTests.cs
git commit -m "feat: add dual-joystick 4-axis jog command factory"
```

---

## Task 10: `IPeriodicTimer` + `SystemPeriodicTimer` + `ManualPeriodicTimer` test double

**Files:**
- Create: `ArctZ/Services/Device/IPeriodicTimer.cs`
- Create: `ArctZ/Services/Device/SystemPeriodicTimer.cs`
- Create: `ArctZ.Tests/Services/Device/ManualPeriodicTimer.cs`

**Interfaces:**
- Produces: `IPeriodicTimer` (`Elapsed` event, `Start(TimeSpan)`, `Stop()`); `SystemPeriodicTimer : IPeriodicTimer, IDisposable` (production, backed by `System.Threading.Timer`); `ManualPeriodicTimer : IPeriodicTimer` (test double with `IsRunning`, `LastInterval`, `RaiseElapsed()`) — used by `JogScheduler` (Task 11), `StatusPoller` (Task 12), and `MockDeviceTransport`'s motion tick (Task 16).

Infrastructure — no independent behavior to TDD.

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

## Task 11: `IJogScheduler` / `JogScheduler`

**Files:**
- Create: `ArctZ/Services/Device/IJogScheduler.cs`
- Create: `ArctZ/Services/Device/JogScheduler.cs`
- Test: `ArctZ.Tests/Services/Device/JogSchedulerTests.cs`

**Interfaces:**
- Consumes: `IJogCommandFactory`/`JogCommandFactory` (Task 9), `ICommandSerializer` (Task 4), `IDeviceTransport`/`FakeDeviceTransport` (Task 5), `IRealtimeCommandChannel`/`RealtimeCommandChannel` (Task 6), `IPeriodicTimer`/`ManualPeriodicTimer` (Task 10), `DualJoystickState`, `MachinePose` (Tasks 9, 2).
- Produces: `IJogScheduler` (`IsActive`, `Start()`, `UpdateState(DualJoystickState)`, `UpdateCurrentPose(MachinePose)`, `Stop()`); `JogScheduler : IJogScheduler` — used by `DeviceSession` (Task 14).

`UpdateCurrentPose` is separate from `UpdateState` because the two update
at different rates: `UpdateState` follows pointer-move events on the two
joysticks, `UpdateCurrentPose` follows `DeviceStatusChanged` (a status
report arriving) — the scheduler needs the latest of each independently
when its timer ticks, so it can hand `JogCommandFactory` a `currentPose`
that's as fresh as possible for delta-clamping.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class JogSchedulerTests
{
    private readonly FakeDeviceTransport _transport = new();
    private readonly ManualPeriodicTimer _timer = new();
    private readonly JogScheduler _scheduler;

    public JogSchedulerTests()
    {
        _scheduler = new JogScheduler(
            new JogCommandFactory(MachineLimits.Default, maxStepDegrees: 5.0, maxFeedUnitsPerMin: 1000.0),
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
    public void Tick_WithState_SendsSerializedJogLineForAllFourAxes()
    {
        _scheduler.Start();
        _scheduler.UpdateState(new DualJoystickState(new JoystickAxisInput(1, 0, 1), new JoystickAxisInput(0, 0, 0)));

        _timer.RaiseElapsed();

        Assert.Equal(new[] { "$J=G91 G21 X5 Y0 Z0 A0 F1000" }, _transport.SentLines);
    }

    [Fact]
    public void Tick_UsesLatestKnownPoseForClamping()
    {
        _scheduler.Start();
        _scheduler.UpdateCurrentPose(new MachinePose(X: 63, Y: 0, Z: 0, A: 0));
        _scheduler.UpdateState(new DualJoystickState(new JoystickAxisInput(1, 0, 1), new JoystickAxisInput(0, 0, 0)));

        _timer.RaiseElapsed();

        Assert.Equal(new[] { "$J=G91 G21 X2 Y0 Z0 A0 F1000" }, _transport.SentLines);
    }

    [Fact]
    public void Stop_StopsTimerAndSendsJogCancel()
    {
        _scheduler.Start();
        _scheduler.UpdateState(new DualJoystickState(new JoystickAxisInput(1, 0, 1), new JoystickAxisInput(0, 0, 0)));

        _scheduler.Stop();

        Assert.False(_scheduler.IsActive);
        Assert.False(_timer.IsRunning);
        Assert.Equal(new byte[] { 0x85 }, _transport.SentRawBytes);
    }

    [Fact]
    public void Tick_AfterStop_SendsNothing()
    {
        _scheduler.Start();
        _scheduler.UpdateState(new DualJoystickState(new JoystickAxisInput(1, 0, 1), new JoystickAxisInput(0, 0, 0)));
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

    void UpdateState(DualJoystickState state);

    void UpdateCurrentPose(MachinePose pose);

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
    private DualJoystickState? _latestState;
    private MachinePose _latestPose = MachinePose.Zero;

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

    public void UpdateState(DualJoystickState state) => _latestState = state;

    public void UpdateCurrentPose(MachinePose pose) => _latestPose = pose;

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

        var command = _commandFactory.Create(_latestState.Value, _latestPose);
        var text = _serializer.Serialize(command);
        _ = _transport.SendLineAsync(text);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter JogSchedulerTests`
Expected: PASS (6 tests).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Services/Device/IJogScheduler.cs ArctZ/Services/Device/JogScheduler.cs ArctZ.Tests/Services/Device/JogSchedulerTests.cs
git commit -m "feat: add throttled dual-joystick jog scheduler"
```

---

## Task 12: `IStatusPoller` / `StatusPoller`

**Files:**
- Create: `ArctZ/Services/Device/IStatusPoller.cs`
- Create: `ArctZ/Services/Device/StatusPoller.cs`
- Test: `ArctZ.Tests/Services/Device/StatusPollerTests.cs`

**Interfaces:**
- Consumes: `IRealtimeCommandChannel`/`RealtimeCommandChannel` (Task 6), `IPeriodicTimer`/`ManualPeriodicTimer` (Task 10), `FakeDeviceTransport` (Task 5).
- Produces: `IStatusPoller` (`Start()`, `Stop()`); `StatusPoller : IStatusPoller` — used by `DeviceSession` (Task 14).

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using ArctZ.Services.Device;

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

## Task 13: `IReconnectPolicy` / `FixedDelayReconnectPolicy`

**Files:**
- Create: `ArctZ/Services/Device/IReconnectPolicy.cs`
- Create: `ArctZ/Services/Device/FixedDelayReconnectPolicy.cs`
- Test: `ArctZ.Tests/Services/Device/FixedDelayReconnectPolicyTests.cs`

**Interfaces:**
- Produces: `IReconnectPolicy` (`MaxAttempts`, `WaitBeforeRetryAsync(int attemptNumber, CancellationToken) : Task`); `FixedDelayReconnectPolicy : IReconnectPolicy` — used by `DeviceSession` (Task 15). Per spec: 3 attempts, 200 ms apart.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class FixedDelayReconnectPolicyTests
{
    [Fact]
    public void MaxAttempts_ReturnsConfiguredValue()
    {
        var policy = new FixedDelayReconnectPolicy(maxAttempts: 3, delay: TimeSpan.FromMilliseconds(200));

        Assert.Equal(3, policy.MaxAttempts);
    }

    [Fact]
    public async Task WaitBeforeRetryAsync_CompletesWithoutThrowing()
    {
        var policy = new FixedDelayReconnectPolicy(maxAttempts: 3, delay: TimeSpan.FromMilliseconds(1));

        await policy.WaitBeforeRetryAsync(attemptNumber: 1);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter FixedDelayReconnectPolicyTests`
Expected: FAIL — `FixedDelayReconnectPolicy` does not exist.

- [ ] **Step 3: Create `ArctZ/Services/Device/IReconnectPolicy.cs`**

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

public interface IReconnectPolicy
{
    int MaxAttempts { get; }

    Task WaitBeforeRetryAsync(int attemptNumber, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Create `ArctZ/Services/Device/FixedDelayReconnectPolicy.cs`**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

/// <summary>Per spec: 3 attempts, 200ms apart, then give up.</summary>
public sealed class FixedDelayReconnectPolicy : IReconnectPolicy
{
    private readonly TimeSpan _delay;

    public FixedDelayReconnectPolicy(int maxAttempts, TimeSpan delay)
    {
        MaxAttempts = maxAttempts;
        _delay = delay;
    }

    public int MaxAttempts { get; }

    public Task WaitBeforeRetryAsync(int attemptNumber, CancellationToken cancellationToken = default) =>
        Task.Delay(_delay, cancellationToken);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter FixedDelayReconnectPolicyTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Services/Device/IReconnectPolicy.cs ArctZ/Services/Device/FixedDelayReconnectPolicy.cs ArctZ.Tests/Services/Device/FixedDelayReconnectPolicyTests.cs
git commit -m "feat: add fixed-delay reconnect policy"
```

---

## Task 14: `IDeviceSession` / `DeviceSession` — core (connect, routing, jog delegation, buffer wiring)

**Files:**
- Create: `ArctZ/Services/Device/ConnectionState.cs`
- Create: `ArctZ/Services/Device/CommandRejectedEventArgs.cs`
- Create: `ArctZ/Services/Device/IDeviceSession.cs`
- Create: `ArctZ/Services/Device/DeviceSession.cs`
- Test: `ArctZ.Tests/Services/Device/DeviceSessionTests.cs`

**Interfaces:**
- Consumes: `IDeviceTransport`/`FakeDeviceTransport` (Task 5), `IBufferAwareCommandQueue`/`BufferAwareCommandQueue` (Task 7), `IStatusParser`/`FluidNcStatusParser` (Task 8), `IJogScheduler`/`JogScheduler` (Task 11), `IStatusPoller`/`StatusPoller` (Task 12), `DualJoystickState` (Task 9).
- Produces: `ConnectionState` enum (`Disconnected`, `Connecting`, `Connected`, `Reconnecting`); `CommandRejectedEventArgs(GCodeLineCommand Command, int? ErrorCode)`; `IDeviceSession` (`ConnectionState`, `DeviceStatus`, `ConnectionStateChanged`, `DeviceStatusChanged`, `CommandRejected`, `AlarmTriggered` events; `ConnectAsync`, `DisconnectAsync`, `BeginJog`, `UpdateJog`, `EndJog`, `SendGCodeAsync`, `HomeAsync`, `ResetAlarmAsync`); `DeviceSession : IDeviceSession` — used by `ConnectionViewModel`/`ProgramViewModel` (Tasks 17, 21, 22) and extended with reconnect in Task 15.

This task covers everything except reconnect-with-backoff (Task 15). It is
also where the buffer-capacity wiring the spec calls for lives:
`DeviceSession` is the only component that sees both a fresh status report
(`Bf:`) and the command queue, so it is responsible for calling
`BufferAwareCommandQueue.UpdateBufferCapacity` and
`JogScheduler.UpdateCurrentPose` whenever one arrives — neither the parser
nor the queue know about each other directly.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Tests.Services.Device;

public class DeviceSessionTests
{
    private readonly FakeDeviceTransport _transport = new();
    private readonly ManualPeriodicTimer _jogTimer = new();
    private readonly ManualPeriodicTimer _pollTimer = new();
    private readonly BufferAwareCommandQueue _commandQueue;
    private readonly DeviceSession _session;

    public DeviceSessionTests()
    {
        var serializer = new FluidNcCommandSerializer();
        var realtimeChannel = new RealtimeCommandChannel(_transport);
        _commandQueue = new BufferAwareCommandQueue(_transport);
        var jogScheduler = new JogScheduler(
            new JogCommandFactory(MachineLimits.Default), serializer, _transport, realtimeChannel, _jogTimer, TimeSpan.FromMilliseconds(100));
        var statusPoller = new StatusPoller(realtimeChannel, _pollTimer, TimeSpan.FromMilliseconds(250));

        _session = new DeviceSession(_transport, _commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller);
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

        _transport.SimulateReceivedLine("<Idle|WPos:0.000,-80.000,-10.540,45.000|FS:0,0>");

        Assert.True(raised);
        Assert.Equal(MachineState.Idle, _session.DeviceStatus!.Value.State);
        Assert.Equal(new MachinePose(0.000, -80.000, -10.540, 45.000), _session.DeviceStatus.Value.WPos);
    }

    [Fact]
    public async Task OnStatusReportLine_UpdatesCommandQueueBufferCapacity()
    {
        await _session.ConnectAsync("COM5");
        _transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|Bf:1,6>");

        _ = _session.SendGCodeAsync("G1 X1"); // len 6, budget (6-1)=5 -> blocked

        Assert.DoesNotContain("G1 X1", _transport.SentLines);

        _transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|Bf:1,20>"); // budget 19 -> unblocks

        Assert.Contains("G1 X1", _transport.SentLines);
    }

    [Fact]
    public async Task OnErrorLine_RaisesCommandRejectedWithErrorCode()
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
    public async Task BeginUpdateEndJog_DelegatesToJogSchedulerWithDualJoystickState()
    {
        await _session.ConnectAsync("COM5");

        _session.BeginJog();
        _session.UpdateJog(new DualJoystickState(new JoystickAxisInput(1, 0, 1), new JoystickAxisInput(0, 0, 0)));
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

public sealed record CommandRejectedEventArgs(GCodeLineCommand Command, int? ErrorCode);
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

    void UpdateJog(DualJoystickState state);

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
    private readonly IBufferAwareCommandQueue _commandQueue;
    private readonly IStatusParser _statusParser;
    private readonly IJogScheduler _jogScheduler;
    private readonly IStatusPoller _statusPoller;

    public DeviceSession(
        IDeviceTransport transport,
        IBufferAwareCommandQueue commandQueue,
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

    public void UpdateJog(DualJoystickState state) => _jogScheduler.UpdateState(state);

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
        if (result.Outcome is CommandOutcome.Rejected or CommandOutcome.Aborted)
        {
            CommandRejected?.Invoke(new CommandRejectedEventArgs(command, result.ErrorCode));
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
                if (report.Status.PlannerBlocksAvailable is { } planner && report.Status.RxBytesAvailable is { } rx)
                {
                    _commandQueue.UpdateBufferCapacity(rx, planner);
                }

                _jogScheduler.UpdateCurrentPose(report.Status.WPos);
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
Expected: PASS (9 tests).

- [ ] **Step 8: Commit**

```bash
git add ArctZ/Services/Device/ConnectionState.cs ArctZ/Services/Device/CommandRejectedEventArgs.cs ArctZ/Services/Device/IDeviceSession.cs ArctZ/Services/Device/DeviceSession.cs ArctZ.Tests/Services/Device/DeviceSessionTests.cs
git commit -m "feat: add DeviceSession orchestrator with buffer-capacity wiring"
```

---

## Task 15: `DeviceSession` reconnect with backoff

**Files:**
- Modify: `ArctZ/Services/Device/IDeviceSession.cs`
- Modify: `ArctZ/Services/Device/DeviceSession.cs`
- Test: `ArctZ.Tests/Services/Device/DeviceSessionReconnectTests.cs`

**Interfaces:**
- Consumes: `IReconnectPolicy`/`FixedDelayReconnectPolicy` (Task 13), `FakeDeviceTransport`'s `ConnectFailuresRemaining`/`SimulateDisconnect` (Task 5).
- Produces: `IDeviceSession` gains `string? LastError { get; }`; `DeviceSession` gains an `IReconnectPolicy` constructor parameter (6th) and reconnect-on-unexpected-disconnect behavior.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class DeviceSessionReconnectTests
{
    private readonly FakeDeviceTransport _transport = new();
    private readonly ManualPeriodicTimer _jogTimer = new();
    private readonly ManualPeriodicTimer _pollTimer = new();
    private readonly DeviceSession _session;

    public DeviceSessionReconnectTests()
    {
        var serializer = new FluidNcCommandSerializer();
        var realtimeChannel = new RealtimeCommandChannel(_transport);
        var commandQueue = new BufferAwareCommandQueue(_transport);
        var jogScheduler = new JogScheduler(
            new JogCommandFactory(MachineLimits.Default), serializer, _transport, realtimeChannel, _jogTimer, TimeSpan.FromMilliseconds(100));
        var statusPoller = new StatusPoller(realtimeChannel, _pollTimer, TimeSpan.FromMilliseconds(250));
        var reconnectPolicy = new FixedDelayReconnectPolicy(maxAttempts: 3, delay: TimeSpan.FromMilliseconds(1));

        _session = new DeviceSession(_transport, commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller, reconnectPolicy);
    }

    private Task WaitForConnectionStateAsync(ConnectionState target)
    {
        if (_session.ConnectionState == target)
        {
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource();
        void Handler()
        {
            if (_session.ConnectionState == target)
            {
                _session.ConnectionStateChanged -= Handler;
                tcs.TrySetResult();
            }
        }

        _session.ConnectionStateChanged += Handler;
        return tcs.Task;
    }

    [Fact]
    public async Task UnexpectedDisconnect_EntersReconnectingStateImmediately()
    {
        await _session.ConnectAsync("COM5");

        _transport.SimulateDisconnect();

        Assert.Equal(ConnectionState.Reconnecting, _session.ConnectionState);

        await WaitForConnectionStateAsync(ConnectionState.Connected).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task SuccessfulReconnect_AfterTransientFailure_ReturnsToConnected()
    {
        await _session.ConnectAsync("COM5");
        _transport.ConnectFailuresRemaining = 1;

        _transport.SimulateDisconnect();
        await WaitForConnectionStateAsync(ConnectionState.Connected).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ConnectionState.Connected, _session.ConnectionState);
        Assert.True(_pollTimer.IsRunning);
    }

    [Fact]
    public async Task ExhaustedRetries_EndsDisconnectedWithLastError()
    {
        await _session.ConnectAsync("COM5");
        _transport.ConnectFailuresRemaining = 10;

        _transport.SimulateDisconnect();
        await WaitForConnectionStateAsync(ConnectionState.Disconnected).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ConnectionState.Disconnected, _session.ConnectionState);
        Assert.NotNull(_session.LastError);
    }

    [Fact]
    public async Task ManualDisconnect_UnsubscribesFromTransportDisconnectedEvent()
    {
        await _session.ConnectAsync("COM5");
        await _session.DisconnectAsync();

        _transport.SimulateDisconnect();

        Assert.Equal(ConnectionState.Disconnected, _session.ConnectionState);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter DeviceSessionReconnectTests`
Expected: FAIL — `DeviceSession` constructor does not accept an `IReconnectPolicy`, `LastError` does not exist.

- [ ] **Step 3: Modify `ArctZ/Services/Device/IDeviceSession.cs`**

Add alongside the existing `DeviceStatus` property:

```csharp
    DeviceStatus? DeviceStatus { get; }

    string? LastError { get; }
```

- [ ] **Step 4: Modify `ArctZ/Services/Device/DeviceSession.cs`**

Replace the whole file with:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public sealed class DeviceSession : IDeviceSession
{
    private readonly IDeviceTransport _transport;
    private readonly IBufferAwareCommandQueue _commandQueue;
    private readonly IStatusParser _statusParser;
    private readonly IJogScheduler _jogScheduler;
    private readonly IStatusPoller _statusPoller;
    private readonly IReconnectPolicy _reconnectPolicy;
    private string? _lastDeviceId;

    public DeviceSession(
        IDeviceTransport transport,
        IBufferAwareCommandQueue commandQueue,
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
    }

    public ConnectionState ConnectionState { get; private set; } = ConnectionState.Disconnected;

    public DeviceStatus? DeviceStatus { get; private set; }

    public string? LastError { get; private set; }

    public event Action? ConnectionStateChanged;

    public event Action? DeviceStatusChanged;

    public event Action<CommandRejectedEventArgs>? CommandRejected;

    public event Action<int>? AlarmTriggered;

    public async Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        _lastDeviceId = deviceId;
        SetConnectionState(ConnectionState.Connecting);

        _transport.LineReceived += OnLineReceived;
        _transport.Disconnected += OnTransportDisconnected;

        await _transport.ConnectAsync(deviceId, cancellationToken).ConfigureAwait(false);

        SetConnectionState(ConnectionState.Connected);
        _statusPoller.Start();
    }

    public async Task DisconnectAsync()
    {
        _statusPoller.Stop();
        _jogScheduler.Stop();

        _transport.Disconnected -= OnTransportDisconnected;
        await _transport.DisconnectAsync().ConfigureAwait(false);
        _transport.LineReceived -= OnLineReceived;

        SetConnectionState(ConnectionState.Disconnected);
    }

    public void BeginJog() => _jogScheduler.Start();

    public void UpdateJog(DualJoystickState state) => _jogScheduler.UpdateState(state);

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

    private async void OnTransportDisconnected()
    {
        _statusPoller.Stop();
        _jogScheduler.Stop();
        SetConnectionState(ConnectionState.Reconnecting);

        for (var attempt = 1; attempt <= _reconnectPolicy.MaxAttempts; attempt++)
        {
            await _reconnectPolicy.WaitBeforeRetryAsync(attempt).ConfigureAwait(false);

            try
            {
                await _transport.ConnectAsync(_lastDeviceId!).ConfigureAwait(false);
                LastError = null;
                SetConnectionState(ConnectionState.Connected);
                _statusPoller.Start();
                return;
            }
            catch
            {
                // try again
            }
        }

        LastError = $"Reconnect failed after {_reconnectPolicy.MaxAttempts} attempts";
        SetConnectionState(ConnectionState.Disconnected);
    }

    private void OnCommandCompleted(GCodeLineCommand command, CommandResult result)
    {
        if (result.Outcome is CommandOutcome.Rejected or CommandOutcome.Aborted)
        {
            CommandRejected?.Invoke(new CommandRejectedEventArgs(command, result.ErrorCode));
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
                if (report.Status.PlannerBlocksAvailable is { } planner && report.Status.RxBytesAvailable is { } rx)
                {
                    _commandQueue.UpdateBufferCapacity(rx, planner);
                }

                _jogScheduler.UpdateCurrentPose(report.Status.WPos);
                DeviceStatusChanged?.Invoke();
                break;
            case UnrecognizedLine:
                break;
        }
    }
}
```

Also update Task 14's `DeviceSessionTests` constructor call site — it now needs a 6th
constructor argument. Add this line to `ArctZ.Tests/Services/Device/DeviceSessionTests.cs`
right before the `_session = new DeviceSession(...)` line:

```csharp
        var reconnectPolicy = new FixedDelayReconnectPolicy(maxAttempts: 3, delay: TimeSpan.FromMilliseconds(1));
```

and change the `_session = new DeviceSession(...)` line to:

```csharp
        _session = new DeviceSession(_transport, _commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller, reconnectPolicy);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "DeviceSessionTests|DeviceSessionReconnectTests"`
Expected: PASS (9 + 4 = 13 tests).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Services/Device/IDeviceSession.cs ArctZ/Services/Device/DeviceSession.cs ArctZ.Tests/Services/Device/DeviceSessionTests.cs ArctZ.Tests/Services/Device/DeviceSessionReconnectTests.cs
git commit -m "feat: add reconnect-with-backoff to DeviceSession"
```

---

## Task 16: `MockDeviceTransport` — simulated controller for Demo mode

**Files:**
- Create: `ArctZ/Services/Device/Simulation/MockDeviceTransport.cs`
- Test: `ArctZ.Tests/Services/Device/MockDeviceTransportTests.cs`

**Interfaces:**
- Consumes: `IDeviceTransport` (Task 5), `MachineLimits`, `MachinePose` (Task 2), `IPeriodicTimer`/`ManualPeriodicTimer` (Task 10), `MachineState`, `DeviceStatus`, `FluidNcStatusParser` (Task 8, reused in the test to parse the mock's own status replies).
- Produces: `MockDeviceTransport : IDeviceTransport` with an additional test/demo hook `ForceNextCommandError(int code)` — registered in DI (Task 24) as a real, user-selectable transport, not just a test double (`FakeDeviceTransport` from Task 5 remains the test-only one).

Unlike `FakeDeviceTransport`, this is a working simulated FluidNC: it
tracks the same RX-byte/planner-block bookkeeping as a real controller
(so `BufferAwareCommandQueue`, Task 7, gets exercised identically against
Demo and real hardware), and a background tick (driven by an injected
`IPeriodicTimer`, real `SystemPeriodicTimer` in production) advances
position toward the last commanded target using the last commanded feed
— a timed simulation, not a physics engine, per spec. One pending line is
processed per tick (parsed, applied, acknowledged), which is what makes
"ok" arrive with buffer-occupancy-proportional delay instead of
instantly. `$H` resets to `MachinePose.Zero`, `$X` clears the alarm flag,
`G4 Pn` blocks motion (state `Run`) for `n` seconds without moving,
`0x85` cancels the current target immediately.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Simulation;

namespace ArctZ.Tests.Services.Device;

public class MockDeviceTransportTests
{
    private readonly ManualPeriodicTimer _ticker = new();
    private readonly MockDeviceTransport _mock;
    private readonly FluidNcStatusParser _parser = new();

    public MockDeviceTransportTests()
    {
        _mock = new MockDeviceTransport(MachineLimits.Default, _ticker, TimeSpan.FromMilliseconds(100));
    }

    private DeviceStatus QueryStatus()
    {
        StatusReportLine? report = null;
        void Handler(string line)
        {
            if (_parser.Parse(line) is StatusReportLine status)
            {
                report = status;
            }
        }

        _mock.LineReceived += Handler;
        _ = _mock.SendRawByteAsync((byte)'?');
        _mock.LineReceived -= Handler;

        return report!.Status;
    }

    [Fact]
    public async Task ConnectAsync_SetsIsConnectedAndStartsMotionTicker()
    {
        await _mock.ConnectAsync("demo");

        Assert.True(_mock.IsConnected);
        Assert.True(_ticker.IsRunning);
    }

    [Fact]
    public async Task SendRawByteAsync_StatusQuery_RepliesWithIdleAtOriginAndFullBuffer()
    {
        await _mock.ConnectAsync("demo");

        var status = QueryStatus();

        Assert.Equal(MachineState.Idle, status.State);
        Assert.Equal(MachinePose.Zero, status.WPos);
        Assert.Equal(15, status.PlannerBlocksAvailable);
        Assert.Equal(128, status.RxBytesAvailable);
    }

    [Fact]
    public async Task SendLineAsync_JogCommand_AcksThenMovesTowardTargetOverTicks()
    {
        await _mock.ConnectAsync("demo");
        string? firstReply = null;
        _mock.LineReceived += line => firstReply ??= line;

        await _mock.SendLineAsync("$J=G91 G21 X10 Y0 Z0 A0 F600");
        _ticker.RaiseElapsed(); // dequeues + acks; F600 units/min = 10/sec, tick=0.1s -> 1 unit/tick

        Assert.Equal("ok", firstReply);

        for (var i = 0; i < 20; i++)
        {
            _ticker.RaiseElapsed();
        }

        var status = QueryStatus();
        Assert.Equal(new MachinePose(10, 0, 0, 0), status.WPos);
        Assert.Equal(MachineState.Idle, status.State);
    }

    [Fact]
    public async Task SendRawByteAsync_JogCancel_StopsMotionImmediately()
    {
        await _mock.ConnectAsync("demo");
        await _mock.SendLineAsync("$J=G91 G21 X10 Y0 Z0 A0 F600");
        _ticker.RaiseElapsed(); // ack + first 1-unit step

        await _mock.SendRawByteAsync(0x85);
        var afterCancel = QueryStatus();

        _ticker.RaiseElapsed();
        _ticker.RaiseElapsed();
        var afterMoreTicks = QueryStatus();

        Assert.Equal(afterCancel.WPos, afterMoreTicks.WPos);
        Assert.Equal(MachineState.Idle, afterMoreTicks.State);
    }

    [Fact]
    public async Task SendLineAsync_Homing_ResetsPoseToZero()
    {
        await _mock.ConnectAsync("demo");
        await _mock.SendLineAsync("$J=G91 G21 X10 Y0 Z0 A0 F600");
        for (var i = 0; i < 21; i++)
        {
            _ticker.RaiseElapsed();
        }

        await _mock.SendLineAsync("$H");
        _ticker.RaiseElapsed();

        var status = QueryStatus();
        Assert.Equal(MachinePose.Zero, status.WPos);
    }

    [Fact]
    public async Task ForceNextCommandError_ReportsErrorInsteadOfOkAndSkipsEffect()
    {
        await _mock.ConnectAsync("demo");
        _mock.ForceNextCommandError(9);
        string? reply = null;
        _mock.LineReceived += line => reply ??= line;

        await _mock.SendLineAsync("$J=G91 G21 X10 Y0 Z0 A0 F600");
        _ticker.RaiseElapsed();

        Assert.Equal("error:9", reply);
        var status = QueryStatus();
        Assert.Equal(MachinePose.Zero, status.WPos);
    }

    [Fact]
    public async Task SendLineAsync_Dwell_BlocksMotionWithoutMovingUntilElapsed()
    {
        await _mock.ConnectAsync("demo");
        await _mock.SendLineAsync("G4 P1");
        _ticker.RaiseElapsed(); // ack + starts 1s dwell; this tick consumes 0.1s -> 0.9s remaining

        var duringDwell = QueryStatus();
        Assert.Equal(MachineState.Run, duringDwell.State);

        for (var i = 0; i < 9; i++)
        {
            _ticker.RaiseElapsed();
        }

        var afterDwell = QueryStatus();
        Assert.Equal(MachineState.Idle, afterDwell.State);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter MockDeviceTransportTests`
Expected: FAIL — `MockDeviceTransport` does not exist.

- [ ] **Step 3: Create `ArctZ/Services/Device/Simulation/MockDeviceTransport.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device.Simulation;

/// <summary>
/// A working IDeviceTransport that behaves like a real FluidNC controller:
/// same RX/planner buffer bookkeeping, same realtime-byte behavior, and a
/// timed (not physically accurate) simulation of motion so Demo mode is
/// usable without hardware.
/// </summary>
public sealed class MockDeviceTransport : IDeviceTransport
{
    private const int RxBufferCapacity = 128;
    private const int PlannerBlockCapacity = 15;

    private readonly MachineLimits _limits;
    private readonly IPeriodicTimer _motionTicker;
    private readonly TimeSpan _tickInterval;
    private readonly Queue<string> _pendingLines = new();

    private MachinePose _currentPose = MachinePose.Zero;
    private MachinePose? _targetPose;
    private double _feedUnitsPerMin = 1;
    private double _dwellSecondsRemaining;
    private bool _alarm;
    private int _rxBytesInFlight;
    private int? _forcedErrorForNextDequeue;

    public MockDeviceTransport(MachineLimits limits, IPeriodicTimer motionTicker, TimeSpan tickInterval)
    {
        _limits = limits;
        _motionTicker = motionTicker;
        _tickInterval = tickInterval;
        _motionTicker.Elapsed += OnTick;
    }

    public bool IsConnected { get; private set; }

    public event Action<string>? LineReceived;
    public event Action? Disconnected;

    /// <summary>Makes the next dequeued command report an error instead of ok, and skips its effect.</summary>
    public void ForceNextCommandError(int code) => _forcedErrorForNextDequeue = code;

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        IsConnected = true;
        _motionTicker.Start(_tickInterval);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        _motionTicker.Stop();
        return Task.CompletedTask;
    }

    public Task SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        _pendingLines.Enqueue(line);
        _rxBytesInFlight += line.Length + 1;
        return Task.CompletedTask;
    }

    public Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default)
    {
        switch (value)
        {
            case (byte)'?':
                LineReceived?.Invoke(FormatStatusLine());
                break;
            case 0x85: // jog cancel
                _targetPose = null;
                break;
        }

        return Task.CompletedTask;
    }

    private void OnTick()
    {
        ProcessOnePendingLine();
        AdvanceMotion();
    }

    private void ProcessOnePendingLine()
    {
        if (_pendingLines.Count == 0)
        {
            return;
        }

        var line = _pendingLines.Dequeue();
        _rxBytesInFlight -= line.Length + 1;

        if (_forcedErrorForNextDequeue is { } code)
        {
            _forcedErrorForNextDequeue = null;
            LineReceived?.Invoke($"error:{code}");
            return;
        }

        ApplyCommand(line);
        LineReceived?.Invoke("ok");
    }

    private void ApplyCommand(string line)
    {
        var trimmed = line.Trim();

        if (trimmed.Equals("$H", StringComparison.OrdinalIgnoreCase))
        {
            _currentPose = MachinePose.Zero;
            _targetPose = null;
            _alarm = false;
            return;
        }

        if (trimmed.Equals("$X", StringComparison.OrdinalIgnoreCase))
        {
            _alarm = false;
            return;
        }

        if (trimmed.StartsWith("G4", StringComparison.OrdinalIgnoreCase))
        {
            var tokens = ParseAxisTokens(trimmed);
            if (tokens.TryGetValue('P', out var seconds))
            {
                _dwellSecondsRemaining = seconds;
            }

            return;
        }

        if (trimmed.StartsWith("$J=", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("G0", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("G1", StringComparison.OrdinalIgnoreCase))
        {
            var tokens = ParseAxisTokens(trimmed);
            var isRelative = trimmed.Contains("G91", StringComparison.OrdinalIgnoreCase);

            var target = new MachinePose(
                X: tokens.TryGetValue('X', out var x) ? (isRelative ? _currentPose.X + x : x) : _currentPose.X,
                Y: tokens.TryGetValue('Y', out var y) ? (isRelative ? _currentPose.Y + y : y) : _currentPose.Y,
                Z: tokens.TryGetValue('Z', out var z) ? (isRelative ? _currentPose.Z + z : z) : _currentPose.Z,
                A: tokens.TryGetValue('A', out var a) ? (isRelative ? _currentPose.A + a : a) : _currentPose.A);

            _targetPose = _limits.Clamp(target);

            if (tokens.TryGetValue('F', out var feed) && feed > 0)
            {
                _feedUnitsPerMin = feed;
            }
        }
    }

    private void AdvanceMotion()
    {
        var elapsedSeconds = _tickInterval.TotalSeconds;

        if (_dwellSecondsRemaining > 0)
        {
            _dwellSecondsRemaining = Math.Max(0, _dwellSecondsRemaining - elapsedSeconds);
            return;
        }

        if (_targetPose is not { } target || target == _currentPose)
        {
            return;
        }

        var stepPerAxis = _feedUnitsPerMin / 60.0 * elapsedSeconds;

        _currentPose = new MachinePose(
            X: StepToward(_currentPose.X, target.X, stepPerAxis),
            Y: StepToward(_currentPose.Y, target.Y, stepPerAxis),
            Z: StepToward(_currentPose.Z, target.Z, stepPerAxis),
            A: StepToward(_currentPose.A, target.A, stepPerAxis));

        if (_currentPose == target)
        {
            _targetPose = null;
        }
    }

    private static double StepToward(double current, double target, double maxStep)
    {
        var diff = target - current;
        return Math.Abs(diff) <= maxStep ? target : current + Math.Sign(diff) * maxStep;
    }

    private string FormatStatusLine()
    {
        var state = CurrentState();
        var plannerAvailable = Math.Max(0, PlannerBlockCapacity - _pendingLines.Count);
        var rxAvailable = Math.Max(0, RxBufferCapacity - _rxBytesInFlight);

        return FormattableString.Invariant(
            $"<{state}|WPos:{_currentPose.X:0.000},{_currentPose.Y:0.000},{_currentPose.Z:0.000},{_currentPose.A:0.000}|Bf:{plannerAvailable},{rxAvailable}|FS:{_feedUnitsPerMin:0},0>");
    }

    private MachineState CurrentState()
    {
        if (_alarm)
        {
            return MachineState.Alarm;
        }

        if (_dwellSecondsRemaining > 0 || (_targetPose is { } target && target != _currentPose))
        {
            return MachineState.Run;
        }

        return MachineState.Idle;
    }

    private static Dictionary<char, double> ParseAxisTokens(string line)
    {
        var result = new Dictionary<char, double>();
        foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var letter = char.ToUpperInvariant(token[0]);
            if (letter is 'X' or 'Y' or 'Z' or 'A' or 'F' or 'P' &&
                double.TryParse(token.AsSpan(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                result[letter] = value;
            }
        }

        return result;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter MockDeviceTransportTests`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Services/Device/Simulation/MockDeviceTransport.cs ArctZ.Tests/Services/Device/MockDeviceTransportTests.cs
git commit -m "feat: add simulated FluidNC controller for demo mode"
```

---

## Task 17: `IDeviceSessionFactory` + `ConnectionViewModel` + DI composition root

**Files:**
- Create: `ArctZ/Services/Device/IDeviceSessionFactory.cs`
- Create: `ArctZ/Services/Device/DeviceSessionFactory.cs`
- Create: `ArctZ/Services/Device/ServiceCollectionExtensions.cs`
- Create: `ArctZ/ViewModels/ConnectionEndpoint.cs`
- Create: `ArctZ/ViewModels/ConnectionViewModel.cs`
- Create: `ArctZ/Views/ConnectionView.axaml`
- Create: `ArctZ/Views/ConnectionView.axaml.cs`
- Test: `ArctZ.Tests/Services/Device/DeviceSessionFactoryTests.cs`
- Test: `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs`
- Modify: `Directory.Packages.props`, `ArctZ/ArctZ.csproj` (add `Microsoft.Extensions.DependencyInjection` package reference)

**Interfaces:**
- Consumes: everything from Tasks 2–16 (`IDeviceTransport`, `MachineLimits`, `BufferAwareCommandQueue`, `FluidNcCommandSerializer`, `RealtimeCommandChannel`, `JogCommandFactory`, `JogScheduler`, `StatusPoller`, `FluidNcStatusParser`, `FixedDelayReconnectPolicy`, `SystemPeriodicTimer`, `DeviceSession`, `MockDeviceTransport`).
- Produces: `IDeviceSessionFactory.Create(IDeviceTransport) : IDeviceSession`; `DeviceSessionFactory : IDeviceSessionFactory` — the one place that wires the whole device stack together (production timers, the spec's 3-attempt/200ms reconnect policy); `ConnectionEndpointKind` enum (`RealDevice`, `Demo`); `ConnectionEndpoint(string Id, string DisplayName, ConnectionEndpointKind Kind)`; `ConnectionViewModel` (`AvailableEndpoints`, `Session`, `SelectedEndpoint`, `ConnectCommand`, `DisconnectCommand`, `HomeCommand`, `ResetAlarmCommand`) — used by `ProgramViewModel` (Task 21).

Per spec, Demo must be selectable everywhere, not auto-fallback — a real
device and the simulated controller are just two different
`IDeviceTransport` instances the user picks between before connecting.
`DeviceSessionFactory` builds a fresh, fully-wired `DeviceSession` around
whichever transport is chosen (each connect attempt gets its own
`JogScheduler`/`StatusPoller`/timers, so reconnecting or switching
endpoints never reuses stale state). Only the *real* `IDeviceTransport` is
platform-specific and comes from DI; the Demo transport is created fresh
per connection attempt via an injected `Func<IDeviceTransport>` factory
delegate (`MockDeviceTransport` carries per-connection mutable state, so
a new instance per attempt avoids leaking state between demo sessions).

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class DeviceSessionFactoryTests
{
    [Fact]
    public void Create_ReturnsSessionBoundToGivenTransport()
    {
        var transport = new FakeDeviceTransport();
        var factory = new DeviceSessionFactory(MachineLimits.Default);

        var session = factory.Create(transport);

        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
    }
}
```

```csharp
using System.Linq;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Tests.Services.Device;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class ConnectionViewModelTests
{
    [Fact]
    public void Constructor_DefaultsToFirstEndpointAndListsRealAndDemo()
    {
        var vm = new ConnectionViewModel(new FakeDeviceTransport(), () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default));

        Assert.Equal(2, vm.AvailableEndpoints.Count);
        Assert.Contains(vm.AvailableEndpoints, e => e.Kind == ConnectionEndpointKind.RealDevice);
        Assert.Contains(vm.AvailableEndpoints, e => e.Kind == ConnectionEndpointKind.Demo);
        Assert.Equal(ConnectionEndpointKind.RealDevice, vm.SelectedEndpoint!.Kind);
    }

    [Fact]
    public async Task ConnectCommand_DemoSelected_ConnectsUsingDemoTransportNotRealTransport()
    {
        var realTransport = new FakeDeviceTransport();
        var demoTransport = new FakeDeviceTransport();
        var vm = new ConnectionViewModel(realTransport, () => demoTransport, new DeviceSessionFactory(MachineLimits.Default));
        vm.SelectedEndpoint = vm.AvailableEndpoints.Single(e => e.Kind == ConnectionEndpointKind.Demo);

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.True(demoTransport.IsConnected);
        Assert.False(realTransport.IsConnected);
        Assert.Equal(ConnectionState.Connected, vm.Session!.ConnectionState);
    }

    [Fact]
    public async Task ConnectCommand_RealDeviceSelected_ConnectsUsingRealTransport()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = new ConnectionViewModel(realTransport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default));

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.True(realTransport.IsConnected);
    }

    [Fact]
    public async Task DisconnectCommand_DisconnectsActiveSession()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = new ConnectionViewModel(realTransport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default));
        await vm.ConnectCommand.ExecuteAsync(null);

        await vm.DisconnectCommand.ExecuteAsync(null);

        Assert.False(realTransport.IsConnected);
        Assert.Equal(ConnectionState.Disconnected, vm.Session!.ConnectionState);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "DeviceSessionFactoryTests|ConnectionViewModelTests"`
Expected: FAIL — `DeviceSessionFactory`/`ConnectionViewModel` do not exist.

- [ ] **Step 3: Add DI package to `Directory.Packages.props` and `ArctZ/ArctZ.csproj`**

`Directory.Packages.props` should already have the `Microsoft.Extensions.DependencyInjection`
`PackageVersion` entry from Task 1. Add the reference in `ArctZ/ArctZ.csproj`'s
`<ItemGroup>` containing the other `PackageReference` entries:

```xml
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
```

- [ ] **Step 4: Create `ArctZ/Services/Device/IDeviceSessionFactory.cs`**

```csharp
namespace ArctZ.Services.Device;

public interface IDeviceSessionFactory
{
    IDeviceSession Create(IDeviceTransport transport);
}
```

- [ ] **Step 5: Create `ArctZ/Services/Device/DeviceSessionFactory.cs`**

```csharp
using System;

namespace ArctZ.Services.Device;

/// <summary>
/// Builds a fully-wired DeviceSession around a caller-supplied transport
/// (real or MockDeviceTransport). Each call gets its own timers/scheduler/
/// poller so switching endpoints never reuses stale state.
/// </summary>
public sealed class DeviceSessionFactory : IDeviceSessionFactory
{
    private static readonly TimeSpan JogInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromMilliseconds(200);
    private const int ReconnectMaxAttempts = 3;

    private readonly MachineLimits _limits;

    public DeviceSessionFactory(MachineLimits limits)
    {
        _limits = limits;
    }

    public IDeviceSession Create(IDeviceTransport transport)
    {
        var serializer = new FluidNcCommandSerializer();
        var realtimeChannel = new RealtimeCommandChannel(transport);
        var commandQueue = new BufferAwareCommandQueue(transport);
        var jogScheduler = new JogScheduler(
            new JogCommandFactory(_limits),
            serializer,
            transport,
            realtimeChannel,
            new SystemPeriodicTimer(),
            JogInterval);
        var statusPoller = new StatusPoller(realtimeChannel, new SystemPeriodicTimer(), StatusPollInterval);
        var reconnectPolicy = new FixedDelayReconnectPolicy(ReconnectMaxAttempts, ReconnectDelay);

        return new DeviceSession(transport, commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller, reconnectPolicy);
    }
}
```

- [ ] **Step 6: Create `ArctZ/ViewModels/ConnectionEndpoint.cs`**

```csharp
namespace ArctZ.ViewModels;

public enum ConnectionEndpointKind
{
    RealDevice,
    Demo
}

public sealed record ConnectionEndpoint(string Id, string DisplayName, ConnectionEndpointKind Kind);
```

- [ ] **Step 7: Create `ArctZ/ViewModels/ConnectionViewModel.cs`**

```csharp
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArctZ.ViewModels;

public partial class ConnectionViewModel : ViewModelBase
{
    private readonly IDeviceTransport _realTransport;
    private readonly Func<IDeviceTransport> _createDemoTransport;
    private readonly IDeviceSessionFactory _sessionFactory;

    [ObservableProperty]
    private IDeviceSession? _session;

    [ObservableProperty]
    private ConnectionEndpoint? _selectedEndpoint;

    public ObservableCollection<ConnectionEndpoint> AvailableEndpoints { get; } = new()
    {
        new ConnectionEndpoint("real", "Устройство", ConnectionEndpointKind.RealDevice),
        new ConnectionEndpoint("demo", "Демо", ConnectionEndpointKind.Demo),
    };

    public ConnectionViewModel(
        IDeviceTransport realTransport,
        Func<IDeviceTransport> createDemoTransport,
        IDeviceSessionFactory sessionFactory)
    {
        _realTransport = realTransport;
        _createDemoTransport = createDemoTransport;
        _sessionFactory = sessionFactory;
        SelectedEndpoint = AvailableEndpoints[0];
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (SelectedEndpoint is null)
        {
            return;
        }

        var transport = SelectedEndpoint.Kind == ConnectionEndpointKind.Demo
            ? _createDemoTransport()
            : _realTransport;

        Session = _sessionFactory.Create(transport);
        await Session.ConnectAsync(SelectedEndpoint.Id);
    }

    [RelayCommand]
    private Task DisconnectAsync() => Session?.DisconnectAsync() ?? Task.CompletedTask;

    [RelayCommand]
    private Task HomeAsync() => Session?.HomeAsync() ?? Task.CompletedTask;

    [RelayCommand]
    private Task ResetAlarmAsync() => Session?.ResetAlarmAsync() ?? Task.CompletedTask;
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "DeviceSessionFactoryTests|ConnectionViewModelTests"`
Expected: PASS (1 + 4 = 5 tests).

- [ ] **Step 9: Create `ArctZ/Services/Device/ServiceCollectionExtensions.cs`**

```csharp
using System;
using Microsoft.Extensions.DependencyInjection;

namespace ArctZ.Services.Device;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything platform-independent. Each head additionally
    /// registers its own IDeviceTransport for the real device (see Task 24)
    /// — this method deliberately does not touch that registration.
    /// </summary>
    public static IServiceCollection AddArctZCore(this IServiceCollection services)
    {
        services.AddSingleton(MachineLimits.Default);
        services.AddSingleton<IDeviceSessionFactory, DeviceSessionFactory>();
        services.AddSingleton<Func<IDeviceTransport>>(sp => () => new Simulation.MockDeviceTransport(
            sp.GetRequiredService<MachineLimits>(),
            new SystemPeriodicTimer(),
            TimeSpan.FromMilliseconds(100)));
        services.AddTransient<ConnectionViewModel>();
        return services;
    }
}
```

- [ ] **Step 10: Create `ArctZ/Views/ConnectionView.axaml`**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:ArctZ.ViewModels"
             x:Class="ArctZ.Views.ConnectionView"
             x:DataType="vm:ConnectionViewModel">
    <StackPanel Orientation="Horizontal" Spacing="8" Margin="8">
        <ComboBox ItemsSource="{Binding AvailableEndpoints}"
                  SelectedItem="{Binding SelectedEndpoint}"
                  DisplayMemberBinding="{Binding DisplayName}" />
        <Button Content="Подключить" Command="{Binding ConnectCommand}" />
        <Button Content="Отключить" Command="{Binding DisconnectCommand}" />
        <Button Content="Homing" Command="{Binding HomeCommand}" />
        <Button Content="Сброс аварии" Command="{Binding ResetAlarmCommand}" />
        <TextBlock Text="{Binding Session.ConnectionState}" VerticalAlignment="Center" />
    </StackPanel>
</UserControl>
```

- [ ] **Step 11: Create `ArctZ/Views/ConnectionView.axaml.cs`**

```csharp
using Avalonia.Controls;

namespace ArctZ.Views;

public partial class ConnectionView : UserControl
{
    public ConnectionView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 12: Verify the solution builds**

Run: `dotnet build ArctZ.slnx`
Expected: build succeeds (Avalonia XAML compiler generates `InitializeComponent`).

- [ ] **Step 13: Commit**

```bash
git add Directory.Packages.props ArctZ/ArctZ.csproj ArctZ/Services/Device/IDeviceSessionFactory.cs ArctZ/Services/Device/DeviceSessionFactory.cs ArctZ/Services/Device/ServiceCollectionExtensions.cs ArctZ/ViewModels/ConnectionEndpoint.cs ArctZ/ViewModels/ConnectionViewModel.cs ArctZ/Views/ConnectionView.axaml ArctZ/Views/ConnectionView.axaml.cs ArctZ.Tests/Services/Device/DeviceSessionFactoryTests.cs ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs
git commit -m "feat: add connection view-model with selectable real/demo endpoints"
```

---

## Task 18: Program domain — `Waypoint`, `TransitionSettings`, `JibProgram`, `ProgramSegment`

**Files:**
- Create: `ArctZ/Services/Program/Waypoint.cs`
- Create: `ArctZ/Services/Program/EaseMode.cs`
- Create: `ArctZ/Services/Program/TransitionSettings.cs`
- Create: `ArctZ/Services/Program/ProgramSegment.cs`
- Create: `ArctZ/Services/Program/JibProgram.cs`
- Test: `ArctZ.Tests/Services/Program/TransitionSettingsTests.cs`
- Test: `ArctZ.Tests/Services/Program/JibProgramTests.cs`

**Interfaces:**
- Consumes: `MachinePose` (Task 2).
- Produces: `Waypoint(Guid Id, string? Label, MachinePose Pose)`; `EaseMode` enum (`None`, `EaseInOut`); `TransitionSettings(double FeedRateUnitsPerMin, double DwellSeconds, EaseMode Ease, bool ContinuousBlend)` with computed `bool StopsAtWaypoint`; `ProgramSegment(int Index, Waypoint From, Waypoint To, TransitionSettings Transition)`; `JibProgram` (`Id`, `Name`, `Waypoints: List<Waypoint>`, `Transitions: List<TransitionSettings>`, `Segments() : IEnumerable<ProgramSegment>`) — used by `ITrajectoryCompiler` (Task 19), `IProgramStorage` (Task 20), and `ProgramViewModel` (Tasks 21, 22).

`StopsAtWaypoint` is the single source of truth for "does this segment end
in a full stop": `DwellSeconds > 0` always forces a stop (a pause requires
one) regardless of `ContinuousBlend`. `Segments()` zips `Waypoints` and
`Transitions` defensively — it stops at `min(Waypoints.Count - 1,
Transitions.Count)` rather than throwing, so a program mid-edit (a
waypoint just added, its transition not yet configured) never crashes the
UI that's rendering it.

- [ ] **Step 1: Write the failing tests**

```csharp
using ArctZ.Services.Program;

namespace ArctZ.Tests.Services.Program;

public class TransitionSettingsTests
{
    [Fact]
    public void StopsAtWaypoint_NotContinuousBlend_IsTrue()
    {
        var transition = new TransitionSettings(FeedRateUnitsPerMin: 500, DwellSeconds: 0, EaseMode.None, ContinuousBlend: false);

        Assert.True(transition.StopsAtWaypoint);
    }

    [Fact]
    public void StopsAtWaypoint_ContinuousBlendButPositiveDwell_IsTrue()
    {
        var transition = new TransitionSettings(FeedRateUnitsPerMin: 500, DwellSeconds: 2, EaseMode.None, ContinuousBlend: true);

        Assert.True(transition.StopsAtWaypoint);
    }

    [Fact]
    public void StopsAtWaypoint_ContinuousBlendAndNoDwell_IsFalse()
    {
        var transition = new TransitionSettings(FeedRateUnitsPerMin: 500, DwellSeconds: 0, EaseMode.None, ContinuousBlend: true);

        Assert.False(transition.StopsAtWaypoint);
    }
}
```

```csharp
using System;
using System.Linq;
using ArctZ.Services.Device;
using ArctZ.Services.Program;

namespace ArctZ.Tests.Services.Program;

public class JibProgramTests
{
    private static TransitionSettings DefaultTransition => new(500, 0, EaseMode.None, ContinuousBlend: false);

    [Fact]
    public void Segments_ZipsWaypointsAndTransitionsInOrder()
    {
        var program = new JibProgram();
        var a = new Waypoint(Guid.NewGuid(), "A", new MachinePose(0, 0, 0, 0));
        var b = new Waypoint(Guid.NewGuid(), "B", new MachinePose(10, 0, 0, 0));
        var c = new Waypoint(Guid.NewGuid(), "C", new MachinePose(20, 0, 0, 0));
        program.Waypoints.AddRange(new[] { a, b, c });
        program.Transitions.AddRange(new[] { DefaultTransition, DefaultTransition });

        var segments = program.Segments().ToList();

        Assert.Equal(2, segments.Count);
        Assert.Equal((0, a, b), (segments[0].Index, segments[0].From, segments[0].To));
        Assert.Equal((1, b, c), (segments[1].Index, segments[1].From, segments[1].To));
    }

    [Fact]
    public void Segments_FewerThanTwoWaypoints_IsEmpty()
    {
        var program = new JibProgram();
        program.Waypoints.Add(new Waypoint(Guid.NewGuid(), "A", MachinePose.Zero));

        Assert.Empty(program.Segments());
    }

    [Fact]
    public void Segments_WaypointAddedWithoutMatchingTransition_StopsBeforeIt()
    {
        var program = new JibProgram();
        var a = new Waypoint(Guid.NewGuid(), "A", MachinePose.Zero);
        var b = new Waypoint(Guid.NewGuid(), "B", new MachinePose(10, 0, 0, 0));
        var c = new Waypoint(Guid.NewGuid(), "C", new MachinePose(20, 0, 0, 0));
        program.Waypoints.AddRange(new[] { a, b, c });
        program.Transitions.Add(DefaultTransition); // only one transition for 2 segments

        var segments = program.Segments().ToList();

        Assert.Single(segments);
        Assert.Equal(a, segments[0].From);
        Assert.Equal(b, segments[0].To);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "TransitionSettingsTests|JibProgramTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Create `ArctZ/Services/Program/Waypoint.cs`**

```csharp
using System;
using ArctZ.Services.Device;

namespace ArctZ.Services.Program;

public sealed record Waypoint(Guid Id, string? Label, MachinePose Pose);
```

- [ ] **Step 4: Create `ArctZ/Services/Program/EaseMode.cs`**

```csharp
namespace ArctZ.Services.Program;

public enum EaseMode
{
    None,
    EaseInOut
}
```

- [ ] **Step 5: Create `ArctZ/Services/Program/TransitionSettings.cs`**

```csharp
namespace ArctZ.Services.Program;

public sealed record TransitionSettings(
    double FeedRateUnitsPerMin,
    double DwellSeconds,
    EaseMode Ease,
    bool ContinuousBlend)
{
    /// <summary>A dwell always forces a stop, regardless of ContinuousBlend.</summary>
    public bool StopsAtWaypoint => !ContinuousBlend || DwellSeconds > 0;
}
```

- [ ] **Step 6: Create `ArctZ/Services/Program/ProgramSegment.cs`**

```csharp
namespace ArctZ.Services.Program;

public sealed record ProgramSegment(int Index, Waypoint From, Waypoint To, TransitionSettings Transition);
```

- [ ] **Step 7: Create `ArctZ/Services/Program/JibProgram.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace ArctZ.Services.Program;

public sealed class JibProgram
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = "Новая программа";

    public List<Waypoint> Waypoints { get; } = new();

    /// <summary>Transitions[i] describes the move from Waypoints[i] to Waypoints[i+1].</summary>
    public List<TransitionSettings> Transitions { get; } = new();

    public IEnumerable<ProgramSegment> Segments()
    {
        var count = Math.Min(Waypoints.Count - 1, Transitions.Count);
        for (var i = 0; i < count; i++)
        {
            yield return new ProgramSegment(i, Waypoints[i], Waypoints[i + 1], Transitions[i]);
        }
    }
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "TransitionSettingsTests|JibProgramTests"`
Expected: PASS (3 + 3 = 6 tests).

- [ ] **Step 9: Commit**

```bash
git add ArctZ/Services/Program/Waypoint.cs ArctZ/Services/Program/EaseMode.cs ArctZ/Services/Program/TransitionSettings.cs ArctZ/Services/Program/ProgramSegment.cs ArctZ/Services/Program/JibProgram.cs ArctZ.Tests/Services/Program/TransitionSettingsTests.cs ArctZ.Tests/Services/Program/JibProgramTests.cs
git commit -m "feat: add waypoint program domain model"
```

---

## Task 19: `ITrajectoryCompiler` / `TrajectoryCompiler`

**Files:**
- Create: `ArctZ/Services/Program/CompiledStep.cs`
- Create: `ArctZ/Services/Program/ITrajectoryCompiler.cs`
- Create: `ArctZ/Services/Program/TrajectoryCompiler.cs`
- Test: `ArctZ.Tests/Services/Program/TrajectoryCompilerTests.cs`

**Interfaces:**
- Consumes: `JibProgram`, `ProgramSegment`, `TransitionSettings`, `EaseMode` (Task 18), `MachinePose` (Task 2), `GCodeLineCommand` (Task 3).
- Produces: `CompiledStep(int SegmentIndex, IDeviceCommand Command, double SegmentProgress)`; `ITrajectoryCompiler.Compile(JibProgram) : IReadOnlyList<CompiledStep>`; `TrajectoryCompiler : ITrajectoryCompiler` — consumed by `ProgramViewModel`'s Playback mode (Task 22), which feeds each `CompiledStep.Command` (always a `GCodeLineCommand` here) into `IBufferAwareCommandQueue.EnqueueAsync` and uses `SegmentIndex`/`SegmentProgress` to drive the progress UI.

Per spec: `EaseMode.None` compiles a segment to one absolute `G1` line at
`Transition.FeedRateUnitsPerMin`. `EaseMode.EaseInOut` subdivides the
segment into `EaseSubdivisions` (6) linearly-interpolated `G1` lines whose
feed ramps 0.3×→1.0×→0.3× target across three equal thirds (accelerate /
cruise / decelerate) — an approximation tunable later, not exact physics.
Whenever `Transition.StopsAtWaypoint` is true, one more `G4 P<seconds>`
step is appended after the motion step(s), also at `SegmentProgress =
1.0`. When it's false, nothing is appended and the segment intentionally
ends with the controller's planner buffer still fed — see
`BufferAwareCommandQueue` (Task 7) for why that is what makes the
transition blend through the waypoint instead of stopping.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Globalization;
using System.Linq;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;
using ArctZ.Services.Program;

namespace ArctZ.Tests.Services.Program;

public class TrajectoryCompilerTests
{
    private readonly TrajectoryCompiler _compiler = new();

    private static JibProgram SingleSegmentProgram(TransitionSettings transition)
    {
        var program = new JibProgram();
        program.Waypoints.Add(new Waypoint(Guid.NewGuid(), "A", MachinePose.Zero));
        program.Waypoints.Add(new Waypoint(Guid.NewGuid(), "B", new MachinePose(60, 0, 0, 0)));
        program.Transitions.Add(transition);
        return program;
    }

    private static double ParseFeed(string line)
    {
        var token = line.Split(' ').Single(t => t.StartsWith("F", StringComparison.Ordinal));
        return double.Parse(token[1..], CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Compile_NoEase_ProducesSingleG1StepAtFullProgress()
    {
        var transition = new TransitionSettings(FeedRateUnitsPerMin: 1000, DwellSeconds: 0, EaseMode.None, ContinuousBlend: false);
        var program = SingleSegmentProgram(transition);

        var steps = _compiler.Compile(program);

        var motionSteps = steps.Where(s => ((GCodeLineCommand)s.Command).Line.StartsWith("G1", StringComparison.Ordinal)).ToList();
        Assert.Single(motionSteps);
        Assert.Equal("G1 X60 Y0 Z0 A0 F1000", ((GCodeLineCommand)motionSteps[0].Command).Line);
        Assert.Equal(1.0, motionSteps[0].SegmentProgress);
    }

    [Fact]
    public void Compile_EaseInOut_ProducesSixSubstepsWithRampedFeedAndIncreasingProgress()
    {
        var transition = new TransitionSettings(FeedRateUnitsPerMin: 1000, DwellSeconds: 0, EaseMode.EaseInOut, ContinuousBlend: false);
        var program = SingleSegmentProgram(transition);

        var steps = _compiler.Compile(program);
        var motionSteps = steps.Where(s => ((GCodeLineCommand)s.Command).Line.StartsWith("G1", StringComparison.Ordinal)).ToList();

        Assert.Equal(6, motionSteps.Count);

        var roundedFeeds = motionSteps.Select(s => Math.Round(ParseFeed(((GCodeLineCommand)s.Command).Line))).ToArray();
        Assert.Equal(new[] { 650.0, 1000.0, 1000.0, 1000.0, 650.0, 300.0 }, roundedFeeds);

        var roundedProgress = motionSteps.Select(s => Math.Round(s.SegmentProgress, 3)).ToArray();
        Assert.Equal(new[] { 0.167, 0.333, 0.5, 0.667, 0.833, 1.0 }, roundedProgress);

        Assert.Equal("G1 X60 Y0 Z0 A0 F300", ((GCodeLineCommand)motionSteps[5].Command).Line);
    }

    [Fact]
    public void Compile_DwellPositive_AppendsG4AfterMotionAtFullProgress()
    {
        var transition = new TransitionSettings(FeedRateUnitsPerMin: 1000, DwellSeconds: 2.5, EaseMode.None, ContinuousBlend: true);
        var program = SingleSegmentProgram(transition);

        var steps = _compiler.Compile(program);

        Assert.Equal(2, steps.Count);
        var dwellStep = steps[1];
        Assert.Equal("G4 P2.5", ((GCodeLineCommand)dwellStep.Command).Line);
        Assert.Equal(1.0, dwellStep.SegmentProgress);
        Assert.Equal(0, dwellStep.SegmentIndex);
    }

    [Fact]
    public void Compile_ContinuousBlendNoDwell_DoesNotAppendDwell()
    {
        var transition = new TransitionSettings(FeedRateUnitsPerMin: 1000, DwellSeconds: 0, EaseMode.None, ContinuousBlend: true);
        var program = SingleSegmentProgram(transition);

        var steps = _compiler.Compile(program);

        Assert.Single(steps);
        Assert.DoesNotContain(steps, s => ((GCodeLineCommand)s.Command).Line.StartsWith("G4", StringComparison.Ordinal));
    }

    [Fact]
    public void Compile_MultipleSegments_AssignsCorrectSegmentIndexToEachStep()
    {
        var program = new JibProgram();
        program.Waypoints.Add(new Waypoint(Guid.NewGuid(), "A", MachinePose.Zero));
        program.Waypoints.Add(new Waypoint(Guid.NewGuid(), "B", new MachinePose(10, 0, 0, 0)));
        program.Waypoints.Add(new Waypoint(Guid.NewGuid(), "C", new MachinePose(20, 0, 0, 0)));
        var transition = new TransitionSettings(FeedRateUnitsPerMin: 500, DwellSeconds: 0, EaseMode.None, ContinuousBlend: false);
        program.Transitions.Add(transition);
        program.Transitions.Add(transition);

        var steps = _compiler.Compile(program);

        Assert.Equal(4, steps.Count); // 2 segments x (1 G1 + 1 G4, since ContinuousBlend=false)
        Assert.All(steps.Take(2), s => Assert.Equal(0, s.SegmentIndex));
        Assert.All(steps.Skip(2), s => Assert.Equal(1, s.SegmentIndex));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter TrajectoryCompilerTests`
Expected: FAIL — `TrajectoryCompiler` does not exist.

- [ ] **Step 3: Create `ArctZ/Services/Program/CompiledStep.cs`**

```csharp
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Program;

public sealed record CompiledStep(int SegmentIndex, IDeviceCommand Command, double SegmentProgress);
```

- [ ] **Step 4: Create `ArctZ/Services/Program/ITrajectoryCompiler.cs`**

```csharp
using System.Collections.Generic;

namespace ArctZ.Services.Program;

public interface ITrajectoryCompiler
{
    IReadOnlyList<CompiledStep> Compile(JibProgram program);
}
```

- [ ] **Step 5: Create `ArctZ/Services/Program/TrajectoryCompiler.cs`**

```csharp
using System.Collections.Generic;
using System.Globalization;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Program;

public sealed class TrajectoryCompiler : ITrajectoryCompiler
{
    private const int EaseSubdivisions = 6;
    private const double MinFeedFraction = 0.3;

    public IReadOnlyList<CompiledStep> Compile(JibProgram program)
    {
        var steps = new List<CompiledStep>();

        foreach (var segment in program.Segments())
        {
            if (segment.Transition.Ease == EaseMode.EaseInOut)
            {
                CompileEased(segment, steps);
            }
            else
            {
                var command = MoveCommand(segment.To.Pose, segment.Transition.FeedRateUnitsPerMin);
                steps.Add(new CompiledStep(segment.Index, command, SegmentProgress: 1.0));
            }

            if (segment.Transition.StopsAtWaypoint)
            {
                var dwellLine = $"G4 P{Format(segment.Transition.DwellSeconds)}";
                steps.Add(new CompiledStep(segment.Index, new GCodeLineCommand(dwellLine), SegmentProgress: 1.0));
            }
        }

        return steps;
    }

    private static void CompileEased(ProgramSegment segment, List<CompiledStep> steps)
    {
        for (var i = 1; i <= EaseSubdivisions; i++)
        {
            var t = (double)i / EaseSubdivisions;
            var pose = Interpolate(segment.From.Pose, segment.To.Pose, t);
            var feed = FeedMultiplier(t) * segment.Transition.FeedRateUnitsPerMin;
            steps.Add(new CompiledStep(segment.Index, MoveCommand(pose, feed), SegmentProgress: t));
        }
    }

    /// <summary>Piecewise-linear ramp: 0.3x -> 1.0x over the first third, cruise at 1.0x, 1.0x -> 0.3x over the last third.</summary>
    private static double FeedMultiplier(double t)
    {
        if (t <= 1.0 / 3)
        {
            return MinFeedFraction + (1 - MinFeedFraction) * (t / (1.0 / 3));
        }

        if (t <= 2.0 / 3)
        {
            return 1.0;
        }

        var local = (t - 2.0 / 3) / (1.0 / 3);
        return 1.0 - (1 - MinFeedFraction) * local;
    }

    private static MachinePose Interpolate(MachinePose from, MachinePose to, double t) => new(
        X: from.X + (to.X - from.X) * t,
        Y: from.Y + (to.Y - from.Y) * t,
        Z: from.Z + (to.Z - from.Z) * t,
        A: from.A + (to.A - from.A) * t);

    private static GCodeLineCommand MoveCommand(MachinePose pose, double feed) => new(
        $"G1 X{Format(pose.X)} Y{Format(pose.Y)} Z{Format(pose.Z)} A{Format(pose.A)} F{Format(feed)}");

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter TrajectoryCompilerTests`
Expected: PASS (5 tests).

- [ ] **Step 7: Commit**

```bash
git add ArctZ/Services/Program/CompiledStep.cs ArctZ/Services/Program/ITrajectoryCompiler.cs ArctZ/Services/Program/TrajectoryCompiler.cs ArctZ.Tests/Services/Program/TrajectoryCompilerTests.cs
git commit -m "feat: add waypoint program trajectory compiler"
```

---

## Task 20: `IProgramStorage` / `JsonFileProgramStorage`

**Files:**
- Create: `ArctZ/Services/Program/ProgramSummary.cs`
- Create: `ArctZ/Services/Program/IProgramStorage.cs`
- Create: `ArctZ/Services/Program/JsonFileProgramStorage.cs`
- Test: `ArctZ.Tests/Services/Program/JsonFileProgramStorageTests.cs`

**Interfaces:**
- Consumes: `JibProgram`, `Waypoint`, `TransitionSettings`, `EaseMode` (Task 18), `MachinePose` (Task 2).
- Produces: `ProgramSummary(Guid Id, string Name, DateTimeOffset ModifiedAt)`; `IProgramStorage` (`ListAsync`, `LoadAsync`, `SaveAsync`, `DeleteAsync`); `JsonFileProgramStorage : IProgramStorage` — used by `ProgramViewModel` (Tasks 21, 22).

One JSON file per program (`{id}.json`) in a directory supplied at
construction — per spec, Desktop/Android/iOS get a real filesystem
directory (wired in Task 24); Browser's storage backend is an explicit
open question in the spec and not implemented here.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Program;

namespace ArctZ.Tests.Services.Program;

public class JsonFileProgramStorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ArctZTests_" + Guid.NewGuid());
    private readonly JsonFileProgramStorage _storage;

    public JsonFileProgramStorageTests()
    {
        _storage = new JsonFileProgramStorage(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static JibProgram SampleProgram(string name)
    {
        var program = new JibProgram { Name = name };
        program.Waypoints.Add(new Waypoint(Guid.NewGuid(), "A", new MachinePose(1, 2, 3, 4)));
        program.Waypoints.Add(new Waypoint(Guid.NewGuid(), "B", new MachinePose(5, 6, 7, 8)));
        program.Transitions.Add(new TransitionSettings(500, 1.5, EaseMode.EaseInOut, ContinuousBlend: true));
        return program;
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsProgramContent()
    {
        var program = SampleProgram("Демо-программа");

        await _storage.SaveAsync(program);
        var loaded = await _storage.LoadAsync(program.Id);

        Assert.Equal(program.Id, loaded.Id);
        Assert.Equal("Демо-программа", loaded.Name);
        Assert.Equal(2, loaded.Waypoints.Count);
        Assert.Equal(program.Waypoints[0].Pose, loaded.Waypoints[0].Pose);
        Assert.Single(loaded.Transitions);
        Assert.Equal(1.5, loaded.Transitions[0].DwellSeconds);
    }

    [Fact]
    public async Task ListAsync_EmptyDirectory_ReturnsEmpty()
    {
        var summaries = await _storage.ListAsync();

        Assert.Empty(summaries);
    }

    [Fact]
    public async Task ListAsync_AfterSavingTwoPrograms_ReturnsBothSummaries()
    {
        await _storage.SaveAsync(SampleProgram("Первая"));
        await _storage.SaveAsync(SampleProgram("Вторая"));

        var summaries = await _storage.ListAsync();

        Assert.Equal(2, summaries.Count);
        Assert.Contains(summaries, s => s.Name == "Первая");
        Assert.Contains(summaries, s => s.Name == "Вторая");
    }

    [Fact]
    public async Task DeleteAsync_RemovesProgramFromList()
    {
        var program = SampleProgram("Удаляемая");
        await _storage.SaveAsync(program);

        await _storage.DeleteAsync(program.Id);

        var summaries = await _storage.ListAsync();
        Assert.DoesNotContain(summaries, s => s.Id == program.Id);
    }

    [Fact]
    public async Task LoadAsync_UnknownId_ThrowsFileNotFoundException()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => _storage.LoadAsync(Guid.NewGuid()));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter JsonFileProgramStorageTests`
Expected: FAIL — `JsonFileProgramStorage` does not exist.

- [ ] **Step 3: Create `ArctZ/Services/Program/ProgramSummary.cs`**

```csharp
using System;

namespace ArctZ.Services.Program;

public sealed record ProgramSummary(Guid Id, string Name, DateTimeOffset ModifiedAt);
```

- [ ] **Step 4: Create `ArctZ/Services/Program/IProgramStorage.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Program;

public interface IProgramStorage
{
    Task<IReadOnlyList<ProgramSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<JibProgram> LoadAsync(Guid id, CancellationToken cancellationToken = default);

    Task SaveAsync(JibProgram program, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Create `ArctZ/Services/Program/JsonFileProgramStorage.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Program;

public sealed class JsonFileProgramStorage : IProgramStorage
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _directoryPath;

    public JsonFileProgramStorage(string directoryPath)
    {
        _directoryPath = directoryPath;
    }

    public async Task<IReadOnlyList<ProgramSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directoryPath))
        {
            return Array.Empty<ProgramSummary>();
        }

        var summaries = new List<ProgramSummary>();
        foreach (var file in Directory.EnumerateFiles(_directoryPath, "*.json"))
        {
            await using var stream = File.OpenRead(file);
            var program = await JsonSerializer.DeserializeAsync<JibProgram>(stream, Options, cancellationToken).ConfigureAwait(false);
            if (program is not null)
            {
                summaries.Add(new ProgramSummary(program.Id, program.Name, File.GetLastWriteTimeUtc(file)));
            }
        }

        return summaries;
    }

    public async Task<JibProgram> LoadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var path = PathFor(id);
        await using var stream = File.OpenRead(path);
        var program = await JsonSerializer.DeserializeAsync<JibProgram>(stream, Options, cancellationToken).ConfigureAwait(false);
        return program ?? throw new InvalidOperationException($"Program file '{path}' deserialized to null.");
    }

    public async Task SaveAsync(JibProgram program, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directoryPath);
        await using var stream = File.Create(PathFor(program.Id));
        await JsonSerializer.SerializeAsync(stream, program, Options, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var path = PathFor(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string PathFor(Guid id) => Path.Combine(_directoryPath, $"{id}.json");
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter JsonFileProgramStorageTests`
Expected: PASS (5 tests).

- [ ] **Step 7: Commit**

```bash
git add ArctZ/Services/Program/ProgramSummary.cs ArctZ/Services/Program/IProgramStorage.cs ArctZ/Services/Program/JsonFileProgramStorage.cs ArctZ.Tests/Services/Program/JsonFileProgramStorageTests.cs
git commit -m "feat: add JSON file-backed program storage"
```

---

## Task 21: `ProgramViewModel` — shared state + Authoring mode

**Files:**
- Create: `ArctZ/ViewModels/ProgramMode.cs`
- Create: `ArctZ/ViewModels/JoystickInputMapper.cs`
- Create: `ArctZ/ViewModels/ProgramViewModel.cs`
- Create: `ArctZ.Tests/Services/Program/FakeProgramStorage.cs`
- Test: `ArctZ.Tests/ViewModels/JoystickInputMapperTests.cs`
- Test: `ArctZ.Tests/ViewModels/ProgramViewModelAuthoringTests.cs`

**Interfaces:**
- Consumes: `ConnectionViewModel` (Task 17), `IProgramStorage`/`ProgramSummary` (Task 20), `ITrajectoryCompiler`/`TrajectoryCompiler` (Task 19), `JibProgram`, `Waypoint`, `TransitionSettings`, `EaseMode` (Task 18), `DualJoystickState`, `JoystickAxisInput`, `MachinePose` (Tasks 9, 2), `Components.VirtualJoystick.JoystickEventArgs` (existing control).
- Produces: `ProgramMode` enum (`Authoring`, `Playback`); `JoystickInputMapper.ToAxisInput(JoystickEventArgs) : JoystickAxisInput`; `ProgramViewModel` (`Connection`, `Mode`, `ProgramId`, `ProgramName`, `SelectedWaypoint`, `Waypoints: ObservableCollection<Waypoint>`, `Transitions: ObservableCollection<TransitionSettings>`, `Library: ObservableCollection<ProgramSummary>`, `RefreshLibraryCommand`, `NewProgramCommand`, `LoadProgramCommand`, `SaveProgramCommand`, `CaptureWaypointCommand`, `RemoveWaypointCommand`, `OnLeftJoystickDown/Move/Up`, `OnRightJoystickDown/Move/Up`) — extended with Playback in Task 22, hosted by `MainView` in Task 23.

`Services.Device`/`Services.Program` stay UI-framework-agnostic (per the
23.07 spec's design rule), so the joystick event type never crosses into
them — `JoystickInputMapper` is the one place that converts
`Components.VirtualJoystick.JoystickEventArgs` (`Force`, `AngleDeg`) into
the normalized `JoystickAxisInput` the device layer understands. Its sign
convention (which way is "up" on the stick) has not been visually checked
against the real control yet; that verification happens once Task 23 has
it running on screen, not from this task alone.

`JibProgram.Waypoints`/`Transitions` (Task 18) are plain `List<T>` by
design — the domain model has no UI framework dependency. `ProgramViewModel`
keeps its own `ObservableCollection<T>` copies as the live editing
surface and only assembles/reads a `JibProgram` at the storage boundary
(`SaveProgramAsync`/`LoadProgramAsync`).

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using ArctZ.Components.VirtualJoystick;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class JoystickInputMapperTests
{
    [Fact]
    public void ToAxisInput_ZeroDegrees_ProducesPositiveXZeroY()
    {
        var result = JoystickInputMapper.ToAxisInput(new JoystickEventArgs { Force = 1.0, AngleDeg = 0 });

        Assert.Equal(1.0, result.X, 3);
        Assert.Equal(0.0, result.Y, 3);
        Assert.Equal(1.0, result.Force);
    }

    [Fact]
    public void ToAxisInput_NinetyDegrees_ProducesNegativeY()
    {
        var result = JoystickInputMapper.ToAxisInput(new JoystickEventArgs { Force = 1.0, AngleDeg = 90 });

        Assert.Equal(0.0, result.X, 3);
        Assert.Equal(-1.0, result.Y, 3);
    }

    [Fact]
    public void ToAxisInput_ZeroForce_ProducesZeroXAndY()
    {
        var result = JoystickInputMapper.ToAxisInput(new JoystickEventArgs { Force = 0, AngleDeg = 45 });

        Assert.Equal(0.0, result.X, 3);
        Assert.Equal(0.0, result.Y, 3);
    }
}
```

```csharp
using System.Linq;
using System.Threading.Tasks;
using ArctZ.Components.VirtualJoystick;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.Device;
using ArctZ.Tests.Services.Program;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class ProgramViewModelAuthoringTests
{
    private static ProgramViewModel CreateViewModel(out FakeDeviceTransport transport, out FakeProgramStorage storage)
    {
        transport = new FakeDeviceTransport();
        storage = new FakeProgramStorage();
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default));
        return new ProgramViewModel(connection, storage, new TrajectoryCompiler());
    }

    [Fact]
    public async Task CaptureWaypoint_UsesCurrentDeviceStatusPosition()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("<Idle|WPos:1,2,3,4|FS:0,0>");

        vm.CaptureWaypointCommand.Execute(null);

        Assert.Single(vm.Waypoints);
        Assert.Equal(new MachinePose(1, 2, 3, 4), vm.Waypoints[0].Pose);
    }

    [Fact]
    public async Task CaptureWaypoint_SecondPoint_AddsDefaultTransition()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureWaypointCommand.Execute(null);

        transport.SimulateReceivedLine("<Idle|WPos:10,0,0,0|FS:0,0>");
        vm.CaptureWaypointCommand.Execute(null);

        Assert.Equal(2, vm.Waypoints.Count);
        Assert.Single(vm.Transitions);
    }

    [Fact]
    public void CaptureWaypoint_NoActiveSession_DoesNothing()
    {
        var vm = CreateViewModel(out _, out _);

        vm.CaptureWaypointCommand.Execute(null);

        Assert.Empty(vm.Waypoints);
    }

    [Fact]
    public async Task RemoveWaypoint_MiddlePoint_RemovesItAndKeepsTransitionsInSync()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.ExecuteAsync(null);
        foreach (var pose in new[] { "0,0,0,0", "10,0,0,0", "20,0,0,0" })
        {
            transport.SimulateReceivedLine($"<Idle|WPos:{pose}|FS:0,0>");
            vm.CaptureWaypointCommand.Execute(null);
        }

        var middle = vm.Waypoints[1];
        vm.RemoveWaypointCommand.Execute(middle);

        Assert.Equal(2, vm.Waypoints.Count);
        Assert.Single(vm.Transitions);
        Assert.DoesNotContain(middle, vm.Waypoints);
    }

    [Fact]
    public async Task SaveProgramAsync_ThenRefreshLibrary_ListsSavedProgram()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureWaypointCommand.Execute(null);
        vm.ProgramName = "Тест";

        await vm.SaveProgramCommand.ExecuteAsync(null);
        await vm.RefreshLibraryCommand.ExecuteAsync(null);

        Assert.Contains(vm.Library, s => s.Name == "Тест");
    }

    [Fact]
    public async Task LeftAndRightJoystick_EndJogOnlyAfterBothSticksReleased()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.ExecuteAsync(null);

        vm.OnLeftJoystickDown(new JoystickEventArgs { Force = 1, AngleDeg = 0 });
        vm.OnRightJoystickDown(new JoystickEventArgs { Force = 1, AngleDeg = 90 });
        vm.OnLeftJoystickUp(new JoystickEventArgs { Force = 0, AngleDeg = 0 });

        Assert.DoesNotContain((byte)0x85, transport.SentRawBytes);

        vm.OnRightJoystickUp(new JoystickEventArgs { Force = 0, AngleDeg = 90 });

        Assert.Contains((byte)0x85, transport.SentRawBytes);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "JoystickInputMapperTests|ProgramViewModelAuthoringTests"`
Expected: FAIL — `ProgramViewModel`/`JoystickInputMapper` do not exist.

- [ ] **Step 3: Create `ArctZ.Tests/Services/Program/FakeProgramStorage.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Program;

namespace ArctZ.Tests.Services.Program;

public sealed class FakeProgramStorage : IProgramStorage
{
    private readonly Dictionary<Guid, JibProgram> _programs = new();

    public Task<IReadOnlyList<ProgramSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProgramSummary>>(
            _programs.Values.Select(p => new ProgramSummary(p.Id, p.Name, DateTimeOffset.UtcNow)).ToList());

    public Task<JibProgram> LoadAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_programs[id]);

    public Task SaveAsync(JibProgram program, CancellationToken cancellationToken = default)
    {
        _programs[program.Id] = program;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _programs.Remove(id);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Create `ArctZ/ViewModels/ProgramMode.cs`**

```csharp
namespace ArctZ.ViewModels;

public enum ProgramMode
{
    Authoring,
    Playback
}
```

- [ ] **Step 5: Create `ArctZ/ViewModels/JoystickInputMapper.cs`**

```csharp
using System;
using ArctZ.Components.VirtualJoystick;
using ArctZ.Services.Device;

namespace ArctZ.ViewModels;

/// <summary>
/// Converts VirtualJoystick's Force/AngleDeg into the normalized -1..1
/// X/Y axis pair Services.Device expects. Sign of Y assumes "stick
/// pushed up" should read as positive despite screen Y growing downward
/// — not yet visually verified against the real control (see Task 23).
/// </summary>
public static class JoystickInputMapper
{
    public static JoystickAxisInput ToAxisInput(JoystickEventArgs e)
    {
        var radians = e.AngleDeg * Math.PI / 180.0;
        return new JoystickAxisInput(
            X: e.Force * Math.Cos(radians),
            Y: -e.Force * Math.Sin(radians),
            Force: e.Force);
    }
}
```

- [ ] **Step 6: Create `ArctZ/ViewModels/ProgramViewModel.cs`**

```csharp
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ArctZ.Components.VirtualJoystick;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArctZ.ViewModels;

public partial class ProgramViewModel : ViewModelBase
{
    private readonly IProgramStorage _storage;
    private readonly ITrajectoryCompiler _compiler;
    private JoystickAxisInput _leftInput;
    private JoystickAxisInput _rightInput;
    private bool _leftActive;
    private bool _rightActive;

    public ConnectionViewModel Connection { get; }

    [ObservableProperty]
    private ProgramMode _mode = ProgramMode.Authoring;

    [ObservableProperty]
    private Guid? _programId;

    [ObservableProperty]
    private string _programName = "Новая программа";

    [ObservableProperty]
    private Waypoint? _selectedWaypoint;

    public ObservableCollection<Waypoint> Waypoints { get; } = new();

    /// <summary>Transitions[i] describes the move from Waypoints[i] to Waypoints[i+1] — kept in sync by CaptureWaypoint/RemoveWaypoint.</summary>
    public ObservableCollection<TransitionSettings> Transitions { get; } = new();

    public ObservableCollection<ProgramSummary> Library { get; } = new();

    public ProgramViewModel(ConnectionViewModel connection, IProgramStorage storage, ITrajectoryCompiler compiler)
    {
        Connection = connection;
        _storage = storage;
        _compiler = compiler;
    }

    [RelayCommand]
    private async Task RefreshLibraryAsync()
    {
        Library.Clear();
        foreach (var summary in await _storage.ListAsync().ConfigureAwait(false))
        {
            Library.Add(summary);
        }
    }

    [RelayCommand]
    private void NewProgram()
    {
        ProgramId = null;
        ProgramName = "Новая программа";
        Waypoints.Clear();
        Transitions.Clear();
        SelectedWaypoint = null;
    }

    [RelayCommand]
    private async Task LoadProgramAsync(ProgramSummary summary)
    {
        var program = await _storage.LoadAsync(summary.Id).ConfigureAwait(false);

        ProgramId = program.Id;
        ProgramName = program.Name;

        Waypoints.Clear();
        foreach (var waypoint in program.Waypoints)
        {
            Waypoints.Add(waypoint);
        }

        Transitions.Clear();
        foreach (var transition in program.Transitions)
        {
            Transitions.Add(transition);
        }

        SelectedWaypoint = null;
    }

    [RelayCommand]
    private async Task SaveProgramAsync()
    {
        var program = new JibProgram { Id = ProgramId ?? Guid.NewGuid(), Name = ProgramName };
        program.Waypoints.AddRange(Waypoints);
        program.Transitions.AddRange(Transitions);

        await _storage.SaveAsync(program).ConfigureAwait(false);
        ProgramId = program.Id;
        await RefreshLibraryAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private void CaptureWaypoint()
    {
        var pose = Connection.Session?.DeviceStatus?.WPos;
        if (pose is null)
        {
            return;
        }

        Waypoints.Add(new Waypoint(Guid.NewGuid(), Label: null, pose.Value));

        if (Waypoints.Count > 1)
        {
            Transitions.Add(new TransitionSettings(FeedRateUnitsPerMin: 500, DwellSeconds: 0, EaseMode.None, ContinuousBlend: false));
        }
    }

    [RelayCommand]
    private void RemoveWaypoint(Waypoint waypoint)
    {
        var index = Waypoints.IndexOf(waypoint);
        if (index < 0)
        {
            return;
        }

        Waypoints.RemoveAt(index);

        if (Transitions.Count > 0)
        {
            var transitionIndexToRemove = Math.Min(Math.Max(0, index - 1), Transitions.Count - 1);
            Transitions.RemoveAt(transitionIndexToRemove);
        }

        if (SelectedWaypoint == waypoint)
        {
            SelectedWaypoint = null;
        }
    }

    public void OnLeftJoystickDown(JoystickEventArgs e) => OnStickDown(isLeft: true, e);

    public void OnLeftJoystickMove(JoystickEventArgs e) => OnStickMove(isLeft: true, e);

    public void OnLeftJoystickUp(JoystickEventArgs e) => OnStickUp(isLeft: true);

    public void OnRightJoystickDown(JoystickEventArgs e) => OnStickDown(isLeft: false, e);

    public void OnRightJoystickMove(JoystickEventArgs e) => OnStickMove(isLeft: false, e);

    public void OnRightJoystickUp(JoystickEventArgs e) => OnStickUp(isLeft: false);

    private void OnStickDown(bool isLeft, JoystickEventArgs e)
    {
        var wasAnyActive = _leftActive || _rightActive;
        if (isLeft)
        {
            _leftActive = true;
        }
        else
        {
            _rightActive = true;
        }

        if (!wasAnyActive)
        {
            Connection.Session?.BeginJog();
        }

        OnStickMove(isLeft, e);
    }

    private void OnStickMove(bool isLeft, JoystickEventArgs e)
    {
        var input = JoystickInputMapper.ToAxisInput(e);
        if (isLeft)
        {
            _leftInput = input;
        }
        else
        {
            _rightInput = input;
        }

        Connection.Session?.UpdateJog(new DualJoystickState(_leftInput, _rightInput));
    }

    private void OnStickUp(bool isLeft)
    {
        if (isLeft)
        {
            _leftInput = default;
            _leftActive = false;
        }
        else
        {
            _rightInput = default;
            _rightActive = false;
        }

        if (!_leftActive && !_rightActive)
        {
            Connection.Session?.EndJog();
        }
        else
        {
            Connection.Session?.UpdateJog(new DualJoystickState(_leftInput, _rightInput));
        }
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "JoystickInputMapperTests|ProgramViewModelAuthoringTests"`
Expected: PASS (3 + 6 = 9 tests).

- [ ] **Step 8: Commit**

```bash
git add ArctZ/ViewModels/ProgramMode.cs ArctZ/ViewModels/JoystickInputMapper.cs ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/Services/Program/FakeProgramStorage.cs ArctZ.Tests/ViewModels/JoystickInputMapperTests.cs ArctZ.Tests/ViewModels/ProgramViewModelAuthoringTests.cs
git commit -m "feat: add ProgramViewModel with dual-joystick authoring mode"
```

---

## Task 22: `ProgramViewModel` — Playback mode

**Files:**
- Modify: `ArctZ/Services/Device/IDeviceSession.cs`
- Modify: `ArctZ/Services/Device/DeviceSession.cs`
- Modify: `ArctZ/Services/Device/DeviceSessionFactory.cs`
- Modify: `ArctZ.Tests/Services/Device/DeviceSessionTests.cs`
- Modify: `ArctZ.Tests/Services/Device/DeviceSessionReconnectTests.cs`
- Create: `ArctZ/ViewModels/PlaybackState.cs`
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs`
- Test: `ArctZ.Tests/Services/Device/DeviceSessionRealtimeTests.cs`
- Test: `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs`

**Interfaces:**
- Consumes: `IRealtimeCommandChannel`/`RealtimeCommandChannel` (Task 6), `RealtimeCommand.FeedHold`/`CycleStartResume` (Task 3), `ITrajectoryCompiler`/`CompiledStep` (Task 19), `IBufferAwareCommandQueue`'s `CommandOutcome`/`CommandResult` (Task 7, via `IDeviceSession.SendGCodeAsync`'s return type), `ConnectionState` (Task 14).
- Produces: `IDeviceSession` gains `FeedHoldAsync`/`ResumeAsync`; `PlaybackState` enum (`Idle`, `Running`, `Paused`, `Completed`, `Faulted`, `Stopped`); `ProgramViewModel` gains `PlaybackState`, `CurrentSegmentIndex`, `SegmentProgress`, `FaultedAtSegmentIndex`, `PlayCommand`, `PauseCommand`, `StopCommand`.

Per spec, "Pause" must actually stop physical motion, not just stop the
host from sending more lines — a real GRBL feed hold (`!`), not a
sender-side trick. `PlayAsync` dispatches **every** compiled step's
`SendGCodeAsync` call up front, in one synchronous burst, *before*
awaiting any of the returned completion tasks — dispatching is what
"leans on" `BufferAwareCommandQueue`'s pipelining (Task 7); if it awaited
each completion before sending the next, buffering would be pointless.
Only after everything is dispatched does it walk the completions in order
(guaranteed FIFO — GRBL/the mock always ack in the order sent) to advance
`CurrentSegmentIndex`/`SegmentProgress`. This means progress reflects
*acknowledged* (parsed/queued) commands, not confirmed physical
completion — the same convention real G-code senders use (see
`docs/protocol/gcode_sender_architecture.md`, "Прогресс: по номеру строки
/ проценту отправленных байтов"), not a shortcut specific to this app.

`PlayCommand` allows concurrent execution (`AllowConcurrentExecutions =
true`) specifically so that clicking Play again while paused — the
original dispatch/await loop is still alive, just stalled behind a feed
hold on the controller — sends `~` (resume) and returns immediately
instead of being blocked by the still-running first invocation.

Link loss during playback: `ProgramViewModel` watches
`Connection.Session.ConnectionStateChanged`. `Reconnecting` while
`Running` pauses (no `!` sent — the link is down, there's nothing to send
it *over*); if the policy's retries are exhausted (`Disconnected`),
playback is marked `Faulted`. If it reconnects successfully, it stays
`Paused` — per spec, resuming after a reconnect is always an explicit
user action, never automatic.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Tests.Services.Device;

public class DeviceSessionRealtimeTests
{
    [Fact]
    public async Task FeedHoldAsync_SendsFeedHoldByte()
    {
        var transport = new FakeDeviceTransport();
        var serializer = new FluidNcCommandSerializer();
        var realtimeChannel = new RealtimeCommandChannel(transport);
        var commandQueue = new BufferAwareCommandQueue(transport);
        var jogScheduler = new JogScheduler(
            new JogCommandFactory(MachineLimits.Default), serializer, transport, realtimeChannel, new ManualPeriodicTimer(), TimeSpan.FromMilliseconds(100));
        var statusPoller = new StatusPoller(realtimeChannel, new ManualPeriodicTimer(), TimeSpan.FromMilliseconds(250));
        var reconnectPolicy = new FixedDelayReconnectPolicy(3, TimeSpan.FromMilliseconds(1));
        var session = new DeviceSession(transport, commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller, reconnectPolicy, realtimeChannel);
        await session.ConnectAsync("COM5");

        await session.FeedHoldAsync();

        Assert.Contains((byte)'!', transport.SentRawBytes);
    }

    [Fact]
    public async Task ResumeAsync_SendsCycleStartResumeByte()
    {
        var transport = new FakeDeviceTransport();
        var serializer = new FluidNcCommandSerializer();
        var realtimeChannel = new RealtimeCommandChannel(transport);
        var commandQueue = new BufferAwareCommandQueue(transport);
        var jogScheduler = new JogScheduler(
            new JogCommandFactory(MachineLimits.Default), serializer, transport, realtimeChannel, new ManualPeriodicTimer(), TimeSpan.FromMilliseconds(100));
        var statusPoller = new StatusPoller(realtimeChannel, new ManualPeriodicTimer(), TimeSpan.FromMilliseconds(250));
        var reconnectPolicy = new FixedDelayReconnectPolicy(3, TimeSpan.FromMilliseconds(1));
        var session = new DeviceSession(transport, commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller, reconnectPolicy, realtimeChannel);
        await session.ConnectAsync("COM5");

        await session.ResumeAsync();

        Assert.Contains((byte)'~', transport.SentRawBytes);
    }
}
```

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.Device;
using ArctZ.Tests.Services.Program;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class ProgramViewModelPlaybackTests
{
    private static ProgramViewModel CreateViewModel(out FakeDeviceTransport transport)
    {
        transport = new FakeDeviceTransport();
        var storage = new FakeProgramStorage();
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default));
        return new ProgramViewModel(connection, storage, new TrajectoryCompiler());
    }

    /// <summary>3 waypoints, 2 continuous-blend segments -> 2 compiled G1 steps, no G4.</summary>
    private static void SeedTwoSegmentProgram(ProgramViewModel vm, FakeDeviceTransport transport)
    {
        foreach (var pose in new[] { "0,0,0,0", "10,0,0,0", "20,0,0,0" })
        {
            transport.SimulateReceivedLine($"<Idle|WPos:{pose}|FS:0,0>");
            vm.CaptureWaypointCommand.Execute(null);
        }

        for (var i = 0; i < vm.Transitions.Count; i++)
        {
            vm.Transitions[i] = new TransitionSettings(500, 0, EaseMode.None, ContinuousBlend: true);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - start > timeout)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task PlayAsync_DispatchesAllStepsBeforeAwaitingAcks_ThenTracksProgress()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.ExecuteAsync(null);
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);

        Assert.Equal(2, transport.SentLines.Count(l => l.StartsWith("G1", StringComparison.Ordinal)));

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
        Assert.Equal(1, vm.CurrentSegmentIndex);
        Assert.Equal(1.0, vm.SegmentProgress);
    }

    [Fact]
    public async Task PlayAsync_ErrorOnFirstStep_MarksFaultedWithItsSegmentIndex()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.ExecuteAsync(null);
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("error:9");
        await playTask;

        Assert.Equal(PlaybackState.Faulted, vm.PlaybackState);
        Assert.Equal(0, vm.FaultedAtSegmentIndex);
    }

    [Fact]
    public async Task Pause_SendsFeedHold_PlayAgainSendsResumeWithoutRedispatching()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.ExecuteAsync(null);
        SeedTwoSegmentProgram(vm, transport);

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        var sentLinesBeforePause = transport.SentLines.Count;

        await vm.PauseCommand.ExecuteAsync(null);
        Assert.Contains((byte)'!', transport.SentRawBytes);
        Assert.Equal(PlaybackState.Paused, vm.PlaybackState);

        await vm.PlayCommand.ExecuteAsync(null);
        Assert.Contains((byte)'~', transport.SentRawBytes);
        Assert.Equal(sentLinesBeforePause, transport.SentLines.Count);

        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await playTask;

        Assert.Equal(PlaybackState.Completed, vm.PlaybackState);
    }

    [Fact]
    public async Task LinkLoss_DuringPlayback_PausesImmediatelyThenFaultsIfReconnectExhausted()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.ExecuteAsync(null);
        SeedTwoSegmentProgram(vm, transport);
        transport.ConnectFailuresRemaining = 10;

        _ = vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateDisconnect();

        Assert.Equal(PlaybackState.Paused, vm.PlaybackState);

        await WaitUntilAsync(() => vm.PlaybackState == PlaybackState.Faulted, TimeSpan.FromSeconds(3));

        Assert.Equal(PlaybackState.Faulted, vm.PlaybackState);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "DeviceSessionRealtimeTests|ProgramViewModelPlaybackTests"`
Expected: FAIL — `FeedHoldAsync`/`ResumeAsync`/`PlaybackState`/`PlayCommand` do not exist.

- [ ] **Step 3: Modify `ArctZ/Services/Device/IDeviceSession.cs`**

Add alongside the existing `HomeAsync`/`ResetAlarmAsync`:

```csharp
    Task FeedHoldAsync(CancellationToken cancellationToken = default);

    Task ResumeAsync(CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Modify `ArctZ/Services/Device/DeviceSession.cs`**

Add a 7th constructor parameter (`IRealtimeCommandChannel`) and the two new
realtime methods. Replace the whole file with:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public sealed class DeviceSession : IDeviceSession
{
    private readonly IDeviceTransport _transport;
    private readonly IBufferAwareCommandQueue _commandQueue;
    private readonly IStatusParser _statusParser;
    private readonly IJogScheduler _jogScheduler;
    private readonly IStatusPoller _statusPoller;
    private readonly IReconnectPolicy _reconnectPolicy;
    private readonly IRealtimeCommandChannel _realtimeChannel;
    private string? _lastDeviceId;

    public DeviceSession(
        IDeviceTransport transport,
        IBufferAwareCommandQueue commandQueue,
        IStatusParser statusParser,
        IJogScheduler jogScheduler,
        IStatusPoller statusPoller,
        IReconnectPolicy reconnectPolicy,
        IRealtimeCommandChannel realtimeChannel)
    {
        _transport = transport;
        _commandQueue = commandQueue;
        _statusParser = statusParser;
        _jogScheduler = jogScheduler;
        _statusPoller = statusPoller;
        _reconnectPolicy = reconnectPolicy;
        _realtimeChannel = realtimeChannel;

        _commandQueue.CommandCompleted += OnCommandCompleted;
    }

    public ConnectionState ConnectionState { get; private set; } = ConnectionState.Disconnected;

    public DeviceStatus? DeviceStatus { get; private set; }

    public string? LastError { get; private set; }

    public event Action? ConnectionStateChanged;

    public event Action? DeviceStatusChanged;

    public event Action<CommandRejectedEventArgs>? CommandRejected;

    public event Action<int>? AlarmTriggered;

    public async Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        _lastDeviceId = deviceId;
        SetConnectionState(ConnectionState.Connecting);

        _transport.LineReceived += OnLineReceived;
        _transport.Disconnected += OnTransportDisconnected;

        await _transport.ConnectAsync(deviceId, cancellationToken).ConfigureAwait(false);

        SetConnectionState(ConnectionState.Connected);
        _statusPoller.Start();
    }

    public async Task DisconnectAsync()
    {
        _statusPoller.Stop();
        _jogScheduler.Stop();

        _transport.Disconnected -= OnTransportDisconnected;
        await _transport.DisconnectAsync().ConfigureAwait(false);
        _transport.LineReceived -= OnLineReceived;

        SetConnectionState(ConnectionState.Disconnected);
    }

    public void BeginJog() => _jogScheduler.Start();

    public void UpdateJog(DualJoystickState state) => _jogScheduler.UpdateState(state);

    public void EndJog() => _jogScheduler.Stop();

    public Task<CommandResult> SendGCodeAsync(string line, CancellationToken cancellationToken = default) =>
        _commandQueue.EnqueueAsync(new GCodeLineCommand(line), cancellationToken);

    public Task<CommandResult> HomeAsync(CancellationToken cancellationToken = default) =>
        _commandQueue.EnqueueAsync(new GCodeLineCommand("$H"), cancellationToken);

    public Task<CommandResult> ResetAlarmAsync(CancellationToken cancellationToken = default) =>
        _commandQueue.EnqueueAsync(new GCodeLineCommand("$X"), cancellationToken);

    public Task FeedHoldAsync(CancellationToken cancellationToken = default) =>
        _realtimeChannel.SendAsync(RealtimeCommand.FeedHold, cancellationToken);

    public Task ResumeAsync(CancellationToken cancellationToken = default) =>
        _realtimeChannel.SendAsync(RealtimeCommand.CycleStartResume, cancellationToken);

    private void SetConnectionState(ConnectionState state)
    {
        ConnectionState = state;
        ConnectionStateChanged?.Invoke();
    }

    private async void OnTransportDisconnected()
    {
        _statusPoller.Stop();
        _jogScheduler.Stop();
        SetConnectionState(ConnectionState.Reconnecting);

        for (var attempt = 1; attempt <= _reconnectPolicy.MaxAttempts; attempt++)
        {
            await _reconnectPolicy.WaitBeforeRetryAsync(attempt).ConfigureAwait(false);

            try
            {
                await _transport.ConnectAsync(_lastDeviceId!).ConfigureAwait(false);
                LastError = null;
                SetConnectionState(ConnectionState.Connected);
                _statusPoller.Start();
                return;
            }
            catch
            {
                // try again
            }
        }

        LastError = $"Reconnect failed after {_reconnectPolicy.MaxAttempts} attempts";
        SetConnectionState(ConnectionState.Disconnected);
    }

    private void OnCommandCompleted(GCodeLineCommand command, CommandResult result)
    {
        if (result.Outcome is CommandOutcome.Rejected or CommandOutcome.Aborted)
        {
            CommandRejected?.Invoke(new CommandRejectedEventArgs(command, result.ErrorCode));
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
                if (report.Status.PlannerBlocksAvailable is { } planner && report.Status.RxBytesAvailable is { } rx)
                {
                    _commandQueue.UpdateBufferCapacity(rx, planner);
                }

                _jogScheduler.UpdateCurrentPose(report.Status.WPos);
                DeviceStatusChanged?.Invoke();
                break;
            case UnrecognizedLine:
                break;
        }
    }
}
```

- [ ] **Step 5: Modify `ArctZ/Services/Device/DeviceSessionFactory.cs`**

Change the final line of `Create` from:

```csharp
        return new DeviceSession(transport, commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller, reconnectPolicy);
```

to:

```csharp
        return new DeviceSession(transport, commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller, reconnectPolicy, realtimeChannel);
```

- [ ] **Step 6: Update the two existing `DeviceSession` test files' constructor call sites**

In `ArctZ.Tests/Services/Device/DeviceSessionTests.cs`, change:

```csharp
        _session = new DeviceSession(_transport, _commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller, reconnectPolicy);
```

to:

```csharp
        _session = new DeviceSession(_transport, _commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller, reconnectPolicy, realtimeChannel);
```

In `ArctZ.Tests/Services/Device/DeviceSessionReconnectTests.cs`, change:

```csharp
        _session = new DeviceSession(_transport, commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller, reconnectPolicy);
```

to:

```csharp
        _session = new DeviceSession(_transport, commandQueue, new FluidNcStatusParser(), jogScheduler, statusPoller, reconnectPolicy, realtimeChannel);
```

(Both files already construct a local `realtimeChannel` variable earlier in their constructor — reuse it.)

- [ ] **Step 7: Create `ArctZ/ViewModels/PlaybackState.cs`**

```csharp
namespace ArctZ.ViewModels;

public enum PlaybackState
{
    Idle,
    Running,
    Paused,
    Completed,
    Faulted,
    Stopped
}
```

- [ ] **Step 8: Modify `ArctZ/ViewModels/ProgramViewModel.cs`**

Add `using System.ComponentModel;`, `using System.Threading.Tasks;` (already present), `using ArctZ.Services.Device.Commands;` to the usings, wire up link-loss handling in the constructor, and add the playback state/commands. Insert into the constructor body (after the existing field assignments):

```csharp
        Connection.PropertyChanged += OnConnectionPropertyChanged;
```

Add these members anywhere inside the class body:

```csharp
    private bool _pausedForLinkLoss;

    [ObservableProperty]
    private PlaybackState _playbackState = PlaybackState.Idle;

    [ObservableProperty]
    private int? _currentSegmentIndex;

    [ObservableProperty]
    private double _segmentProgress;

    [ObservableProperty]
    private int? _faultedAtSegmentIndex;

    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionViewModel.Session) && Connection.Session is not null)
        {
            Connection.Session.ConnectionStateChanged += OnSessionConnectionStateChanged;
        }
    }

    private void OnSessionConnectionStateChanged()
    {
        var state = Connection.Session?.ConnectionState;

        if (state == ConnectionState.Reconnecting && PlaybackState == PlaybackState.Running)
        {
            _pausedForLinkLoss = true;
            PlaybackState = PlaybackState.Paused;
        }
        else if (state == ConnectionState.Disconnected && _pausedForLinkLoss)
        {
            _pausedForLinkLoss = false;
            PlaybackState = PlaybackState.Faulted;
        }
        // ConnectionState.Connected after Reconnecting: stays Paused — resuming is an explicit user action.
    }

    private JibProgram BuildProgram()
    {
        var program = new JibProgram { Id = ProgramId ?? Guid.NewGuid(), Name = ProgramName };
        program.Waypoints.AddRange(Waypoints);
        program.Transitions.AddRange(Transitions);
        return program;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task PlayAsync()
    {
        if (Connection.Session is null || PlaybackState == PlaybackState.Running)
        {
            return;
        }

        if (PlaybackState == PlaybackState.Paused)
        {
            _pausedForLinkLoss = false;
            PlaybackState = PlaybackState.Running;
            if (Connection.Session.ConnectionState == ConnectionState.Connected)
            {
                await Connection.Session.ResumeAsync().ConfigureAwait(false);
            }

            return;
        }

        var steps = _compiler.Compile(BuildProgram());
        if (steps.Count == 0)
        {
            return;
        }

        PlaybackState = PlaybackState.Running;
        CurrentSegmentIndex = null;
        SegmentProgress = 0;
        FaultedAtSegmentIndex = null;

        var dispatched = new (CompiledStep Step, Task<CommandResult> Completion)[steps.Count];
        for (var i = 0; i < steps.Count; i++)
        {
            var line = ((GCodeLineCommand)steps[i].Command).Line;
            dispatched[i] = (steps[i], Connection.Session.SendGCodeAsync(line));
        }

        foreach (var (step, completion) in dispatched)
        {
            var result = await completion.ConfigureAwait(false);

            if (PlaybackState == PlaybackState.Stopped)
            {
                return;
            }

            if (result.Outcome != CommandOutcome.Acknowledged)
            {
                PlaybackState = PlaybackState.Faulted;
                FaultedAtSegmentIndex = step.SegmentIndex;
                return;
            }

            CurrentSegmentIndex = step.SegmentIndex;
            SegmentProgress = step.SegmentProgress;
        }

        if (PlaybackState == PlaybackState.Running)
        {
            PlaybackState = PlaybackState.Completed;
        }
    }

    [RelayCommand]
    private Task PauseAsync()
    {
        if (PlaybackState != PlaybackState.Running || Connection.Session is null)
        {
            return Task.CompletedTask;
        }

        PlaybackState = PlaybackState.Paused;
        return Connection.Session.FeedHoldAsync();
    }

    [RelayCommand]
    private Task StopAsync()
    {
        PlaybackState = PlaybackState.Stopped;
        CurrentSegmentIndex = null;
        SegmentProgress = 0;
        return Connection.Session?.FeedHoldAsync() ?? Task.CompletedTask;
    }
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "DeviceSessionTests|DeviceSessionReconnectTests|DeviceSessionRealtimeTests|ProgramViewModelPlaybackTests"`
Expected: PASS (9 + 4 + 2 + 4 = 19 tests).

- [ ] **Step 10: Commit**

```bash
git add ArctZ/Services/Device/IDeviceSession.cs ArctZ/Services/Device/DeviceSession.cs ArctZ/Services/Device/DeviceSessionFactory.cs ArctZ.Tests/Services/Device/DeviceSessionTests.cs ArctZ.Tests/Services/Device/DeviceSessionReconnectTests.cs ArctZ/ViewModels/PlaybackState.cs ArctZ/ViewModels/ProgramViewModel.cs ArctZ.Tests/Services/Device/DeviceSessionRealtimeTests.cs ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs
git commit -m "feat: add playback mode with feed-hold pause and link-loss handling"
```

---

## Task 23: `MainView` — single screen, dual mode, retire `MainViewModel`

**Files:**
- Delete: `ArctZ/ViewModels/MainViewModel.cs`
- Modify: `ArctZ/ViewModels/ProgramViewModel.cs`
- Modify: `ArctZ/Views/MainView.axaml`
- Modify: `ArctZ/Views/MainView.axaml.cs`

**Interfaces:**
- Consumes: `ProgramViewModel` (Tasks 21, 22), `Components.VirtualJoystick.VirtualJoystick`/`JoystickEventArgs` (existing control), `ConnectionView` (Task 17).
- Produces: `ProgramViewModel.IsAuthoring`/`IsPlayback` computed properties; `MainView` bound to `ProgramViewModel` instead of the old placeholder `MainViewModel`.

`MainViewModel` (`Greeting` + single-joystick telemetry properties) was
the pre-device-control skeleton described in
`docs/software/app-architecture.md` — everything it did is now covered by
`ProgramViewModel`, so it is deleted rather than kept alongside it. This
is the task that finally gives the two joysticks (left = boom, right =
camera) a screen, and switches between Authoring/Playback panes on one
`Mode` toggle, per spec — not separate tabs/screens.

- [ ] **Step 1: Delete `ArctZ/ViewModels/MainViewModel.cs`**

- [ ] **Step 2: Modify `ArctZ/ViewModels/ProgramViewModel.cs` — add mode-visibility helpers**

Change the `Mode` property declaration from:

```csharp
    [ObservableProperty]
    private ProgramMode _mode = ProgramMode.Authoring;
```

to:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuthoring))]
    [NotifyPropertyChangedFor(nameof(IsPlayback))]
    private ProgramMode _mode = ProgramMode.Authoring;

    public bool IsAuthoring => Mode == ProgramMode.Authoring;

    public bool IsPlayback => Mode == ProgramMode.Playback;
```

Add `using CommunityToolkit.Mvvm.ComponentModel;` if not already present (it
already is, for `[ObservableProperty]`) — `[NotifyPropertyChangedFor]` lives
in the same namespace.

- [ ] **Step 3: Replace `ArctZ/Views/MainView.axaml`**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:vm="using:ArctZ.ViewModels"
             xmlns:js="using:ArctZ.Components.VirtualJoystick"
             xmlns:program="using:ArctZ.Services.Program"
             mc:Ignorable="d" d:DesignWidth="1000" d:DesignHeight="600"
             x:Class="ArctZ.Views.MainView"
             x:DataType="vm:ProgramViewModel">
    <DockPanel>
        <Grid DockPanel.Dock="Top" ColumnDefinitions="*,Auto,Auto" Margin="8">
            <ContentControl Grid.Column="0" Content="{Binding Connection}" />
            <ToggleButton Grid.Column="1" Content="Программирование" IsChecked="{Binding IsAuthoring}" Click="OnAuthoringModeClicked" Margin="4,0" />
            <ToggleButton Grid.Column="2" Content="Выполнение" IsChecked="{Binding IsPlayback}" Click="OnPlaybackModeClicked" Margin="4,0" />
        </Grid>

        <ListBox DockPanel.Dock="Left" Width="200" Margin="8"
                 ItemsSource="{Binding Library}"
                 SelectionChanged="OnLibrarySelectionChanged">
            <ListBox.ItemTemplate>
                <DataTemplate x:DataType="program:ProgramSummary">
                    <TextBlock Text="{Binding Name}" />
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>

        <Grid IsVisible="{Binding IsAuthoring}" ColumnDefinitions="Auto,*,Auto" Margin="8">
            <js:VirtualJoystick Grid.Column="0" Radius="80" Mode="Fixed" Shape="Circle"
                                 JoystickDown="OnLeftJoystickDown" JoystickMove="OnLeftJoystickMove" JoystickUp="OnLeftJoystickUp" />

            <StackPanel Grid.Column="1" Spacing="8" Margin="16,0">
                <TextBox Text="{Binding ProgramName}" Watermark="Имя программы" />
                <Button Content="Захватить точку" Command="{Binding CaptureWaypointCommand}" />
                <Button Content="Новая программа" Command="{Binding NewProgramCommand}" />
                <Button Content="Сохранить" Command="{Binding SaveProgramCommand}" />
                <ListBox ItemsSource="{Binding Waypoints}" SelectedItem="{Binding SelectedWaypoint}" Height="200">
                    <ListBox.ItemTemplate>
                        <DataTemplate x:DataType="program:Waypoint">
                            <TextBlock Text="{Binding Pose}" />
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
                <Button Content="Удалить точку" Command="{Binding RemoveWaypointCommand}" CommandParameter="{Binding SelectedWaypoint}" />
            </StackPanel>

            <js:VirtualJoystick Grid.Column="2" Radius="80" Mode="Fixed" Shape="Circle"
                                 JoystickDown="OnRightJoystickDown" JoystickMove="OnRightJoystickMove" JoystickUp="OnRightJoystickUp" />
        </Grid>

        <StackPanel IsVisible="{Binding IsPlayback}" Spacing="8" Margin="8">
            <StackPanel Orientation="Horizontal" Spacing="8">
                <Button Content="Play" Command="{Binding PlayCommand}" />
                <Button Content="Pause" Command="{Binding PauseCommand}" />
                <Button Content="Stop" Command="{Binding StopCommand}" />
                <TextBlock Text="{Binding PlaybackState}" VerticalAlignment="Center" />
            </StackPanel>
            <ListBox ItemsSource="{Binding Waypoints}">
                <ListBox.ItemTemplate>
                    <DataTemplate x:DataType="program:Waypoint">
                        <TextBlock Text="{Binding Pose}" />
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
            <TextBlock Text="{Binding CurrentSegmentIndex, StringFormat='Сегмент: {0}'}" />
            <ProgressBar Minimum="0" Maximum="1" Value="{Binding SegmentProgress}" />
            <TextBlock Text="{Binding FaultedAtSegmentIndex, StringFormat='Ошибка на сегменте: {0}'}"
                       IsVisible="{Binding FaultedAtSegmentIndex, Converter={x:Static ObjectConverters.IsNotNull}}" />
        </StackPanel>
    </DockPanel>
</UserControl>
```

- [ ] **Step 4: Replace `ArctZ/Views/MainView.axaml.cs`**

```csharp
using ArctZ.Components.VirtualJoystick;
using ArctZ.Services.Program;
using ArctZ.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ArctZ.Views
{
    public partial class MainView : UserControl
    {
        public MainView()
        {
            InitializeComponent();
        }

        private ProgramViewModel? ViewModel => DataContext as ProgramViewModel;

        private void OnLeftJoystickDown(object? sender, JoystickEventArgs e) => ViewModel?.OnLeftJoystickDown(e);

        private void OnLeftJoystickMove(object? sender, JoystickEventArgs e) => ViewModel?.OnLeftJoystickMove(e);

        private void OnLeftJoystickUp(object? sender, JoystickEventArgs e) => ViewModel?.OnLeftJoystickUp(e);

        private void OnRightJoystickDown(object? sender, JoystickEventArgs e) => ViewModel?.OnRightJoystickDown(e);

        private void OnRightJoystickMove(object? sender, JoystickEventArgs e) => ViewModel?.OnRightJoystickMove(e);

        private void OnRightJoystickUp(object? sender, JoystickEventArgs e) => ViewModel?.OnRightJoystickUp(e);

        private void OnAuthoringModeClicked(object? sender, RoutedEventArgs e)
        {
            if (ViewModel is { } vm)
            {
                vm.Mode = ProgramMode.Authoring;
            }
        }

        private void OnPlaybackModeClicked(object? sender, RoutedEventArgs e)
        {
            if (ViewModel is { } vm)
            {
                vm.Mode = ProgramMode.Playback;
            }
        }

        private async void OnLibrarySelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ViewModel is { } vm && sender is ListBox { SelectedItem: ProgramSummary summary })
            {
                await vm.LoadProgramCommand.ExecuteAsync(summary);
            }
        }
    }
}
```

- [ ] **Step 5: Verify the solution builds**

Run: `dotnet build ArctZ/ArctZ.csproj`
Expected: build succeeds (this only compiles the core project — full-app
verification, including the platform heads that construct `ProgramViewModel`
via DI, happens in Task 24).

- [ ] **Step 6: Commit**

```bash
git rm ArctZ/ViewModels/MainViewModel.cs
git add ArctZ/ViewModels/ProgramViewModel.cs ArctZ/Views/MainView.axaml ArctZ/Views/MainView.axaml.cs
git commit -m "feat: wire dual-joystick single-screen Authoring/Playback view"
```

---

## Task 24: Platform DI wiring — real Desktop transport, `NotSupportedDeviceTransport` elsewhere, per-platform storage

**Files:**
- Modify: `ArctZ/Services/Device/ServiceCollectionExtensions.cs`
- Modify: `ArctZ/App.axaml.cs`
- Create: `ArctZ.Desktop/DesktopSerialTransport.cs`
- Modify: `ArctZ.Desktop/Program.cs`
- Modify: `ArctZ.Desktop/ArctZ.Desktop.csproj`
- Create: `ArctZ.Android/NotSupportedDeviceTransport.cs`
- Modify: `ArctZ.Android/Application.cs`
- Create: `ArctZ.iOS/NotSupportedDeviceTransport.cs`
- Modify: `ArctZ.iOS/AppDelegate.cs`
- Create: `ArctZ.Browser/NotSupportedDeviceTransport.cs`
- Create: `ArctZ.Browser/InMemoryProgramStorage.cs`
- Modify: `ArctZ.Browser/Program.cs`
- Modify: `Directory.Packages.props`

**Interfaces:**
- Consumes: everything registered by `AddArctZCore` (Tasks 2–22), `IDeviceTransport` (Task 5), `IProgramStorage`/`JsonFileProgramStorage` (Task 20), `ProgramViewModel` (Tasks 21–23).
- Produces: a working `App.Services` composition root per head; `DesktopSerialTransport : IDeviceTransport` (real, `System.IO.Ports.SerialPort`-backed); `NotSupportedDeviceTransport : IDeviceTransport` (Android/iOS/Browser — real Bluetooth wiring for those three is out of scope for this plan, see Global Constraints); `InMemoryProgramStorage : IProgramStorage` (Browser-only stand-in until IndexedDB-backed storage is built, per spec's open question).

This is the task that finally makes the app runnable end-to-end: `App`
resolves `ProgramViewModel` from a static `App.Services` provider each
head populates before Avalonia starts, instead of `new`-ing view-models
directly. `MockDeviceTransport` (Task 16) needs no platform-specific
wiring — `AddArctZCore` already registers the `Func<IDeviceTransport>`
that creates it, so **Demo mode works identically on all four heads
without any of this task's platform-specific code**; this task is only
about the *real* transport and the program storage location.

- [ ] **Step 1: Modify `ArctZ/Services/Device/ServiceCollectionExtensions.cs`**

Replace the whole method body:

```csharp
using System;
using ArctZ.Services.Program;
using ArctZ.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ArctZ.Services.Device;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything platform-independent. Each head additionally
    /// registers its own IDeviceTransport (real transport) and
    /// IProgramStorage (storage location) — this method deliberately does
    /// not touch either.
    /// </summary>
    public static IServiceCollection AddArctZCore(this IServiceCollection services)
    {
        services.AddSingleton(MachineLimits.Default);
        services.AddSingleton<IDeviceSessionFactory, DeviceSessionFactory>();
        services.AddSingleton<Func<IDeviceTransport>>(sp => () => new Simulation.MockDeviceTransport(
            sp.GetRequiredService<MachineLimits>(),
            new SystemPeriodicTimer(),
            TimeSpan.FromMilliseconds(100)));
        services.AddSingleton<ITrajectoryCompiler, TrajectoryCompiler>();
        services.AddSingleton<ConnectionViewModel>();
        services.AddSingleton<ProgramViewModel>();
        return services;
    }
}
```

(`ConnectionViewModel` changes from `AddTransient` to `AddSingleton` here —
platform heads may resolve `ProgramViewModel` more than once, e.g. Android
activity recreation, and each resolution must see the same connection
state, not a fresh one.)

- [ ] **Step 2: Modify `ArctZ/App.axaml.cs`**

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
        public static IServiceProvider? Services { get; set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            var viewModel = Services!.GetRequiredService<ProgramViewModel>();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = viewModel
                };
            }
            else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
            {
                singleViewFactoryApplicationLifetime.MainViewFactory = () => new MainView { DataContext = viewModel };
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                singleViewPlatform.MainView = new MainView
                {
                    DataContext = viewModel
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
```

- [ ] **Step 3: Add `System.IO.Ports` package version to `Directory.Packages.props`**

```xml
        <PackageVersion Include="System.IO.Ports" Version="9.0.0" />
```

- [ ] **Step 4: Create `ArctZ.Desktop/DesktopSerialTransport.cs`**

```csharp
using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Desktop;

/// <summary>
/// Real transport for Desktop: the OS exposes a paired Bluetooth Classic
/// SPP device as an ordinary COM port, so this is a thin SerialPort
/// wrapper. `deviceId` passed to ConnectAsync is the COM port name
/// (e.g. "COM5").
/// </summary>
public sealed class DesktopSerialTransport : IDeviceTransport
{
    private SerialPort? _port;

    public bool IsConnected => _port?.IsOpen ?? false;

    public event Action<string>? LineReceived;

    public event Action? Disconnected;

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        _port = new SerialPort(deviceId, 115200) { NewLine = "\n" };
        _port.DataReceived += OnDataReceived;
        _port.ErrorReceived += OnErrorReceived;
        _port.Open();
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        if (_port is not null)
        {
            _port.DataReceived -= OnDataReceived;
            _port.ErrorReceived -= OnErrorReceived;
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
        if (_port is null)
        {
            return;
        }

        try
        {
            while (_port.BytesToRead > 0)
            {
                LineReceived?.Invoke(_port.ReadLine());
            }
        }
        catch (TimeoutException)
        {
        }
        catch (IOException)
        {
            Disconnected?.Invoke();
        }
    }

    private void OnErrorReceived(object sender, SerialErrorReceivedEventArgs e) => Disconnected?.Invoke();
}
```

- [ ] **Step 5: Add `System.IO.Ports` reference to `ArctZ.Desktop/ArctZ.Desktop.csproj`**

Add inside its `<ItemGroup>` containing `PackageReference` entries:

```xml
    <PackageReference Include="System.IO.Ports" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
```

- [ ] **Step 6: Modify `ArctZ.Desktop/Program.cs`**

```csharp
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;

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
            services.AddSingleton<IDeviceTransport, DesktopSerialTransport>();
            services.AddSingleton<IProgramStorage>(_ => new JsonFileProgramStorage(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ArctZ", "Programs")));
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

- [ ] **Step 7: Create `ArctZ.Android/NotSupportedDeviceTransport.cs`**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Android;

/// <summary>
/// Real Android Bluetooth (BluetoothSocket/RFCOMM) is out of scope for
/// this plan — no physical hardware exists yet to validate against.
/// Demo mode (Task 16) is fully usable regardless.
/// </summary>
public sealed class NotSupportedDeviceTransport : IDeviceTransport
{
    public bool IsConnected => false;

    public event Action<string>? LineReceived { add { } remove { } }

    public event Action? Disconnected { add { } remove { } }

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Real Bluetooth is not available on this platform yet. Use Demo mode."));

    public Task DisconnectAsync() => Task.CompletedTask;

    public Task SendLineAsync(string line, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
```

- [ ] **Step 8: Modify `ArctZ.Android/Application.cs`**

```csharp
using Android.App;
using Android.Runtime;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using Avalonia;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

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
            services.AddSingleton<IDeviceTransport, NotSupportedDeviceTransport>();
            services.AddSingleton<IProgramStorage>(_ => new JsonFileProgramStorage(
                Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "ArctZ", "Programs")));
            App.Services = services.BuildServiceProvider();

            return base.CustomizeAppBuilder(builder)
                .WithInterFont();
        }
    }
}
```

- [ ] **Step 9: Add DI package reference to `ArctZ.Android/ArctZ.Android.csproj`**

Add inside its `<ItemGroup>` containing `PackageReference` entries:

```xml
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
```

- [ ] **Step 10: Create `ArctZ.iOS/NotSupportedDeviceTransport.cs`**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.iOS;

/// <summary>
/// CoreBluetooth on iOS is BLE-only; classic SPP needs an MFi-certified
/// ExternalAccessory integration, out of scope for this plan. Demo mode
/// (Task 16) is fully usable regardless.
/// </summary>
public sealed class NotSupportedDeviceTransport : IDeviceTransport
{
    public bool IsConnected => false;

    public event Action<string>? LineReceived { add { } remove { } }

    public event Action? Disconnected { add { } remove { } }

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Real Bluetooth is not available on this platform yet. Use Demo mode."));

    public Task DisconnectAsync() => Task.CompletedTask;

    public Task SendLineAsync(string line, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
```

- [ ] **Step 11: Modify `ArctZ.iOS/AppDelegate.cs`**

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.iOS;
using Avalonia.Media;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using UIKit;

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
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            services.AddSingleton<IProgramStorage>(_ => new JsonFileProgramStorage(Path.Combine(documentsPath, "ArctZ", "Programs")));
            App.Services = services.BuildServiceProvider();

            return base.CustomizeAppBuilder(builder)
                .WithInterFont();
        }
    }
}
```

- [ ] **Step 12: Add DI package reference to `ArctZ.iOS/ArctZ.iOS.csproj`**

Add inside its `<ItemGroup>` containing `PackageReference` entries:

```xml
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
```

- [ ] **Step 13: Create `ArctZ.Browser/NotSupportedDeviceTransport.cs`**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Browser;

/// <summary>
/// Web Bluetooth is BLE-only with limited browser support; classic SPP
/// is unreachable from WASM. Demo mode (Task 16) is fully usable
/// regardless.
/// </summary>
public sealed class NotSupportedDeviceTransport : IDeviceTransport
{
    public bool IsConnected => false;

    public event Action<string>? LineReceived { add { } remove { } }

    public event Action? Disconnected { add { } remove { } }

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Real Bluetooth is not available on this platform yet. Use Demo mode."));

    public Task DisconnectAsync() => Task.CompletedTask;

    public Task SendLineAsync(string line, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
```

- [ ] **Step 14: Create `ArctZ.Browser/InMemoryProgramStorage.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Program;

namespace ArctZ.Browser;

/// <summary>
/// Non-persistent stand-in for Browser until IndexedDB-backed storage is
/// built (spec's open question — WASM has no ordinary filesystem).
/// Programs are lost on page reload.
/// </summary>
public sealed class InMemoryProgramStorage : IProgramStorage
{
    private readonly Dictionary<Guid, JibProgram> _programs = new();

    public Task<IReadOnlyList<ProgramSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProgramSummary>>(
            _programs.Values.Select(p => new ProgramSummary(p.Id, p.Name, DateTimeOffset.UtcNow)).ToList());

    public Task<JibProgram> LoadAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_programs[id]);

    public Task SaveAsync(JibProgram program, CancellationToken cancellationToken = default)
    {
        _programs[program.Id] = program;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _programs.Remove(id);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 15: Modify `ArctZ.Browser/Program.cs`**

```csharp
using ArctZ;
using ArctZ.Browser;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
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
        services.AddSingleton<IProgramStorage, InMemoryProgramStorage>();
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

- [ ] **Step 16: Add DI package reference to `ArctZ.Browser/ArctZ.Browser.csproj`**

Add inside its `<ItemGroup>` containing `PackageReference` entries:

```xml
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
```

- [ ] **Step 17: Verify every head builds**

Run:
```bash
dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj
dotnet build ArctZ.Android/ArctZ.Android.csproj
dotnet build ArctZ.iOS/ArctZ.iOS.csproj
dotnet build ArctZ.Browser/ArctZ.Browser.csproj
dotnet test ArctZ.Tests/ArctZ.Tests.csproj
```
Expected: all four heads build; the full test suite still passes.

- [ ] **Step 18: Manually verify Desktop runs end-to-end in Demo mode**

Run: `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: window opens, "Демо" is selectable and connects, both
joysticks jog the (invisible-for-now, no geometry view — see spec's
"Вне скоупа") simulated pose, capturing waypoints and pressing Play moves
`PlaybackState` through `Running` → `Completed` against the simulated
controller. This is the one step in this plan that needs an actual human
(or `run`-skill-driven) look at the running app — it is not covered by
`ArctZ.Tests`.

- [ ] **Step 19: Commit**

```bash
git add ArctZ/Services/Device/ServiceCollectionExtensions.cs ArctZ/App.axaml.cs Directory.Packages.props ArctZ.Desktop/DesktopSerialTransport.cs ArctZ.Desktop/Program.cs ArctZ.Desktop/ArctZ.Desktop.csproj ArctZ.Android/NotSupportedDeviceTransport.cs ArctZ.Android/Application.cs ArctZ.Android/ArctZ.Android.csproj ArctZ.iOS/NotSupportedDeviceTransport.cs ArctZ.iOS/AppDelegate.cs ArctZ.iOS/ArctZ.iOS.csproj ArctZ.Browser/NotSupportedDeviceTransport.cs ArctZ.Browser/InMemoryProgramStorage.cs ArctZ.Browser/Program.cs ArctZ.Browser/ArctZ.Browser.csproj
git commit -m "feat: wire DI composition root across all four platform heads"
```

---

## Task 25: Sync draft docs with resolved decisions

**Files:**
- Modify: `docs/hardware/mechanics.md`
- Modify: `docs/protocol/bluetooth-gcode-control.md`

**Interfaces:** none — documentation only, no code.

Both files were written before this brainstorming session and are still
marked "не определено"/"открытый вопрос" for things this plan just
resolved and implemented (axis count/ranges, joystick-to-axis mapping).
Leaving them stale would mislead the next person reading the docs instead
of the code.

- [ ] **Step 1: Update `docs/hardware/mechanics.md`**

Replace the "Оси движения (черновой список)" table and its "не определено"
statuses with the 4 resolved axes: X — подъём/опускание стрелы (-15°..+65°,
диапазон будет уточняться), Y — поворот стрелы (не ограничен), Z — пан
камеры (0..360°), A — наклон камеры (0..360°). Remove the corresponding
bullet from "Открытые вопросы" at the bottom of the file.

- [ ] **Step 2: Update `docs/protocol/bluetooth-gcode-control.md`**

In the "От джойстика к G-code" section, replace the open question about
axis mapping with the resolved mapping: left joystick X/Y → machine X/Y
(boom lift/rotation), right joystick X/Y → machine Z/A (camera pan/tilt),
implemented in `ArctZ/ViewModels/JoystickInputMapper.cs` and
`ArctZ/Services/Device/JogCommandFactory.cs`. Remove the corresponding
bullet from "Открытые вопросы".

- [ ] **Step 3: Commit**

```bash
git add docs/hardware/mechanics.md docs/protocol/bluetooth-gcode-control.md
git commit -m "docs: sync mechanics/protocol drafts with resolved axis decisions"
```

