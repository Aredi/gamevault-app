using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Text.RegularExpressions;

namespace gamevault.UserControls
{
    public partial class DateRangeSelector : UserControl
    {
        public string YearToPlaceholder { get; set; }
        public string YearFromPlaceholder { get; set; } = "1980";
        public event EventHandler EntriesUpdated;

        private static readonly Regex NonDigits = new Regex("[^0-9]+");

        public DateRangeSelector()
        {
            InitializeComponent();
            YearToPlaceholder = DateTime.Now.Year.ToString();
            this.DataContext = this;
            uiFilterYearFrom.AddHandler(TextInputEvent, YearSelector_Changed, RoutingStrategies.Tunnel);
            uiFilterYearTo.AddHandler(TextInputEvent, YearSelector_Changed, RoutingStrategies.Tunnel);
            uiContainer.LostFocus += StackPanel_LostFocus;
        }

        public bool IsValid()
        {
            return !string.IsNullOrEmpty(uiFilterYearFrom.Text) && !string.IsNullOrEmpty(uiFilterYearTo.Text);
        }

        public string GetYearFrom()
        {
            return uiFilterYearFrom.Text ?? "";
        }

        public string GetYearTo()
        {
            return uiFilterYearTo.Text ?? "";
        }

        public void ClearSelection()
        {
            uiFilterYearFrom.Text = string.Empty; uiFilterYearTo.Text = string.Empty;
        }

        private void StackPanel_LostFocus(object? sender, RoutedEventArgs e)
        {
            if (EntriesUpdated != null && IsValid())
                EntriesUpdated(this, e);
        }

        private void YearSelector_Changed(object? sender, TextInputEventArgs e)
        {
            string text = e.Text ?? "";
            e.Handled = (string.IsNullOrEmpty(((TextBox)sender!).Text) && text == "0") || NonDigits.IsMatch(text);
        }
    }
}
