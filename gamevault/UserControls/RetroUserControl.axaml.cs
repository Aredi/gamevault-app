using Avalonia.Controls;
using Avalonia.Interactivity;
using GameVault.Core;
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
                string folder = RetroService.Folder(game, InstallLocationUserControl.DefaultLocation());
                Directory.CreateDirectory(folder);
                string metadata = Path.Combine(folder, RetroService.DownloadMetadata);
                var download = new HttpClientDownloadWithProgress(RetroService.ContentUrl(game), folder, game.FileName);
                download.ProgressChanged += (_, _, _, percentage, _) => game.Status = Loc.F("Downloading... {0:0}%", percentage ?? 0);
                // A download stopped half way goes on from where it was
                await download.StartDownload(tryResume: File.Exists(metadata));
                File.Delete(metadata);
                await RetroArchManager.EnsureReadyAsync(game.Platform.Core, text => game.Status = text);
                game.Status = "";
                game.Refresh();
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
                await RetroArchManager.EnsureReadyAsync(game.Platform.Core, text => game.Status = text);
                var process = RetroArchManager.Launch(game.Platform.Core, rom);
                game.Status = Loc.T("Playing: Select+Start to quit");
                // Busy while it runs: not started twice, not deleted under RetroArch
                _ = process.WaitForExitAsync().ContinueWith(_ => { game.Status = ""; game.Busy = false; });
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                game.Status = Loc.F("Failed: {0}", ex.Message);
                game.Busy = false;
            }
        }

        private async void Delete_Click(object? sender, RoutedEventArgs e)
        {
            if (GameOf(sender) is not RetroGame game || game.Busy || game.RomFile is not string rom)
                return;
            MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync(Loc.F("Delete '{0}' from this PC? Its saves are kept.", game.Name),
                "", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = Loc.T("Yes"), NegativeButtonText = Loc.T("No") });
            if (result != MessageDialogResult.Affirmative)
                return;
            try
            {
                Directory.Delete(Path.GetDirectoryName(rom)!, true);
            }
            catch (Exception ex) { game.Status = Loc.F("Failed: {0}", ex.Message); }
            game.Refresh();
        }
    }
}
