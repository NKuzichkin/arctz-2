using ArctZ.Services.Device;
using ArctZ.Services.Program;

namespace ArctZ.Tests.Services.Program;

public class InverseTimeMoveTests
{
    [Fact]
    public void Line_FifteenSeconds_EmitsG93WithInverseFeedOfFour()
    {
        var line = InverseTimeMove.Line(new MachinePose(60, 0, 0, 0), 15);

        Assert.Equal("G93 G1 X60 Y0 Z0 A0 F4", line);
    }

    [Fact]
    public void Line_FormatsCoordinatesWithThreeDecimalsAndInvariantCulture()
    {
        var line = InverseTimeMove.Line(new MachinePose(12.345, -6.7, 0, 90), 7.5);

        Assert.Equal("G93 G1 X12.345 Y-6.7 Z0 A90 F8", line);
    }

    /// <summary>Час перехода даёт F0.0166667 — формат "0.###" округлил бы до 0.017 (ошибка ~2%).</summary>
    [Fact]
    public void Line_OneHourTransition_KeepsSevenDecimalsOfFeedPrecision()
    {
        var line = InverseTimeMove.Line(new MachinePose(60, 0, 0, 0), 3600);

        Assert.Equal("G93 G1 X60 Y0 Z0 A0 F0.0166667", line);
    }

    /// <summary>Ноль — это старый файл программы или пустое поле ввода, а не «максимально быстро».</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    public void Line_NonPositiveSeconds_FallsBackToTheDefaultFiveSeconds(double seconds)
    {
        var line = InverseTimeMove.Line(new MachinePose(60, 0, 0, 0), seconds);

        Assert.Equal("G93 G1 X60 Y0 Z0 A0 F12", line);
    }

    [Theory]
    [InlineData(0.0, 5.0)]
    [InlineData(-1.0, 5.0)]
    [InlineData(12.5, 12.5)]
    public void EffectiveSeconds_ReplacesNonPositiveValuesOnly(double input, double expected)
    {
        Assert.Equal(expected, InverseTimeMove.EffectiveSeconds(input));
    }
}
