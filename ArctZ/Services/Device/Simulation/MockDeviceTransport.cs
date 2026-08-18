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
///
/// All mutable state is guarded by <c>_lock</c>: in production three
/// independent threads touch it — the caller of SendLineAsync, the motion
/// ticker's timer thread (OnTick), and the status poller / jog scheduler
/// timer threads (SendRawByteAsync). Following the BufferAwareCommandQueue
/// idiom, any line to raise via LineReceived is built while holding the lock
/// but the event is invoked only after the lock is released, so subscriber
/// code never runs under the lock.
/// </summary>
public sealed class MockDeviceTransport : IDeviceTransport, IMockDeviceControl
{
    private const int RxBufferCapacity = 128;
    private const int PlannerBlockCapacity = 15;

    private readonly MachineLimits _limits;
    private readonly IPeriodicTimer _motionTicker;
    private readonly TimeSpan _tickInterval;
    private readonly Queue<string> _pendingLines = new();
    private readonly object _lock = new();

    private MachinePose _currentPose = MachinePose.Zero;
    private MachinePose? _targetPose;
    private double _feedUnitsPerMin = 1;

    // Второй режим движения: для G93 задано время, а не скорость, поэтому поза
    // интерполируется от стартовой к целевой по накопленному времени, и все оси
    // приходят одновременно. _moveTotalSeconds > 0 означает «идёт G93-движение».
    private MachinePose _moveStartPose = MachinePose.Zero;
    private double _moveTotalSeconds;
    private double _moveElapsedSeconds;
    private double _dwellSecondsRemaining;
    private bool _alarm;
    private bool _held;
    private int _rxBytesInFlight;
    private int? _forcedErrorForNextDequeue;
    private int _responseDelayTicks;
    private int _ticksUntilNextProcess;

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
    public void ForceNextCommandError(int code)
    {
        lock (_lock)
        {
            _forcedErrorForNextDequeue = code;
        }
    }

    public void TriggerAlarm(int code)
    {
        lock (_lock)
        {
            _alarm = true;
            _targetPose = null; // авария останавливает движение, как в реальном FluidNC
        }

        LineReceived?.Invoke($"ALARM:{code}");
    }

    public void SetResponseDelay(TimeSpan delay)
    {
        lock (_lock)
        {
            _responseDelayTicks = (int)Math.Round(delay.TotalMilliseconds / _tickInterval.TotalMilliseconds);
            _ticksUntilNextProcess = _responseDelayTicks;
        }
    }

    public void SetPose(MachinePose pose)
    {
        lock (_lock)
        {
            _currentPose = _limits.Clamp(pose);

            // Движение обязано отмениться: иначе ближайший тик пересчитает позу от
            // _moveStartPose (G93) или дошагает к старой цели (G0/G1/jog), и телепорт
            // откатится назад. Авария, feed hold, пауза G4 и очередь строк — отдельные
            // ручки мока и здесь не трогаются.
            _targetPose = null;
            _moveTotalSeconds = 0;
            _moveElapsedSeconds = 0;
        }
    }

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
        lock (_lock)
        {
            _pendingLines.Enqueue(line);
            _rxBytesInFlight += line.Length + 1;
        }

