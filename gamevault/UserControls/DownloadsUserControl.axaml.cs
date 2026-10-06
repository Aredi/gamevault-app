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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using gamevault.Helper.Integrations;
using System.IO;

namespace gamevault.UserControls
{
    /// <summary>
    /// Interaction logic for DownloadsUserControl.xaml
    /// </summary>
    public partial class DownloadsUserControl : UserControl
    {

        public DownloadsUserControl()
        {
            InitializeComponent();

            this.DataContext = DownloadsViewModel.Instance;
        }
        public async Task RestoreDownloadedGames()
        {
            Dictionary<Game, string>? games = await Task.Run<Dictionary<Game, string>?>(async () =>
             {

                 if (SettingsViewModel.Instance.RootDirectories.Count == 0)
                     return null;

                 List<string> allDirectoriesFromRootDirectories = new List<string>();
                 foreach (DirectoryEntry dirEntry in SettingsViewModel.Instance.RootDirectories)
                 {
                     if (Directory.Exists(Path.Combine(dirEntry.Uri, "GameVault", "Downloads")))
                         allDirectoriesFromRootDirectories.AddRange(Directory.GetDirectories(Path.Combine(dirEntry.Uri, "GameVault", "Downloads")));
                 }
                 Dictionary<int, string> foundPathsById = new Dictionary<int, string>();
                 foreach (string dir in allDirectoriesFromRootDirectories)
                 {
                     try
                     {
                         if (new DirectoryInfo(dir).GetFiles().Length == 0)
                             continue;

                         string dirName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
                         string gameId = dirName.Substring(1, dirName.IndexOf(')') - 1);

                         if (int.TryParse(gameId, out int id))
                             foundPathsById.Add(id, SettingsViewModel.Instance.RootDirectories
                            .Where(x => dir.Contains(x.Uri))
                            .OrderByDescending(x => x.Uri.Length)
                            .First().Uri);

                     }
                     catch { continue; }
                 }
                 if (foundPathsById.Count == 0)
                     return null;
                 try
                 {
                     if (LoginManager.Instance.IsLoggedIn())
                     {
                         string gameList = await WebHelper.GetAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/games?filter.id=$in:{string.Join(',', foundPathsById.Keys)}&limit=-1");
                         Dictionary<Game, string> foundGames = new Dictionary<Game, string>();
                         foreach (Game game in JsonSerializer.Deserialize<PaginatedData<Game>>(gameList)?.Data)
                         {
                             if (foundPathsById.TryGetValue(game.ID, out string path))
                             {
                                 foundGames.Add(game, path);
                             }
                         }
                         return foundGames;
                     }
                     Dictionary<Game, string> offlineCacheGames = new Dictionary<Game, string>();
                     foreach (int id in foundPathsById.Keys)
                     {
                         string objectFromFile = Preferences.Get(id.ToString(), LoginManager.Instance.GetUserProfile().OfflineCache);
                         if (objectFromFile == string.Empty)
                             continue;

                         string decompressedObject = StringCompressor.DecompressString(objectFromFile);
                         Game? deserializedObject = JsonSerializer.Deserialize<Game>(decompressedObject);
                         if (deserializedObject != null)
                         {
                             if (foundPathsById.TryGetValue(deserializedObject.ID, out string path))
                             {
                                 offlineCacheGames.Add(deserializedObject, path);
                             }
                         }
                     }
                     return offlineCacheGames;
                 }
                 catch (FormatException exFormat)
                 {
                     MainWindowViewModel.Instance.AppBarText = "The offline cache is corrupted";
                 }
                 catch (Exception ex)
                 {
                     string webMsg = WebExceptionHelper.TryGetServerMessage(ex);
                     MainWindowViewModel.Instance.AppBarText = webMsg;
                 }
                 return null;
             });
            if (games == null)
                return;

            var validGameIds = new HashSet<int>(games.Keys.Select(g => g.ID));
            // Remove controls not in the dictionary, unless they're downloading
            for (int i = DownloadsViewModel.Instance.DownloadedGames.Count - 1; i >= 0; i--)
            {
                var control = DownloadsViewModel.Instance.DownloadedGames[i];
                int gameId = control.GetGameId();

                bool existsInDict = validGameIds.Contains(gameId);
                
                if (control.IsDownloading())
                {
                    control.PauseDownload();
                }
                if (!existsInDict)
                {
                    DownloadsViewModel.Instance.DownloadedGames.RemoveAt(i);
                }
            }
            var existingIds = new HashSet<int>(DownloadsViewModel.Instance.DownloadedGames.Select(c => c.GetGameId()));

            foreach (var game in games)
            {
                if (!existingIds.Contains(game.Key.ID))
                {
                    DownloadsViewModel.Instance.DownloadedGames.Add(new GameDownloadUserControl(game.Key, game.Value, false));
                }
            }
        }
        public void RefreshGame(Game game)
        {
            for (int i = 0; i < DownloadsViewModel.Instance.DownloadedGames.Count; i++)
            {
                if (DownloadsViewModel.Instance.DownloadedGames[i].GetGameId() == game.ID)
                {
                    DownloadsViewModel.Instance.DownloadedGames[i].Refresh(game);
                    return;
                }
            }
        }
        /// <summary>Pauses the running downloads; they keep their progress and can be resumed at the next start.</summary>
        public void PauseAllDownloads()
        {
            foreach (var download in DownloadsViewModel.Instance.DownloadedGames.ToList())
                download.PauseDownload();
        }
        public void CancelAllDownloads()
        {
            foreach (var download in DownloadsViewModel.Instance.DownloadedGames)
            {
                download.CancelDownload();
            }
        }

