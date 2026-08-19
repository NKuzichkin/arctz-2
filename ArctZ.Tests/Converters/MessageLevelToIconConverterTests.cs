using System.Globalization;
using ArctZ.Converters;
using ArctZ.Services.Program;
using Material.Icons;

namespace ArctZ.Tests.Converters;

public class MessageLevelToIconConverterTests
{
    [Theory]
    [InlineData(MessageLevel.Info, MaterialIconKind.InformationOutline)]
    [InlineData(MessageLevel.Warning, MaterialIconKind.Alert)]
    [InlineData(MessageLevel.Error, MaterialIconKind.AlertOctagon)]
    public void Convert_MapsEachLevelToItsIcon(MessageLevel level, MaterialIconKind expected)
    {
        var converter = new MessageLevelToIconConverter();

        var result = converter.Convert(level, typeof(MaterialIconKind), null, CultureInfo.InvariantCulture);

        Assert.Equal(expected, result);
    }
}
