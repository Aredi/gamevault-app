using Avalonia;
using Avalonia.Controls.Primitives;

namespace gamevault.UserControls
{
    /// <summary>
    /// Indeterminate spinner (replacement for MahApps' ProgressRing). Look defined in Base.axaml.
    /// </summary>
    public class ProgressRing : TemplatedControl
    {
        public static readonly StyledProperty<bool> IsActiveProperty =
            AvaloniaProperty.Register<ProgressRing, bool>(nameof(IsActive), true);

        public bool IsActive
        {
            get => GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }
    }
}
