using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;

namespace gamevault.UserControls
{
    /// <summary>
    /// One setting: its title and hint on the left, its control on the right. The look is defined by the
    /// SettingRow ControlTheme in Resources/Assets/Base.axaml; the rows of a card go in a Border.rows.
    /// </summary>
    public class SettingRow : ContentControl
    {
        public static readonly StyledProperty<string?> TitleProperty =
            AvaloniaProperty.Register<SettingRow, string?>(nameof(Title));
        public static readonly StyledProperty<string?> HintProperty =
            AvaloniaProperty.Register<SettingRow, string?>(nameof(Hint));

        protected override System.Type StyleKeyOverride => typeof(SettingRow);

        private ContentPresenter? content;
        private bool? stacked;

        protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            content = e.NameScope.Find<ContentPresenter>("PART_Content");
            stacked = null;
            Stack(Bounds.Width);
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            Stack(e.NewSize.Width);
        }

        /// <summary>A narrow page (a phone): the control under the title, on its own line, not squeezing it to a column of letters.</summary>
        private void Stack(double width)
        {
            bool narrow = width > 0 && width < 480;
            if (content == null || stacked == narrow)
                return;
            stacked = narrow;
            Grid.SetRow(content, narrow ? 1 : 0);
            Grid.SetColumn(content, narrow ? 0 : 1);
            Grid.SetColumnSpan(content, narrow ? 2 : 1);
            content.HorizontalAlignment = narrow ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Stretch;
            content.Margin = narrow ? new Thickness(0, 8, 0, 0) : default;
        }

        public string? Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }
        public string? Hint
        {
            get => GetValue(HintProperty);
            set => SetValue(HintProperty, value);
        }
    }
}
