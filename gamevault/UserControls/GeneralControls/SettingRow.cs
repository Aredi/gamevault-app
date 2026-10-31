using Avalonia;
using Avalonia.Controls;

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
