using Avalonia.Data.Converters;
using GameVault.Core;
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
    internal class ContainsCurrentUserConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                // Most games have no bookmarks (null): no exception to throw and catch for each card
                if (value is List<User> users && LoginManager.Instance.GetCurrentUser() is User current)
                    return users.Any(u => u.ID == current.ID);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return false;
        }
    }
}
