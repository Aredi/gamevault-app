using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.ViewModels;
using System;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>Ask for a game that is not on the server, and see what became of the requests.</summary>
    public partial class GameRequestsUserControl : UserControl
    {
        public GameRequestsUserControl() : this("") { }

        /// <param name="game">What was searched in the library and not found.</param>
        public GameRequestsUserControl(string game)
        {
            InitializeComponent();
            uiGame.Text = game;
            AttachedToVisualTree += async (_, _) =>
            {
                uiGame.Focus();
                await LoadAsync();
            };
        }

        /// <summary>The player's requests; also says whether the server takes requests at all.</summary>
        public async Task LoadAsync()
        {
            try
            {
                var requests = await GameRequestsService.ListAsync();
                bool available = requests != null;
                uiForm.IsVisible = available;
                uiUnavailable.IsVisible = !available;
                uiRequests.ItemsSource = requests;
                uiMine.IsVisible = requests?.Count > 0;
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                uiStatus.Text = Loc.T("The requests could not be loaded.");
            }
        }

        private async void Send_Click(object? sender, RoutedEventArgs e) => await SendAsync();

        private async void Game_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                await SendAsync();
        }

        public async Task SendAsync()
        {
            string game = uiGame.Text?.Trim() ?? "";
            if (game.Length == 0)
            {
                uiStatus.Text = Loc.T("Enter the name of the game.");
                return;
            }
            uiSend.IsEnabled = false;
            try
            {
                bool joined = await GameRequestsService.AskAsync(game, uiDetails.Text?.Trim());
                uiStatus.Text = joined
                    ? Loc.F("Someone already asked for '{0}': you are told too when it is added.", game)
                    : Loc.F("'{0}' is asked for.", game);
                uiGame.Text = "";
                uiDetails.Text = "";
                await LoadAsync();
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                uiStatus.Text = ex is InvalidOperationException ? ex.Message : Loc.T("The request could not be sent.");
            }
            uiSend.IsEnabled = true;
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => MainWindowViewModel.Instance.ClosePopup();
    }
}
