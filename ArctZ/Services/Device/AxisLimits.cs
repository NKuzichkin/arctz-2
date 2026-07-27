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
