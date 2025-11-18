using System;
using System.Globalization;
using System.Windows.Data;

namespace SystemCleaner.App.Converters
{
    /// <summary>
    /// Converts a percentage (0-100) and the available width into a pixel width for progress rendering.
    /// </summary>
    public sealed class PercentToActualWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values is null || values.Length < 2)
            {
                return 0d;
            }

            if (!TryGetDouble(values[0], out var width) || !TryGetDouble(values[1], out var percentage))
            {
                return 0d;
            }

            percentage = Math.Clamp(percentage, 0d, 100d);
            return width * (percentage / 100d);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
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
                default:
                    result = 0d;
                    return false;
            }
        }
    }
}
