using Avalonia.Data.Converters;
using gamevault.Helper;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gamevault.Converter
{
    internal class GameSettingsTabVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (((Game)value) == null)
                return false;
            if (parameter.ToString() == "local")
            {
                if (InstallViewModel.Instance.InstalledGames.Where(g => g.Key.ID == ((Game)value).ID).Count() > 0)
                {
                    return true;
                }
            }
            else if (parameter.ToString() == "server")
            {
                if (LoginManager.Instance.IsLoggedIn() && (LoginManager.Instance.GetCurrentUser() != null && LoginManager.Instance.GetCurrentUser().Role >= PERMISSION_ROLE.EDITOR))
                {
                    return true;
                }
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return false;
        }
    }
}
