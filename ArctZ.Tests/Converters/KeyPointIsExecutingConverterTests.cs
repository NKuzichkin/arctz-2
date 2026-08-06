using System;
using System.Globalization;
using ArctZ.Converters;

namespace ArctZ.Tests.Converters;

public class KeyPointIsExecutingConverterTests
{
    [Fact]
    public void Convert_ReturnsTrue_WhenTileIdMatchesExecutingId()
    {
        var id = Guid.NewGuid();
        var converter = new KeyPointIsExecutingConverter();

        var result = converter.Convert(new object?[] { id, (Guid?)id }, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(true, result);
    }

    [Fact]
    public void Convert_ReturnsFalse_WhenIdsDiffer()
    {
        var converter = new KeyPointIsExecutingConverter();

        var result = converter.Convert(new object?[] { Guid.NewGuid(), (Guid?)Guid.NewGuid() }, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }

    [Fact]
    public void Convert_ReturnsFalse_WhenExecutingIdIsNull()
    {
        var id = Guid.NewGuid();
        var converter = new KeyPointIsExecutingConverter();

        var result = converter.Convert(new object?[] { id, null }, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }
}
