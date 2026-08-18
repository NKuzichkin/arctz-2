using System;
using ArctZ.Services.Diagnostics;

namespace ArctZ.Tests.Services.Diagnostics;

public class DiagnosticLogEntryTests
{
    private static readonly DateTimeOffset At = new(2026, 8, 17, 22, 44, 2, TimeSpan.FromHours(3));

    [Fact]
    public void DeviceExchangeEntry_RendersSentLinesWithAnOutgoingArrow()
    {
        var entry = new DeviceExchangeEntry(At, DeviceExchangeDirection.Sent, "G1 X10 Y20 F500");

        Assert.Equal("22:44:02 → G1 X10 Y20 F500", entry.Format());
    }

    [Fact]
    public void DeviceExchangeEntry_RendersReceivedLinesWithAnIncomingArrow()
    {
        var entry = new DeviceExchangeEntry(At, DeviceExchangeDirection.Received, "error:9");

        Assert.Equal("22:44:02 ← error:9", entry.Format());
    }

    [Theory]
    [InlineData(DiagnosticErrorKind.Connection, "22:44:02 [связь] порт закрыт")]
    [InlineData(DiagnosticErrorKind.Alarm, "22:44:02 [авария] порт закрыт")]
    [InlineData(DiagnosticErrorKind.Endpoint, "22:44:02 [подключение] порт закрыт")]
    public void DiagnosticErrorEntry_LabelsTheKindOfFailure(DiagnosticErrorKind kind, string expected)
    {
        var entry = new DiagnosticErrorEntry(At, kind, "порт закрыт");

        Assert.Equal(expected, entry.Format());
    }
}
