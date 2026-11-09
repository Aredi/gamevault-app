using gamevault.Localization;
using Avalonia.Data.Converters;
using gamevault.Models;
using System;
using System.Globalization;

namespace gamevault.Converter
{
    internal class GameSizeConverter : IValueConverter
    {
        private static readonly string[] Suffixes = { "B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };

        /// <summary>The one way a size is written: "35,85 Go", its units in the interface's language.</summary>
        public static string Text(double bytes, int unit = 1024)
        {
            int index = 0;
            while (bytes >= unit && index < Suffixes.Length - 1)
            {
                bytes /= unit;
                index++;
            }
            return $"{Math.Round(bytes, 2).ToString(CultureInfo.CurrentCulture)} {Loc.T(Suffixes[index])}";
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                if (value == null)
                {
                    throw new ArgumentException(Loc.T("Invalid input value"));
                }

                // IEC (1024) by default, for storage; SI (1000) as the parameter, for download speeds
                return Text(double.Parse(value.ToString()!), parameter is int unit ? unit : 1024);
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
