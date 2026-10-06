using Avalonia.Data.Converters;
using gamevault.Models;
using gamevault.Localization;
using System;
using System.ComponentModel;
using System.Globalization;

namespace gamevault.Converter
{
    internal class GameStateDescriptionConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            State enumValue = (State)Enum.Parse(typeof(State), value as string);
            string result = ((DescriptionAttribute[])typeof(State).GetField(((State)enumValue).ToString()).GetCustomAttributes(typeof(DescriptionAttribute), false))[0].Description.ToString();
            return Loc.T(result);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return string.Empty;
        }
    }
}
