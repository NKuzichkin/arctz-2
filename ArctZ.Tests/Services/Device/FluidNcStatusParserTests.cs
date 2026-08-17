using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class FluidNcStatusParserTests
{
    private readonly FluidNcStatusParser _parser = new();

    [Fact]
    public void Parse_StatusReportLine_ExtractsStateWorkPositionAndBuffer()
    {
        var result = _parser.Parse("<Idle|WPos:0.000,-80.000,-10.540,45.000|Bf:15,128|FS:0,0|Ov:100,100,100>");

        var report = Assert.IsType<StatusReportLine>(result);
        Assert.Equal(MachineState.Idle, report.Status.State);
        Assert.Equal(new MachinePose(0.000, -80.000, -10.540, 45.000), report.Status.WPos);
        Assert.Equal(15, report.Status.PlannerBlocksAvailable);
        Assert.Equal(128, report.Status.RxBytesAvailable);
    }

    /// <summary>Состояние может нести подсостояние через двоеточие. По спецификации grbl 1.1
    /// "Hold:0" — торможение закончено, "Hold:1" — ещё идёт (и сброс в этот момент выбросит
    /// аварию), поэтому суффикс нужен целиком, а не только имя состояния.</summary>
    [Theory]
    [InlineData("Hold:0", MachineState.Hold, 0)]
    [InlineData("Hold:1", MachineState.Hold, 1)]
    [InlineData("Door:1", MachineState.Unknown, 1)]
    [InlineData("Run", MachineState.Run, null)]
    public void Parse_StatusReportLine_ReadsStateAndSubState(string field, MachineState expectedState, int? expectedSubState)
    {
        var result = _parser.Parse($"<{field}|MPos:0.000,0.000,0.000,0.000|Bf:15,128>");

        var report = Assert.IsType<StatusReportLine>(result);
        Assert.Equal(expectedState, report.Status.State);
        Assert.Equal(expectedSubState, report.Status.SubState);
    }

    [Fact]
    public void Parse_StatusReportLine_MissingAxis_DefaultsToZero()
    {
        var result = _parser.Parse("<Run|WPos:1.000,2.000,3.000|FS:0,0>");

        var report = Assert.IsType<StatusReportLine>(result);
        Assert.Equal(new MachinePose(1.000, 2.000, 3.000, 0), report.Status.WPos);
    }

    /// <summary>With `$10=1` — the setting this machine actually runs — the firmware reports MPos
    /// and never WPos, so a parser that only knows WPos silently hands out a zero pose forever.</summary>
    [Fact]
    public void Parse_StatusReportLine_WithMPosInsteadOfWPos_ExtractsPosition()
    {
        var result = _parser.Parse("<Jog|MPos:0.000,169.910,0.000,0.000|FS:1000,0>");

        var report = Assert.IsType<StatusReportLine>(result);
        Assert.Equal(new MachinePose(0.000, 169.910, 0.000, 0.000), report.Status.WPos);
    }

    /// <summary>MPos is machine-absolute; the work position the rest of the app wants is MPos minus
    /// the work-coordinate offset.</summary>
    [Fact]
    public void Parse_StatusReportLine_WithMPosAndWco_SubtractsTheOffset()
    {
        var result = _parser.Parse("<Idle|MPos:10.000,20.000,30.000,40.000|WCO:1.000,2.000,3.000,4.000>");

        var report = Assert.IsType<StatusReportLine>(result);
        Assert.Equal(new MachinePose(9.000, 18.000, 27.000, 36.000), report.Status.WPos);
    }

    /// <summary>The firmware sends WCO only every so often, so the offset from an earlier report has
    /// to keep applying to the MPos-only reports that follow it.</summary>
    [Fact]
    public void Parse_StatusReportLine_WithMPosAfterAnEarlierWco_KeepsApplyingThatOffset()
    {
        _parser.Parse("<Idle|MPos:10.000,20.000,30.000,40.000|WCO:1.000,2.000,3.000,4.000>");

        var result = _parser.Parse("<Idle|MPos:10.000,20.000,30.000,40.000>");

        var report = Assert.IsType<StatusReportLine>(result);
        Assert.Equal(new MachinePose(9.000, 18.000, 27.000, 36.000), report.Status.WPos);
    }

    /// <summary>WPos is already the work position, so a WCO in the same report must not be applied
    /// to it a second time.</summary>
    [Fact]
    public void Parse_StatusReportLine_WithWPosAndWco_UsesWPosUnchanged()
    {
        var result = _parser.Parse("<Idle|WPos:9.000,18.000,27.000,36.000|WCO:1.000,2.000,3.000,4.000>");

        var report = Assert.IsType<StatusReportLine>(result);
        Assert.Equal(new MachinePose(9.000, 18.000, 27.000, 36.000), report.Status.WPos);
    }

    [Fact]
    public void Parse_StatusReportLine_MissingBf_ReturnsNullBufferInfo()
    {
        var result = _parser.Parse("<Idle|WPos:0,0,0,0|FS:0,0>");

        var report = Assert.IsType<StatusReportLine>(result);
        Assert.Null(report.Status.PlannerBlocksAvailable);
        Assert.Null(report.Status.RxBytesAvailable);
    }

    [Fact]
    public void Parse_Ok_ReturnsOkLine()
    {
        Assert.IsType<OkLine>(_parser.Parse("ok"));
    }

    [Fact]
    public void Parse_Error_ReturnsErrorLineWithCode()
    {
        var result = Assert.IsType<ErrorLine>(_parser.Parse("error:9"));
        Assert.Equal(9, result.Code);
    }

    [Fact]
    public void Parse_Alarm_ReturnsAlarmLineWithCode()
    {
        var result = Assert.IsType<AlarmLine>(_parser.Parse("ALARM:1"));
        Assert.Equal(1, result.Code);
    }

    [Fact]
    public void Parse_UnknownText_ReturnsUnrecognizedLine()
    {
        var result = Assert.IsType<UnrecognizedLine>(_parser.Parse("garbage"));
        Assert.Equal("garbage", result.Raw);
    }
}
