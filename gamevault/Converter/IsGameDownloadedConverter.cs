using Avalonia.Data.Converters;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gamevault.Converter
{
    internal class IsGameDownloadedConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            //Debug.WriteLine("IsDownloaded");
            if (value == null)
                return false;
            bool downloaded = DownloadsViewModel.Instance.DownloadedGames.Any(gameUC => gameUC.GetGameId() == (int)value);
            return parameter?.ToString() == "invert" ? !downloaded : downloaded;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return false;
        }
    }
}
