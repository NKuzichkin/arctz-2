using System;
using System.Globalization;

namespace ArctZ.Services.Diagnostics;

/// <summary>Renders byte counts the way a person reads them, for the "О программе" report.</summary>
public static class ByteSizeFormatter
{
    private static readonly string[] Units = { "КБ", "МБ", "ГБ", "ТБ" };

    public static string Format(long bytes)
    {
        if (bytes < 0)
        {
            bytes = 0;
        }

        if (bytes < 1024)
        {
            return $"{bytes} Б";
        }

        var value = bytes / 1024d;
        var unit = 0;

        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        // Formatted invariantly and then switched to a comma rather than formatted under
        // ru-RU: the browser head can run with globalization in invariant mode, where
        // culture-specific separators silently fall back to a dot.
        var number = value.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');
        return $"{number} {Units[unit]}";
    }

    /// <summary>Null when the total is unknown or zero, so callers show a dash instead of "0 %".</summary>
    public static int? Percent(long part, long total) =>
        total > 0 ? (int)Math.Round(part * 100d / total, MidpointRounding.AwayFromZero) : null;
}
