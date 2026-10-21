using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
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
        private bool loaded;

        public RetroUserControl()
        {
            InitializeComponent();
            DataContext = ViewModel;
            AttachedToVisualTree += (_, _) =>
            {
                Focus();
                if (!loaded)
                    _ = LoadPlatformsAsync();
            };
        }

        private void Show(string? message, bool loading = false)
        {
            ViewModel.Message = message;
            ViewModel.Loading = loading;
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
                ViewModel.Platforms = platforms;
                await SelectPlatformAsync(platforms[0]);
            }
            catch (Exception ex)
            {
                loaded = false;// tried again when the page is opened again
                Show(WebExceptionHelper.TryGetServerMessage(ex));
            }
        }

        internal async Task SelectPlatformAsync(RetroPlatform platform)
        {
            foreach (RetroPlatform other in ViewModel.Platforms)
                other.IsSelected = other == platform;
            SizeCards(platform);
            ViewModel.SelectedGame = null;
            ViewModel.AllGames = new();
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

        private void Reload_Click(object? sender, RoutedEventArgs e) => _ = LoadPlatformsAsync();

        private void GameCard_Click(object? sender, RoutedEventArgs e)
        {
            if (GameOf(sender) is RetroGame game)
                ViewModel.SelectedGame = game;
        }

        /// <summary>As in the library: a double click plays, or installs.</summary>
        private async void GameCard_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
        {
            if (GameOf(sender) is not RetroGame game)
                return;
            if (game.Installed)
                await PlayAsync(game);
            else
                await InstallAsync(game);
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
        internal async Task InstallAsync(RetroGame game)
        {
            if (game.Busy)
                return;
            try
            {
                game.Status = Loc.T("Checking the game...");
                string? refused = await RetroDownloads.StartAsync(game);
                if (refused != null)
                    game.Status = refused;
                else
                    MainWindowViewModel.Instance.AppBarText = Loc.F("'{0}' is in the downloads", game.Name);
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                game.Status = Loc.F("Failed: {0}", WebExceptionHelper.TryGetServerMessage(ex));
            }
        }

        private async void Play_Click(object? sender, RoutedEventArgs e)
        {
            if (GameOf(sender) is RetroGame game)
                await PlayAsync(game);
        }

        internal async Task PlayAsync(RetroGame game)
        {
            if (game.Busy || game.RomFile is not string rom)
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
            ViewModel.ApplyFilter();
        }
    }
}
