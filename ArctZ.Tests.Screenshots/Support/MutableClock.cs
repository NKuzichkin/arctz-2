using System;

namespace ArctZ.Tests.Screenshots.Support;

/// <summary>
/// The clock handed to ProgramViewModel in place of DateTimeOffset.Now. Screens that need elapsed
/// time to have passed (playback progress, a time-overage warning) advance it explicitly instead
/// of sleeping, which keeps both the captured frames and the About report's uptime deterministic.
/// </summary>
public sealed class MutableClock
{
    public MutableClock(DateTimeOffset startedAt) => Now = startedAt;

    public DateTimeOffset Now { get; private set; }

    public void Advance(TimeSpan by) => Now += by;
}
