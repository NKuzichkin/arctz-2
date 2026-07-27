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
