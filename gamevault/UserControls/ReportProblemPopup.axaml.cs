using Avalonia.Controls;
using Avalonia.Interactivity;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    internal sealed class ReportCategory : ViewModelBase
    {
        public ReportCategory(string code) { Code = code; Text = ReportsService.CategoryText(code); }
        public string Code { get; }
        public string Text { get; }
        private bool isChecked;
        public bool IsChecked { get => isChecked; set { isChecked = value; OnPropertyChanged(); } }
    }

    /// <summary>A player reports a problem on a game (from its page or a right click on its cover): the administrators see it in their console.</summary>
    public partial class ReportProblemPopup : UserControl
    {
        private readonly string kind, title, context;
        private readonly int gameId;
        private readonly string? platform;
        private readonly List<ReportCategory> categories = ReportsService.Categories.Select(c => new ReportCategory(c)).ToList();

        public ReportProblemPopup() : this("pc", 0, null, "", "") { }

        private ReportProblemPopup(string kind, int gameId, string? platform, string title, string context)
        {
            InitializeComponent();
            (this.kind, this.gameId, this.platform, this.title, this.context) = (kind, gameId, platform, title, context);
            uiTitle.Text = title;
            uiCategories.ItemsSource = categories;
            uiContext.Text = Loc.F("Sent with it: {0}", context);
            AttachedToVisualTree += async (_, _) => await ShowMineAsync();
        }

        private static string Common => $"SanctuaryVault {SettingsViewModel.Instance.Version} · {RuntimeInformation.OSDescription}";

        internal static ReportProblemPopup For(Game game) =>
            new("pc", game.ID, null, game.Metadata?.Title ?? game.Title, string.IsNullOrEmpty(game.Version) ? Common : $"{Loc.F("Version {0}", game.Version)} · {Common}");

        internal static ReportProblemPopup For(RetroGame game) =>
            new("retro", game.Id, game.Platform.Slug, game.Name, $"{game.Platform.Name} · {game.FileName} · {Common}");

        internal static void Open(Game game) => MainWindowViewModel.Instance.OpenPopup(For(game));
        internal static void Open(RetroGame game) => MainWindowViewModel.Instance.OpenPopup(For(game));

        /// <summary>A right click on a game of the library (cover, row...) offers to report a problem on it.</summary>
        internal static void OfferOnRightClick(Control library) => library.ContextRequested += (_, e) =>
        {
            if (e.Source is not Control { DataContext: Game or RetroGame } source)
                return;
            var item = new MenuItem { Header = Loc.T("Report a problem") };
            item.Click += (_, _) =>
            {
                if (source.DataContext is Game game)
                    Open(game);
                else if (source.DataContext is RetroGame retro)
                    Open(retro);
            };
            new ContextMenu { ItemsSource = new[] { item } }.Open(source);
            e.Handled = true;
        };

        internal IReadOnlyList<ReportCategory> Categories => categories;
        internal TextBox Details => uiDetails;

        private async Task ShowMineAsync()
        {
            try
            {
                var mine = (await ReportsService.ListAsync())?.Where(r => r.UserId == LoginManager.Instance.GetCurrentUser()?.ID && r.Kind == kind && r.GameId == gameId && r.Platform == platform).ToList();
                uiMine.ItemsSource = mine;
                uiMine.IsVisible = mine?.Count > 0;
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => MainWindowViewModel.Instance.ClosePopup();

        private async void Send_Click(object? sender, RoutedEventArgs e) => await SendAsync();

        internal async Task SendAsync()
        {
            string? category = categories.FirstOrDefault(c => c.IsChecked)?.Code;
            string details = uiDetails.Text?.Trim() ?? "";
            if (category == null || category == "other" && details.Length == 0)
            {
                uiStatus.Text = category == null ? Loc.T("Choose what the problem is.") : Loc.T("Describe the problem.");
                return;
            }
            uiSend.IsEnabled = false;
            try
            {
                await ReportsService.SendAsync(kind, gameId, platform, title, category, details.Length > 0 ? details : null, context);
                MainWindowViewModel.Instance.ClosePopup();
                MainWindowViewModel.Instance.AppBarText = Loc.T("Thank you! Your report was sent to the administrators.");
            }
            catch (Exception ex) { uiStatus.Text = WebExceptionHelper.TryGetServerMessage(ex); }
            finally { uiSend.IsEnabled = true; }
        }
    }
}