        return Task.CompletedTask;
    }

    public Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default)
    {
        string? statusLine = null;

        lock (_lock)
        {
            switch (value)
            {
                case (byte)'?':
                    statusLine = FormatStatusLine();
                    break;
                case (byte)'!': // feed hold
                    _held = true;
                    break;
                case (byte)'~': // cycle start / resume
                    _held = false;
                    break;
                case 0x85: // jog cancel
                    _targetPose = null;
                    _moveTotalSeconds = 0;
                    break;
                case 0x18: // soft reset (Ctrl-X)
                    // В отличие от feed hold, выбрасывает всё принятое, но ещё не исполненное:
                    // после него станку нечего возобновлять.
                    _pendingLines.Clear();
                    _rxBytesInFlight = 0;
                    _targetPose = null;
                    _moveTotalSeconds = 0;
                    _dwellSecondsRemaining = 0;
                    _held = false;
                    break;
            }
        }

        if (statusLine is not null)
        {
            LineReceived?.Invoke(statusLine);
        }

        return Task.CompletedTask;
    }

    private void OnTick()
    {
        string? lineToRaise;

        lock (_lock)
        {
            lineToRaise = ProcessOnePendingLine();
            AdvanceMotion();
        }

        if (lineToRaise is not null)
        {
            LineReceived?.Invoke(lineToRaise);
        }
    }

    /// <summary>Caller must hold `_lock`. Returns the line to raise via LineReceived (after releasing the lock), or null.</summary>
    private string? ProcessOnePendingLine()
    {
        if (_pendingLines.Count == 0)
        {
            return null;
        }

        if (_ticksUntilNextProcess > 0)
        {
            _ticksUntilNextProcess--;
            return null;
        }

        var line = _pendingLines.Dequeue();
        _rxBytesInFlight -= line.Length + 1;
        _ticksUntilNextProcess = _responseDelayTicks;

        if (_forcedErrorForNextDequeue is { } code)
        {
            _forcedErrorForNextDequeue = null;
            return $"error:{code}";
        }

        var trimmed = line.Trim();
        if (_alarm &&
            !trimmed.Equals("$X", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.Equals("$H", StringComparison.OrdinalIgnoreCase))
        {
            return "error:9";
        }

        ApplyCommand(line);
        return "ok";
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

        var isInverseTime = trimmed.Contains("G93", StringComparison.OrdinalIgnoreCase);

        if (trimmed.StartsWith("$J=", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("G0", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("G1", StringComparison.OrdinalIgnoreCase) ||
            isInverseTime)
        {
            var tokens = ParseAxisTokens(trimmed);
            var isRelative = trimmed.Contains("G91", StringComparison.OrdinalIgnoreCase);

            var target = new MachinePose(
                X: tokens.TryGetValue('X', out var x) ? (isRelative ? _currentPose.X + x : x) : _currentPose.X,
                Y: tokens.TryGetValue('Y', out var y) ? (isRelative ? _currentPose.Y + y : y) : _currentPose.Y,
                Z: tokens.TryGetValue('Z', out var z) ? (isRelative ? _currentPose.Z + z : z) : _currentPose.Z,
                A: tokens.TryGetValue('A', out var a) ? (isRelative ? _currentPose.A + a : a) : _currentPose.A);

            _targetPose = _limits.Clamp(target);

            tokens.TryGetValue('F', out var feed);

            if (isInverseTime && feed > 0)
            {
                _moveStartPose = _currentPose;
                _moveTotalSeconds = 60.0 / feed;
                _moveElapsedSeconds = 0;
                // FS: должен показывать эффективную подачу, а не 1/t.
                _feedUnitsPerMin = Distance(_currentPose, _targetPose.Value) / _moveTotalSeconds * 60.0;
            }
            else
            {
                _moveTotalSeconds = 0;
                if (feed > 0)
                {
                    _feedUnitsPerMin = feed;
                }
            }
        }
    }

    private void AdvanceMotion()
    {
        if (_held)
        {
            // Feed hold: motion and the dwell countdown both freeze until '~' resumes.
            return;
        }

        var elapsedSeconds = _tickInterval.TotalSeconds;

        if (_dwellSecondsRemaining > 0)
        {
            _dwellSecondsRemaining = _dwellSecondsRemaining <= elapsedSeconds + 1e-10 ? 0 : _dwellSecondsRemaining - elapsedSeconds;
            return;
        }

        if (_targetPose is not { } target || target == _currentPose)
        {
            return;
        }

        if (_moveTotalSeconds > 0)
        {
            _moveElapsedSeconds += elapsedSeconds;
            // Tolerance mirrors the dwell countdown above: summing elapsedSeconds tick by tick
            // accumulates IEEE-754 error (50 * 0.1 = 4.999999999999998, not 5.0), which would
            // otherwise strand the move a hair short of its commanded arrival tick.
            var progress = _moveElapsedSeconds + 1e-9 >= _moveTotalSeconds
                ? 1.0
                : _moveElapsedSeconds / _moveTotalSeconds;

            _currentPose = progress >= 1.0
                ? target
                : new MachinePose(
                    X: _moveStartPose.X + (target.X - _moveStartPose.X) * progress,
                    Y: _moveStartPose.Y + (target.Y - _moveStartPose.Y) * progress,
                    Z: _moveStartPose.Z + (target.Z - _moveStartPose.Z) * progress,
                    A: _moveStartPose.A + (target.A - _moveStartPose.A) * progress);

            if (progress >= 1.0)
            {
                _targetPose = null;
                _moveTotalSeconds = 0;
            }

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

        if (_held)
        {
            return MachineState.Hold;
        }

        if (_dwellSecondsRemaining > 0 || (_targetPose is { } target && target != _currentPose))
        {
            return MachineState.Run;
        }

        return MachineState.Idle;
    }

    private static double Distance(MachinePose a, MachinePose b) => Math.Sqrt(
        Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2) + Math.Pow(b.Z - a.Z, 2) + Math.Pow(b.A - a.A, 2));

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
