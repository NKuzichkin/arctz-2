using System;
using System.Globalization;
using ArctZ.Services.Program;
using Avalonia.Data.Converters;
using Material.Icons;

namespace ArctZ.Converters;

public class MessageLevelToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        MessageLevel.Info => MaterialIconKind.InformationOutline,
        MessageLevel.Warning => MaterialIconKind.Alert,
        MessageLevel.Error => MaterialIconKind.AlertOctagon,
        _ => MaterialIconKind.InformationOutline,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
