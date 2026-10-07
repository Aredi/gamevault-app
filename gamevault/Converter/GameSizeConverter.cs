using gamevault.Localization;
using Avalonia.Data.Converters;
using gamevault.Models;
using System;
using System.Globalization;

namespace gamevault.Converter
{
    internal class GameSizeConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                if (value == null)
                {
                    throw new ArgumentException(Loc.T("Invalid input value"));
                }

                double size = double.Parse(value.ToString());

                // DEFAULTS TO IEC (1024) used for storage capacity etc.
                int baseValue = 1024;
                if (parameter != null && parameter is int)
                {
                    // Other Standard could be SI (1000) used for download speeds etc.
                    baseValue = (int)parameter;
                }

                string[] sizeSuffixes = { "B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };

                int suffixIndex = 0;
                while (size >= baseValue && suffixIndex < sizeSuffixes.Length - 1)
                {
                    size /= baseValue;
                    suffixIndex++;
                }

                size = Math.Round(size, 2);
                return $"{size.ToString(CultureInfo.CurrentCulture)} {gamevault.Localization.Loc.T(sizeSuffixes[suffixIndex])}";
            }
            catch (Exception ex)
            {
                GameVault.Core.Log.Ignored(ex);
                return "ERR";
            }
        }


        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return "";
        }
    }
}
