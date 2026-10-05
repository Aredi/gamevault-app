using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using System.Collections.Generic;
using System.Linq;

namespace gamevault.Helper
{
    internal class VisualHelper
    {
        internal static T? FindNextParentByType<T>(Visual child) where T : class
        {
            return child.GetVisualAncestors().OfType<T>().FirstOrDefault();
        }

        internal static IEnumerable<T> FindVisualChildren<T>(Visual? depObj) where T : Visual
        {
            if (depObj == null)
                return Enumerable.Empty<T>();
            return depObj.GetVisualDescendants().OfType<T>();
        }
    }
}
