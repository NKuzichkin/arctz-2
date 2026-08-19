using System;
using System.Globalization;
using ArctZ.Converters;

namespace ArctZ.Tests.Converters;

public class KeyPointHasTimeWarningConverterTests
{
    [Fact]
    public void Convert_ReturnsTrue_WhenTileIsExecutingAndHasWarning()
    {
        var id = Guid.NewGuid();
        var converter = new KeyPointHasTimeWarningConverter();

        var result = converter.Convert(new object?[] { id, (Guid?)id, true }, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(true, result);
    }

    [Fact]
    public void Convert_ReturnsFalse_WhenExecutingButNoWarning()
    {
        var id = Guid.NewGuid();
        var converter = new KeyPointHasTimeWarningConverter();

        var result = converter.Convert(new object?[] { id, (Guid?)id, false }, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }

    [Fact]
    public void Convert_ReturnsFalse_WhenWarningButNotExecuting()
    {
        var converter = new KeyPointHasTimeWarningConverter();

        var result = converter.Convert(new object?[] { Guid.NewGuid(), (Guid?)Guid.NewGuid(), true }, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }

    [Fact]
    public void Convert_ReturnsFalse_WhenExecutingIdIsNull()
    {
        var id = Guid.NewGuid();
        var converter = new KeyPointHasTimeWarningConverter();

        var result = converter.Convert(new object?[] { id, null, true }, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }
}
