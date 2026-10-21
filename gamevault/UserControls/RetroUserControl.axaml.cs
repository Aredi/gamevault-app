using Avalonia.Controls;
using Avalonia.Interactivity;
using GameVault.Core;
using GameVault.Core.Retro;
using gamevault.Helper;
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
        private List<RetroGame> games = new();
        internal IEnumerable<RetroGame> Shown => uiGames.ItemsSource as IEnumerable<RetroGame> ?? Array.Empty<RetroGame>();
        private bool loaded;

        public RetroUserControl()
        {
            InitializeComponent();
            AttachedToVisualTree += (_, _) =>
            {
                Focus();
                if (!loaded)
                    _ = LoadPlatformsAsync();
            };
        }

        private void Show(string? message, bool loading = false)
        {
            uiMessage.Text = message;
            uiMessage.IsVisible = message != null;
            uiLoading.IsActive = uiLoading.IsVisible = loading;
        }

        internal async Task LoadPlatformsAsync()
        {
            loaded = true;
            Show(null, loading: true);
            try
            {
                List<RetroPlatform>? platforms = await RetroService.ListPlatformsAsync();
                if (platforms == null || platforms.Count == 0)
                {
                    Show(platforms == null ? Loc.T("The games of the other platforms are not available on this server.") : Loc.T("No game of a supported platform on the server yet."));
                    return;
                }
                uiPlatforms.ItemsSource = platforms;
                uiPlatforms.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                loaded = false;// tried again when the page is opened again
                Show(WebExceptionHelper.TryGetServerMessage(ex));
            }
        }

        private async void Platform_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (uiPlatforms.SelectedItem is not RetroPlatform platform)
                return;
            Show(null, loading: true);
            uiGames.ItemsSource = null;
            try
            {
                games = await RetroService.ListGamesAsync(platform);
                Show(null);
                ApplyFilter();
            }
            catch (Exception ex) { Show(WebExceptionHelper.TryGetServerMessage(ex)); }
        }

        private void Search_TextChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            string search = uiSearch.Text?.Trim() ?? "";
            uiGames.ItemsSource = search.Length == 0 ? games : games.Where(g => g.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static RetroGame? GameOf(object? sender) => (sender as Control)?.DataContext as RetroGame;

        private async void Install_Click(object? sender, RoutedEventArgs e)
        {
            if (GameOf(sender) is RetroGame game)
                await InstallAsync(game);
        }

        /// <summary>The game's file, then RetroArch and the core when this PC has them not yet.</summary>
        internal async Task InstallAsync(RetroGame game)
        {
            if (game.Busy)
                return;
            if ((SettingsViewModel.Instance.RootDirectories?.Count ?? 0) == 0)
            {
                MainWindowViewModel.Instance.AppBarText = Loc.T("No Root Directory configured! Go to ⚙️Settings->Data");
                return;
            }
            game.Busy = true;
            try
            {
                bool cia = string.Equals(Path.GetExtension(game.FileName), ".cia", StringComparison.OrdinalIgnoreCase);
                // Most 3DS packages are encrypted, which Azahar can not play: told before gigabytes come down
                if (cia)
                {
                    game.Status = Loc.T("Checking the game...");
                    if (CiaReader.Inspect(await RetroService.ReadStartAsync(game, CiaReader.HeaderBytes), game.Size).Verdict != CiaReader.Verdict.Playable)
                    {
                        game.Status = Loc.T("This 3DS game is encrypted: Azahar can not play it. A decrypted copy is needed in RomM.");
                        return;
                    }
                }
                string folder = RetroService.Folder(game, InstallLocationUserControl.DefaultLocation());
                Directory.CreateDirectory(folder);
                string metadata = Path.Combine(folder, RetroService.DownloadMetadata);
                var download = new HttpClientDownloadWithProgress(RetroService.ContentUrl(game), folder, game.FileName);
                download.ProgressChanged += (_, _, _, percentage, _) => game.Status = Loc.F("Downloading... {0:0}%", percentage ?? 0);
                // A download stopped half way goes on from where it was
                await download.StartDownload(tryResume: File.Exists(metadata));
                File.Delete(metadata);
                string downloaded = Directory.EnumerateFiles(folder).OrderByDescending(File.GetLastWriteTimeUtc).First();
                if (game.Multi && string.Equals(Path.GetExtension(downloaded), ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    // A game in several files (a CD and its tracks): RomM sends them zipped
                    var sevenZip = new SevenZipHelper();
                    sevenZip.Process += (_, e) => game.Status = Loc.F("Unpacking... {0}%", e.PercentageDone);
                    game.Status = Loc.F("Unpacking... {0}%", 0);
                    if (await sevenZip.ExtractArchive(downloaded, folder) != 0)
                        throw new IOException(sevenZip.LastError);
                    File.Delete(downloaded);
                }
                else if (cia)
                {
                    game.Status = Loc.T("Preparing the 3DS game...");
                    byte[] start = new byte[CiaReader.HeaderBytes];
                    int read;
                    await using (FileStream file = File.OpenRead(downloaded))
                        read = await file.ReadAsync(start);
                    await CiaReader.ExtractAsync(downloaded, CiaReader.Inspect(start.AsSpan(0, read), new FileInfo(downloaded).Length));
                }
                await RetroArchManager.EnsureReadyAsync(game.Platform, text => game.Status = text);
                game.Refresh();
                game.Status = game.Installed ? "" : Loc.T("None of this game's files can be started by the emulator.");
            }
            catch (RetroArchManager.MissingBiosException ex)
            {
                // The game is there: the BIOS comes when an administrator adds it, and Play fetches it
                game.Refresh();
                game.Status = ex.Message;
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                game.Status = Loc.F("Failed: {0}", WebExceptionHelper.TryGetServerMessage(ex));
            }
            finally { game.Busy = false; }
        }

        private async void Play_Click(object? sender, RoutedEventArgs e)
        {
            if (GameOf(sender) is not RetroGame game || game.Busy || game.RomFile is not string rom)
                return;
            game.Busy = true;
            try
            {
                await RetroArchManager.EnsureReadyAsync(game.Platform, text => game.Status = text);
                if (await RetroArchManager.TakeCloudSavesAsync(text => game.Status = text) is string kept)
                    MainWindowViewModel.Instance.AppBarText = Loc.F("The saves of another PC were newer and were taken; the ones of this PC are kept in {0}", kept);
                var process = RetroArchManager.Launch(game.Platform.System.Core, rom);
                game.Status = Loc.T("Playing: Select+Start to quit");
                _ = WhenClosedAsync(process, game);
            }
            catch (RetroArchManager.MissingBiosException ex)
            {
                game.Status = ex.Message;
                game.Busy = false;
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                game.Status = Loc.F("Failed: {0}", ex.Message);
                game.Busy = false;
            }
        }

        /// <summary>Busy while it runs (not started twice, not deleted under RetroArch), then the saves go to the cloud.</summary>
        private static async Task WhenClosedAsync(System.Diagnostics.Process process, RetroGame game)
        {
            await process.WaitForExitAsync();
            game.Status = Loc.T("Sending the saves...");
            await RetroArchManager.SendCloudSavesAsync();
            game.Status = "";
            game.Busy = false;
        }

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
        }
    }
}
