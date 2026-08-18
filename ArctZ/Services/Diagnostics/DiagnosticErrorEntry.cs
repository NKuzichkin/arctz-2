using System;
using System.Globalization;

namespace ArctZ.Services.Diagnostics;

public enum DiagnosticErrorKind
{
    /// <summary>The link to the machine failed (DeviceSession.LastError).</summary>
    Connection,

    /// <summary>The machine reported an ALARM state.</summary>
    Alarm,

    /// <summary>Picking or opening an endpoint failed, before a session existed.</summary>
    Endpoint,
}

/// <summary>One failure worth remembering, kept for the diagnostic error log.</summary>
public sealed record DiagnosticErrorEntry(DateTimeOffset Timestamp, DiagnosticErrorKind Kind, string Message)
{
    public string Format() =>
        $"{Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} [{KindLabel}] {Message}";

    private string KindLabel => Kind switch
    {
        DiagnosticErrorKind.Alarm => "авария",
        DiagnosticErrorKind.Endpoint => "подключение",
        _ => "связь",
    };
}
