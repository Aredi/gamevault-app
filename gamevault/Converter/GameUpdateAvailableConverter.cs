using Avalonia.Data.Converters;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gamevault.Converter
{
    internal class GameUpdateAvailableConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                if (value is not Game game)
                    return false;
                KeyValuePair<Game, string> installed = InstallViewModel.Instance.InstalledGames.FirstOrDefault(g => g.Key.ID == game.ID);
                return installed.Value != null && InstalledGameState.HasUpdate(game, installed.Value);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
