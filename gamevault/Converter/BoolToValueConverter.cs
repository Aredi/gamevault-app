using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace gamevault.Converter
{
    /// <summary>
    /// Returns <see cref="TrueValue"/> or <see cref="FalseValue"/>; replaces the many WPF DataTriggers that only swapped a value.
    /// </summary>
    public class BoolToValueConverter : IValueConverter
    {
        public object? TrueValue { get; set; }
        public object? FalseValue { get; set; }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? TrueValue : FalseValue;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return Equals(value, TrueValue);
        }
    }
}
