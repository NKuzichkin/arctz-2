using System;
using ArctZ.Services.Diagnostics;

namespace ArctZ.Tests.Services.Diagnostics;

public class UptimeFormatterTests
{
    [Fact]
    public void Format_ShowsSecondsOnlyUnderAMinute()
    {
        Assert.Equal("45 с", UptimeFormatter.Format(TimeSpan.FromSeconds(45)));
    }

    [Fact]
    public void Format_ShowsMinutesAndSecondsUnderAnHour()
    {
        Assert.Equal("5 мин 3 с", UptimeFormatter.Format(new TimeSpan(0, 5, 3)));
    }

    [Fact]
    public void Format_ShowsHoursAndMinutesUnderADay()
    {
        Assert.Equal("2 ч 5 мин", UptimeFormatter.Format(new TimeSpan(2, 5, 30)));
    }

    [Fact]
    public void Format_ShowsDaysAndHoursBeyondADay()
    {
        Assert.Equal("1 д 3 ч", UptimeFormatter.Format(new TimeSpan(1, 3, 40, 0)));
    }

    [Fact]
    public void Format_ClampsNegativeDurationsToZero()
    {
        Assert.Equal("0 с", UptimeFormatter.Format(TimeSpan.FromSeconds(-5)));
    }
}
