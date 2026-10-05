using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using System;

namespace gamevault.UserControls
{
    public enum ButtonKind
    {
        Primary,
        Skeleton,
        Danger
    }

    /// <summary>
    /// Button with an optional geometry icon in front of its text. The look is defined by the
    /// IconButton ControlTheme in Resources/Assets/Base.axaml; <see cref="Kind"/> maps to the
    /// :primary, :skeleton and :danger pseudo classes.
    /// </summary>
    public class IconButton : Button
    {
        public static readonly StyledProperty<Geometry?> IconProperty =
            AvaloniaProperty.Register<IconButton, Geometry?>(nameof(Icon));
        public static readonly StyledProperty<string?> TextProperty =
            AvaloniaProperty.Register<IconButton, string?>(nameof(Text));
        public static readonly StyledProperty<ButtonKind> KindProperty =
            AvaloniaProperty.Register<IconButton, ButtonKind>(nameof(Kind), ButtonKind.Primary);
        public static readonly StyledProperty<bool> OverrideIconTransformProperty =
            AvaloniaProperty.Register<IconButton, bool>(nameof(OverrideIconTransform), true);
        public static readonly StyledProperty<double> IconScaleProperty =
            AvaloniaProperty.Register<IconButton, double>(nameof(IconScale), 1.0);
        public static readonly StyledProperty<Thickness> IconMarginProperty =
            AvaloniaProperty.Register<IconButton, Thickness>(nameof(IconMargin));

        private Path? iconPart;

        protected override Type StyleKeyOverride => typeof(IconButton);

        public Geometry? Icon
        {
            get => GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }
        public string? Text
        {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }
        public ButtonKind Kind
        {
            get => GetValue(KindProperty);
            set => SetValue(KindProperty, value);
        }
        public bool OverrideIconTransform
        {
            get => GetValue(OverrideIconTransformProperty);
            set => SetValue(OverrideIconTransformProperty, value);
        }
        public double IconScale
        {
            get => GetValue(IconScaleProperty);
            set => SetValue(IconScaleProperty, value);
        }
        public Thickness IconMargin
        {
            get => GetValue(IconMarginProperty);
            set => SetValue(IconMarginProperty, value);
        }

        public IconButton()
        {
            UpdateKind();
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            iconPart = e.NameScope.Find<Path>("PART_Icon");
            UpdateIconTransform();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == KindProperty)
            {
                UpdateKind();
            }
            else if (change.Property == IconScaleProperty || change.Property == IconMarginProperty || change.Property == OverrideIconTransformProperty)
            {
                UpdateIconTransform();
            }
        }

        private void UpdateKind()
        {
            PseudoClasses.Set(":primary", Kind == ButtonKind.Primary);
            PseudoClasses.Set(":skeleton", Kind == ButtonKind.Skeleton);
            PseudoClasses.Set(":danger", Kind == ButtonKind.Danger);
        }

        private void UpdateIconTransform()
        {
            if (iconPart == null)
                return;
            if (OverrideIconTransform)
            {
                iconPart.RenderTransform = new ScaleTransform(IconScale, IconScale);
                iconPart.Margin = IconMargin;
            }
            else
            {
                iconPart.RenderTransform = null;
                iconPart.Margin = default;
            }
        }
    }
}
