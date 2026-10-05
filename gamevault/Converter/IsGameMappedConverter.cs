using Avalonia.Data.Converters;
using gamevault.Models;
using gamevault.Models.Mapping;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gamevault.Converter
{
    internal class IsGameMappedConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object parameter, CultureInfo culture)
        {
            object result = ConvertCore(values);
            if (parameter?.ToString() == "opacity")
                return result is true ? 1.0 : 0.3;
            return result;
        }
        private object ConvertCore(IList<object?> values)
        {
            try
            {
                if (values[1].GetType() == typeof(GameMetadata))
                {
                    return ((List<GameMetadata>)values[0]).Any(entry => entry.ProviderSlug == ((GameMetadata)values[1]).ProviderSlug);
                }
                else if(values[1].GetType() == typeof(MetadataProviderDto))
                {
                    return ((List<GameMetadata>)values[0]).Any(entry => entry.ProviderSlug == ((MetadataProviderDto)values[1]).Slug);
                }
                else
                {
                    return false;
                }
            }
            catch { return false; }
        }
    }
}
