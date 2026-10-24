using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>Create a room (its game among the LAN games) or join one, then shows it on the Rooms page.</summary>
    public partial class RoomJoinPopup : UserControl
    {
        private readonly int? gameId;

        public RoomJoinPopup() : this(null) { }

        /// <param name="game">The game page's game: chosen for the new room.</param>
        public RoomJoinPopup(Game? game)
        {
            InitializeComponent();
            gameId = game?.ID;
            string me = LoginManager.Instance.GetCurrentUser()?.Username ?? "";
            uiName.Text = game != null ? Loc.F("{0} with {1}", game.Title, me) : Loc.F("{0}'s room", me);
            AttachedToVisualTree += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            try
            {
                var games = await RoomsUserControl.LanGamesAsync();
                uiGame.ItemsSource = games;
                uiGame.SelectedItem = games.FirstOrDefault(g => g.ID == gameId);
                var open = (await RoomsService.ListAsync()).Where(r => r.Public && !r.Member && r.Members.Count < r.MaxMembers).ToList();
                uiOpen.ItemsSource = open;
                uiOpenTitle.IsVisible = open.Count > 0;
            }
            catch (Exception ex) { uiStatus.Text = ex.Message; }
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => MainWindowViewModel.Instance.ClosePopup();

        private async Task EnterAsync(Func<string, Task<RoomItem>> ask)
        {
            uiCreate.IsEnabled = uiJoin.IsEnabled = false;
            try
            {
                RoomItem room = await RoomsService.EnterAsync(ask, text => uiStatus.Text = text);
                MainWindowViewModel.Instance.ClosePopup();
                MainWindowViewModel.Instance.SetActiveControl(MainControl.Rooms);
                await MainWindowViewModel.Instance.Rooms.LoadAsync(room.Id);
            }
            catch (Exception ex) { uiStatus.Text = ex.Message; }
            finally { uiCreate.IsEnabled = uiJoin.IsEnabled = true; }
        }

        private async void Create_Click(object? sender, RoutedEventArgs e)
        {
            string name = uiName.Text?.Trim() ?? "";
            if (name.Length == 0)
            {
                uiStatus.Text = Loc.T("Give the room a name.");
                return;
            }
            Game? game = uiGame.SelectedItem as Game;
            bool isPublic = uiPublic.IsChecked == true;
            await EnterAsync(node => RoomsService.CreateAsync(name, game?.ID, game?.Title, isPublic, node));
        }

        private async void Join_Click(object? sender, RoutedEventArgs e) => await JoinByCodeAsync();

        private async void Code_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                await JoinByCodeAsync();
        }

        private async Task JoinByCodeAsync()
        {
            string code = uiCode.Text?.Trim() ?? "";
            if (code.Length == 0)
            {
                uiStatus.Text = Loc.T("Enter the room's code.");
                return;
            }
            await EnterAsync(node => RoomsService.JoinAsync(null, code, node));
        }

        private async void Open_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is RoomItem room)
                await EnterAsync(node => RoomsService.JoinAsync(room.Id, null, node));
        }
    }
}
