using Avalonia.Data.Converters;
using gamevault.Helper;
using gamevault.Models;
using System;
using System.Diagnostics;
using System.Globalization;

namespace gamevault.Converter
{
    internal class LibraryPermissionRoleToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            //Debug.WriteLine("PermRoleVis");
            if ((LoginManager.Instance.GetCurrentUser() != null && LoginManager.Instance.GetCurrentUser().Role >= PERMISSION_ROLE.EDITOR))
            {
                return true;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return false;
        }
    }
}
