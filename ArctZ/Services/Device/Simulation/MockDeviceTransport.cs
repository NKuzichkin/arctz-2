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
