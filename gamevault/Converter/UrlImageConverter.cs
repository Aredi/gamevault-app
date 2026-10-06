using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using gamevault.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gamevault.Converter
{
    internal class UrlImageConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // Only local files / avares assets can be loaded synchronously; remote images go through CacheImage.
            try
            {
                string path = (string)value;
                if (path.StartsWith("avares://"))
                    return new Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri(path)));
                return System.IO.File.Exists(path) ? new Bitmap(path) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
