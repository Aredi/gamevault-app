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
        /// <summary>Each platform's games, asked once (RomM sends megabytes for a big one); Reload asks again.</summary>
        private readonly Dictionary<int, List<RetroGame>> lists = new();

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
            ViewModel.PropertyChanged += (_, e) =>
            {
                // Another list (platform, search, filter, sort) starts at the top. Below it, the grid measures its cards with
                // an old card, which it then leaves drawn where it was, at its old size, over the others (ItemsRepeater 11.1)
                if (e.PropertyName == nameof(RetroViewModel.Games))
                    uiScroll.Offset = default;
                // As the collections' list: new items, the chosen one set back
                else if (e.PropertyName == nameof(RetroViewModel.Genres))
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => uiGenres.SelectedIndex = ViewModel.Genre);
                else if (e.PropertyName == nameof(RetroViewModel.Decades))
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => uiDecades.SelectedIndex = ViewModel.Decade);
            };
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
            lists.Clear();
            LoadCollections();
            _ = RetroLibrary.LoadAsync();
            _ = LoadOtherCollectionsAsync();
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
            await SelectAllAsync();
        }

        /// <summary>The player's RomM collections (with their account linked) and the series of games, in the collections' list.</summary>
        private async Task LoadOtherCollectionsAsync()
        {
            var others = new List<(string, IEnumerable<int>)>();
            try { others.AddRange((await RommAccount.CollectionsAsync()).Select(c => (Loc.F("{0} (RomM)", c.Name), (IEnumerable<int>)c.Games))); }
            catch (Exception ex) { Log.Ignored(ex); }
            try { others.AddRange((await RetroService.SeriesAsync()).Select(c => (Loc.F("{0} (series)", c.Name), (IEnumerable<int>)c.Games))); }
            catch (Exception ex) { Log.Ignored(ex); }
            ViewModel.SetOtherCollections(others);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => uiCollections.SelectedIndex = ViewModel.Collection);
        }

        /// <summary>The games of every platform, each asked once (the quick search, a collection, "All").</summary>
        internal async Task<List<RetroGame>> AllGamesAsync()
        {
            await EnsureLoadedAsync();
            return await ListAllGamesAsync();
        }

        /// <summary><see cref="AllGamesAsync"/> once the platforms are known (the first visit shows "All" while loading them).</summary>
        private async Task<List<RetroGame>> ListAllGamesAsync()
        {
            if (offlineGames != null)
                return offlineGames;
            await Task.WhenAll(ViewModel.Platforms.Where(p => !lists.ContainsKey(p.Id)).Select(async platform =>
            {
                try { lists[platform.Id] = await RetroService.ListGamesAsync(platform); }
                catch (Exception ex) { Log.Ignored(ex); }
            }));
            return ViewModel.Platforms.SelectMany(p => lists.GetValueOrDefault(p.Id) ?? new()).ToList();
        }

        /// <summary>The games already listed here, whatever their platform.</summary>
        internal IEnumerable<RetroGame> KnownGames => offlineGames ?? lists.Values.SelectMany(l => l);

        /// <summary>"All": the games of every platform at once.</summary>
        internal async Task SelectAllAsync()
        {
            foreach (RetroPlatform platform in ViewModel.Platforms)
                platform.IsSelected = false;
            ViewModel.ShowsAll = true;
            SizeCards(null);
            ViewModel.SelectedGame = null;
            Show(null, loading: true);
            var games = await ListAllGamesAsync();
            if (!ViewModel.ShowsAll)
                return;
            ViewModel.AllGames = games;
            Show(offlineGames != null ? Loc.T("Offline: only the retro games installed on this PC are shown.") : null);
        }

        internal async Task SelectPlatformAsync(RetroPlatform platform)
        {
            ViewModel.ShowsAll = false;
            // A collection is across the platforms: choosing one platform leaves it
            if (ViewModel.ShowsACollection)
            {
                ViewModel.Collection = 0;
                uiCollections.SelectedIndex = 0;
            }
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
                if (!lists.TryGetValue(platform.Id, out List<RetroGame>? games))
                    games = lists[platform.Id] = await RetroService.ListGamesAsync(platform);
                // Another platform was chosen meanwhile: its games are the ones shown
                if (!platform.IsSelected)
                    return;
                ViewModel.AllGames = games;
                Show(null);
            }
            catch (Exception ex)
            {
                if (platform.IsSelected)
                    Show(WebExceptionHelper.TryGetServerMessage(ex));
            }
        }

        /// <summary>As the library's cards (170 wide, the title and info below), with the shape of the platform's boxes.</summary>
        /// <param name="platform">None: all the platforms (their boxes are mostly 3:4).</param>
        internal void SizeCards(RetroPlatform? platform)
        {
            var layout = (UniformGridLayout)uiGames.Layout!;
            layout.MinItemWidth = CardWidth + 16;
            layout.MinItemHeight = CardWidth * (platform?.System.CoverRatio ?? 4 / 3.0) + 70;
            uiGames.Classes.Set("all", platform == null);
        }

        private async void All_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Avalonia.Controls.Primitives.ToggleButton chip)
                chip.IsChecked = true;
            await SelectAllAsync();
        }

        /// <summary>A collection holds games of several platforms: all of them are shown, then filtered.</summary>
        private async void Collections_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ViewModel.ShowsACollection && !ViewModel.ShowsAll && ViewModel.Platforms.Count > 0)
                await SelectAllAsync();
        }

        private void ClearFilters_Click(object? sender, RoutedEventArgs e) => ViewModel.ClearFilters();

        private async void Platform_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is RetroPlatform platform)
                await TogglePlatformAsync(platform);
        }

        /// <summary>The chip of a platform: chosen again, it is left for "All".</summary>
        internal Task TogglePlatformAsync(RetroPlatform platform) => platform.IsSelected ? SelectAllAsync() : SelectPlatformAsync(platform);

        private void Reload_Click(object? sender, RoutedEventArgs e) => loading = LoadPlatformsAsync();

        /// <summary>Opens a game from elsewhere (quick search, recent activity, a profile): its platform, then its page.</summary>
        internal async Task ShowGameAsync(string platformSlug, int romId)
        {
            MainWindowViewModel.Instance.SetActiveControl(MainControl.Retro);
            await EnsureLoadedAsync();
            if (ViewModel.Platforms.FirstOrDefault(p => p.Slug == platformSlug) is not RetroPlatform platform)
                return;
            if (!platform.IsSelected && !(ViewModel.ShowsAll && ViewModel.AllGames.Any(g => g.Id == romId && g.Platform == platform)))
                await SelectPlatformAsync(platform);
            if (ViewModel.AllGames.FirstOrDefault(g => g.Id == romId) is not RetroGame game)
                return;
            if (!ViewModel.Games.Contains(game))
            {
                ViewModel.Search = "";
                ViewModel.OnlyInstalled = ViewModel.OnlyFavorites = false;
                ViewModel.Collection = 0;
                ViewModel.ClearFilters();
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

        /// <summary>As the library: the arrows move the selection, Enter opens the game's page, Escape clears the selection.</summary>
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

        /// <summary>As the library's gallery: a card opens the game's page.</summary>
        private void GameCard_Click(object? sender, RoutedEventArgs e)
        {
            if (GameOf(sender) is not RetroGame game)
                return;
            ViewModel.SelectedGame = game;
            OpenGame(game);
        }

        /// <summary>A game of "Continue playing": its console, then its page.</summary>
        private async void RecentCard_Click(object? sender, RoutedEventArgs e)
        {
            if (GameOf(sender) is RetroGame game)
                await ShowGameAsync(game.Platform.Slug, game.Id);
        }

        internal static void OpenGame(RetroGame game) => MainWindowViewModel.Instance.SetActiveControl(new RetroGameViewUserControl(game));

        private static RetroGame? GameOf(object? sender) => (sender as Control)?.DataContext as RetroGame;

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
    }
}
