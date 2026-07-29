using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using ArctZ.Services.Device;
using System;
using System.Globalization;

namespace ArctZ.Converters;

public class ConnectionStateToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            ConnectionState.Connected => "HudAccentBrush",
            ConnectionState.Connecting or ConnectionState.Reconnecting => "HudWarningBrush",
            _ => "HudTextSecondaryBrush",
        };

        return Application.Current!.TryGetResource(key, ThemeVariant.Dark, out var brush)
            ? brush
            : Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
