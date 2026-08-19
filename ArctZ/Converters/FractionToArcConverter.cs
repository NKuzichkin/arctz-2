using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ArctZ.Converters;

/// <summary>Converts a 0..1 "remaining" fraction into an open arc Geometry (stroked, not filled),
/// 12 o'clock start, clockwise sweep — the shrinking-ring key-point progress badge. 1.0 = full
/// ring (just arrived at the point, or transition not yet started), 0.0 = empty (dwell finished /
/// no dwell).</summary>
public sealed class FractionToArcConverter : IValueConverter
{
    private const double Diameter = 14;
    private const double Radius = Diameter / 2;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double fraction)
        {
            return null;
        }

        fraction = Math.Clamp(fraction, 0, 1);
        var center = new Point(Radius, Radius);

        if (fraction <= 0)
        {
            return Geometry.Parse($"M{Radius.ToString(CultureInfo.InvariantCulture)},{Radius.ToString(CultureInfo.InvariantCulture)} Z");
        }

        if (fraction >= 1)
        {
            return new EllipseGeometry(new Rect(0, 0, Diameter, Diameter));
        }

        var angle = fraction * 2 * Math.PI;
        var start = new Point(center.X, center.Y - Radius);
        var end = new Point(center.X + Radius * Math.Sin(angle), center.Y - Radius * Math.Cos(angle));

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments!.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(Radius, Radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = fraction > 0.5,
        });

        var geometry = new PathGeometry();
        geometry.Figures!.Add(figure);
        return geometry;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
