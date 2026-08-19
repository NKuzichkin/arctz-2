using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ArctZ.Converters;

public class KeyPointHasTimeWarningConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count != 3 || values[0] is not Guid tileId || values[1] is not Guid executingId || values[2] is not bool hasWarning)
        {
            return false;
        }

        return tileId == executingId && hasWarning;
    }
}
