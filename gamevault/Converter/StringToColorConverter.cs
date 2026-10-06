using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace gamevault.Converter
{
    internal class StringToColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string stringValue)
            {
                // string.GetHashCode() is randomized per process on .NET Core, so names changed colour on every start.
                int hashCode = StableHash(stringValue);
                Color generatedColor = GenerateColor(hashCode);
                SolidColorBrush brush = new SolidColorBrush(generatedColor);
                                return brush;
            }
            return Brushes.Black;
        }
        private static int StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in value)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return (int)hash;
            }
        }
        private Color GenerateColor(int numericValue)
        {
            byte red = (byte)((numericValue >> 16) & 0xFF);
            byte green = (byte)((numericValue >> 8) & 0xFF);
            byte blue = (byte)(numericValue & 0xFF);

            // Ensure the color is saturated by setting the maximum component to 255
            byte maxComponent = Math.Max(red, Math.Max(green, blue));
            if (maxComponent > 0)
            {
                float scale = 200f / maxComponent;
                red = (byte)(red * scale);
                green = (byte)(green * scale);
                blue = (byte)(blue * scale);
            }

            return Color.FromRgb(red, green, blue);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return Colors.White;
        }
    }
}
