using gamevault.Localization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GameVault.Core;
using GameVault.Core.Publishing;
using gamevault.Helper;
using gamevault.Models;
using gamevault.Models.Mapping;
using IO.Swagger.Model;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace gamevault.UserControls.SettingsComponents
{
    /// <summary>
    /// Admin assistant: packs a game for the server (file name with type/version/year tags, archive written
    /// atomically into the server's games folder), waits until the server indexed it and sets the launch and
    /// installer metadata, so users only press "Install &amp; Play".
    /// </summary>
    public partial class PublishGameUserControl : UserControl
    {
        private readonly PublishGameViewModel ViewModel = new();

        public PublishGameUserControl()
        {
            InitializeComponent();
            DataContext = ViewModel;
            ViewModel.TargetDirectory = Preferences.Get(AppConfigKey.PublishTargetDirectory, LoginManager.Instance.GetUserProfile().UserConfigFile);
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape && ViewModel.IsIdle)
                    MainWindowViewModel.Instance.ClosePopup();
            };
        }

        private void Close_Click(object? sender, PointerReleasedEventArgs e)
        {
            if (ViewModel.IsIdle)
                MainWindowViewModel.Instance.ClosePopup();
        }

        #region Source
        private async void PickFolder_Click(object sender, RoutedEventArgs e)
        {
            string? folder = await StorageHelper.PickFolderAsync("Select the game folder");
            if (!string.IsNullOrEmpty(folder))
                await AnalyzeAsync(folder);
        }

        private async void PickFile_Click(object sender, RoutedEventArgs e)
        {
            string? file = await StorageHelper.PickFileAsync("Select the installer or the archive", new Dictionary<string, string[]>
            {
                { "Installers and archives", new[] { "*.exe", "*.msi", "*.zip", "*.7z", "*.rar", "*.iso", "*.tar.gz", "*.tar.xz", "*.tar" } },
            });
            if (!string.IsNullOrEmpty(file))
                await AnalyzeAsync(file);
        }

        private async void PickTarget_Click(object sender, RoutedEventArgs e)
        {
            string? folder = await StorageHelper.PickFolderAsync("Select the folder the GameVault server reads its games from", ViewModel.TargetDirectory);
            if (!string.IsNullOrEmpty(folder))
                ViewModel.TargetDirectory = folder;
        }

        /// <summary>Guesses title, year, type, main executable and installer of the selected source.</summary>
        private async Task AnalyzeAsync(string source)
        {
            ViewModel.PublishedGame = null;
            ViewModel.SourcePath = source;
            ViewModel.Title = GameFileName.GuessTitle(source);
            Match year = Regex.Match(GameFileName.DescriptiveName(source), @"[\(\[ ._-]((19|20)\d{2})[\)\] ._-]");
            ViewModel.Year = year.Success ? year.Groups[1].Value : "";
            Match version = Regex.Match(GameFileName.DescriptiveName(source), @"v?(\d+(\.\d+)+)", RegexOptions.IgnoreCase);
            ViewModel.Version = version.Success ? version.Groups[1].Value : "";
            ViewModel.Executables = new List<string>();
            ViewModel.Installers = new List<string>();
            ViewModel.SelectedExecutable = null;
            ViewModel.SelectedInstaller = null;
            ViewModel.InstallerParameters = "";
            ViewModel.InstallerHint = "";
            ViewModel.LaunchParameters = "";

            if (ViewModel.IsArchiveSource)
            {
                // The content is only known after extraction on the user's computer
                ViewModel.TypeIndex = 0;
                ViewModel.Status = Loc.T("Archives are published as they are. Choose whether the archive contains a portable game or an installer.");
                return;
            }

            ViewModel.Status = Loc.T("Analyzing the game files...");
            string folder = ViewModel.SourceFolder;
            string title = ViewModel.Title;
            string[] ignored = SettingsViewModel.Instance.IgnoreList ?? Array.Empty<string>();
            bool fileIsInstaller = File.Exists(source);
            var analysis = await Task.Run(() =>
            {
                var installers = fileIsInstaller ? new List<string> { Path.GetFileName(source) } : ExecutableFinder.FindInstallers(folder);
                var windows = ExecutableFinder.Rank(folder, title, ignored);
                var linux = windows.Count == 0 ? ExecutableFinder.Rank(folder, title, ignored, linux: true) : new List<ExecutableCandidate>();
                bool hasSetupData = Directory.EnumerateFiles(folder, "*.bin", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).Any();
                return (installers, windows, linux, hasSetupData);
            });

            ViewModel.Installers = analysis.installers;
            ViewModel.SelectedInstaller = analysis.installers.FirstOrDefault();
            // An installer next to its data files (setup.exe + *.bin), or a single installer: published as installer
            bool isSetup = fileIsInstaller || (analysis.installers.Count > 0 && (analysis.hasSetupData || analysis.windows.Count(c => c.Score > 0) == 0));
            if (analysis.windows.Count == 0 && analysis.linux.Count > 0)
            {
                ViewModel.TypeIndex = 2;
                ViewModel.Executables = analysis.linux.Select(c => c.RelativePath).ToList();
            }
            else
            {
                ViewModel.TypeIndex = isSetup ? 1 : 0;
                ViewModel.Executables = analysis.windows.Where(c => c.Score > -50).Select(c => c.RelativePath).ToList();
            }
            ViewModel.SelectedExecutable = ViewModel.Executables.FirstOrDefault();
            DetectInstaller();
            ViewModel.Status = isSetup
                ? Loc.T("Installer found. Check the installer parameters, then publish.")
                : ViewModel.SelectedExecutable == null ? Loc.T("No executable found.") : Loc.F("Main executable: {0}. Check it, then publish.", ViewModel.SelectedExecutable);
        }

        private void Installer_SelectionChanged(object? sender, SelectionChangedEventArgs e) => DetectInstaller();

        private void DetectInstaller()
        {
            if (ViewModel.SelectedInstaller == null)
            {
                ViewModel.InstallerHint = ViewModel.IsSetup ? Loc.T("No installer found in the folder.") : "";
                return;
            }
            var info = InstallerDetector.Detect(Path.Combine(ViewModel.SourceFolder, ViewModel.SelectedInstaller));
            ViewModel.InstallerParameters = info.SilentParameters ?? "";
            ViewModel.InstallerHint = info.Description;
        }
        #endregion

        #region Publish
        private async void Publish_Click(object sender, RoutedEventArgs e)
        {
            string target = ViewModel.TargetDirectory?.Trim() ?? "";
            if (!ViewModel.HasSource || string.IsNullOrWhiteSpace(ViewModel.FileName))
            {
                ViewModel.Status = Loc.T("Choose the game files and a title first.");
                return;
            }
            if (!Directory.Exists(target))
            {
                ViewModel.Status = Loc.T("The server games folder does not exist.");
                return;
            }
            string destination = Path.Combine(target, ViewModel.FileName);
            if (Path.GetFullPath(ViewModel.SourcePath).StartsWith(Path.GetFullPath(target).TrimEnd('/', '\\') + Path.DirectorySeparatorChar) && Directory.Exists(ViewModel.SourcePath))
            {
                ViewModel.Status = Loc.T("The game folder is inside the server games folder: the server would index its files one by one. Move it elsewhere first.");
                return;
            }
            if (File.Exists(destination) && !await DialogService.ConfirmAsync(Loc.F("{0} already exists on the server. Replace it?", ViewModel.FileName), Loc.T("Publish a Game")))
                return;
            Preferences.Set(AppConfigKey.PublishTargetDirectory, target, LoginManager.Instance.GetUserProfile().UserConfigFile);

            ViewModel.IsBusy = true;
            ViewModel.Progress = 0;
            ViewModel.PublishedGame = null;
            try
            {
                var progress = new Progress<double>(p => ViewModel.Progress = p);
                if (ViewModel.IsArchiveSource)
                {
                    ViewModel.Status = Loc.F("Copying {0} to the server...", Path.GetFileName(ViewModel.SourcePath));
                    await GamePackager.CopyFileAsync(ViewModel.SourcePath, destination, progress);
                }
                else
                {
                    ViewModel.Status = Loc.F("Creating {0}... (large games take a while)", ViewModel.FileName);
                    await GamePackager.CreateArchiveAsync(ViewModel.SourceFolder, destination, ViewModel.Compress, progress);
                }

                ViewModel.Status = Loc.T("Waiting for the server to index the game...");
                Game? game = await WaitForIndexedGameAsync(ViewModel.FileName, ViewModel.Title);
                if (game == null)
                {
                    ViewModel.Status = Loc.F("{0} was created, but the server has not indexed it yet. Check that this folder is the one mounted as /files, then reindex from the admin console.", ViewModel.FileName);
                    return;
                }

                ViewModel.Status = Loc.T("Saving launch and installation settings...");
                await SaveMetadataAsync(game);
                await MainWindowViewModel.Instance.Library.LoadLibrary();
                ViewModel.PublishedGame = game;
                ViewModel.Status = Loc.F("'{0}' is published. Users can install and start it with \"Install & Play\".", game.Title);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Publishing failed");
                ViewModel.Status = Loc.F("Publishing failed: {0}", WebExceptionHelper.TryGetServerMessage(ex));
            }
            finally
            {
                ViewModel.IsBusy = false;
            }
        }

        /// <summary>
        /// The server indexes new files by itself (file watcher / interval); a reindex is requested when that takes long.
        /// </summary>
        private static async Task<Game?> WaitForIndexedGameAsync(string fileName, string title)
        {
            bool reindexRequested = false;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                if (attempt == 5 && !reindexRequested)
                {
                    reindexRequested = true;
                    try { await WebHelper.PutAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games/reindex", string.Empty); }
                    catch (Exception ex) { Log.Ignored(ex); }
                }
                try
                {
                    string search = Uri.EscapeDataString(title);
                    string json = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games?search={search}&limit=50");
                    var games = JsonSerializer.Deserialize<PaginatedData<Game>>(json)?.Data ?? Array.Empty<Game>();
                    Game? match = games.FirstOrDefault(g => g.Path != null && Path.GetFileName(g.Path.Replace('\\', '/')) == fileName);
                    if (match != null)
                        return match;
                }
                catch (Exception ex) { Log.Ignored(ex); }
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
            return null;
        }

        private async Task SaveMetadataAsync(Game game)
        {
            var metadata = new UpdateGameUserMetadataDto();
            if (ViewModel.IsSetup)
            {
                metadata.InstallerExecutable = string.IsNullOrWhiteSpace(ViewModel.SelectedInstaller) ? null : ViewModel.SelectedInstaller;
                metadata.InstallerParameters = string.IsNullOrWhiteSpace(ViewModel.InstallerParameters) ? null : ViewModel.InstallerParameters.Trim();
            }
            else
            {
                metadata.LaunchExecutable = string.IsNullOrWhiteSpace(ViewModel.SelectedExecutable) ? null : ViewModel.SelectedExecutable;
            }
            metadata.LaunchParameters = string.IsNullOrWhiteSpace(ViewModel.LaunchParameters) ? null : ViewModel.LaunchParameters.Trim();
            if (metadata.LaunchExecutable == null && metadata.LaunchParameters == null && metadata.InstallerExecutable == null && metadata.InstallerParameters == null)
                return;
            var update = new UpdateGameDto { UserMetadata = metadata };
            var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            await WebHelper.PutAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games/{game.ID}", JsonSerializer.Serialize(update, options));
        }

        private void OpenGame_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.PublishedGame == null)
                return;
            MainWindowViewModel.Instance.ClosePopup();
            MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(ViewModel.PublishedGame, true));
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SourcePath = "";
            ViewModel.Title = "";
            ViewModel.Version = "";
            ViewModel.Year = "";
            ViewModel.Status = "";
            ViewModel.PublishedGame = null;
        }
        #endregion
    }
}
