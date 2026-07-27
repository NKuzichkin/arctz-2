namespace ArctZ.Services.Device;

/// <summary>
/// A pose of all 4 machine axes. All 4 are angular (degrees) — X boom
/// lift, Y boom rotation, Z camera pan, A camera tilt — not linear.
/// </summary>
public readonly record struct MachinePose(double X, double Y, double Z, double A)
{
    public static readonly MachinePose Zero = new(0, 0, 0, 0);
}
