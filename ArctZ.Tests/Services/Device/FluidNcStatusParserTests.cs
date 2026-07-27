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

    [Fact]
    public void Parse_StatusReportLine_MissingAxis_DefaultsToZero()
    {
        var result = _parser.Parse("<Run|WPos:1.000,2.000,3.000|FS:0,0>");

        var report = Assert.IsType<StatusReportLine>(result);
        Assert.Equal(new MachinePose(1.000, 2.000, 3.000, 0), report.Status.WPos);
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
