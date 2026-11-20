using gamevault.Localization;
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
using gamevault.Helper.Integrations;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    public partial class InstallUserControl : UserControl
    {
        private List<FileSystemWatcher> m_FileWatcherList = new List<FileSystemWatcher>();
        private bool gamesRestored = false;
        private ObservableCollection<KeyValuePair<Game, string>>? observedGames;
        private int lastObservedCount = -1;
        public InstallUserControl()
        {
            InitializeComponent();
            this.DataContext = InstallViewModel.Instance;
            // Open unless the user closed it
            uiInstalledGames.IsChecked = Preferences.Get(AppConfigKey.InstalledGamesOpen, LoginManager.Instance.GetUserProfile().UserConfigFile) != "0";
            uiInstalledGames.IsCheckedChanged += (_, _) => InstalledGames_Toggled(uiInstalledGames, new RoutedEventArgs());
            // Mouse wheel scrolls the installed games horizontally, or the page when there is nothing to scroll.
            uiInstalledGamesScroll.AddHandler(PointerWheelChangedEvent, InstalledGames_PointerWheelChanged, RoutingStrategies.Tunnel);
            InstallViewModel.Instance.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(InstallViewModel.InstalledGames))
                    ObserveInstalledGames();
            };
            ObserveInstalledGames();
            Loaded += UserControl_Loaded;
        }

        private void ObserveInstalledGames()
        {
            if (observedGames != null)
                observedGames.CollectionChanged -= InstalledGames_CollectionChanged;
            observedGames = InstallViewModel.Instance.InstalledGames;
            observedGames.CollectionChanged += InstalledGames_CollectionChanged;
            InstalledGames_CollectionChanged(null, null);
        }

        private async void InstalledGames_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs? e)
        {
            int oldCount = lastObservedCount;
            lastObservedCount = InstallViewModel.Instance.InstalledGames.Count;
            await Collection_Updated(oldCount, lastObservedCount);
        }

        private void InstalledGames_PointerWheelChanged(object? sender, PointerWheelEventArgs e) =>
            LibraryUserControl.RowWheel(uiInstalledGamesScroll, e);

        private async void UserControl_Loaded(object? sender, RoutedEventArgs e)
        {
            if (this.IsVisible == true && gamesRestored && InstallViewModel.Instance.InstalledGames.Count > 0 && string.IsNullOrEmpty(searchText))
            {
                InstallViewModel.Instance.InstalledGames = await SortInstalledGamesByLastPlayed(InstallViewModel.Instance.InstalledGames);
                SetFilteredView();
            }
        }
        private List<Game> GetGamesFromOfflineCache(Dictionary<int, string> games)
        {
            List<Game> offlineCacheGames = new List<Game>();
            foreach (KeyValuePair<int, string> entry in games)
            {
                string objectFromFile = Preferences.Get(entry.Key.ToString(), LoginManager.Instance.GetUserProfile().OfflineCache);
                if (objectFromFile != string.Empty)
                {
                    try
                    {
                        string decompressedObject = GameVault.Core.StringCompressor.Decompress(objectFromFile);
                        Game? deserializedObject = JsonSerializer.Deserialize<Game>(decompressedObject);
                        if (deserializedObject != null)
                        {
                            offlineCacheGames.Add(deserializedObject);
                        }
                    }
                    catch (FormatException exFormat) { Log.Ignored(exFormat); }
                }
                else
                {
                    string gameTitle = Path.GetFileName(entry.Value);
                    offlineCacheGames.Add(new Game() { ID = entry.Key, Title = gameTitle.Substring(gameTitle.IndexOf(')') + 1) });
                }
            }
            return offlineCacheGames;
        }
        public async Task RestoreInstalledGames(bool fromCLI = false)
        {
            // The list stays as it is while the disks and the server are read: cleared, every game would show
            // "Install & Play" meanwhile, and one registered meanwhile would be lost
            var shownBefore = InstallViewModel.Instance.InstalledGames.ToList();
            Dictionary<int, string> foundGames = new Dictionary<int, string>();
            InstallViewModel.Instance.InstalledGamesDuplicates.Clear();
            Game[]? games = await Task<Game[]>.Run(async () =>
            {
                if (SettingsViewModel.Instance.RootDirectories.Count > 0)
                {
                    try { GameMover.RemoveLeftovers(); }
                    catch (Exception ex) { Log.Ignored(ex); }

                    List<string> allDirectoriesFromRootDirectories = new List<string>();
                    foreach (DirectoryEntry dirEntry in SettingsViewModel.Instance.RootDirectories)
                    {
                        if (Directory.Exists(Path.Combine(dirEntry.Uri, "GameVault", "Installations")))
                            allDirectoriesFromRootDirectories.AddRange(Directory.GetDirectories(Path.Combine(dirEntry.Uri, "GameVault", "Installations")));
                    }


                    foreach (string dir in allDirectoriesFromRootDirectories)
                    {
                        // One folder gone meanwhile or not readable must not hide all the others
                        try
                        {
                            int id = GameIdOf(dir);
                            if (id == -1)
                                continue;
                            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                            {
                                AddSystemFileWatcher(dir);
                            }
                            else if (!foundGames.ContainsKey(id))
                            {
                                foundGames.Add(id, dir);
                            }
                            else if (!InstallViewModel.Instance.InstalledGamesDuplicates.ContainsKey(id))
                            {
                                InstallViewModel.Instance.InstalledGamesDuplicates.Add(id, dir);
                            }
                        }
                        catch (Exception ex) { Log.Ignored(ex); }
                    }
                    try
                    {
                        if (foundGames.Count > 0)
                        {
                            string gameIds = string.Join(",", foundGames.Keys.Where(key => !string.IsNullOrEmpty(foundGames[key])));
                            if (LoginManager.Instance.IsLoggedIn())
                            {
                                string gameList = await WebHelper.GetAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/games?filter.id=$in:{gameIds}&limit=-1");
                                Game[] serverGames = JsonSerializer.Deserialize<PaginatedData<Game>>(gameList)!.Data;

                                var serverGameIds = new HashSet<int>(serverGames.Select(g => g.ID));
                                var missingGames = foundGames.Where(kvp => !serverGameIds.Contains(kvp.Key))
                                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                                //Make sure to display installed games that are already deleted on the server
                                if (missingGames.Count > 0)
                                {
                                    List<Game> offlineCacheGames = GetGamesFromOfflineCache(foundGames);
                                    if (offlineCacheGames.Count > 0)
                                    {
                                        serverGames = serverGames.Concat(offlineCacheGames).ToArray();
                                    }
                                }
                                return serverGames;
                            }
                            else
                            {
                                List<Game> offlineCacheGames = GetGamesFromOfflineCache(foundGames);
                                return offlineCacheGames.ToArray();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // The server answered with an error (restarting...): the installed games stay shown from the cache
                        MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex);
                        try { return GetGamesFromOfflineCache(foundGames).ToArray(); }
                        catch (Exception cacheError) { Log.Ignored(cacheError); return null; }
                    }
                }
                return null;
            });
            // None found (no game folder left, nothing installed): the list is emptied, as it was cleared before
            if (games != null || gamesRestored)
            {
                games ??= Array.Empty<Game>();
                ObservableCollection<KeyValuePair<Game, string>> TempInstalledGames = new ObservableCollection<KeyValuePair<Game, string>>();
                for (int count = 0; count < foundGames.Count; count++)
                {
                    try
                    {
                        Game? game = games.Where(x => x.ID == foundGames.ElementAt(count).Key).FirstOrDefault();
                        if (game != null)
                        {
                            TempInstalledGames.Add(new KeyValuePair<Game, string>(game, foundGames.ElementAt(count).Value));
                            if (LoginManager.Instance.IsLoggedIn())
                            {
                                if (!Preferences.Exists(game.ID.ToString(), LoginManager.Instance.GetUserProfile().OfflineCache))
                                {
                                    await CacheHelper.CreateOfflineCacheAsync(game);
                                }
                                else
                                {
                                    string offlineCacheGameString = Preferences.Get(game.ID.ToString(), LoginManager.Instance.GetUserProfile().OfflineCache);
                                    offlineCacheGameString = GameVault.Core.StringCompressor.Decompress(offlineCacheGameString);
                                    Game offlineCacheGame = JsonSerializer.Deserialize<Game>(offlineCacheGameString);
                                    if (game.EntityVersion != offlineCacheGame?.EntityVersion)
                                    {
                                        await CacheHelper.CreateOfflineCacheAsync(game);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ignored) { Log.Ignored(ignored); }
                }
                // Registered while the disks were read (an installation that just finished): kept
                foreach (KeyValuePair<Game, string> registered in InstallViewModel.Instance.InstalledGames.Except(shownBefore).ToList())
                {
                    if (!TempInstalledGames.Any(g => g.Key.ID == registered.Key.ID) && IsInstallation(registered.Value))
                        TempInstalledGames.Add(registered);
                }
                InstallViewModel.Instance.InstalledGames = await SortInstalledGamesByLastPlayed(TempInstalledGames);
                SetFilteredView();
            }
            if (!fromCLI)
                gamesRestored = true;
            if (games != null && LoginManager.Instance.IsLoggedIn())
                AnnounceUpdates();
        }
        /// <summary>Tells about new builds of installed games, once per build.</summary>
        private static void AnnounceUpdates()
        {
            try
            {
                Game[] updates = InstalledGameState.GamesWithUpdates();
                string configFile = LoginManager.Instance.GetUserProfile().UserConfigFile;
                var announced = Preferences.Get(AppConfigKey.UpdatesNotified, configFile).Split(';', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                var current = updates.Select(game => $"{game.ID}:{game.Path}").ToList();
                Game[] fresh = updates.Where(game => !announced.Contains($"{game.ID}:{game.Path}")).ToArray();
                Preferences.Set(AppConfigKey.UpdatesNotified, string.Join(";", current), configFile);
                if (fresh.Length == 0)
                    return;
                string titles = string.Join(", ", fresh.Select(game => string.IsNullOrEmpty(game.Version) ? game.Title : $"{game.Title} {game.Version}"));
                MainWindowViewModel.Instance.AppBarText = Loc.F("Update available: {0}", titles);
                ToastMessageHelper.CreateToastMessage(fresh.Length == 1 ? Loc.T("Update available") : Loc.F("{0} updates available", fresh.Length), titles);
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }
        private async Task<ObservableCollection<KeyValuePair<Game, string>>> SortInstalledGamesByLastPlayed(ObservableCollection<KeyValuePair<Game, string>> collection)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string lastTimePlayed = Preferences.Get(AppConfigKey.LastPlayed, LoginManager.Instance.GetUserProfile().UserConfigFile);
                    List<string> lastPlayedDates = lastTimePlayed.Split(';').ToList();

                    collection = new System.Collections.ObjectModel.ObservableCollection<KeyValuePair<Game, string>>(collection.OrderByDescending(item =>
                    {
                        int key = item.Key.ID;
                        return lastPlayedDates.Contains(key.ToString()) ? lastPlayedDates.IndexOf(key.ToString()) : int.MaxValue;
                    }).Reverse());
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
                return collection;
            });
        }
        public void SetLastPlayedGame(int gameID)
        {
            try
            {
                string lastTimePlayed = Preferences.Get(AppConfigKey.LastPlayed, LoginManager.Instance.GetUserProfile().UserConfigFile);
                // Whole ids only: removing "1;" as text would also cut "21;" down to "2"
                var ids = lastTimePlayed.Split(';', StringSplitOptions.RemoveEmptyEntries).Where(id => id != gameID.ToString()).Prepend(gameID.ToString());
                Preferences.Set(AppConfigKey.LastPlayed, string.Join("", ids.Select(id => id + ";")), LoginManager.Instance.GetUserProfile().UserConfigFile);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }

        public void AddSystemFileWatcher(string path)
        {
            // Called from the UI and from the restore running in the background
            lock (m_FileWatcherList)
            {
                // One left from a folder deleted since (a download deleted, a setup cancelled) sees nothing any
                // more: replaced, else the game installed there again would never be registered
                foreach (FileSystemWatcher old in m_FileWatcherList.Where(x => x.Path == path).ToList())
                {
                    m_FileWatcherList.Remove(old);
                    old.Dispose();
                }
                var watcher = new FileSystemWatcher();
                watcher.Path = path;
                watcher.Filter = "*.*";
                watcher.Created += new FileSystemEventHandler(OnCreated);
                watcher.EnableRaisingEvents = true;
                m_FileWatcherList.Add(watcher);
            }
        }
        private async void OnCreated(object sender, FileSystemEventArgs e)
        {
            var watcher = (FileSystemWatcher)sender;
            string dir = watcher.Path;
            watcher.Created -= new FileSystemEventHandler(OnCreated);
            // Its work is done: the folder is watched by the system until the watcher is disposed
            lock (m_FileWatcherList)
                m_FileWatcherList.Remove(watcher);
            watcher.Dispose();
            try { await RegisterInstallationAsync(dir); }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>
        /// A game just installed in <paramref name="dir"/> joins the installed games (Play instead of Install &amp; Play):
        /// the server's game, else the one cached for offline play, else its title from the folder's name.
        /// </summary>
        /// <param name="known">The game as the installation has it: the server is not asked again.</param>
        public async Task RegisterInstallationAsync(string dir, Game? known = null)
        {
            int id = GameIdOf(dir);
            if (id == -1 || Dispatcher.UIThread.Invoke(() => InstallViewModel.Instance.InstalledGames.Any(x => x.Key.ID == id)))
                return;
            Game? game = known?.ID == id ? known : null;
            if (game == null && LoginManager.Instance.IsLoggedIn())
            {
                try { game = JsonSerializer.Deserialize<Game>(await WebHelper.GetAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/games/{id}")); }
                catch (Exception ex) { Log.Ignored(ex); }// the server is busy: the game still counts as installed
            }
            game ??= GetGamesFromOfflineCache(new Dictionary<int, string> { [id] = dir }).FirstOrDefault();
            if (game == null)
                return;
            Dispatcher.UIThread.Invoke(delegate
            {
                // Checked again on the UI thread, where the list changes
                if (InstallViewModel.Instance.InstalledGames.Any(x => x.Key.ID == id))
                    return;
                InstallViewModel.Instance.InstalledGames.Insert(0, new KeyValuePair<Game, string>(game, dir));
                SetLastPlayedGame(game.ID);
            });
        }

        /// <summary>A game's folder with something in it: an installation, not one waiting for it.</summary>
        internal static bool IsInstallation(string dir)
        {
            try { return Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any(); }
            catch (Exception ex) { Log.Ignored(ex); return false; }
        }

        /// <summary>The game's id from its folder's name, "(42)Title"; -1 when it is no game's folder.</summary>
        internal static int GameIdOf(string dir)
        {
            string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(dir));
            int end = name.IndexOf(')');
            return name.StartsWith('(') && end > 1 && int.TryParse(name.AsSpan(1, end - 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id) ? id : -1;
        }
        private void Hyperlink_RequestNavigate(object? sender, RoutedEventArgs e)
        {
            try
            {
                string url = (string)((Control)sender!).Tag!;
                PlatformInfo.OpenUrl(url);
                e.Handled = true;
            }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
        }
        private void GameCard_Clicked(object sender, RoutedEventArgs e)
        {
            if (((KeyValuePair<Game, string>)((Control)sender).DataContext).Key == null)
            {
                MainWindowViewModel.Instance.AppBarText = Loc.T("Cannot open game");
                return;
            }
            MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(((KeyValuePair<Game, string>)((Control)sender).DataContext).Key, LoginManager.Instance.IsLoggedIn()));
        }
        private string searchText = "";
        /// <summary>The library search also narrows the installed games.</summary>
        public void ApplySearch(string? text)
        {
            searchText = text?.Trim() ?? "";
            ApplyFilter();
        }

        private void SetFilteredView()
        {
            var view = new FilteredCollectionView<KeyValuePair<Game, string>>(InstallViewModel.Instance.InstalledGames);
            ((System.Collections.Specialized.INotifyCollectionChanged)view).CollectionChanged += (_, _) => InstallViewModel.Instance.VisibleInstalledCount = view.Count;
            InstallViewModel.Instance.InstalledGamesFilter = view;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var view = InstallViewModel.Instance.InstalledGamesFilter;
            if (view == null)
                return;
            string text = searchText;
            view.Filter = string.IsNullOrEmpty(text) ? null : item => ((KeyValuePair<Game, string>)item).Key?.Title?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
            InstallViewModel.Instance.VisibleInstalledCount = view.Count;
        }

        private void ScrollPrevious_Click(object? sender, RoutedEventArgs e) => ScrollRow(-1);
        private void ScrollNext_Click(object? sender, RoutedEventArgs e) => ScrollRow(1);
        private void ScrollRow(int direction)
        {
            var scroll = uiInstalledGamesScroll;
            double page = Math.Max(316, scroll.Viewport.Width - 316);
            scroll.Offset = new Vector(Math.Clamp(scroll.Offset.X + direction * page, 0, Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width)), 0);
        }

        private async void Play_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            ((Control)sender).IsEnabled = false;
            await PlayGame(((KeyValuePair<Game, string>)((Control)sender).DataContext).Key.ID);
            ((Control)sender).IsEnabled = true;
        }

        public static async Task PlayGame(int gameId)
        {

            string path = "";
            KeyValuePair<Game, string> result = InstallViewModel.Instance.InstalledGames.Where(g => g.Key.ID == gameId).FirstOrDefault();
            if (!result.Equals(default(KeyValuePair<Game, string>)))
            {
                path = result.Value;
            }
            if (GameMover.IsMoving(gameId))
            {
                MainWindowViewModel.Instance.AppBarText = Loc.F("'{0}' is being moved: wait until it is finished", result.Key.Title);
                return;
            }
            if (!Directory.Exists(path))
            {
                MainWindowViewModel.Instance.AppBarText = Loc.F("Can not find part of '{0}'", path);
                return;
            }
            if (SettingsViewModel.Instance.CloudSaves)
            {
                MainWindowViewModel.Instance.AppBarText = Loc.T("Syncing cloud save...");
                string status = await SaveGameHelper.Instance.RestoreBackup(gameId, path);
                if (status == CloudSaveStatus.Cancelled)
                {
                    MainWindowViewModel.Instance.AppBarText = status;
                    return;
                }
            }
            string savedExecutable = Preferences.Get(AppConfigKey.Executable, Path.Combine(path, "gamevault-exec"));
            string parameter = Preferences.Get(AppConfigKey.LaunchParameter, Path.Combine(path, "gamevault-exec"));
            if (savedExecutable == string.Empty)
            {
                if (GameSettingsUserControl.TryPrepareLaunchExecutable(path, result.Key?.Metadata?.Title ?? result.Key?.Title))
                {
                    savedExecutable = Preferences.Get(AppConfigKey.Executable, Path.Combine(path, "gamevault-exec"));
                }
                else
                {
                    MainWindowViewModel.Instance.AppBarText = Loc.T("No valid Executable found");
                    return;
                }
            }
            if (File.Exists(savedExecutable))
            {
                if (OperatingSystem.IsLinux() && ProcessHelper.IsWindowsProgram(savedExecutable))
                {
                    // protonfixes lookup and winetricks components of this game
                    try { await GameFixes.PrepareAsync(path, result.Key?.Metadata?.Title ?? result.Key?.Title, status => MainWindowViewModel.Instance.AppBarText = status); }
                    catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
                }
                try
                {
                    ProcessHelper.StartApp(savedExecutable, parameter, installationDirectory: path);
                }
                catch (Exception ex) when (!OperatingSystem.IsWindows())
                {
                    // e.g. the game's compatibility tool is missing
                    MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex);
                    return;
                }
                catch
                {

                    try
                    {
                        ProcessHelper.StartApp(savedExecutable, parameter, true, path);
                    }
                    catch
                    {
                        MainWindowViewModel.Instance.AppBarText = Loc.F("Can not execute '{0}'", savedExecutable);
                    }
                }
                MainWindowViewModel.Instance.Library.GetGameInstalls().SetLastPlayedGame(result.Key.ID);
                DiscordHelper.Instance.GameStarted(result.Key.ID);
            }
            else
            {
                MainWindowViewModel.Instance.AppBarText = Loc.F("Could not find Executable '{0}'", savedExecutable);
            }
        }
        private async void Settings_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            try
            {
                int ID = ((KeyValuePair<Game, string>)((Control)sender).DataContext).Key.ID;
                string result = await WebHelper.GetAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/games/{ID}");
                Game resultGame = JsonSerializer.Deserialize<Game>(result);
                MainWindowViewModel.Instance.OpenPopup(new GameSettingsUserControl(resultGame) { Width = 1200, Height = 800, Margin = new Thickness(50) });
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex);
            }
        }
        private void InstalledGames_Toggled(object? sender, RoutedEventArgs e)
        {
            Preferences.Set(AppConfigKey.InstalledGamesOpen, uiInstalledGames.IsChecked == true ? "1" : "0", LoginManager.Instance.GetUserProfile().UserConfigFile);
        }

        private Task Collection_Updated(int oldCount, int newCount)
        {
            ApplyFilter();
            return Task.CompletedTask;
        }
    }
}
