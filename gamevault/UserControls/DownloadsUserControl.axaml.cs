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

                         string decompressedObject = GameVault.Core.StringCompressor.Decompress(objectFromFile);
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
                     GameVault.Core.Log.Ignored(exFormat);
                     MainWindowViewModel.Instance.AppBarText = Loc.T("The offline cache is corrupted");
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
            // Remove the controls whose files are gone, but not the ones running or waiting in the queue: a queued
            // download has no file yet, and a running one keeps running (a root directory was added, space was freed)
            for (int i = DownloadsViewModel.Instance.DownloadedGames.Count - 1; i >= 0; i--)
            {
                var control = DownloadsViewModel.Instance.DownloadedGames[i];
                if (!validGameIds.Contains(control.GetGameId()) && !control.IsBusy() && !DownloadQueue.IsWaiting(control))
                    DownloadsViewModel.Instance.DownloadedGames.RemoveAt(i);
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
            foreach (var download in DownloadsViewModel.Instance.RetroDownloads.ToList())
                download.Pause(advance: false);
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
                MainWindowViewModel.Instance.AppBarText = Loc.F("'{0}' starts as soon as it is installed", game.Title);
                return;
            }
            if (existing != null && existing.HasDownloadedFiles())
            {
                MainWindowViewModel.Instance.AppBarText = Loc.F("Installing '{0}'...", game.Title);
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
                MainWindowViewModel.Instance.AppBarText = Loc.F("'{0}' is not installed", game.Title);
                return;
            }
            if (!LoginManager.Instance.IsLoggedIn())
            {
                MainWindowViewModel.Instance.AppBarText = Loc.T("You are not logged in or offline");
                return;
            }
            GameDownloadUserControl? existing = DownloadsViewModel.Instance.DownloadedGames.FirstOrDefault(d => d.GetGameId() == game.ID);
            if (existing != null)
            {
                if (existing.IsBusy() || DownloadQueue.IsWaiting(existing))
                {
                    MainWindowViewModel.Instance.AppBarText = Loc.F("'{0}' is already being downloaded", game.Title);
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
                MainWindowViewModel.Instance.AppBarText = Loc.F("Not enough space available to update '{0}'", game.Title);
                return;
            }
            DownloadsViewModel.Instance.DownloadedGames.Insert(0, new GameDownloadUserControl(game, root, true, installed.Value) { IsUpdate = true });
            MainWindowViewModel.Instance.AppBarText = Loc.F("'{0}' is updated{1}, your saves and settings are kept", game.Title, (string.IsNullOrEmpty(game.Version) ? "" : Loc.F(" to {0}", game.Version)));
        }
        /// <param name="unattended">Asked from elsewhere (remote install): nobody answers questions, the last
        /// install location is used and a game already downloaded stays as it is.</param>
        /// <returns>Null when the download was added, else why not (also shown in the app bar).</returns>
        public async Task<string?> TryStartDownload(Game game, bool playWhenInstalled = false, bool unattended = false)
        {
            static string Fail(string message)
            {
                MainWindowViewModel.Instance.AppBarText = message;
                return message;
            }
            bool ask = !unattended && (SettingsViewModel.Instance.AskInstallLocation || SettingsViewModel.Instance.RootDirectories.Count == 0);
            if (!ask && SettingsViewModel.Instance.RootDirectories.Count == 0)
                return Fail(Loc.T("No Root Directory configured! Go to ⚙️Settings->Data"));
            string selectedDirectory;
            if (!ask)
            {
                selectedDirectory = InstallLocationUserControl.DefaultLocation();
            }
            else
            {
                long.TryParse(game.Size, out long size);
                var installLocationPicker = new InstallLocationUserControl(size);
                MainWindowViewModel.Instance.OpenPopup(installLocationPicker);
                selectedDirectory = await installLocationPicker.SelectInstallLocation();
            }
            if (selectedDirectory == string.Empty)
                return Loc.T("No install location was chosen");

            if (!Directory.Exists(selectedDirectory))
                return Fail(Loc.T("Selected directory does not exist"));
            if (LoginManager.Instance.IsLoggedIn() == false)
                return Fail(Loc.T("You are not logged in or offline"));
            if (GameMover.IsMoving(game.ID))
                return Fail(Loc.F("'{0}' is being moved: wait until it is finished", game.Title));
            if (IsAlreadyDownloading(game.ID))
                return Fail(Loc.F("'{0}' is already in the download queue", game.Title));
            if (unattended && DownloadsViewModel.Instance.DownloadedGames.Any(g => g.GetGameId() == game.ID))
                return Fail(Loc.F("'{0}' is already downloaded on this PC", game.Title));
            if (!unattended && await IsAlreadyDownloaded(game.ID))
                return Loc.T("The game was already downloaded");
            if (!IsEnoughDriveSpaceAvailable(Convert.ToInt64(game.Size), selectedDirectory))
            {
                string? driveName = PlatformInfo.GetDriveForPath(selectedDirectory)?.Name ?? Path.GetPathRoot(selectedDirectory);
                return Fail(Loc.F("Not enough space available for drive {0}", driveName));
            }
            GameDownloadUserControl? oldDownloadEntry = DownloadsViewModel.Instance.DownloadedGames.Where(g => g.GetGameId() == game.ID).FirstOrDefault();
            if (oldDownloadEntry != null)
            {
                DownloadQueue.Remove(oldDownloadEntry);
                DownloadsViewModel.Instance.DownloadedGames.Remove(oldDownloadEntry);
            }
            DownloadsViewModel.Instance.DownloadedGames.Insert(0, new GameDownloadUserControl(game, selectedDirectory, true) { PlayWhenInstalled = playWhenInstalled });
            MainWindowViewModel.Instance.AppBarText = playWhenInstalled
                ? Loc.F("'{0}' is downloaded, installed and started for you", game.Title)
                : Loc.F("'{0}' has been added to the download queue", game.Title);
            return null;
        }
        private async Task<bool> IsAlreadyDownloaded(int id)
        {
            if (DownloadsViewModel.Instance.DownloadedGames.Where(gameUC => gameUC.GetGameId() == id).Count() > 0)
            {
                MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync(Loc.T("This game was already downloaded. Do you want to overwrite this file?"),
                    "", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = Loc.T("Yes"), NegativeButtonText = Loc.T("No") });
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
            MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync(Loc.T("Are you sure you want to delete all canceled and completed downloads?\n\nThis cannot be undone."), "", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = Loc.T("Yes"), NegativeButtonText = Loc.T("No") });

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

        private static RetroDownload? RetroOf(object? sender) => (sender as Control)?.DataContext as RetroDownload;

        private void RetroOpenFolder_Click(object? sender, RoutedEventArgs e)
        {
            if (RetroOf(sender) is RetroDownload download)
                PlatformInfo.OpenFolder(download.Folder);
        }

        private async void RetroCancel_Click(object? sender, RoutedEventArgs e)
        {
            if (RetroOf(sender) is not RetroDownload download)
                return;
            MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync(Loc.F("Are you sure you want to delete '{0}' ?", download.Game.Name), "",
                MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = Loc.T("Yes"), NegativeButtonText = Loc.T("No") });
            if (result == MessageDialogResult.Affirmative)
                download.Cancel();
        }

        private void RetroPauseResume_Click(object? sender, RoutedEventArgs e)
        {
            if (RetroOf(sender) is not RetroDownload download)
                return;
            if (download.IsPaused)
                download.Resume();
            else
                download.Pause();
        }

        private void RetroStartNow_Click(object? sender, RoutedEventArgs e) => RetroOf(sender)?.StartNow();
        private void RetroRetry_Click(object? sender, RoutedEventArgs e) => RetroOf(sender)?.Retry();
        private void RetroDismiss_Click(object? sender, RoutedEventArgs e) => RetroOf(sender)?.Dismiss();

        private async void RetroPlay_Click(object? sender, RoutedEventArgs e)
        {
            if (RetroOf(sender) is RetroDownload download)
                await RetroPlay.PlayAsync(download.Game);
        }

        private void Storage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => MainWindowViewModel.Instance.OpenPopup(new StorageUserControl());
    }
}
