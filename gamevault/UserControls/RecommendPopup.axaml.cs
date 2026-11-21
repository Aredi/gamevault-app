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
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    public partial class RecommendPopup : UserControl
    {
        private readonly ChatGame game;
        private List<User> players = new();

        public RecommendPopup() : this(new ChatGame()) { }

        internal RecommendPopup(ChatGame game)
        {
            InitializeComponent();
            this.game = game;
            uiTitle.Text = Loc.F("Recommend {0}", game.Title);
            uiSearch.TextChanged += (_, _) => ShowPlayers();
            uiPlayers.SelectionChanged += (_, _) => uiSend.IsEnabled = uiPlayers.SelectedItem != null;
            uiSend.IsEnabled = false;
            AttachedToVisualTree += async (_, _) => await LoadPlayersAsync();
        }

        /// <summary>The server's other players.</summary>
        internal async Task LoadPlayersAsync()
        {
            try
            {
                int me = LoginManager.Instance.GetCurrentUser()?.ID ?? 0;
                var all = JsonSerializer.Deserialize<User[]>(await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/users")) ?? Array.Empty<User>();
                players = all.Where(u => u.DeletedAt == null && u.Activated != false && u.ID != me).OrderBy(u => u.Username, StringComparer.CurrentCultureIgnoreCase).ToList();
                ShowPlayers();
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                Say(Loc.T("The players could not be loaded."));
            }
        }

        private void ShowPlayers()
        {
            string search = uiSearch.Text?.Trim() ?? "";
            object? chosen = uiPlayers.SelectedItem;
            var shown = players.Where(p => search.Length == 0 || p.Username.Contains(search, StringComparison.CurrentCultureIgnoreCase)).ToList();
            uiPlayers.ItemsSource = shown;
            // The friend chosen stays chosen while still in the list
            if (chosen is User user && shown.Contains(user))
                uiPlayers.SelectedItem = user;
            uiSend.IsEnabled = uiPlayers.SelectedItem != null;
        }

        private void Say(string text)
        {
            uiStatus.Text = text;
            uiStatus.IsVisible = true;
        }

        private async void Send_Click(object? sender, RoutedEventArgs e) => await SendAsync();

        internal async Task SendAsync()
        {
            if (uiPlayers.SelectedItem is not User friend)
                return;
            uiSend.IsEnabled = false;
            try
            {
                await SocialService.RecommendAsync(friend.ID, game, uiNote.Text?.Trim() is { Length: > 0 } note ? note : null);
                MainWindowViewModel.Instance.AppBarText = Loc.F("{0} recommended to {1}", game.Title, friend.Username);
                MainWindowViewModel.Instance.ClosePopup();
            }
            catch (Exception ex)
            {
                Say(WebExceptionHelper.TryGetServerMessage(ex));
                uiSend.IsEnabled = true;
            }
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => MainWindowViewModel.Instance.ClosePopup();
    }
}
