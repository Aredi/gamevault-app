using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
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
    /// <summary>
    /// The LAN rooms: the player's and the public ones on the left, the chosen one on the right with its code, its game
    /// and its members (their address on the room's network, and how this PC reaches theirs).
    /// </summary>
    public partial class RoomsUserControl : UserControl
    {
        public sealed record MemberRow(string Name, string Initial, string Ip, string State, IBrush Dot);

        private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(10) };
        private string? selectedId;
        private bool showing;

        public RoomsUserControl()
        {
            InitializeComponent();
            refresh.Tick += async (_, _) => await LoadAsync();
            AttachedToVisualTree += async (_, _) =>
            {
                Focus();
                refresh.Start();
                await LoadAsync();
            };
            DetachedFromVisualTree += (_, _) => refresh.Stop();
        }

        internal List<RoomItem> Rooms { get; private set; } = new();
        internal RoomItem? Selected => Rooms.FirstOrDefault(r => r.Id == selectedId);

        /// <summary>The rooms again, the chosen one kept.</summary>
        internal async Task LoadAsync(string? select = null)
        {
            selectedId = select ?? selectedId;
            bool ready = ZeroTierAgent.IsInstalled && ZeroTierAgent.HasAccess;
            uiSetup.IsVisible = !ready;
            string? myNode = null;
            Dictionary<string, ZeroTierAgent.Peer> peers = new();
            if (ready)
            {
                try
                {
                    var (address, online) = await ZeroTierAgent.StatusAsync();
                    myNode = address;
                    peers = await ZeroTierAgent.PeersAsync();
                    ShowAgent(online ? Loc.T("ZeroTier ready") : Loc.T("ZeroTier offline"), online ? "Brush.Success" : "Brush.Warning");
                }
                catch (Exception ex)
                {
                    Log.Ignored(ex);
                    ShowAgent(Loc.T("ZeroTier is not running"), "Brush.Warning");
                }
            }
            else
                ShowAgent(ZeroTierAgent.IsInstalled ? Loc.T("ZeroTier to set up") : Loc.T("ZeroTier not installed"), "Brush.Muted");

            try
            {
                Rooms = await RoomsService.ListAsync();
                uiStatus.IsVisible = false;
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                ShowStatus(ex is NotSupportedException or InvalidOperationException ? ex.Message : Loc.T("The SanctuaryVault service could not be reached."));
                uiCreate.IsEnabled = uiJoin.IsEnabled = ex is not NotSupportedException;
                return;
            }
            uiCreate.IsEnabled = uiJoin.IsEnabled = true;
            showing = true;
            uiRooms.ItemsSource = Rooms;
            uiRooms.SelectedItem = Selected ?? Rooms.FirstOrDefault(r => r.Member);
            showing = false;
            uiEmpty.IsVisible = Rooms.Count == 0;
            await ShowAsync(uiRooms.SelectedItem as RoomItem, myNode, peers);
        }

        private void ShowAgent(string text, string brush)
        {
            uiAgentState.Text = text;
            uiAgentDot.Background = this.TryFindResource(brush, out object? found) && found is IBrush b ? b : Brushes.Gray;
        }

        private void ShowStatus(string text)
        {
            uiStatus.Text = text;
            uiStatus.IsVisible = text.Length > 0;
        }

        private int? shownGame;

        private async Task ShowAsync(RoomItem? room, string? myNode, Dictionary<string, ZeroTierAgent.Peer> peers)
        {
            selectedId = room?.Id;
            uiDetail.IsVisible = room != null;
            uiNothing.IsVisible = room == null;
            if (room == null)
                return;
            int me = LoginManager.Instance.GetCurrentUser()?.ID ?? -1;
            uiName.Text = room.Name;
            uiCodePanel.IsVisible = room.Code != null;
            uiCode.Text = room.ShownCode;
            uiGameTitle.Text = room.GameTitle ?? Loc.T("No game chosen yet");
            uiEnter.IsVisible = !room.Member;
            uiLeave.IsVisible = room.Member;
            bool installed = room.GameId is int id && InstallViewModel.Instance.InstalledGames.Any(g => g.Key.ID == id);
            uiPlay.IsVisible = room.Member && installed;
            uiOpenGame.IsVisible = room.GameId != null && !(room.Member && installed);
            bool owner = room.OwnerId == me;
            uiChangeGame.IsVisible = owner;
            if (owner && uiChangeGame.ItemsSource == null)
                _ = FillGamesAsync();
            if (shownGame != room.GameId)
            {
                shownGame = room.GameId;
                uiCover.Data = room.GameId is int gameId ? await GameAsync(gameId) : null;
            }
            uiMembers.ItemsSource = room.Members.Select(m =>
            {
                string name = m.UserId == me ? Loc.F("{0} (you)", m.UserName) : m.UserName;
                if (m.UserId == room.OwnerId)
                    name += " · " + Loc.T("host");
                (string state, string brush) = m.NodeId == myNode ? (Loc.T("This PC"), "Brush.Success")
                    : peers.TryGetValue(m.NodeId, out var peer) && peer.Latency >= 0
                        ? (peer.Direct ? Loc.F("Direct · {0} ms", peer.Latency) : Loc.F("Relayed · {0} ms", peer.Latency), peer.Direct ? "Brush.Success" : "Brush.Warning")
                        : (Loc.T("Not connected"), "Brush.Muted");
                IBrush dot = this.TryFindResource(brush, out object? found) && found is IBrush b ? b : Brushes.Gray;
                return new MemberRow(name, m.UserName.Length > 0 ? m.UserName[..1].ToUpperInvariant() : "?", m.Ip, state, dot);
            }).ToList();
            string? host = room.Members.FirstOrDefault(m => m.UserId == room.OwnerId)?.Ip;
            uiHostHint.Text = host == null ? "" : Loc.F("The game does not see the party? Type the host's address: {0}", host);
        }

        private static async Task<Game?> GameAsync(int id)
        {
            try { return JsonSerializer.Deserialize<Game>(await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games/{id}")); }
            catch (Exception ex) { Log.Ignored(ex); return null; }
        }

        /// <summary>The games the administrators marked playable in LAN, by title.</summary>
        internal static async Task<List<Game>> LanGamesAsync()
        {
            HashSet<int> ids = await RoomsService.LanGamesAsync();
            if (ids.Count == 0)
                return new();
            string json = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games?filter.id=$in:{string.Join(",", ids)}&limit=-1");
            return (JsonSerializer.Deserialize<PaginatedData<Game>>(json)?.Data ?? Array.Empty<Game>()).Where(g => g.DeletedAt == null).OrderBy(g => g.Title).ToList();
        }

        private async Task FillGamesAsync()
        {
            try { uiChangeGame.ItemsSource = await LanGamesAsync(); }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private async void Rooms_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (showing || uiRooms.SelectedItem is not RoomItem room || room.Id == selectedId)
                return;
            selectedId = room.Id;
            await LoadAsync();
        }

        /// <summary>ZeroTier installed and usable by SanctuaryVault; else the setup is shown.</summary>
        internal static bool Ready()
        {
            if (ZeroTierAgent.IsInstalled && ZeroTierAgent.HasAccess)
                return true;
            MainWindowViewModel.Instance.OpenPopup(new ZeroTierSetupPopup());
            return false;
        }

        private void Setup_Click(object? sender, RoutedEventArgs e) => Ready();

        private void Create_Click(object? sender, RoutedEventArgs e)
        {
            if (Ready())
                MainWindowViewModel.Instance.OpenPopup(new RoomJoinPopup());
        }

        private void Join_Click(object? sender, RoutedEventArgs e)
        {
            if (Ready())
                MainWindowViewModel.Instance.OpenPopup(new RoomJoinPopup());
        }

        private async void Enter_Click(object? sender, RoutedEventArgs e)
        {
            if (Selected is not RoomItem room || !Ready())
                return;
            uiEnter.IsEnabled = false;
            try
            {
                await RoomsService.EnterAsync(node => RoomsService.JoinAsync(room.Id, null, node), ShowStatus);
                ShowStatus("");
                await LoadAsync(room.Id);
            }
            catch (Exception ex) { ShowStatus(ex.Message); }
            finally { uiEnter.IsEnabled = true; }
        }

        private async void Leave_Click(object? sender, RoutedEventArgs e)
        {
            if (Selected is not RoomItem room)
                return;
            uiLeave.IsEnabled = false;
            try
            {
                await RoomsService.LeaveAsync(room.Id);
                if (ZeroTierAgent.HasAccess)
                    await ZeroTierAgent.LeaveAsync(room.Id);
                selectedId = null;
                await LoadAsync();
            }
            catch (Exception ex) { ShowStatus(ex.Message); }
            finally { uiLeave.IsEnabled = true; }
        }

        private async void Play_Click(object? sender, RoutedEventArgs e)
        {
            if (Selected?.GameId is int id)
                await InstallUserControl.PlayGame(id);
        }

        private void OpenGame_Click(object? sender, RoutedEventArgs e)
        {
            if (Selected?.GameId is int id)
                MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(new Game { ID = id }));
        }

        private async void ChangeGame_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (Selected is not RoomItem room || uiChangeGame.SelectedItem is not Game game || game.ID == room.GameId)
                return;
            try
            {
                await RoomsService.SetGameAsync(room.Id, game.ID, game.Title);
                uiChangeGame.SelectedItem = null;
                await LoadAsync();
            }
            catch (Exception ex) { ShowStatus(ex.Message); }
        }

        private void CopyCode_Click(object? sender, RoutedEventArgs e)
        {
            if (Selected?.ShownCode is { Length: > 0 } code)
            {
                ClipboardHelper.SetText(code);
                MainWindowViewModel.Instance.AppBarText = Loc.T("Code copied");
            }
        }
    }
}
