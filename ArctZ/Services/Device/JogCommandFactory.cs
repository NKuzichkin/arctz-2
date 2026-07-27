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
