using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace gamevault.Localization
{
    /// <summary>Shows a text kept in English by the code (e.g. a download state the code compares) translated.</summary>
    public class LocConverter : IValueConverter
    {
        public static readonly LocConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is string text ? Loc.T(text) : value;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
    }
}
