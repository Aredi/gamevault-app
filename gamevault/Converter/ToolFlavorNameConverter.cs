using Avalonia.Data.Converters;
using GameVault.Core.Compatibility;
using System;
using System.Globalization;

namespace gamevault.Converter
{
    internal class ToolFlavorNameConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is ToolFlavor flavor ? ToolCatalog.DisplayName(flavor) : value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
