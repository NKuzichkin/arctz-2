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
