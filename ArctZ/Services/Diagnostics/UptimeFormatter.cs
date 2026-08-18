using System;

namespace ArctZ.Services.Diagnostics;

/// <summary>Renders how long the app has been running, in the coarsest two units that still say something.</summary>
public static class UptimeFormatter
{
    public static string Format(TimeSpan uptime)
    {
        if (uptime < TimeSpan.Zero)
        {
            uptime = TimeSpan.Zero;
        }

        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays} д {uptime.Hours} ч";
        }

        if (uptime.TotalHours >= 1)
        {
            return $"{(int)uptime.TotalHours} ч {uptime.Minutes} мин";
        }

        if (uptime.TotalMinutes >= 1)
        {
            return $"{(int)uptime.TotalMinutes} мин {uptime.Seconds} с";
        }

        return $"{uptime.Seconds} с";
    }
}
