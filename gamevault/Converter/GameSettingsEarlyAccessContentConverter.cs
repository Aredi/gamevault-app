using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gamevault.Converter
{
    internal class GameSettingsEarlyAccessContentConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values[1]!=null)
            {
                return values[1];
            }
            return values[0];
        }
    }
}
