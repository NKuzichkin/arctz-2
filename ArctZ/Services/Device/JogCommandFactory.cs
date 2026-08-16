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
    private readonly TimeSpan _jogInterval;
    private readonly double _maxFeedUnitsPerMin;
    private readonly double _lookaheadFactor;

    /// <param name="jogInterval">How often the scheduler emits a block. Step size is derived from
    /// this so that one block's travel time matches one interval — a fixed step would encode a
    /// travel time unrelated to the send rate and either starve or flood the planner.</param>
    /// <param name="lookaheadFactor">Travel time per block as a multiple of the interval. Above 1
    /// so normal timer/link jitter cannot empty the planner (an empty planner decelerates to a
    /// stop at the last queued block, which the operator feels as stuttering).</param>
    public JogCommandFactory(
        MachineLimits limits,
        TimeSpan jogInterval,
        double maxFeedUnitsPerMin = 1000.0,
        double lookaheadFactor = 1.5)
    {
        _limits = limits;
        _jogInterval = jogInterval;
        _maxFeedUnitsPerMin = maxFeedUnitsPerMin;
        _lookaheadFactor = lookaheadFactor;
    }

    public JogCommand Create(DualJoystickState state, MachinePose currentPose)
    {
        var force = Math.Max(state.Left.Force, state.Right.Force);
        var feed = Math.Max(1.0, force * _maxFeedUnitsPerMin);

        var direction = new MachinePose(
            X: state.Left.X,
            Y: state.Left.Y,
            Z: state.Right.X,
            A: state.Right.Y);

        var magnitude = Math.Sqrt(
            direction.X * direction.X +
            direction.Y * direction.Y +
            direction.Z * direction.Z +
            direction.A * direction.A);

        if (magnitude <= 0)
        {
            return new JogCommand(MachinePose.Zero, feed);
        }

        // Feed applies to the vector magnitude of the move, so scaling the unit direction by the
        // distance covered in one interval makes travel time independent of stick deflection.
        var distance = feed / 60.0 * _jogInterval.TotalSeconds * _lookaheadFactor;
        var scale = distance / magnitude;

        var rawDeltas = new MachinePose(
            X: direction.X * scale,
            Y: direction.Y * scale,
            Z: direction.Z * scale,
            A: direction.A * scale);

        return new JogCommand(_limits.ClampDelta(currentPose, rawDeltas), feed);
    }
}
