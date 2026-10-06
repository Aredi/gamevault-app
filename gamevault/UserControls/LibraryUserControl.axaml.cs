using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using gamevault.Helper.Platform;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Models;
using gamevault.ViewModels;
using GameVault.Core.Library;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>
    /// Interaction logic for LibraryUserControl.xaml
    /// </summary>
    public partial class LibraryUserControl : UserControl
    {
        private LibraryViewModel ViewModel;
        private InputTimer inputTimer { get; set; }

        private bool scrollBlocked = false;
        private Guid searchCancellationToken = Guid.Empty;
        public LibraryUserControl()
        {
            InitializeComponent();
            ViewModel = new LibraryViewModel();
            int sortByIndex = 0;
            if (SettingsViewModel.Instance.RetainLibarySortByAndOrderBy)
            {
                try
                {
                    uiFilterOrderBy.IsChecked = Preferences.Get(AppConfigKey.LastLibraryOrderBy, LoginManager.Instance.GetUserProfile().UserConfigFile) == "desc" ? true : false;

                    string lastSortBy = Preferences.Get(AppConfigKey.LastLibrarySortBy, LoginManager.Instance.GetUserProfile().UserConfigFile);

                    for (int i = 0; i < ViewModel.GameFilterSortByValues.Count; i++)
                    {
                        if (ViewModel.GameFilterSortByValues.ElementAt(i).Value == lastSortBy)
                        {
                            sortByIndex = i;
                            break;
                        }
                    }
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
            }
            this.DataContext = ViewModel;
            uiFilterSortBy.SelectedIndex = sortByIndex;
            uiFilterOrderBy.IsCheckedChanged += OrderBy_Changed;
            uiFilterSortBy.SelectionChanged += SelectedGameFilterSortBy_SelectionChanged;
            Loaded += (_, _) => Dispatcher.UIThread.Post(() => sortSelectionReady = true, DispatcherPriority.Background);
            foreach (var selector in new[] { uiFilterGameTypeSelector, uiFilterTagSelector, uiFilterGenreSelector, uiFilterDeveloperSelector, uiFilterPublisherSelector, uiFilterGameStateSelector })
                selector.EntriesUpdated += FilterUpdated;
            uiFilterReleaseDateRangeSelector.EntriesUpdated += FilterUpdated;
            KeyDown += ReloadLibrary_Click;
            uiBtnReloadLibrary.Click += ReloadLibrary_Click;
            uiFilterEarlyAccess.Click += FilterUpdated;
            uiFilterBookmarks.Click += FilterUpdated;
            uiFilterPlayStatus.SelectedIndex = 0;
            ReloadCollectionNames();
            uiFilterPlayStatus.SelectionChanged += FilterUpdated;
            uiFilterCollection.SelectionChanged += (s, e) =>
            {
                if (!reloadingCollections)
                    FilterUpdated(s, e);
            };
            LibraryData.CollectionsChanged += (_, _) => Dispatcher.UIThread.Post(ReloadCollectionNames);
            PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty && IsVisible)
                    this.Focus();
            };
            InitTimer();
        }
        public async Task LoadLibrary()
        {
            await Search();
        }
        public void ShowLibraryError()
        {
            ViewModel.CanLoadServerGames = false;
            if (!uiExpanderGameCards.IsExpanded)
            {
                uiExpanderGameCards.IsExpanded = true;
            }
        }
        private void Search_TextChanged(object? sender, TextChangedEventArgs e)
        {
            inputTimer.Stop();
            inputTimer.Data = ((TextBox)sender).Text;
            inputTimer.Start();
        }
        private void InitTimer()
        {
            inputTimer = new InputTimer();
            inputTimer.Interval = TimeSpan.FromMilliseconds(400);
            inputTimer.Tick += InputTimerElapsed;
        }
        private async void InputTimerElapsed(object sender, EventArgs e)
        {
            inputTimer?.Stop();
            await Search();
        }

        private async Task Search()
        {
            Guid currentSearchToken = Guid.NewGuid();
            searchCancellationToken = currentSearchToken;

            if (!LoginManager.Instance.IsLoggedIn())
            {
                MainWindowViewModel.Instance.AppBarText = "You are offline";
                return;
            }
            if (!uiExpanderGameCards.IsExpanded)
            {
                uiExpanderGameCards.IsExpanded = true;
            }
            uiServerGamesScroll.ScrollToHome();

            TaskQueue.Instance.ClearQueue();

            string gameSortByFilter = ViewModel.SelectedGameFilterSortBy.Value;
            string gameOrderByFilter = (bool)uiFilterOrderBy.IsChecked ? "DESC" : "ASC";
            ViewModel.GameCards.Clear();

            // Collections, played / never played and sorting by play data are resolved here,
            // then sent to the server as a list of game ids.
            bool sortByPlay = gameSortByFilter is LibraryViewModel.LastPlayedSort or LibraryViewModel.MyPlaytimeSort;
            PlayStatusFilter playStatus = ViewModel.SelectedPlayStatus.Value;
            List<PlayRecord> playRecords = new();
            if (sortByPlay || playStatus != PlayStatusFilter.All)
            {
                try { playRecords = await LibraryData.GetMyPlayRecordsAsync(); }
                catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
            }
            List<int>? collectionIds = null;
            if (!string.IsNullOrEmpty(ViewModel.SelectedCollection) && ViewModel.SelectedCollection != LibraryViewModel.AllCollections)
                collectionIds = LibraryData.Collections.Load().FirstOrDefault(c => c.Name == ViewModel.SelectedCollection)?.GameIds ?? new List<int>();
            var playedIds = playRecords.Select(r => r.GameId).ToList();
            List<int>? playOrder = null;
            string idFilter;
            if (sortByPlay)
            {
                // Only games with play data can be sorted by it
                playOrder = LibraryQuery.OrderByPlay(playRecords, gameSortByFilter == LibraryViewModel.MyPlaytimeSort, ascending: gameOrderByFilter == "ASC");
                idFilter = LibraryQuery.IdFilter(collectionIds, playStatus == PlayStatusFilter.NeverPlayed ? PlayStatusFilter.NeverPlayed : PlayStatusFilter.Played, playedIds);
                if (playStatus == PlayStatusFilter.NeverPlayed)
                    idFilter = "&filter.id=$eq:-1";
            }
            else
            {
                idFilter = LibraryQuery.IdFilter(collectionIds, playStatus, playedIds);
            }
            string serverSort = sortByPlay ? "sort_title:ASC" : $"{gameSortByFilter}:{gameOrderByFilter}";
            string filterUrl = @$"{SettingsViewModel.Instance.ServerUrl}/api/games?search={Uri.EscapeDataString(inputTimer.Data ?? "")}&sortBy={serverSort}&limit={(sortByPlay ? -1 : 50)}";
            filterUrl = ApplyFilter(filterUrl) + idFilter;

            PaginatedData<Game>? gameResult = await GetGamesData(filterUrl);
            if (currentSearchToken != searchCancellationToken)
                return;
            if (gameResult?.Data != null && playOrder != null)
            {
                var position = playOrder.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index);
                gameResult.Data = gameResult.Data.OrderBy(g => position.TryGetValue(g.ID, out int index) ? index : int.MaxValue).ToArray();
                gameResult.Links.Next = null;
            }

            if (gameResult != null)
            {
                ViewModel.CanLoadServerGames = true;
                ViewModel.TotalGamesCount = gameResult.Meta.TotalItems;
                if (gameResult.Data.Length > 0)
                {
                    ViewModel.NextPage = gameResult.Links.Next;
                    await ProcessGamesData(gameResult);
                }
            }
            else
            {
                ViewModel.CanLoadServerGames = false;
            }
        }
        private async void ReloadLibrary_Click(object? sender, EventArgs e)
        {
            if (e is RoutedEventArgs routed && e is not KeyEventArgs)
                routed.Handled = true;

            //Block spamming the reload button and F5 at the same time
            if (uiBtnReloadLibrary.IsEnabled == false || (e is KeyEventArgs keyArgs && keyArgs.Key != Key.F5))
                return;

            uiBtnReloadLibrary.IsEnabled = false;
            await Search();
            if (e is KeyEventArgs { Key: Key.F5 })
            {
                await MainWindowViewModel.Instance.Library.GetGameInstalls().RestoreInstalledGames();
            }
            uiBtnReloadLibrary.IsEnabled = true;
        }
        public InstallUserControl GetGameInstalls()
        {
            return uiGameInstalls;
        }
        private async Task<PaginatedData<Game>?> GetGamesData(string url)
        {
            try
            {
                string gameList = await WebHelper.GetAsync(url);
                return JsonSerializer.Deserialize<PaginatedData<Game>>(gameList);
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex);
                return null;
            }
        }
        private async Task ProcessGamesData(PaginatedData<Game> gameResult)
        {
            await Task.Run(() =>
            {
                foreach (Game game in gameResult.Data)
                {
                    Dispatcher.UIThread.Invoke(delegate
                    {
                        ViewModel.GameCards.Add(game);
                    });
                }
            });
        }
        private void Hyperlink_RequestNavigate(object? sender, RoutedEventArgs e)
        {
            try
            {
                string url = (string)((Control)sender!).Tag!;
                PlatformInfo.OpenUrl(url);
                e.Handled = true;
            }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = ex.Message; }
        }
        private void GameCard_Clicked(object sender, RoutedEventArgs e)
        {
            if ((Game)((Control)sender).DataContext == null)
                return;
            MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl((Game)((Control)sender).DataContext));
        }

        private void Filter_Click(object? sender, PointerPressedEventArgs e)
        {
            e.Handled = true;
            if (!uiExpanderGameCards.IsExpanded)
            {
                uiExpanderGameCards.IsExpanded = true;
            }
            ViewModel.FilterVisibility = !ViewModel.FilterVisibility;
        }
        private void OpenFilterIfClosed()
        {
            if (!uiExpanderGameCards.IsExpanded)
            {
                uiExpanderGameCards.IsExpanded = true;
            }
            if (ViewModel.FilterVisibility == false)
            {
                ViewModel.FilterVisibility = true;
            }
        }
        private async void ClearAllFilters_Click(object? sender, PointerPressedEventArgs e)
        {
            e.Handled = true;
            ClearAllFilters();
            await Search();
        }
        public void ClearAllFilters()
        {
            uiFilterGameTypeSelector.ClearEntries();
            uiFilterGenreSelector.ClearEntries();
            uiFilterTagSelector.ClearEntries();
            uiFilterGameStateSelector.ClearEntries();
            uiFilterReleaseDateRangeSelector.ClearSelection();
            uiFilterPublisherSelector.ClearEntries();
            uiFilterDeveloperSelector.ClearEntries();
            uiFilterBookmarks.IsChecked = false;
            uiFilterEarlyAccess.IsChecked = false;
            uiFilterPlayStatus.SelectedIndex = 0;
            uiFilterCollection.SelectedIndex = 0;

            RefreshFilterCounter();
        }
        private async void Library_ScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            var scroll = (ScrollViewer)sender!;
            double scrollableHeight = scroll.Extent.Height - scroll.Viewport.Height;
            if (scrollableHeight > 0)
            {
                double scrollPercentage = scroll.Offset.Y / scrollableHeight * 100;

                ViewModel.ScrollToTopVisibility = scrollPercentage > 10;

                if (scrollBlocked == false && ViewModel.NextPage != null && scrollPercentage > 90)
                {
                    scrollBlocked = true;
                    PaginatedData<Game>? gameResult = await GetGamesData(ViewModel.NextPage);
                    ViewModel.NextPage = gameResult?.Links.Next;
                    if (gameResult == null || gameResult.Data == null)
                    {
                        MainWindowViewModel.Instance.AppBarText = "Failed to load next Page";
                        return;
                    }
                    await ProcessGamesData(gameResult);
                    scrollBlocked = false;
                }
            }
        }
        private void ScrollToTop_Click(object? sender, PointerReleasedEventArgs e)
        {
            uiServerGamesScroll.ScrollToHome();
        }

        private async void OrderBy_Changed(object? sender, RoutedEventArgs e)
        {
            Preferences.Set(AppConfigKey.LastLibraryOrderBy, (bool)uiFilterOrderBy.IsChecked ? "desc" : "asc", LoginManager.Instance.GetUserProfile().UserConfigFile);
            await Search();
        }
        private string ApplyFilter(string filter)
        {
            string gameType = uiFilterGameTypeSelector.GetSelectedEntries();
            if (gameType != string.Empty)
            {
                filter += $"&filter.type=$in:{gameType}";
            }
            if (uiFilterEarlyAccess.IsChecked == true)
            {
                filter += "&filter.metadata.early_access=$eq:true";
            }
            if (uiFilterReleaseDateRangeSelector.IsValid())
            {
                filter += $"&filter.metadata.release_date=$btw:{uiFilterReleaseDateRangeSelector.GetYearFrom()}-01-01,{uiFilterReleaseDateRangeSelector.GetYearTo()}-12-31";
            }
            string genres = uiFilterGenreSelector.GetSelectedEntries();
            if (genres != string.Empty)
            {
                filter += $"&filter.metadata.genres.name=$in:{genres}";
            }
            string tags = uiFilterTagSelector.GetSelectedEntries();
            if (tags != string.Empty)
            {
                filter += $"&filter.metadata.tags.name=$in:{tags}";
            }
            string developers = uiFilterDeveloperSelector.GetSelectedEntries();
            if (developers != string.Empty)
            {
                filter += $"&filter.metadata.developers.name=$in:{developers}";
            }
            string publishers = uiFilterPublisherSelector.GetSelectedEntries();
            if (publishers != string.Empty)
            {
                filter += $"&filter.metadata.publishers.name=$in:{publishers}";
            }
            string gameStates = uiFilterGameStateSelector.GetSelectedEntries();
            if (gameStates != string.Empty)
            {
                filter += $"&filter.progresses.state=$in:{gameStates}&filter.progresses.user.id=$eq:{LoginManager.Instance.GetCurrentUser()?.ID}";
            }
            if (uiFilterBookmarks.IsChecked == true)
            {
                filter += $"&filter.bookmarked_users.id=$eq:{LoginManager.Instance.GetCurrentUser()?.ID}";
            }
            return filter;
        }
        private bool sortSelectionReady;
        private void SelectedGameFilterSortBy_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // Selections made while the page is built (restored sort, binding) do not search;
            // every change made by the user does.
            if (sortSelectionReady)
                FilterUpdated(sender, e);
        }
        private async void FilterUpdated(object? sender, EventArgs e)
        {
            if (sender == uiFilterSortBy)
            {
                Preferences.Set(AppConfigKey.LastLibrarySortBy, ViewModel.SelectedGameFilterSortBy.Value, LoginManager.Instance.GetUserProfile().UserConfigFile);
            }
            OpenFilterIfClosed();
            RefreshFilterCounter();
            await Search();
        }


        private async void RandomGame_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            ((Control)sender).IsEnabled = false;
            Random random = new Random();
            if (random.Next(0, 100) < 7)
            {
                try
                {
                    uiImgRandom.IsVisible = true;
                    uiImgRandom.NavigateToString("<html><body style='margin:0;background:transparent'><video src='https://phalco.de/images/gamevault/eastereggs/777.mp4' autoplay muted style='width:100%;height:100%'></video></body></html>");
                    DispatcherTimer timer = new DispatcherTimer();
                    timer.Interval = TimeSpan.FromMilliseconds(6000);
                    timer.Tick += (s, e) => { timer.Stop(); uiImgRandom.NavigateToString("<html></html>"); uiImgRandom.IsVisible = false; PlatformInfo.OpenUrl("https://www.ncpgambling.org/help-treatment/"); };
                    timer.Start();
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
            }
            else
            {
                Game? result = null;
                try
                {
                    string randomGame = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games/random");
                    result = JsonSerializer.Deserialize<Game>(randomGame);
                }
                catch (Exception ex)
                {
                    MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex);
                }

                if (result != null)
                {
                    MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(result, true));
                }
            }
            ((Control)sender).IsEnabled = true;
        }
        private readonly HashSet<int> busyCards = new HashSet<int>();
        private async void CardSettings_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            try
            {
                int cardId = ((Game)((Control)sender).DataContext!).ID;
                if (!busyCards.Add(cardId))
                    return;

                try
                {
                    string result = await WebHelper.GetAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/games/{((Game)((Control)sender).DataContext).ID}");
                    Game resultGame = JsonSerializer.Deserialize<Game>(result);
                    MainWindowViewModel.Instance.OpenPopup(new GameSettingsUserControl(resultGame) { Width = 1200, Height = 800, Margin = new Thickness(50) });
                }
                catch (Exception ex)
                {
                    MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex);
                }
                busyCards.Remove(cardId);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private async void CardBookmark_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            try
            {
                int cardId = -((Game)((Control)sender).DataContext!).ID - 1;
                if (!busyCards.Add(cardId))
                {
                    ((ToggleButton)sender).IsChecked = !((ToggleButton)sender).IsChecked;
                    return;
                }
                try
                {
                    Game currentGame = (Game)((Control)sender).DataContext;
                    if ((bool)((ToggleButton)sender).IsChecked == false)
                    {
                        await WebHelper.DeleteAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/users/me/bookmark/{currentGame.ID}");
                        currentGame.BookmarkedUsers = new List<User>();
                    }
                    else
                    {
                        await WebHelper.PostAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/users/me/bookmark/{currentGame.ID}", "");
                        currentGame.BookmarkedUsers = new List<User> { LoginManager.Instance.GetCurrentUser()! };
                    }

                }
                catch (Exception ex)
                {
                    string message = WebExceptionHelper.TryGetServerMessage(ex);
                    MainWindowViewModel.Instance.AppBarText = message;
                }
                busyCards.Remove(cardId);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private async void Download_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            await MainWindowViewModel.Instance.Downloads.TryStartDownload((Game)(((Control)sender).DataContext));
        }
        private bool reloadingCollections;
        private void ReloadCollectionNames()
        {
            if (!LoginManager.Instance.IsLoggedIn() && LoginManager.Instance.GetUserProfile() == null)
                return;
            reloadingCollections = true;
            string? selected = ViewModel.SelectedCollection;
            var names = new List<string> { LibraryViewModel.AllCollections };
            try { names.AddRange(LibraryData.Collections.Load().Select(c => c.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)); }
            catch (Exception ex) { Log.Ignored(ex); }
            ViewModel.CollectionNames = names;
            ViewModel.SelectedCollection = names.Contains(selected ?? "") ? selected : LibraryViewModel.AllCollections;
            reloadingCollections = false;
        }
        private void ManageCollections_Click(object sender, RoutedEventArgs e)
        {
            CollectionsFlyout.ShowManager((Control)sender);
        }
        private void RefreshFilterCounter()
        {
            int filterCount = 0;
            filterCount += uiFilterGameTypeSelector.HasEntries() ? 1 : 0;
            filterCount += uiFilterGenreSelector.HasEntries() ? 1 : 0;
            filterCount += uiFilterTagSelector.HasEntries() ? 1 : 0;
            filterCount += uiFilterDeveloperSelector.HasEntries() ? 1 : 0;
            filterCount += uiFilterPublisherSelector.HasEntries() ? 1 : 0;
            filterCount += uiFilterGameStateSelector.HasEntries() ? 1 : 0;
            filterCount += (bool)uiFilterEarlyAccess.IsChecked ? 1 : 0;
            filterCount += (bool)uiFilterBookmarks.IsChecked ? 1 : 0;
            filterCount += uiFilterPlayStatus.SelectedIndex > 0 ? 1 : 0;
            filterCount += uiFilterCollection.SelectedIndex > 0 ? 1 : 0;

            filterCount += (uiFilterReleaseDateRangeSelector.IsValid()) ? 1 : 0;
            ViewModel.FilterCounter = filterCount == 0 ? string.Empty : filterCount.ToString();
        }
        public void RefreshGame(Game gameToRefreshParam)
        {
            Game? gameToRefresh = ViewModel.GameCards.Where(g => g.ID == gameToRefreshParam.ID).FirstOrDefault();
            if (gameToRefresh != null)
            {
                int index = ViewModel.GameCards.IndexOf(gameToRefresh);
                ViewModel.GameCards[index] = null;
                ViewModel.GameCards[index] = gameToRefreshParam;
            }
        }


    }
}
