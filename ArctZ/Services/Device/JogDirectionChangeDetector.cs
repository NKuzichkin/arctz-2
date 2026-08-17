using System;

namespace ArctZ.Services.Device;

/// <summary>
/// Decides whether the stick has swung far enough from the jog already handed to the machine that
/// the committed motion should be cancelled rather than waited out. Compares the 4-axis direction
/// vector and the commanded speed, since either can leave the operator watching the old move.
/// </summary>
public sealed class JogDirectionChangeDetector
{
    public const double DefaultMaxAngleDegrees = 30.0;
    public const double DefaultForceChangeFraction = 0.20;

    /// <summary>Keeps the relative force comparison finite when the committed force is (near) zero,
    /// where any push at all is an infinite relative change.</summary>
    private const double MinComparableForce = 0.01;

    private readonly double _minCosine;
    private readonly double _forceChangeFraction;

    public JogDirectionChangeDetector(
        double maxAngleDegrees = DefaultMaxAngleDegrees,
        double forceChangeFraction = DefaultForceChangeFraction)
    {
        _minCosine = Math.Cos(maxAngleDegrees * Math.PI / 180.0);
        _forceChangeFraction = forceChangeFraction;
    }

    /// <param name="committed">Stick state behind the last jog line actually sent — what the
    /// machine is executing, not the most recent pointer sample.</param>
    /// <param name="requested">Stick state the operator is asking for now.</param>
    public bool IsSharpChange(DualJoystickState committed, DualJoystickState requested)
    {
        var committedForce = Force(committed);
        var requestedForce = Force(requested);
        if (Math.Abs(requestedForce - committedForce) >
            _forceChangeFraction * Math.Max(committedForce, MinComparableForce))
        {
            return true;
        }

        var committedDirection = Direction(committed);
        var requestedDirection = Direction(requested);
        var committedMagnitude = Magnitude(committedDirection);
        var requestedMagnitude = Magnitude(requestedDirection);

        // A centred stick has no direction to turn away from; the force test above is the only
        // meaningful comparison in that case.
        if (committedMagnitude <= 0 || requestedMagnitude <= 0)
        {
            return false;
        }

        var cosine = Dot(committedDirection, requestedDirection) / (committedMagnitude * requestedMagnitude);
        return cosine < _minCosine;
    }

    /// <summary>Matches how JogCommandFactory derives feed, so the test tracks commanded speed.</summary>
    private static double Force(DualJoystickState state) => Math.Max(state.Left.Force, state.Right.Force);

    private static MachinePose Direction(DualJoystickState state) => new(
        X: state.Left.X,
        Y: state.Left.Y,
        Z: state.Right.X,
        A: state.Right.Y);

    private static double Magnitude(MachinePose d) =>
        Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z + d.A * d.A);

    private static double Dot(MachinePose a, MachinePose b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.A * b.A;
}
