using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using GameVault.Core;
using GameVault.Core.Retro;
using gamevault.Helper;
using gamevault.Helper.Platform;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>The page of a retro game, as the library's game page: opened from the Retro page, the search, the activity.</summary>
    public partial class RetroGameViewUserControl : UserControl
    {
        internal RetroGameViewModel ViewModel { get; }

        public RetroGameViewUserControl() : this(new RetroGame { Platform = new RetroPlatform { Slug = "nes", Name = "" } }) { }

        internal RetroGameViewUserControl(RetroGame game)
        {
            InitializeComponent();
            ViewModel = new RetroGameViewModel(game);
            DataContext = ViewModel;
            _ = game.CoverImage;// asks for it when the page opens from elsewhere
            uiFixMatch.IsVisible = LoginManager.Instance.GetCurrentUser()?.Role == PERMISSION_ROLE.ADMIN;
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Back();
                }
            };
            SizeChanged += (_, e) => ApplyWidth(e.NewSize.Width);
            RetroLibrary.Changed += LibraryChanged;
            game.PropertyChanged += Game_PropertyChanged;
            DetachedFromVisualTree += (_, _) =>
            {
                RetroLibrary.Changed -= LibraryChanged;
                game.PropertyChanged -= Game_PropertyChanged;
            };
            ViewModel.MineChanged += async changes => await SetMineAsync(changes);
            AttachedToVisualTree += (_, _) => Focus();
            Loaded += async (_, _) => await LoadAsync();
        }

        private void LibraryChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ViewModel.Game.RefreshPlayer();
            ViewModel.RefreshPlayer();
        });

        /// <summary>A game ended (or was installed, deleted): its saves and save states again.</summary>
        private void Game_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(RetroGame.Busy) or nameof(RetroGame.Installed) && !ViewModel.Game.Busy)
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    RefreshSaves();
                    LoadStates();
                });
        }

        internal async Task LoadAsync()
        {
            RefreshSaves();
            LoadStates();
            _ = RetroLibrary.LoadAsync();
            Task romm = LoadRommAsync();
            await ViewModel.Game.LoadScreenshotsAsync();
            ViewModel.SelectedScreenshot ??= ViewModel.Game.ScreenshotImages?.FirstOrDefault();
            await LoadPlayersAsync();
            await romm;
        }

        /// <summary>The player's status and rating of the game, and its achievements, from their RomM.</summary>
        private async Task LoadRommAsync()
        {
            if (!RommAccount.IsLinked)
            {
                ViewModel.RommNote = Loc.T("Link your RomM account (Settings > Retro).");
                return;
            }
            try
            {
                JsonElement rom = await RommAccount.RomAsync(ViewModel.Game.Id);
                ViewModel.SetMine(RommProgress.MineOf(rom));
                JsonElement me = default;
                try { me = await RommAccount.MeAsync(); }
                catch (Exception ex) { Log.Ignored(ex); }
                var achievements = RommProgress.AchievementsOf(rom, me);
                ViewModel.Achievements = achievements.Select(a => new RetroAchievementRow(a)).ToList();
                // Earned elsewhere too (another PC, RetroAchievements' site): the profile and the activity follow
                _ = RetroLibrary.ShareAchievementsAsync(ViewModel.Game, achievements);
                // The badges, a few at a time (a game has up to a few hundred)
                await Parallel.ForEachAsync(ViewModel.Achievements.Where(a => a.BadgeUrl != null), new ParallelOptions { MaxDegreeOfParallelism = 6 }, async (row, _) =>
                {
                    try
                    {
                        var badge = await RetroService.BadgeImageAsync(row.BadgeUrl!);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => row.Badge = badge);
                    }
                    catch (Exception ex) { Log.Ignored(ex); }
                });
            }
            catch (RommAccount.MissingScopeException ex) { ViewModel.RommNote = WebExceptionHelper.TryGetServerMessage(ex); }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private async Task SetMineAsync(object changes)
        {
            try { await RommAccount.SetMineAsync(ViewModel.Game.Id, changes); }
            catch (RommAccount.MissingScopeException ex)
            {
                ViewModel.HasRomm = false;
                ViewModel.RommNote = WebExceptionHelper.TryGetServerMessage(ex);
            }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
        }

        /// <summary>The save states of RetroArch's games, with the picture RetroArch took.</summary>
        private void LoadStates()
        {
            try
            {
                RetroGame game = ViewModel.Game;
                if (StandaloneEmulator.Of(game.Platform.System) != null || game.RomFile is not string rom)
                {
                    ViewModel.States = new();
                    return;
                }
                ViewModel.States = RetroArchManager.Layout.SaveStates(RetroArchManager.Player, game.Platform.System.Core, rom).Select(state =>
                {
                    Bitmap? picture = null;
                    try { picture = state.Picture != null ? new Bitmap(state.Picture) : null; }
                    catch (Exception ex) { Log.Ignored(ex); }
                    return new RetroStateRow(state.Slot, picture, Loc.F("Slot {0}", state.Slot), Converter.RelativeDate.Format(state.Saved));
                }).ToList();
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private async void ResumeState_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is RetroStateRow state)
                await RetroPlay.PlayAsync(ViewModel.Game, state.Slot);
        }

        /// <summary>The friends who played it, with their avatar when the server gives the players.</summary>
        private async Task LoadPlayersAsync()
        {
            try
            {
                var played = await RetroLibrary.PlayersOfAsync(ViewModel.Game);
                User[] users = Array.Empty<User>();
                try { users = JsonSerializer.Deserialize<User[]>(await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/users")) ?? users; }
                catch (Exception ex) { Log.Ignored(ex); }
                ViewModel.Players = played.Select(p => new RetroPlayerRow(users.FirstOrDefault(u => u.ID == p.UserId), p.Username,
                    (string)new Converter.GameTimeConverter().Convert(p.Minutes, typeof(string), null, CultureInfo.CurrentCulture)!,
                    Converter.RelativeDate.Format(p.LastPlayed))).ToList();
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>Where the game's cloud saves are, under its buttons.</summary>
        internal void RefreshSaves()
        {
            try
            {
                var saves = RetroCloudSaves.Of(ViewModel.Game);
                int files = saves.Files().Count;
                string local = files == 0 ? Loc.T("No save on this PC") : Loc.F("{0} save file(s) on this PC", files);
                string cloud = !SettingsViewModel.Instance.CloudSaves ? Loc.T("cloud saves off (Settings > Data)")
                    : saves.LastSync > 0 ? Loc.F("synced {0}", Converter.RelativeDate.Format(DateTimeOffset.FromUnixTimeMilliseconds(saves.LastSync).UtcDateTime))
                    : Loc.T("not in the cloud yet");
                ViewModel.SavesText = $"{local} · {cloud} · {RetroCloudSaves.Destination}";
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private bool? narrowLayout;
        /// <summary>As the library's game page: narrow windows stack the columns.</summary>
        private void ApplyWidth(double width)
        {
            bool narrow = width < 1100;
            uiContent.Margin = narrow ? new Thickness(24, 200, 24, 40) : new Thickness(44, 250, 44, 48);
            uiTitle.FontSize = width < 900 ? 32 : narrow ? 40 : 50;
            uiCoverFrame.Width = narrow ? 160 : 220;
            if (narrowLayout == narrow)
                return;
            narrowLayout = narrow;
            uiBody.ColumnDefinitions = narrow ? new ColumnDefinitions("*") : new ColumnDefinitions("*,36,330");
            Grid.SetColumn(uiSide, narrow ? 0 : 2);
            Grid.SetRow(uiSide, narrow ? 1 : 0);
            uiSide.Margin = narrow ? new Thickness(0, 28, 0, 0) : new Thickness(0);
        }

        private void Back() => MainWindowViewModel.Instance.UndoActiveControl();
        private void Back_Click(object? sender, RoutedEventArgs e) => Back();

        private async void InstallAndPlay_Click(object? sender, RoutedEventArgs e) => await RetroPlay.InstallAsync(ViewModel.Game, playWhenDone: true);
        private async void Install_Click(object? sender, RoutedEventArgs e) => await RetroPlay.InstallAsync(ViewModel.Game);
        private async void Play_Click(object? sender, RoutedEventArgs e) => await RetroPlay.PlayAsync(ViewModel.Game);

        private void Collections_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control anchor)
                CollectionsFlyout.ShowForGame(anchor, ViewModel.Game.Id, ViewModel.Game.Name, RetroLibrary.Collections);
        }

        private async void Delete_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.Game.Busy || RetroService.InstalledFolder(ViewModel.Game) is not string folder)
                return;
            if (!await DialogService.ConfirmAsync(Loc.F("Delete '{0}' from this PC? Its saves are kept.", ViewModel.Game.Name)))
                return;
            try { Directory.Delete(folder, true); }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
            ViewModel.Game.Refresh();
        }

        private async void Manual_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.Game.Manual is not string manual)
                return;
            try { PlatformInfo.OpenFile(await RetroService.MediaFileAsync(manual)); }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
        }

        private void OpenFolder_Click(object? sender, RoutedEventArgs e)
        {
            if (RetroService.InstalledFolder(ViewModel.Game) is string folder)
                PlatformInfo.OpenFolder(folder);
        }

        private async void Menu_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (StandaloneEmulator.Of(ViewModel.Game.Platform.System) is StandaloneEmulator emulator)
                    await emulator.OpenAsync(text => ViewModel.Game.Status = text);
                else
                    await RetroArchManager.OpenMenuAsync(text => ViewModel.Game.Status = text);
            }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
            ViewModel.Game.Status = "";
        }

        private void FixMatch_Click(object? sender, RoutedEventArgs e)
        {
            // RomM changes the match with the admin's own account: it checks they may
            if (!RommAccount.IsLinked)
                MainWindowViewModel.Instance.AppBarText = Loc.T("Link your RomM account first (Settings > Retro): RomM changes the match with it.");
            else
                MainWindowViewModel.Instance.OpenPopup(new RetroMatchPopup(ViewModel.Game));
        }

        private void ReportProblem_Click(object? sender, RoutedEventArgs e) => ReportProblemPopup.Open(ViewModel.Game);

        private void Video_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.Game.Video is string video)
                PlatformInfo.OpenUrl($"https://www.youtube.com/watch?v={Uri.EscapeDataString(video)}");
        }

        private void PreviousScreenshot_Click(object? sender, RoutedEventArgs e) => ViewModel.MoveScreenshot(-1);
        private void NextScreenshot_Click(object? sender, RoutedEventArgs e) => ViewModel.MoveScreenshot(1);

        private void Screenshot_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is Bitmap screenshot)
                ViewModel.SelectedScreenshot = screenshot;
        }

        #region Cloud saves, per game as the library's
        private void Controls_Click(object? sender, RoutedEventArgs e)
        {
            var controls = new RetroControlsUserControl(ViewModel.Game);
            controls.DetachedFromVisualTree += (_, _) => ViewModel.ControlsChanged();
            MainWindowViewModel.Instance.OpenPopup(controls);
        }

        private async void RestoreCloudSaves_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.Game.Busy)
                return;
            if (!await DialogService.ConfirmAsync(Loc.F("Take the cloud saves of '{0}'? The ones of this PC are kept in a backup.", ViewModel.Game.Name)))
                return;
            await RunSavesAsync(sender, () => RetroCloudSaves.RestoreAsync(ViewModel.Game));
        }

        private async void UploadCloudSaves_Click(object? sender, RoutedEventArgs e)
        {
            if (!ViewModel.Game.Busy)
                await RunSavesAsync(sender, () => RetroCloudSaves.SendAsync(ViewModel.Game, force: true));
        }

        private async Task RunSavesAsync(object? sender, Func<Task<string>> action)
        {
            if (sender is Control button)
                button.IsEnabled = false;
            MainWindowViewModel.Instance.AppBarText = Loc.T("Syncing cloud save...");
            MainWindowViewModel.Instance.AppBarText = await action();
            if (sender is Control again)
                again.IsEnabled = true;
            RefreshSaves();
        }

        private void SaveHistory_Click(object? sender, RoutedEventArgs e) =>
            MainWindowViewModel.Instance.OpenPopup(new RetroSaveHistoryUserControl(ViewModel.Game, RefreshSaves));
        #endregion
    }
}
