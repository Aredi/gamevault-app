using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace gamevault.UserControls
{
    /// <summary>
    /// Shows a small badge over the top right corner of its content (replacement for MahApps' Badged).
    /// The badge is hidden while <see cref="Badge"/> is null or an empty string.
    /// </summary>
    public class Badged : ContentControl
    {
        public static readonly StyledProperty<object?> BadgeProperty =
            AvaloniaProperty.Register<Badged, object?>(nameof(Badge));
        public static readonly StyledProperty<IBrush?> BadgeBackgroundProperty =
            AvaloniaProperty.Register<Badged, IBrush?>(nameof(BadgeBackground), Brushes.Red);
        public static readonly DirectProperty<Badged, bool> HasBadgeProperty =
            AvaloniaProperty.RegisterDirect<Badged, bool>(nameof(HasBadge), o => o.HasBadge);

        private bool hasBadge;

        public object? Badge
        {
            get => GetValue(BadgeProperty);
            set => SetValue(BadgeProperty, value);
        }
        public IBrush? BadgeBackground
        {
            get => GetValue(BadgeBackgroundProperty);
            set => SetValue(BadgeBackgroundProperty, value);
        }
        public bool HasBadge
        {
            get => hasBadge;
            private set => SetAndRaise(HasBadgeProperty, ref hasBadge, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == BadgeProperty)
            {
                HasBadge = Badge is string s ? s.Length > 0 : Badge != null;
            }
        }
    }
}
