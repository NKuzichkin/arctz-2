using System;
using System.Globalization;

namespace ArctZ.Services.Diagnostics;

public enum DeviceExchangeDirection
{
    Sent,
    Received,
}

/// <summary>One line of the FluidNC conversation, kept for the diagnostic exchange log.</summary>
public sealed record DeviceExchangeEntry(DateTimeOffset Timestamp, DeviceExchangeDirection Direction, string Line)
{
    public string Format()
    {
        var arrow = Direction == DeviceExchangeDirection.Sent ? "→" : "←";
        return $"{Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} {arrow} {Line}";
    }
}
