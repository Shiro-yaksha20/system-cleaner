using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SystemCleaner.App.Converters
{
    /// <summary>
    /// Creates a StreamGeometry representing a circular arc based on a percentage value.
    /// </summary>
    public sealed class UsagePercentToArcGeometryConverter : IValueConverter
    {
        /// <summary>
        /// Starting angle for the gauge in degrees. Default is -135 to create a 270° sweep.
        /// </summary>
        public double StartAngle { get; set; } = -135d;

        /// <summary>
        /// Total sweep angle for the gauge in degrees. Default is 270° to leave a 90° gap.
        /// </summary>
        public double SweepAngle { get; set; } = 270d;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || value == DependencyProperty.UnsetValue)
            {
                return Geometry.Empty;
            }

            if (!TryGetDouble(value, out var percent))
            {
                return Geometry.Empty;
            }

            percent = Math.Clamp(percent, 0d, 100d);

            if (percent <= 0d)
            {
                return Geometry.Empty;
            }

            var size = 140d;
            if (parameter != null && TryGetDouble(parameter, out var parsedSize) && parsedSize > 0d)
            {
                size = parsedSize;
            }

            var radius = size / 2d;
            var center = new Point(radius, radius);

            var sweep = SweepAngle * (percent / 100d);
            var endAngle = StartAngle + sweep;
            var startPoint = PointOnCircle(center, radius, StartAngle);
            var endPoint = PointOnCircle(center, radius, endAngle);

            var isLargeArc = sweep > 180d;

            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(startPoint, false, false);
                context.ArcTo(endPoint,
                              new Size(radius, radius),
                              rotationAngle: 0,
                              isLargeArc,
                              SweepDirection.Clockwise,
                              isStroked: true,
                              isSmoothJoin: false);
            }

            geometry.Freeze();
            return geometry;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

        private static Point PointOnCircle(Point center, double radius, double angleDegrees)
        {
            var angleRadians = angleDegrees * Math.PI / 180d;
            var x = center.X + radius * Math.Cos(angleRadians);
            var y = center.Y + radius * Math.Sin(angleRadians);
            return new Point(x, y);
        }

        private static bool TryGetDouble(object value, out double result)
        {
            switch (value)
            {
                case double d:
                    result = d;
                    return true;
                case float f:
                    result = f;
                    return true;
                case int i:
                    result = i;
                    return true;
                case long l:
                    result = l;
                    return true;
                case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                    result = parsed;
                    return true;
                default:
                    result = 0d;
                    return false;
            }
        }
    }
}
