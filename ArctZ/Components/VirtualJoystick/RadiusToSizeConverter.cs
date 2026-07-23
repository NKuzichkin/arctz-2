using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace ArctZ.Components.VirtualJoystick;

public class RadiusToSizeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double radius)
            return radius * 2.0;
        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double size)
            return size / 2.0;
        return 0.0;
    }
}
