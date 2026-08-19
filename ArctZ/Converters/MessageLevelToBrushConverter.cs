using System;
using System.Globalization;
using ArctZ.Services.Program;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace ArctZ.Converters;

public class MessageLevelToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            // Warning and Error share HudWarningBrush: the palette deliberately keeps a single
            // "attention" amber (see Colors.axaml) rather than a full traffic-light set.
            MessageLevel.Warning or MessageLevel.Error => "HudWarningBrush",
            _ => "HudTextSecondaryBrush",
        };

        return Application.Current!.TryGetResource(key, ThemeVariant.Dark, out var brush)
            ? brush
            : Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
