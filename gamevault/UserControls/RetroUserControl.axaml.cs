using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using GameVault.Core;
using GameVault.Core.Retro;
using gamevault.Helper;
using gamevault.Helper.Platform;
using gamevault.Localization;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    public partial class RetroUserControl : UserControl
    {
        internal RetroViewModel ViewModel { get; } = new();
        private const double CardWidth = 170;
        internal IEnumerable<RetroGame> Shown => ViewModel.Games;
        private Task? loading;
        /// <summary>Set when the service can not be reached: the games installed on this PC, found on disk.</summary>
        private List<RetroGame>? offlineGames;

        public RetroUserControl()
        {
            InitializeComponent();
            DataContext = ViewModel;
            AttachedToVisualTree += (_, _) =>
            {
                Focus();
                _ = EnsureLoadedAsync();
            };
            KeyDown += Page_KeyDown;
        }

        /// <summary>A list whose items change loses its selection (Avalonia does not take the bound index again): set back.</summary>
        private void LoadCollections()
        {
            ViewModel.LoadCollections();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => uiCollections.SelectedIndex = ViewModel.Collection);
        }

        /// <summary>Loaded once; again after a failure or offline, when the page is shown again.</summary>
        internal Task EnsureLoadedAsync() => loading ??= LoadPlatformsAsync();

        private void Show(string? message, bool loading = false)
        {
            ViewModel.Message = message;
            ViewModel.Loading = loading;
        }

        internal async Task LoadPlatformsAsync()
        {
            Show(null, loading: true);
            LoadCollections();
            _ = RetroLibrary.LoadAsync();
            List<RetroPlatform>? platforms = null;
            string? failure = null;
            try
            {
                if (!MainWindowViewModel.Instance.IsOffline)
                    platforms = await RetroService.ListPlatformsAsync();
                else
                    failure = Loc.T("You are not logged in or offline");
            }
            catch (Exception ex) { failure = WebExceptionHelper.TryGetServerMessage(ex); }
            offlineGames = null;
            if (failure != null || platforms == null)
            {
                // As the library offline: the games installed here stay playable
                loading = null;
                var installed = RetroLibrary.InstalledGames();
                if (installed.Count == 0)
                {
                    ViewModel.Platforms = new();
                    ViewModel.AllGames = new();
                    Show(failure ?? Loc.T("The games of the other platforms are not available on this server."));
                    return;
                }
                platforms = installed.GroupBy(g => g.Platform.Slug).Select(group =>
                {
                    var platform = new RetroPlatform { Id = group.First().Platform.Id, Slug = group.Key, Name = group.First().Platform.Name, Count = group.Count() };
                    foreach (RetroGame game in group)
                        game.Platform = platform;
                    return platform;
                }).OrderBy(p => p.Name).ToList();
                offlineGames = installed;
            }
            if (platforms.Count == 0)
            {
                Show(Loc.T("No game of a supported platform on the server yet."));
                return;
            }
            ViewModel.Platforms = platforms;
            await SelectPlatformAsync(platforms[0]);
        }

        internal async Task SelectPlatformAsync(RetroPlatform platform)
        {
            foreach (RetroPlatform other in ViewModel.Platforms)
                other.IsSelected = other == platform;
            SizeCards(platform);
            ViewModel.SelectedGame = null;
            ViewModel.AllGames = new();
            if (offlineGames != null)
            {
                ViewModel.AllGames = offlineGames.Where(g => g.Platform == platform).ToList();
                Show(Loc.T("Offline: only the retro games installed on this PC are shown."));
                return;
            }
            Show(null, loading: true);
            try
            {
                ViewModel.AllGames = await RetroService.ListGamesAsync(platform);
                Show(null);
            }
            catch (Exception ex) { Show(WebExceptionHelper.TryGetServerMessage(ex)); }
        }

        /// <summary>As the library's cards (170 wide, the title and info below), with the shape of the platform's boxes.</summary>
        internal void SizeCards(RetroPlatform platform)
        {
            var layout = (UniformGridLayout)uiGames.Layout!;
            layout.MinItemWidth = CardWidth + 16;
            layout.MinItemHeight = CardWidth * platform.System.CoverRatio + 70;
        }

        private async void Platform_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is RetroPlatform platform)
                await SelectPlatformAsync(platform);
        }

        private void Reload_Click(object? sender, RoutedEventArgs e) => loading = LoadPlatformsAsync();

        /// <summary>Opens a game from elsewhere (quick search, recent activity, a profile): its platform, then its details.</summary>
        internal async Task ShowGameAsync(string platformSlug, int romId)
        {
            MainWindowViewModel.Instance.SetActiveControl(MainControl.Retro);
            await EnsureLoadedAsync();
            if (ViewModel.Platforms.FirstOrDefault(p => p.Slug == platformSlug) is not RetroPlatform platform)
                return;
            if (!platform.IsSelected)
                await SelectPlatformAsync(platform);
            if (ViewModel.AllGames.FirstOrDefault(g => g.Id == romId) is not RetroGame game)
                return;
            if (!ViewModel.Games.Contains(game))
            {
                ViewModel.Search = "";
                ViewModel.OnlyInstalled = ViewModel.OnlyFavorites = false;
                ViewModel.Collection = 0;
            }
            Select(game);
            // As the library's games opened from the search or the activity: their page
            OpenGame(game);
        }

        private void Select(RetroGame game)
        {
            ViewModel.SelectedGame = game;
            int index = ViewModel.Games.IndexOf(game);
            if (index >= 0)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => uiGames.GetOrCreateElement(index).BringIntoView());
        }

        /// <summary>As the library's shelf: the arrows move the selection, Enter opens the game's page, Escape closes the details.</summary>
        private void Page_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Source is Avalonia.Visual source && (source is TextBox || source.FindAncestorOfType<ComboBox>(includeSelf: true) != null || source is ComboBoxItem))
                return;
            var games = ViewModel.Games;
            if (games.Count == 0)
                return;
            RetroGame? selected = ViewModel.SelectedGame;
            if (e.Key == Avalonia.Input.Key.Escape && selected != null)
            {
                e.Handled = true;
                ViewModel.SelectedGame = null;
                return;
            }
            if (e.Key == Avalonia.Input.Key.Enter && selected != null)
            {
                e.Handled = true;
                OpenGame(selected);
                return;
            }
            int columns = Math.Max(1, (int)(uiGames.Bounds.Width / ((UniformGridLayout)uiGames.Layout!).MinItemWidth));
            int index = selected == null ? -1 : games.IndexOf(selected);
            int next = e.Key switch
            {
                Avalonia.Input.Key.Left => index - 1,
                Avalonia.Input.Key.Right => index + 1,
                Avalonia.Input.Key.Up => index - columns,
                Avalonia.Input.Key.Down => index + columns,
                _ => int.MinValue,
            };
            if (next == int.MinValue)
                return;
            e.Handled = true;
            Select(games[index < 0 ? 0 : Math.Clamp(next, 0, games.Count - 1)]);
        }

        private void Collections_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control anchor && GameOf(sender) is RetroGame game)
                CollectionsFlyout.ShowForGame(anchor, game.Id, game.Name, RetroLibrary.Collections, () =>
                {
                    LoadCollections();
                    ViewModel.ApplyFilter();
                });
        }

        private void GameCard_Click(object? sender, RoutedEventArgs e)
        {
            if (GameOf(sender) is RetroGame game)
                ViewModel.SelectedGame = game;
            Focus();
        }

        /// <summary>As the library's shelf: a double click opens the game's page.</summary>
        private void GameCard_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
        {
            if (GameOf(sender) is RetroGame game)
                OpenGame(game);
        }

        internal static void OpenGame(RetroGame game) => MainWindowViewModel.Instance.SetActiveControl(new RetroGameViewUserControl(game));

        private void OpenGame_Click(object? sender, RoutedEventArgs e)
        {
            if (GameOf(sender) is RetroGame game)
                OpenGame(game);
        }

        private void CloseDetails_Click(object? sender, RoutedEventArgs e) => ViewModel.SelectedGame = null;

        private async void Manual_Click(object? sender, RoutedEventArgs e)
        {
            if (GameOf(sender) is not RetroGame { Manual: string manual })
                return;
            try { PlatformInfo.OpenFile(await RetroService.MediaFileAsync(manual)); }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
        }

        private void Video_Click(object? sender, RoutedEventArgs e)
        {
            if (GameOf(sender) is RetroGame { Video: string video })
                PlatformInfo.OpenUrl($"https://www.youtube.com/watch?v={Uri.EscapeDataString(video)}");
        }

        private static RetroGame? GameOf(object? sender) => (sender as Control)?.DataContext as RetroGame;

        private async void Install_Click(object? sender, RoutedEventArgs e)
        {
            if (GameOf(sender) is RetroGame game)
                await InstallAsync(game);
        }

        /// <summary>
        /// Into the Downloads page and its queue, as the library's games: the download goes on whatever page is shown,
        /// and the game is made playable (unpacked, RetroArch, core, BIOS) when it is complete.
        /// </summary>
        internal Task InstallAsync(RetroGame game) => RetroPlay.InstallAsync(game);

        private async void Play_Click(object? sender, RoutedEventArgs e)
        {
            if (GameOf(sender) is RetroGame game)
                await PlayAsync(game);
        }

        internal Task PlayAsync(RetroGame game) => RetroPlay.PlayAsync(game);

        private async void Menu_Click(object? sender, RoutedEventArgs e)
        {
            uiMenu.IsEnabled = false;
            try
            {
                await RetroArchManager.OpenMenuAsync(text => Show(text));
                Show(null);
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                Show(Loc.F("Failed: {0}", ex.Message));
            }
            finally { uiMenu.IsEnabled = true; }
        }

        private async void Delete_Click(object? sender, RoutedEventArgs e)
        {
            if (GameOf(sender) is not RetroGame game || game.Busy || RetroService.InstalledFolder(game) is not string folder)
                return;
            MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync(Loc.F("Delete '{0}' from this PC? Its saves are kept.", game.Name),
                "", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = Loc.T("Yes"), NegativeButtonText = Loc.T("No") });
            if (result != MessageDialogResult.Affirmative)
                return;
            try
            {
                Directory.Delete(folder, true);
            }
            catch (Exception ex) { game.Status = Loc.F("Failed: {0}", ex.Message); }
            game.Refresh();
            ViewModel.ApplyFilter();
        }
    }
}
