using Avalonia.Data.Converters;
using gamevault.Helper;
using gamevault.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gamevault.Converter
{
    internal class PermissionRoleToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if ((int)value == 0 || (LoginManager.Instance.GetCurrentUser() != null && LoginManager.Instance.GetCurrentUser().Role == PERMISSION_ROLE.ADMIN))
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
