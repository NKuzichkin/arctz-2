using ArctZ.Services.Diagnostics;

namespace ArctZ.Tests.Services.Diagnostics;

public class ByteSizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 Б")]
    [InlineData(512, "512 Б")]
    [InlineData(1023, "1023 Б")]
    public void Format_LeavesSmallSizesInBytes(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSizeFormatter.Format(bytes));
    }

    [Theory]
    [InlineData(1024, "1,0 КБ")]
    [InlineData(1536, "1,5 КБ")]
    [InlineData(1048576, "1,0 МБ")]
    [InlineData(155189248, "148,0 МБ")]
    [InlineData(34252226560, "31,9 ГБ")]
    [InlineData(1099511627776, "1,0 ТБ")]
    public void Format_ScalesToTheLargestUnitThatKeepsTheNumberReadable(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSizeFormatter.Format(bytes));
    }

    [Fact]
    public void Format_UsesACommaAsTheDecimalSeparatorRegardlessOfMachineCulture()
    {
        Assert.Contains(",", ByteSizeFormatter.Format(1536));
        Assert.DoesNotContain(".", ByteSizeFormatter.Format(1536));
    }

    [Fact]
    public void Format_ClampsNegativeSizesToZero()
    {
        Assert.Equal("0 Б", ByteSizeFormatter.Format(-1));
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(39, 100, 39)]
    [InlineData(100, 100, 100)]
    [InlineData(1, 3, 33)]
    public void Percent_RoundsToAWholeNumber(long part, long total, int expected)
    {
        Assert.Equal(expected, ByteSizeFormatter.Percent(part, total));
    }

    [Fact]
    public void Percent_IsUnknownWhenTheTotalIsNotPositive()
    {
        Assert.Null(ByteSizeFormatter.Percent(5, 0));
    }
}