        /// <summary>
        /// One click from the game page: download (or continue an existing download), extract, install and start.
        /// </summary>
        public async Task InstallAndPlay(Game game)
        {
            GameDownloadUserControl? existing = DownloadsViewModel.Instance.DownloadedGames.FirstOrDefault(d => d.GetGameId() == game.ID);
            if (existing != null && (existing.IsDownloading() || DownloadQueue.IsWaiting(existing) || existing.IsPaused()))
            {
                existing.PlayWhenInstalled = true;
                MainWindowViewModel.Instance.AppBarText = $"'{game.Title}' starts as soon as it is installed";
                return;
            }
            if (existing != null && existing.HasDownloadedFiles())
            {
                MainWindowViewModel.Instance.AppBarText = $"Installing '{game.Title}'...";
                await existing.ContinueToPlay();
                return;
            }
            await TryStartDownload(game, playWhenInstalled: true);
        }
        /// <summary>
        /// Downloads the server's current build of an installed game and installs it over the installation
        /// (saves, settings and the Wine prefix stay).
        /// </summary>
        public async Task UpdateGame(Game game)
        {
            KeyValuePair<Game, string> installed = InstallViewModel.Instance.InstalledGames.FirstOrDefault(g => g.Key.ID == game.ID);
            if (installed.Key == null || !Directory.Exists(installed.Value))
            {
                MainWindowViewModel.Instance.AppBarText = $"'{game.Title}' is not installed";
                return;
            }
            if (!LoginManager.Instance.IsLoggedIn())
            {
                MainWindowViewModel.Instance.AppBarText = "You are not logged in or offline";
                return;
            }
            GameDownloadUserControl? existing = DownloadsViewModel.Instance.DownloadedGames.FirstOrDefault(d => d.GetGameId() == game.ID);
            if (existing != null)
            {
                if (existing.IsBusy() || DownloadQueue.IsWaiting(existing))
                {
                    MainWindowViewModel.Instance.AppBarText = $"'{game.Title}' is already being downloaded";
                    return;
                }
                // The archive of the old version is not needed anymore
                DownloadQueue.Remove(existing);
                await existing.DeleteFile(confirm: false);
                DownloadsViewModel.Instance.DownloadedGames.Remove(existing);
            }
            // <root>/GameVault/Installations/(id)Title
            string? root = Directory.GetParent(installed.Value)?.Parent?.Parent?.FullName;
            if (root == null)
                return;
            if (!IsEnoughDriveSpaceAvailable(Convert.ToInt64(game.Size), root))
            {
                MainWindowViewModel.Instance.AppBarText = $"Not enough space available to update '{game.Title}'";
                return;
            }
            DownloadsViewModel.Instance.DownloadedGames.Insert(0, new GameDownloadUserControl(game, root, true, installed.Value) { IsUpdate = true });
            MainWindowViewModel.Instance.AppBarText = $"'{game.Title}' is updated{(string.IsNullOrEmpty(game.Version) ? "" : $" to {game.Version}")}, your saves and settings are kept";
        }
        public async Task TryStartDownload(Game game, bool playWhenInstalled = false)
        {
            if (SettingsViewModel.Instance.RootDirectories.Count == 0)
            {
                MainWindowViewModel.Instance.AppBarText = "No Root Directory configured! Go to ⚙️Settings->Data";
                return;
            }
            var installLocationPicker = new InstallLocationUserControl();
            MainWindowViewModel.Instance.OpenPopup(installLocationPicker);
            string selectedDirectory = await installLocationPicker.SelectInstallLocation();
            if (selectedDirectory == string.Empty)
                return;

            if (!Directory.Exists(selectedDirectory))
            {
                MainWindowViewModel.Instance.AppBarText = "Selected directory does not exist";
                return;
            }
            if (LoginManager.Instance.IsLoggedIn() == false)
            {
                MainWindowViewModel.Instance.AppBarText = "You are not logged in or offline";
                return;
            }
            if (IsAlreadyDownloading(game.ID))
            {
                MainWindowViewModel.Instance.AppBarText = $"'{game.Title}' is already in the download queue";
                return;
            }
            if (await IsAlreadyDownloaded(game.ID))
            {
                return;
            }
            if (IsEnoughDriveSpaceAvailable(Convert.ToInt64(game.Size), selectedDirectory))
            {
                GameDownloadUserControl? oldDownloadEntry = DownloadsViewModel.Instance.DownloadedGames.Where(g => g.GetGameId() == game.ID).FirstOrDefault();
                if (oldDownloadEntry != null)
                {
                    DownloadQueue.Remove(oldDownloadEntry);
                    DownloadsViewModel.Instance.DownloadedGames.Remove(oldDownloadEntry);
                }
                DownloadsViewModel.Instance.DownloadedGames.Insert(0, new GameDownloadUserControl(game, selectedDirectory, true) { PlayWhenInstalled = playWhenInstalled });
                MainWindowViewModel.Instance.AppBarText = playWhenInstalled
                    ? $"'{game.Title}' is downloaded, installed and started for you"
                    : $"'{game.Title}' has been added to the download queue";
            }
            else
            {
                string? driveName = PlatformInfo.GetDriveForPath(selectedDirectory)?.Name ?? Path.GetPathRoot(selectedDirectory);
                MainWindowViewModel.Instance.AppBarText = $"Not enough space available for drive {driveName}";
            }
        }
        private async Task<bool> IsAlreadyDownloaded(int id)
        {
            if (DownloadsViewModel.Instance.DownloadedGames.Where(gameUC => gameUC.GetGameId() == id).Count() > 0)
            {
                MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync($"This game was already downloaded. Do you want to overwrite this file?",
                    "", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = "Yes", NegativeButtonText = "No" });
                if (result == MessageDialogResult.Affirmative)
                {
                    return false;
                }
                return true;
            }
            return false;
        }
        private bool IsAlreadyDownloading(int id)
        {
            if (DownloadsViewModel.Instance.DownloadedGames.Any(gameUC => gameUC.IsGameIdDownloading(id) || (gameUC.GetGameId() == id && DownloadQueue.IsWaiting(gameUC))))
            {
                return true;
            }
            return false;
        }
        private bool IsEnoughDriveSpaceAvailable(long gameSize, string directory)
        {
            DriveInfo? drive = PlatformInfo.GetDriveForPath(directory);
            if (drive == null)
                return false;
            return (drive.AvailableFreeSpace - 1000) > gameSize;
        }

        private async void DeleteAllDownloads_Click(object sender, RoutedEventArgs e)
        {
            MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync($"Are you sure you want to delete all canceled and completed downloads?\n\nThis cannot be undone.", "", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = "Yes", NegativeButtonText = "No" });

            if (result == MessageDialogResult.Affirmative)
            {
                try
                {
                    for (int count = DownloadsViewModel.Instance.DownloadedGames.Count - 1; count >= 0; count--)
                    {
                        await DownloadsViewModel.Instance.DownloadedGames[count].DeleteFile(false);
                    }
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
            }
        }
    }
}
