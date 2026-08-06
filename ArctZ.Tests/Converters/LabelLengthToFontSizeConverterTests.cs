using ArctZ.Converters;

namespace ArctZ.Tests.Converters;

public class LabelLengthToFontSizeConverterTests
{
    [Theory]
    [InlineData(null, 16)]
    [InlineData("", 16)]
    [InlineData("Точка 1", 16)]           // 7 симв. -> <=10
    [InlineData("1234567890", 16)]        // ровно 10 -> <=10
    [InlineData("12345678901", 14)]       // 11 симв. -> <=18
    [InlineData("123456789012345678", 14)] // ровно 18 -> <=18
    [InlineData("1234567890123456789", 12)] // 19 симв. -> <=26
    [InlineData("12345678901234567890123456", 12)] // ровно 26 -> <=26
    [InlineData("123456789012345678901234567", 10)] // 27 симв. -> <=30
    [InlineData("123456789012345678901234567890", 10)] // ровно 30 -> <=30
    public void ComputeFontSize_ReturnsExpectedStep(string? label, double expected)
    {
        var fontSize = LabelLengthToFontSizeConverter.ComputeFontSize(label);

        Assert.Equal(expected, fontSize);
    }
}
