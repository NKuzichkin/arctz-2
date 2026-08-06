using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ArctZ.Converters;

public class LabelLengthToFontSizeConverter : IValueConverter
{
    public static double ComputeFontSize(string? label)
    {
        var length = label?.Length ?? 0;
        return length switch
        {
            <= 10 => 16,
            <= 18 => 14,
            <= 26 => 12,
            _ => 10,
        };
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ComputeFontSize(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
