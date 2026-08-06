using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ArctZ.Converters;

public class KeyPointIsExecutingConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count != 2 || values[0] is not Guid tileId || values[1] is not Guid executingId)
        {
            return false;
        }

        return tileId == executingId;
    }
}
