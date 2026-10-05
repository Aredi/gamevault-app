using Avalonia.Data.Converters;
using gamevault.Models;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace gamevault.Converter
{
    /// <summary>
    /// values[0]: Game, values[1]: SettingsViewModel.ShowMappedTitle.
    /// Shows the metadata title when enabled and available, the file based title otherwise.
    /// </summary>
    internal class MappedTitleConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count == 0 || values[0] is not Game game)
                return string.Empty;
            bool showMapped = values.Count > 1 && values[1] is bool b && b;
            if (showMapped && !string.IsNullOrEmpty(game.Metadata?.Title))
                return game.Metadata!.Title;
            return game.Title;
        }
    }
}
