using ArctZ.Services.Diagnostics;

namespace ArctZ.Tests.Services.Diagnostics;

public class DeviceExchangeFilterTests
{
    [Theory]
    [InlineData("<Idle|MPos:0.000,0.000,0.000,0.000|FS:0,0>")]
    [InlineData("<Run|MPos:12.500,0.000,0.000,0.000|FS:500,0|Bf:14,127>")]
    public void StatusReports_AreExcluded(string line)
    {
        Assert.False(DeviceExchangeFilter.ShouldLog(line));
    }

    [Theory]
    [InlineData("$J=G91 G21 X1.5 F1000")]
    [InlineData("$j=g91 x-1 f600")]
    public void JogCommands_AreExcluded(string line)
    {
        Assert.False(DeviceExchangeFilter.ShouldLog(line));
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("  ok  ")]
    [InlineData("OK")]
    public void BareAcknowledgements_AreExcluded(string line)
    {
        Assert.False(DeviceExchangeFilter.ShouldLog(line));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankLines_AreExcluded(string line)
    {
        Assert.False(DeviceExchangeFilter.ShouldLog(line));
    }

    [Theory]
    [InlineData("error:9")]
    [InlineData("ALARM:1")]
    [InlineData("[MSG:INFO: FluidNC v3.7.0]")]
    [InlineData("Grbl 3.7 [FluidNC v3.7.0 (wifi) '$' for help]")]
    [InlineData("G1 X10 Y20 F500")]
    [InlineData("$$")]
    [InlineData("$120=2.000")]
    [InlineData("$H")]
    public void EverythingElse_IsLogged(string line)
    {
        Assert.True(DeviceExchangeFilter.ShouldLog(line));
    }
}
