using System.Text;
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
using gamevault.Models;
using gamevault.ViewModels;
using System.IO;
using System;
using gamevault.Helper;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Text.Json;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using gamevault.Helper.Integrations;
using gamevault.Windows;
using gamevault.UserControls.SettingsComponents;

namespace gamevault.UserControls
{
    /// <summary>
    /// Interaction logic for SettingsUserControl.xaml
    /// </summary>
    public partial class SettingsUserControl : UserControl
    {
        private SettingsViewModel ViewModel { get; set; }
        private bool loaded = false;
        public SettingsUserControl()
        {
            InitializeComponent();
            ViewModel = SettingsViewModel.Instance;
            this.DataContext = ViewModel;
            uiForkLink.Tag = AppRepository.ReleasesPage.Replace("/releases", "");
            uiForkLinkText.Text = $"{AppRepository.Owner}/{AppRepository.Name} (unofficial fork)";
            Loaded += UserControl_Loaded;
            uiSteamSync.IsCheckedChanged += SyncSteamShortcuts_Toggled;
        }
        public void SetTabIndex(int index)
        {
            uiTabControl.SelectedIndex = index;
        }
        private void ClearImageCache_Clicked(object sender, RoutedEventArgs e)
        {

            try
            {
                Directory.Delete(LoginManager.Instance.GetUserProfile().ImageCacheDir, true);
                Directory.CreateDirectory(LoginManager.Instance.GetUserProfile().ImageCacheDir);
                ViewModel.ImageCacheSize = 0;
                MainWindowViewModel.Instance.AppBarText = "Image cache cleared";
            }
            catch
            {
                MainWindowViewModel.Instance.AppBarText = "Something went wrong while the image cache was cleared";
            }

        }
        private async void ClearOfflineCache_Clicked(object sender, RoutedEventArgs e)
        {
            MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync($"Are you sure you want delete the offline cache? \nThis can lead to games not being displayed correctly when you are offline.", "", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = "Yes", NegativeButtonText = "No" });
            if (result == MessageDialogResult.Affirmative)
            {
                try
                {
                    if (File.Exists(LoginManager.Instance.GetUserProfile().IgnoreList))
                    {
                        File.Delete(LoginManager.Instance.GetUserProfile().IgnoreList);
                    }
                    if (File.Exists(LoginManager.Instance.GetUserProfile().OfflineCache))
                    {
                        File.Delete(LoginManager.Instance.GetUserProfile().OfflineCache);
                    }
                    ViewModel.OfflineCacheSize = 0;
                    MainWindowViewModel.Instance.AppBarText = "Offline cache cleared";
                }
                catch
                {
                    MainWindowViewModel.Instance.AppBarText = "Something went wrong while the offline cache was cleared";
                }
            }
        }
        private void UserControl_Loaded(object? sender, RoutedEventArgs e)
        {
            if (loaded)
                return;

            loaded = true;
            uiAutostartToggle.IsChecked = AutostartHelper.IsEnabled();
            uiAutostartToggle.IsCheckedChanged += AppAutostart_Toggled;
            LoadThemes();
            uiPwExtraction.Text = Preferences.Get(AppConfigKey.ExtractionPassword, LoginManager.Instance.GetUserProfile().UserConfigFile, true);
        }
        private void AppAutostart_Toggled(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (uiAutostartToggle.IsChecked == true)
                    AutostartHelper.Enable();
                else
                    AutostartHelper.Disable();
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = $"Unable to change autostart: {ex.Message}";
            }
        }

        private async void TabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.Source != uiTabControl)
                return;
            if (uiTabControl.SelectedIndex == 5)
            {
                // Tools may have been installed meanwhile
                uiDetectedTools.Text = ViewModel.DetectedCompatibilityTools;
            }
            if (uiTabControl.SelectedIndex == 3)
            {
                ViewModel.ImageCacheSize = await CalculateDirectorySize(new DirectoryInfo(LoginManager.Instance.GetUserProfile().ImageCacheDir));
                ViewModel.OfflineCacheSize = (File.Exists(LoginManager.Instance.GetUserProfile().OfflineCache) ? new FileInfo(LoginManager.Instance.GetUserProfile().OfflineCache).Length : 0);
            }
        }
        private async Task<long> CalculateDirectorySize(DirectoryInfo d)
        {
            return await Task<long>.Run(async () =>
            {
                long size = 0;
                try
                {
                    FileInfo[] fis = d.GetFiles();
                    foreach (FileInfo fi in fis)
                    {
                        size += fi.Length;
                    }
                    DirectoryInfo[] dis = d.GetDirectories();
                    foreach (DirectoryInfo di in dis)
                    {
                        size += await CalculateDirectorySize(di);
                    }
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
                return size;
            });
        }

        private void ChangeUserProfile_Click(object sender, RoutedEventArgs e)
        {
            ((Control)sender).IsEnabled = false;
            Preferences.DeleteKey(AppConfigKey.LastUserProfile, ProfileManager.ProfileConfigFile);
            SwitchToLoginWindow();
            ((Control)sender).IsEnabled = true;
        }
        private static void SwitchToLoginWindow()
        {
            var mainWindow = (MainWindow)App.Instance.MainWindow!;
            var loginWindow = new LoginWindow(true);
            App.Instance.MainWindow = loginWindow;
            loginWindow.Show();
            mainWindow.Dispose();
        }
        private async void Logout_Click(object sender, RoutedEventArgs e)
        {
            ((Control)sender).IsEnabled = false;
            MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync($"Are you sure you want to log out?", "", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = "Yes", NegativeButtonText = "No" });
            if (result == MessageDialogResult.Affirmative)
            {
                try
                {
                    bool isLoggedInWithSSO = Preferences.Get(AppConfigKey.IsLoggedInWithSSO, LoginManager.Instance.GetUserProfile().UserConfigFile) == "1";
                    Preferences.DeleteKey(AppConfigKey.SessionToken, LoginManager.Instance.GetUserProfile().UserConfigFile);
                    await WebHelper.PostAsync($"{SettingsViewModel.Instance.ServerUrl}/api/auth/revoke", "{" + $"\"refresh_token\": \"{WebHelper.GetRefreshToken()}\"" + "}");
                    if (!isLoggedInWithSSO)
                    {
                        Preferences.DeleteKey(AppConfigKey.Password, LoginManager.Instance.GetUserProfile().UserConfigFile);
                    }
                    Preferences.DeleteKey(AppConfigKey.LastUserProfile, ProfileManager.ProfileConfigFile);
                    SwitchToLoginWindow();
                }
                catch (Exception ex)
                {
                    MainWindowViewModel.Instance.AppBarText = ex.Message;
                }
            }
            ((Control)sender).IsEnabled = true;
        }
        private async void LogoutFromAllDevices_Click(object sender, RoutedEventArgs e)
        {
            ((Control)sender).IsEnabled = false;
            MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync($"Are you sure you want to log out from all devices?", "", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = "Yes", NegativeButtonText = "No" });
            if (result == MessageDialogResult.Affirmative)
            {
                try
                {
                    bool isLoggedInWithSSO = Preferences.Get(AppConfigKey.IsLoggedInWithSSO, LoginManager.Instance.GetUserProfile().UserConfigFile) == "1";
                    Preferences.DeleteKey(AppConfigKey.SessionToken, LoginManager.Instance.GetUserProfile().UserConfigFile);
                    await WebHelper.PostAsync($"{SettingsViewModel.Instance.ServerUrl}/api/auth/revoke/all", "");
                    if (!isLoggedInWithSSO)
                    {
                        Preferences.DeleteKey(AppConfigKey.Password, LoginManager.Instance.GetUserProfile().UserConfigFile);
                    }
                    Preferences.DeleteKey(AppConfigKey.LastUserProfile, ProfileManager.ProfileConfigFile);
                    SwitchToLoginWindow();
                }
                catch (Exception ex)
                {
                    MainWindowViewModel.Instance.AppBarText = ex.Message;
                }
            }
            ((Control)sender).IsEnabled = true;
        }

        private void DownloadLimit_InputValidation(object? sender, TextChangedEventArgs e)
        {
            var box = (TextBox)sender!;
            string digits = new string((box.Text ?? "").Where(char.IsDigit).ToArray());
            if (string.IsNullOrEmpty(digits))
                digits = "0";
            if (digits != box.Text)
                box.Text = digits;
        }

        private void DownloadLimit_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                DownloadLimit_Save(sender, e);
        }

        private void DownloadLimit_Save(object? sender, RoutedEventArgs e)
        {
            ViewModel.DownloadLimit = ViewModel.DownloadLimitUIValue;
            Preferences.Set(AppConfigKey.DownloadLimit, ViewModel.DownloadLimit, LoginManager.Instance.GetUserProfile().UserConfigFile);
            MainWindowViewModel.Instance.AppBarText = "Successfully saved download limit";
        }

        private void EditUser_Click(object sender, RoutedEventArgs e)
        {
            if (LoginManager.Instance.IsLoggedIn())
            {
                MainWindowViewModel.Instance.OpenPopup(new UserSettingsUserControl(LoginManager.Instance.GetCurrentUser()) { Width = 1200, Height = 800, Margin = new Thickness(50) });
            }
            else { MainWindowViewModel.Instance.AppBarText = "You are not logged in or offline"; }
        }
        #region THEMES
        private bool loadingThemes;
        private void Themes_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (loadingThemes || uiCbTheme.SelectedItem is not ThemeItem selectedTheme)
                return;

            ViewModel.IsCommunityThemeSelected = !ThemeManager.IsBuiltIn(selectedTheme.Path) && File.Exists(selectedTheme.Path);
            if (ThemeManager.CurrentThemePath == ThemeManager.NormalizePath(selectedTheme.Path))
                return;
            try
            {
                App.Instance.SetTheme(selectedTheme.Path);
                Preferences.Set(AppConfigKey.Theme, JsonSerializer.Serialize(selectedTheme), LoginManager.Instance.GetUserProfile().UserConfigFile, true);
            }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = ex.Message; }
        }
        private static ThemeItem CreateThemeItem(string path, ThemeManager.ThemeDefinition definition)
        {
            return new ThemeItem() { DisplayName = definition.DisplayName, Description = definition.Description, Author = definition.Author, Path = path };
        }
        private void LoadThemes()
        {
            loadingThemes = true;
            try
            {
                if (ViewModel.Themes == null)
                {
                    ViewModel.Themes = new ObservableCollection<ThemeItem>();
                }
                else
                {
                    ViewModel.Themes.Clear();
                }
                //Load embedded Themes
                foreach (string builtIn in ThemeManager.BuiltInThemes)
                {
                    string path = ThemeManager.BuiltInThemeBase + builtIn;
                    ViewModel.Themes.Add(CreateThemeItem(path, ThemeManager.Load(path)));
                }

                if (Directory.Exists(LoginManager.Instance.GetUserProfile().ThemesLoadDir))
                {
                    foreach (var file in Directory.GetFiles(LoginManager.Instance.GetUserProfile().ThemesLoadDir, "*.xaml", SearchOption.AllDirectories))
                    {
                        try
                        {
                            ViewModel.Themes.Add(CreateThemeItem(file, ThemeManager.Load(file)));
                        }
                        catch (Exception ignored) { Log.Ignored(ignored); }
                    }
                }
                string current = ThemeManager.CurrentThemePath ?? ThemeManager.DefaultTheme;
                int themeIndex = ViewModel.Themes.ToList().FindIndex(i => ThemeManager.NormalizePath(i.Path) == current);
                uiCbTheme.SelectedIndex = themeIndex != -1 ? themeIndex : 0;
                ViewModel.IsCommunityThemeSelected = uiCbTheme.SelectedItem is ThemeItem item && !ThemeManager.IsBuiltIn(item.Path) && File.Exists(item.Path);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Loading themes");
                uiCbTheme.SelectedIndex = 0;
            }
            loadingThemes = false;
        }

        private void OpenThemeFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(LoginManager.Instance.GetUserProfile().ThemesLoadDir);
                PlatformInfo.OpenFolder(LoginManager.Instance.GetUserProfile().ThemesLoadDir);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private void OpenCommunityThemeRepository_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PlatformInfo.OpenUrl("https://github.com/Phalcode/gamevault-community-themes");
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private async void ReloadThemeList_Click(object sender, RoutedEventArgs e)
        {
            ((Control)sender).IsEnabled = false;
            LoadThemes();
            await Task.Delay(500);
            ((Control)sender).IsEnabled = true;
        }
        private async Task<List<JsonElement>> LoadCommunityThemesHeader()
        {
            string jsonResponse = await WebHelper.BaseGetAsync("https://api.github.com/repos/phalcode/gamevault-community-themes/contents/v1");
            return JsonSerializer.Deserialize<List<JsonElement>>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        private async Task<ThemeItem> LoadThemeItemFromUrl(string url)
        {
            try
            {
                string result = await WebHelper.BaseGetAsync(url);
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(result));
                return CreateThemeItem(url, ThemeManager.Parse(stream));
            }
            catch { return null; }
        }
        private async Task LoadCommunityThemes()
        {
            try
            {
                List<JsonElement> fetchedList = await LoadCommunityThemesHeader();
                foreach (var entry in fetchedList)
                {
                    if (entry.TryGetProperty("name", out JsonElement nameElement) && nameElement.GetString()?.First() != '_' && entry.TryGetProperty("download_url", out JsonElement downloadUrlElement))
                    {
                        ThemeItem theme = await LoadThemeItemFromUrl(downloadUrlElement.GetString()!);
                        if (theme != null)
                        {
                            ViewModel.CommunityThemes.Add(theme);
                        }
                    }
                }
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private async void CommunityThemes_DropDownOpened(object? sender, EventArgs e)
        {
            if (ViewModel.CommunityThemes == null)
            {
                ViewModel.CommunityThemes = new ObservableCollection<ThemeItem>();
                await LoadCommunityThemes();
            }
        }
        private async void ReloadCommunityThemeList_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.CommunityThemes == null)
            {
                ViewModel.CommunityThemes = new ObservableCollection<ThemeItem>();
            }
            else
            {
                ViewModel.CommunityThemes.Clear();
            }
            ((Control)sender).IsEnabled = false;
            await LoadCommunityThemes();
            ((Control)sender).IsEnabled = true;
        }
        private async void InstallCommunityTheme_Click(object sender, RoutedEventArgs e)
        {
            if (uiCBCommunityThemes.SelectedItem == null)
            {
                MainWindowViewModel.Instance.AppBarText = "No Theme selected";
                return;
            }
            ((Control)sender).IsEnabled = false;
            try
            {
                ThemeItem theme = (ThemeItem)uiCBCommunityThemes.SelectedItem;
                Directory.CreateDirectory(LoginManager.Instance.GetUserProfile().ThemesLoadDir);
                string safeName = string.Concat(theme.DisplayName.Split(Path.GetInvalidFileNameChars()));
                string installationPath = Path.Combine(LoginManager.Instance.GetUserProfile().ThemesLoadDir, safeName + ".xaml");
                string result = await WebHelper.BaseGetAsync(theme.Path);
                File.WriteAllText(installationPath, result);
                LoadThemes();
                try
                {
                    int installedThemeIndex = ViewModel.Themes.IndexOf(ViewModel.Themes.First(t => t.DisplayName == theme.DisplayName));
                    uiCbTheme.SelectedIndex = installedThemeIndex;
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
                MainWindowViewModel.Instance.AppBarText = $"Successfully installed {theme.DisplayName}";
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = ex.Message;
                ((Control)sender).IsEnabled = true;
            }
            ((Control)sender).IsEnabled = true;
        }
        private void UninstallTheme_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(((ThemeItem)uiCbTheme.SelectedItem)?.Path))
                {
                    File.Delete(((ThemeItem)uiCbTheme.SelectedItem).Path);
                    uiCbTheme.SelectedIndex = 0;
                    LoadThemes();
                }
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        #endregion
        private void Hyperlink_RequestNavigate(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (((Control)sender!).Tag is string url)
                    PlatformInfo.OpenUrl(url);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }

        private void ExtractionPasswordSave_Click(object sender, RoutedEventArgs e)
        {
            Preferences.Set(AppConfigKey.ExtractionPassword, uiPwExtraction.Text ?? "", LoginManager.Instance.GetUserProfile().UserConfigFile, true);
            MainWindowViewModel.Instance.AppBarText = "Successfully saved extraction password";
        }
        private async void IgnoredExecutablesReset_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(LoginManager.Instance.GetUserProfile().IgnoreList))
                    File.Delete(LoginManager.Instance.GetUserProfile().IgnoreList);

                await SettingsViewModel.Instance.InitIgnoreList();
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private void IgnoredExecutablesSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Preferences.Set("IL", SettingsViewModel.Instance.IgnoreList, LoginManager.Instance.GetUserProfile().IgnoreList);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }

        private async void SyncSteamShortcuts_Toggled(object? sender, RoutedEventArgs e)
        {
            if (!this.loaded)//Make sure the toggle came from the ui
                return;

            if (((ToggleSwitch)sender!).IsChecked == true)
            {
                await SteamHelper.SyncGamesWithSteamShortcuts(InstallViewModel.Instance.InstalledGames.ToDictionary(pair => pair.Key, pair => pair.Value));
            }
            else
            {
                SteamHelper.RemoveGameVaultGamesFromSteamShortcuts();
            }
        }

        private async void RestoreSteamShortcutBackup_Click(object sender, RoutedEventArgs e)
        {
            MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync($"Are you sure you want to restore the backup? Your current shortcuts will be reset to the state when the backup was created. This can lead to some shortcuts being lost.", "", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = "Yes", NegativeButtonText = "No" });
            if (result == MessageDialogResult.Affirmative)
            {
                SteamHelper.RestoreBackup();
            }
        }
        private int devModeCount = 0;
        private void DevMode_Click(object? sender, PointerPressedEventArgs e)
        {
            devModeCount++;
            if (devModeCount == 5)
            {
                ViewModel.DevModeEnabled = true;
            }
        }
        private void RemoveCustomCloudSaveManifest_Click(object sender, RoutedEventArgs e)
        {
            int index = ViewModel.CustomCloudSaveManifests.IndexOf(((DirectoryEntry)((Control)sender).DataContext));
            if (index >= 0)
            {
                ViewModel.CustomCloudSaveManifests.RemoveAt(index);
            }
        }

        private void AddCustomCloudSaveManifest_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.CustomCloudSaveManifests.Add(new DirectoryEntry());
        }

        private void SaveCustomCloudSaveManifests_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string result = string.Join(";", ViewModel.CustomCloudSaveManifests.Where(entry => !string.IsNullOrWhiteSpace(entry.Uri)).Select(entry => entry.Uri));
                Preferences.Set(AppConfigKey.CustomCloudSaveManifests, result, LoginManager.Instance.GetUserProfile().UserConfigFile);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
            MainWindowViewModel.Instance.AppBarText = "Successfully saved custom Ludusavi Manifests";
        }
        private async void AddRootDirectory_Click(object sender, RoutedEventArgs e)
        {
            ((Control)sender).IsEnabled = false;
            try
            {
                string selectedDirectory = await SettingsViewModel.Instance.SelectDownloadPath();
                if (Directory.Exists(selectedDirectory))
                {
                    ViewModel.RootDirectories.Add(new DirectoryEntry() { Uri = selectedDirectory });
                    string result = string.Join(";", ViewModel.RootDirectories.Select(entry => entry.Uri));
                    Preferences.Set(AppConfigKey.RootDirectories, result, LoginManager.Instance.GetUserProfile().UserConfigFile);
                    await MainWindowViewModel.Instance.Library.GetGameInstalls().RestoreInstalledGames();
                    await MainWindowViewModel.Instance.Downloads.RestoreDownloadedGames();
                    if (InstallViewModel.Instance.InstalledGamesDuplicates.Any())
                    {
                        await ShowInstalledGameDuplicates();
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = ex.Message;
            }
            ((Control)sender).IsEnabled = true;
        }
        private async Task ShowInstalledGameDuplicates()
        {
            string duplicateMessage = "";
            foreach (var duplicate in InstallViewModel.Instance.InstalledGamesDuplicates)
            {
                var matchingGame = InstallViewModel.Instance.InstalledGames?.FirstOrDefault(game => game.Key.ID == duplicate.Key);
                if (string.IsNullOrEmpty(matchingGame?.Key?.Title))
                    continue;

                duplicateMessage += $"\n\n'{matchingGame?.Key?.Title}' is already installed at:\n{duplicate.Value}";
            }
            await App.Instance.MainWindow.ShowMessageAsync("Duplicate game installation detected", duplicateMessage, MessageDialogStyle.Affirmative, new MetroDialogSettings() { AffirmativeButtonText = "Ok", DialogTitleFontSize = 20 });
        }
        private async void RemoveRootDirectory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int index = ViewModel.RootDirectories.IndexOf(((DirectoryEntry)((Control)sender).DataContext));
                if (index >= 0)
                {
                    ViewModel.RootDirectories.RemoveAt(index);
                    string result = string.Join(";", ViewModel.RootDirectories.Select(entry => entry.Uri));
                    Preferences.Set(AppConfigKey.RootDirectories, result, LoginManager.Instance.GetUserProfile().UserConfigFile);

                    ((Control)sender).IsEnabled = false;//Disable the add button to block async restoring installed games
                    await MainWindowViewModel.Instance.Library.GetGameInstalls().RestoreInstalledGames();
                    await MainWindowViewModel.Instance.Downloads.RestoreDownloadedGames();
                }
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = ex.Message;
            }
            ((Control)sender).IsEnabled = true;
        }
        private void OpenUserCacheFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(LoginManager.Instance.GetUserProfile().RootDir))
            {
                PlatformInfo.OpenFolder(LoginManager.Instance.GetUserProfile().RootDir);
            }
        }
        private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(ProfileManager.ErrorLogDir))
                PlatformInfo.OpenFolder(ProfileManager.ErrorLogDir);
        }
        private async void BrowseWinePrefix_Click(object sender, RoutedEventArgs e)
        {
            string? folder = await StorageHelper.PickFolderAsync("Select the Wine prefix", ViewModel.EffectiveWinePrefix);
            if (!string.IsNullOrEmpty(folder))
                ViewModel.WinePrefix = folder;
        }
        private async void BrowseProtonPath_Click(object sender, RoutedEventArgs e)
        {
            string? folder = await StorageHelper.PickFolderAsync("Select a Proton build (e.g. ~/.steam/steam/compatibilitytools.d/GE-Proton...)");
            if (!string.IsNullOrEmpty(folder))
                ViewModel.ProtonPath = folder;
        }
    }
}