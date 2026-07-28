using System;
using ArctZ.Services.Device;

namespace ArctZ.Services.Program;

/// <summary>
/// A single stop in a program: a machine pose plus how the machine gets
/// there and how long it stays. Number is the point's 1-based position in
/// JibProgram.KeyPoints, kept in sync by whoever mutates that list.
/// </summary>
public sealed record KeyPoint(
    Guid Id,
    int Number,
    string? Label,
    MachinePose Pose,
    double DwellSeconds,
    double FeedRateUnitsPerMin,
    EaseMode Ease,
    bool ContinuousBlend)
{
    /// <summary>A dwell always forces a stop, regardless of ContinuousBlend.</summary>
    public bool StopsAtWaypoint => !ContinuousBlend || DwellSeconds > 0;
}
