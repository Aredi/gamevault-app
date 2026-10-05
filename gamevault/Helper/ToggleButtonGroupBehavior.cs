using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using System.Linq;

namespace gamevault.Helper
{
    /// <summary>
    /// The WPF client emulated a radio group with ToggleButtons; the Avalonia views use RadioButtons
    /// with a GroupName (styled as toggle buttons). These helpers find them in the visual tree.
    /// </summary>
    public static class ToggleButtonGroupBehavior
    {
        public static ToggleButton? GetCheckedToggleButton(string groupName, Visual? searchRoot)
        {
            return searchRoot?.GetVisualDescendants().OfType<RadioButton>()
                .FirstOrDefault(r => r.GroupName == groupName && r.IsChecked == true);
        }

        public static void CheckToggleButtonByIndex(string groupName, Visual? searchRoot, int index)
        {
            if (searchRoot == null || index < 0)
                return;
            var buttons = searchRoot.GetVisualDescendants().OfType<RadioButton>().Where(r => r.GroupName == groupName).ToList();
            if (index < buttons.Count)
                buttons[index].IsChecked = true;
        }
    }
}
