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
using gamevault.Converter;
using gamevault.Helper;
using gamevault.Helper.Integrations;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace gamevault.UserControls
{
    /// <summary>
    /// Interaction logic for NewGameViewUserControl.xaml
    /// </summary>
    public partial class GameViewUserControl : UserControl
    {
        private GameViewViewModel ViewModel { get; set; }
        private int gameID { get; set; }
        private bool loaded = false;


        #region MediaSlider     
        private YoutubeClient YoutubeClient { get; set; }
        private async Task PrepareMetadataMedia(GameMetadata data)
        {
            List<Tuple<string, string>> MediaUrls = new List<Tuple<string, string>>();
            if (YoutubeClient == null)
            {
                YoutubeClient = new YoutubeClient();
            }
            //Load first video separately, as it might take a while until the first playback
            bool trailerPreloaded = false;
            bool gameplayPreloaded = false;
            if (data?.Trailers?.Count() > 0)
            {
                var preloaded = await ConvertYoutubeLinkToEmbedded(data?.Trailers[0]);
                if (preloaded != null)
                {
                    trailerPreloaded = true;
                    MediaUrls.Add(preloaded);
                    await uiMediaSlider.LoadFirstElement(preloaded);
                }
            }
            else if (data?.Gameplays?.Count() > 0)
            {
                var preloaded = await ConvertYoutubeLinkToEmbedded(data?.Gameplays[0]);
                if (preloaded != null)
                {
                    gameplayPreloaded = true;
                    MediaUrls.Add(preloaded);
                    await uiMediaSlider.LoadFirstElement(preloaded);
                }
            }

            for (int i = 0; i < data?.Trailers?.Count(); i++)
            {
                if (i == 0 && trailerPreloaded)
                {
                    continue;//Prevent the first element from being reloaded
                }
                var url = await ConvertYoutubeLinkToEmbedded(data?.Trailers[i]);
                if (url != null)
                {
                    MediaUrls.Add(url);
                }
            }
            for (int i = 0; i < data?.Gameplays?.Count(); i++)
            {
                if (i == 0 && gameplayPreloaded)
                {
                    continue;//Prevent the first element from being reloaded
                }
                var url = await ConvertYoutubeLinkToEmbedded(data?.Gameplays[i]);
                if (url != null)
                {
                    MediaUrls.Add(url);
                }
            }
            for (int i = 0; i < data?.Screenshots?.Count(); i++)
            {
                MediaUrls.Add(new Tuple<string, string>(data?.Screenshots[i], ""));
            }

            uiMediaSlider.SetMediaList(MediaUrls);
            if (trailerPreloaded == false && gameplayPreloaded == false)
            {
                await uiMediaSlider.LoadFirstElement();
            }
        }
        private async Task<Tuple<string, string>> ConvertYoutubeLinkToEmbedded(string input)
        {
            try
            {
                if (input.Contains("youtu", StringComparison.OrdinalIgnoreCase))
                {
                    var streamManifest = await YoutubeClient.Videos.Streams.GetManifestAsync(input);
                    var videoStreamInfo = streamManifest.GetVideoStreams().GetWithHighestVideoQuality();
                    var audioStreamInfo = streamManifest.GetAudioStreams().GetWithHighestBitrate();
                    return new Tuple<string, string>(videoStreamInfo.Url, audioStreamInfo.Url);
                }
                else
                {
                    return new Tuple<string, string>(input, "");
                }
            }
            catch { return null; }
        }
        #endregion

        public GameViewUserControl(Game game, bool reloadGameObject = true)
        {
            InitializeComponent();
            ViewModel = new GameViewViewModel();
            if (false == reloadGameObject)
            {
                ViewModel.Game = game;
            }
            gameID = game.ID;
            this.DataContext = ViewModel;
            Loaded += UserControl_Loaded;
            Loaded += (_, _) => ObserveInstallState(true);
            Unloaded += (_, _) =>
            {
                ObserveInstallState(false);
                // Stop trailers when the page is left.
                if (loaded && !uiMediaSlider.IsWebViewNull())
                    uiMediaSlider.UnloadMediaSlider();
            };
            KeyDown += ReloadGameView_Click;
            uiChipsScroll.AddHandler(PointerWheelChangedEvent, (s, e) =>
            {
                if (uiChipsScroll.Extent.Width > uiChipsScroll.Viewport.Width)
                {
                    uiChipsScroll.Offset = new Vector(Math.Max(0, uiChipsScroll.Offset.X - e.Delta.Y * 50), 0);
                    e.Handled = true;
                }
            }, RoutingStrategies.Tunnel);
        }
        private async void UserControl_Loaded(object? sender, RoutedEventArgs e)
        {
            this.Focus();
            if (!loaded)
            {
                loaded = true;

                if (ViewModel.Game == null)
                {
                    try
                    {
                        string result = await WebHelper.GetAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/games/{gameID}");
                        ViewModel.Game = JsonSerializer.Deserialize<Game>(result);
                        ViewModel.UserProgresses = ViewModel.Game.Progresses.Where(p => p.User.ID != LoginManager.Instance.GetCurrentUser().ID).ToArray();
                        ViewModel.CurrentUserProgress = ViewModel.Game.Progresses.FirstOrDefault(progress => progress.User.ID == LoginManager.Instance.GetCurrentUser()?.ID) ?? new Progress { MinutesPlayed = 0, State = State.UNPLAYED.ToString() };
                    }
                    catch (Exception ex) { Log.Ignored(ex); }
                }
                ViewModel.IsInstalled = IsGameInstalled(ViewModel.Game);
                ViewModel.IsDownloaded = IsGameDownloaded(ViewModel.Game);
                ViewModel.IsUpdateAvailable = HasUpdate(ViewModel.Game);
                PrepareMarkdownElements();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        SaveGameHelper.Instance.PrepareConfigFile("", Path.Combine(LoginManager.Instance.GetUserProfile().CloudSaveConfigDir, "config.yaml"));
                        string gameMetadataTitle = ViewModel?.Game?.Metadata?.Title ?? "";
                        if (gameMetadataTitle == "")
                        {
                            gameMetadataTitle = ViewModel?.Game?.Title ?? "";
                        }
                        ViewModel.CloudSaveMatchTitle = await SaveGameHelper.Instance.SearchForLudusaviGameTitle(gameMetadataTitle);
                    }
                    catch (Exception ignored) { Log.Ignored(ignored); }
                });
                //MediaSlider
                try
                {
                    await uiMediaSlider.InitVideoPlayer();
                    await PrepareMetadataMedia(ViewModel?.Game?.Metadata);
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
                //###########              
            }
        }
        private async void ReloadGameView_Click(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Back();
                return;
            }
            if (e.Key != Key.F5)
                return;

            await ReloadGameView();
        }
        private async Task ReloadGameView()
        {
            this.IsEnabled = false;
            try
            {
                string result = await WebHelper.GetAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/games/{gameID}");
                ViewModel.Game = JsonSerializer.Deserialize<Game>(result);
                ViewModel.UserProgresses = ViewModel.Game.Progresses.Where(p => p.User.ID != LoginManager.Instance.GetCurrentUser().ID).ToArray();
                ViewModel.CurrentUserProgress = ViewModel.Game.Progresses.FirstOrDefault(progress => progress.User.ID == LoginManager.Instance.GetCurrentUser()?.ID) ?? new Progress { MinutesPlayed = 0, State = State.UNPLAYED.ToString() };
            }
            catch (Exception ex) { Log.Ignored(ex); }
            ViewModel.IsInstalled = IsGameInstalled(ViewModel.Game);
            ViewModel.IsDownloaded = IsGameDownloaded(ViewModel.Game);
            ViewModel.IsUpdateAvailable = HasUpdate(ViewModel.Game);
            PrepareMarkdownElements();
            this.IsEnabled = true;
        }
        public void RefreshGame(Game game)
        {
            ViewModel.Game = game;
            PrepareMarkdownElements();
        }

        #region Install state
        // Keeps Play / Install & Play right while the page is open (e.g. after "Install & Play" finished).
        private System.Collections.Specialized.INotifyCollectionChanged? observedInstalledGames;
        private void ObserveInstallState(bool observe)
        {
            InstallViewModel.Instance.PropertyChanged -= InstallViewModel_PropertyChanged;
            DownloadsViewModel.Instance.DownloadedGames.CollectionChanged -= InstallState_CollectionChanged;
            if (observedInstalledGames != null)
                observedInstalledGames.CollectionChanged -= InstallState_CollectionChanged;
            observedInstalledGames = null;
            if (!observe)
                return;
            InstallViewModel.Instance.PropertyChanged += InstallViewModel_PropertyChanged;
            DownloadsViewModel.Instance.DownloadedGames.CollectionChanged += InstallState_CollectionChanged;
            observedInstalledGames = InstallViewModel.Instance.InstalledGames;
            observedInstalledGames.CollectionChanged += InstallState_CollectionChanged;
            RefreshInstallState();
        }
        private void InstallViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(InstallViewModel.InstalledGames))
                ObserveInstallState(true);
        }
        private void InstallState_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RefreshInstallState();
        private void RefreshInstallState() => Dispatcher.UIThread.Post(() =>
        {
            if (ViewModel.Game == null)
                return;
            ViewModel.IsInstalled = IsGameInstalled(ViewModel.Game);
            ViewModel.IsDownloaded = IsGameDownloaded(ViewModel.Game);
            ViewModel.IsUpdateAvailable = HasUpdate(ViewModel.Game);
        });
        private static bool HasUpdate(Game? game)
        {
            if (game == null)
                return false;
            var installed = InstallViewModel.Instance.InstalledGames.FirstOrDefault(g => g.Key.ID == game.ID);
            return installed.Value != null && InstalledGameState.HasUpdate(game, installed.Value);
        }
        private async void UpdateGame_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.Game == null)
                return;
            ((Control)sender).IsEnabled = false;
            await MainWindowViewModel.Instance.Downloads.UpdateGame(ViewModel.Game);
            ViewModel.IsUpdateAvailable = false;
            ((Control)sender).IsEnabled = true;
        }
        #endregion

        private bool IsGameInstalled(Game? game)
        {
            if (game == null)
                return false;
            KeyValuePair<Game, string> result = InstallViewModel.Instance.InstalledGames.Where(g => g.Key.ID == game.ID).FirstOrDefault();
            if (result.Equals(default(KeyValuePair<Game, string>)))
                return false;
            return true;
        }
        private bool IsGameDownloaded(Game? game)
        {
            if (game == null)
                return false;
            return DownloadsViewModel.Instance.DownloadedGames.Where(gameUC => gameUC.GetGameId() == game.ID).Count() > 0;
        }
        private void Back_Click(object? sender, PointerReleasedEventArgs e)
        {
            Back();
        }
        private void Back()
        {
            MainWindowViewModel.Instance.UndoActiveControl();
        }
        private async void GamePlay_Click(object sender, RoutedEventArgs e)
        {
            ((Control)sender).IsEnabled = false;
            await InstallUserControl.PlayGame(ViewModel.Game.ID);
            ((Control)sender).IsEnabled = true;
        }
        private void GameSettings_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.Game == null)
                return;

            MainWindowViewModel.Instance.OpenPopup(new GameSettingsUserControl(ViewModel.Game) { Width = 1200, Height = 800, Margin = new Thickness(50) });
        }
        private async void InstallAndPlay_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.Game == null)
                return;
            await MainWindowViewModel.Instance.Downloads.InstallAndPlay(ViewModel.Game);
        }
        private async void GameDownload_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.Game == null)
                return;

            if (IsGameDownloaded(ViewModel.Game))
            {
                uiMediaSlider.UnloadMediaSlider();
            }
            await MainWindowViewModel.Instance.Downloads.TryStartDownload(ViewModel.Game);
        }
        private void Collections_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.Game == null || sender is not Control anchor)
                return;
            CollectionsFlyout.ShowForGame(anchor, ViewModel.Game.ID, ViewModel.Game.Title);
        }
        private void Website_Navigate(object? sender, RoutedEventArgs e)
        {
            try
            {
                string? url = ((Control)sender!).Tag as string;
                if (string.IsNullOrEmpty(url))
                    return;
                PlatformInfo.OpenUrl(url);
                e.Handled = true;
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private async void GameState_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.RemovedItems.Count == 0 || !LoginManager.Instance.IsLoggedIn())
                return;
            if (e.AddedItems.Count > 0)
            {
                try
                {
                    await WebHelper.PutAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/progresses/user/{LoginManager.Instance.GetCurrentUser().ID}/game/{gameID}", System.Text.Json.JsonSerializer.Serialize(new Progress() { State = ViewModel.CurrentUserProgress.State }));
                }
                catch (Exception ex)
                {
                    string msg = WebExceptionHelper.TryGetServerMessage(ex);
                    MainWindowViewModel.Instance.AppBarText = msg;
                }
            }
        }
        private void ShowProgressUser_Click(object? sender, PointerReleasedEventArgs e)
        {
            Progress selectedProgress = ((Control)sender).DataContext as Progress;
            if (selectedProgress != null)
            {
                MainWindowViewModel.Instance.Community.ShowUser(selectedProgress.User);
            }
        }
        private void GameTitle_Click(object? sender, PointerReleasedEventArgs e)
        {
            try
            {
                SettingsViewModel.Instance.ShowMappedTitle = !SettingsViewModel.Instance.ShowMappedTitle;
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private async void Bookmark_Click(object sender, RoutedEventArgs e)
        {
            if (((Control)sender).Tag as string == "busy")
            {
                ((ToggleButton)sender).IsChecked = !((ToggleButton)sender).IsChecked;
                return;
            }
            ((Control)sender).Tag = "busy";
            try
            {
                if ((bool)((ToggleButton)sender).IsChecked == false)
                {
                    await WebHelper.DeleteAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/users/me/bookmark/{ViewModel.Game.ID}");
                    ViewModel.Game.BookmarkedUsers = new List<User>();
                }
                else
                {
                    await WebHelper.PostAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/users/me/bookmark/{ViewModel.Game.ID}", "");
                    ViewModel.Game.BookmarkedUsers = new List<User> { LoginManager.Instance.GetCurrentUser()! };
                }
                MainWindowViewModel.Instance.Library.RefreshGame(ViewModel.Game);
            }
            catch (Exception ex)
            {
                string message = WebExceptionHelper.TryGetServerMessage(ex);
                MainWindowViewModel.Instance.AppBarText = message;
            }
            ((Control)sender).Tag = "";
        }
        private void Genre_Clicked(object sender, RoutedEventArgs e)
        {
            try
            {
                GenreMetadata data = (GenreMetadata)((Control)sender).DataContext;
                MainWindowViewModel.Instance.Library.ClearAllFilters();
                MainWindowViewModel.Instance.Library.uiFilterGenreSelector.SetEntries(new Pill[] { new Pill() { ID = data.ID, Name = data.Name, ProviderDataId = data.ProviderDataId } });
                MainWindowViewModel.Instance.SetActiveControl(MainControl.Library);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private void Tag_Clicked(object sender, RoutedEventArgs e)
        {
            try
            {
                TagMetadata data = (TagMetadata)((Control)sender).DataContext;
                MainWindowViewModel.Instance.Library.ClearAllFilters();
                MainWindowViewModel.Instance.Library.uiFilterTagSelector.SetEntries(new Pill[] { new Pill() { ID = data.ID, Name = data.Name, ProviderDataId = data.ProviderDataId } });
                MainWindowViewModel.Instance.SetActiveControl(MainControl.Library);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private void Developer_Clicked(object? sender, PointerReleasedEventArgs e)
        {
            try
            {
                DeveloperMetadata data = (DeveloperMetadata)((Control)sender).DataContext;
                MainWindowViewModel.Instance.Library.ClearAllFilters();
                MainWindowViewModel.Instance.Library.uiFilterDeveloperSelector.SetEntries(new Pill[] { new Pill() { ID = (int)data.ID!, Name = data.Name, ProviderDataId = data.ProviderDataId } });
                MainWindowViewModel.Instance.SetActiveControl(MainControl.Library);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private void Publisher_Clicked(object? sender, PointerReleasedEventArgs e)
        {
            try
            {
                PublisherMetadata data = (PublisherMetadata)((Control)sender).DataContext;
                MainWindowViewModel.Instance.Library.ClearAllFilters();
                MainWindowViewModel.Instance.Library.uiFilterPublisherSelector.SetEntries(new Pill[] { new Pill() { ID = (int)data.ID!, Name = data.Name, ProviderDataId = data.ProviderDataId } });
                MainWindowViewModel.Instance.SetActiveControl(MainControl.Library);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private void GameType_Clicked(object sender, RoutedEventArgs e)
        {
            try
            {
                MainWindowViewModel.Instance.Library.ClearAllFilters();
                MainWindowViewModel.Instance.Library.uiFilterGameTypeSelector.SetEntries(new Pill[] { new Pill() { OriginName = ViewModel.Game.Type.ToString(), Name = (string)new EnumDescriptionConverter().Convert(ViewModel.Game.Type, null, null, null) } });
                MainWindowViewModel.Instance.SetActiveControl(MainControl.Library);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private void Share_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string shareLink = $"gamevault://show?gameid={ViewModel?.Game?.ID}";
                ClipboardHelper.SetText(shareLink);
                MainWindowViewModel.Instance.AppBarText = "Sharelink copied to clipboard";
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private async void BackupCloudSaves_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!LoginManager.Instance.IsLoggedIn())
                {
                    MainWindowViewModel.Instance.AppBarText = CloudSaveStatus.Offline;
                    return;
                }
                MainWindowViewModel.Instance.AppBarText = "Uploading Savegame to the Server...";
                ((Control)sender).IsEnabled = false;
                string status = await SaveGameHelper.Instance.BackupSaveGame(ViewModel!.Game!.ID);
                MainWindowViewModel.Instance.AppBarText = status;
            }
            catch
            {
                MainWindowViewModel.Instance.AppBarText = CloudSaveStatus.BackupFailed;
            }
            ((Control)sender).IsEnabled = true;
        }
        private async void RestoreCloudSaves_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MainWindowViewModel.Instance.AppBarText = $"Syncing cloud save...";
                ((Control)sender).IsEnabled = false;
                string installationDir = InstallViewModel.Instance.InstalledGames.First(g => g.Key.ID == ViewModel!.Game!.ID).Value;
                string status = await SaveGameHelper.Instance.RestoreBackup(ViewModel!.Game!.ID, installationDir);
                MainWindowViewModel.Instance.AppBarText = status;
            }
            catch
            {
                MainWindowViewModel.Instance.AppBarText = CloudSaveStatus.RestoreFailed;
            }
            ((Control)sender).IsEnabled = true;
        }
        #region Markdown        
        private void PrepareMarkdownElements()
        {
            try
            {
                if (ViewModel?.Game?.Metadata?.Description != null)
                {
                    ViewModel.DescriptionMarkdown = ViewModel.Game.Metadata.Description;
                }
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
            try
            {
                if (ViewModel?.Game?.Metadata?.Notes != null)
                {
                    ViewModel.NotesMarkdown = ViewModel.Game.Metadata.Notes;
                }
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }

        #endregion

    }
}