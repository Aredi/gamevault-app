using gamevault.Localization;
using Avalonia.Platform.Storage;
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
using GameVault.Core.Compatibility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using gamevault.Converter;
using gamevault.Models.Mapping;
using IO.Swagger.Model;


namespace gamevault.UserControls
{
    /// <summary>
    /// Interaction logic for GameSettingsUserControl.xaml
    /// </summary>
    public partial class GameSettingsUserControl : UserControl
    {
        private bool startup = true;
        private bool loaded = false;
        private GameSettingsViewModel ViewModel { get; set; }
        private string SavedExecutable { get; set; }
        private GameSizeConverter gameSizeConverter { get; set; }

        internal GameSettingsUserControl(Game game)
        {
            InitializeComponent();
            ViewModel = new GameSettingsViewModel();
            ViewModel.Game = game;
            ViewModel.UpdateGame = new UpdateGameDto() { UserMetadata = new UpdateGameUserMetadataDto() };
            gameSizeConverter = new GameSizeConverter();
            if (IsGameInstalled(game))
            {
                FindGameExecutables(ViewModel.Directory, true);
                if (OperatingSystem.IsLinux())
                    LoadGameCompatibility();
                if (Directory.Exists(ViewModel.Directory))
                {
                    ViewModel.LaunchParameter = Preferences.Get(AppConfigKey.LaunchParameter, Path.Combine(ViewModel.Directory, "gamevault-exec"));
                    string installedVersion = Preferences.Get(AppConfigKey.InstalledGameVersion, Path.Combine(ViewModel.Directory, "gamevault-exec"));
                    ViewModel.InstalledGameVersion = installedVersion == string.Empty ? null : installedVersion;
                }
                InitDiskUsagePieChart();//Task
            }
            this.DataContext = ViewModel;
            Loaded += GameSettings_Loaded;
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                    MainWindowViewModel.Instance.ClosePopup();
            };
            uiLaunchParameter.LostFocus += LaunchParameter_Changed;
            foreach (var zone in new[] { uiBackgroundDropZone, uiBoxDropZone })
            {
                zone.AddHandler(DragDrop.DropEvent, Image_Drop);
                zone.KeyDown += Image_Paste;
                zone.PointerEntered += (s, _) => ((Control)s!).Focus();
            }
        }
        private async void GameSettings_Loaded(object? sender, RoutedEventArgs e)
        {
            if (!loaded)
            {
                loaded = true;
                this.Focus();
                // WPF TabControls selected their first tab by themselves, the ListBoxes used here do not
                startup = false;
                if (((Control)uiSettingsHeadersLocal.Parent!).IsVisible)
                    uiSettingsHeadersLocal.SelectedIndex = 0;
                else if (((Control)uiSettingsHeadersRemote.Parent!).IsVisible)
                    uiSettingsHeadersRemote.SelectedIndex = 0;
                await LoadGameMedatataProviders();
            }
        }
        private void Help_Click(object? sender, PointerReleasedEventArgs e)
        {
            try
            {
                string url = "";
                int currentIndex = 0;
                if (uiSettingsHeadersLocal.SelectedIndex == -1)
                    currentIndex = uiSettingsHeadersLocal.Items.Count + uiSettingsHeadersRemote.SelectedIndex;
                if (uiSettingsHeadersRemote.SelectedIndex == -1)
                    currentIndex = uiSettingsHeadersLocal.SelectedIndex;

                switch (currentIndex)
                {
                    case 0:
                        {
                            url = "https://gamevau.lt/docs/client-docs/gui#installation";
                            break;
                        }
                    case 1:
                        {
                            url = "https://gamevau.lt/docs/client-docs/gui#launch-options";
                            break;
                        }
                    case 2:
                        {
                            url = "https://gamevau.lt/docs/client-docs/gui/#edit-game-images";
                            break;
                        }
                    case 3:
                        {
                            url = "https://gamevau.lt/docs/client-docs/gui#metadata";
                            break;
                        }
                    case 4:
                        {
                            url = "https://gamevau.lt/docs/client-docs/gui#custom-metadata";
                            break;
                        }
                }
                PlatformInfo.OpenUrl(url);
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = ex.Message;
            }
        }
        private bool IsGameInstalled(Game game)
        {
            KeyValuePair<Game, string> result = InstallViewModel.Instance.InstalledGames.Where(g => g.Key.ID == game.ID).FirstOrDefault();
            if (result.Equals(default(KeyValuePair<Game, string>)))
                return false;

            ViewModel.Directory = result.Value;
            return true;
        }
        private void SettingsTabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (((ListBox)sender!).SelectedIndex == -1)
                return;

            if (sender == uiSettingsHeadersLocal)
            {
                uiSettingsHeadersRemote.SelectedIndex = -1;
                uiSettingsContent.SelectedIndex = uiSettingsHeadersLocal.SelectedIndex;
            }
            else if (sender == uiSettingsHeadersRemote)
            {
                if (startup && ViewModel.Directory != null)
                {
                    startup = false;
                    uiSettingsHeadersRemote.SelectedIndex = -1;
                }
                else
                {
                    uiSettingsHeadersLocal.SelectedIndex = -1;
                    uiSettingsContent.SelectedIndex = uiSettingsHeadersRemote.SelectedIndex + uiSettingsHeadersLocal.Items.Count;
                }
            }
        }

        private void Close_Click(object? sender, PointerReleasedEventArgs e)
        {
            MainWindowViewModel.Instance.ClosePopup();
        }
        #region INSTALLATION        
        private void OpenDirectory_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(ViewModel.Directory))
                PlatformInfo.OpenFolder(ViewModel.Directory);
        }
        private async void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            ((Control)sender).IsEnabled = false;
            await UninstallGame();
            ((Control)sender).IsEnabled = true;
        }

        public async Task UninstallGame()
        {
            //Check for forced installation type
            try
            {
                if (Directory.Exists(ViewModel.Directory) && int.TryParse(Preferences.Get(AppConfigKey.ForcedInstallationType, Path.Combine(ViewModel.Directory, "gamevault-exec")), out int intValue))
                {
                    if (Enum.IsDefined(typeof(GameType), intValue))
                    {
                        ViewModel.Game.Type = (GameType)intValue;
                    }
                }
            }
            catch (Exception ignored) { Log.Ignored(ignored); }

            if (ViewModel.Game.Type is GameType.WINDOWS_PORTABLE or GameType.LINUX_PORTABLE)
            {
                MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync(Loc.F("Are you sure you want to uninstall '{0}' ?", ViewModel.Game.Title), "", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = Loc.T("Yes"), NegativeButtonText = Loc.T("No") });
                if (result == MessageDialogResult.Affirmative)
                {
                    try
                    {
                        if (Directory.Exists(ViewModel.Directory))
                            Directory.Delete(ViewModel.Directory, true);

                        InstallViewModel.Instance.InstalledGames.Remove(InstallViewModel.Instance.InstalledGames.Where(g => g.Key.ID == ViewModel.Game.ID).First());
                        DesktopHelper.RemoveShotcut(ViewModel.Game);
                        MainWindowViewModel.Instance.ClosePopup();
                    }
                    catch
                    {
                        MainWindowViewModel.Instance.AppBarText = Loc.T("Something went wrong when deleting the files. Maybe they are opened by another process.");
                    }
                }
            }
            else if (ViewModel.Game.Type == GameType.WINDOWS_SETUP)
            {
                MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync(Loc.F("Are you sure you want to uninstall '{0}' ?\nAs this is a Windows Setup Game, you will need to select an uninstall executable manually", ViewModel.Game.Title), "", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = Loc.T("Yes"), NegativeButtonText = Loc.T("No") });
                if (result == MessageDialogResult.Affirmative)
                {
                    string selectedUninstallerExecutablePath = "";
                    if (!string.IsNullOrWhiteSpace(ViewModel.Game?.Metadata?.UninstallerExecutable))
                    {
                        var entry = Directory.GetFiles(ViewModel.Directory, "*", SearchOption.AllDirectories)
                                    .Select((file) => new { Key = file.Substring(ViewModel.Directory.Length + 1), Value = file })
                                    .FirstOrDefault(item => item.Key.Replace('\\', '/').Contains(ViewModel.Game?.Metadata?.UninstallerExecutable.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
                        if (entry != null)
                        {
                            selectedUninstallerExecutablePath = entry.Value;
                            if (!File.Exists(selectedUninstallerExecutablePath))
                            {
                                selectedUninstallerExecutablePath = "";
                            }
                        }
                    }
                    if (selectedUninstallerExecutablePath == "")
                    {
                        string? pickedFile = await StorageHelper.PickFileAsync("Select the uninstaller", new Dictionary<string, string[]> { { "uninstall", new[] { "*.exe", "*.EXE" } } }, ViewModel.Directory);
                        if (!string.IsNullOrEmpty(pickedFile) && File.Exists(pickedFile))
                        {
                            MessageDialogResult pickResult = await App.Instance.MainWindow.ShowMessageAsync(Loc.F("Are you sure you want to uninstall the game using '{0}' ?", Path.GetFileName(pickedFile)), "", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = Loc.T("Yes"), NegativeButtonText = Loc.T("No") });
                            if (pickResult != MessageDialogResult.Affirmative)
                            {
                                return;
                            }
                            selectedUninstallerExecutablePath = pickedFile;
                        }
                    }
                    if (!File.Exists(selectedUninstallerExecutablePath))
                    {
                        MainWindowViewModel.Instance.AppBarText = Loc.T("No valid uninstall executable selected");
                        return;
                    }
                    Process uninstProcess = null;
                    try
                    {
                        uninstProcess = ProcessHelper.StartApp(selectedUninstallerExecutablePath, ViewModel.Game?.Metadata?.UninstallerParameters, installationDirectory: ViewModel.Directory);
                    }
                    catch (Exception ex) when (!OperatingSystem.IsWindows())
                    {
                        MainWindowViewModel.Instance.AppBarText = ex.Message;
                        return;
                    }
                    catch
                    {

                        try
                        {
                            uninstProcess = ProcessHelper.StartApp(selectedUninstallerExecutablePath, ViewModel.Game?.Metadata?.UninstallerParameters, true, ViewModel.Directory);
                        }
                        catch
                        {
                            MainWindowViewModel.Instance.AppBarText = Loc.F("Can not execute '{0}'", selectedUninstallerExecutablePath);
                        }
                    }
                    if (uninstProcess != null)
                    {
                        await uninstProcess.WaitForExitAsync();
                        try
                        {
                            if (Directory.Exists(ViewModel.Directory))
                            {
                                //Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(ViewModel.Directory, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.DeletePermanently);                                        
                                Directory.Delete(ViewModel.Directory, true);
                            }

                            InstallViewModel.Instance.InstalledGames.Remove(InstallViewModel.Instance.InstalledGames.Where(g => g.Key.ID == ViewModel.Game.ID).First());
                            DesktopHelper.RemoveShotcut(ViewModel.Game);
                            MainWindowViewModel.Instance.ClosePopup();
                        }
                        catch (Exception ignored) { Log.Ignored(ignored); }
                    }
                }
            }
            else if (ViewModel.Game.Type == GameType.UNDETECTABLE)
            {
                MainWindowViewModel.Instance.AppBarText = Loc.T("Game Type cannot be determined");
            }
        }

        private void InitDiskUsagePieChart()
        {
            Task.Run(() =>
            {
                // The drive holding the installation: on Linux the most specific mount point.
                var drive = DriveInfo.GetDrives()
                    .Where(d => { try { return d.IsReady && ViewModel.Directory.StartsWith(d.RootDirectory.FullName, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal); } catch { return false; } })
                    .OrderByDescending(d => d.RootDirectory.FullName.Length)
                    .FirstOrDefault();

                if (drive == null)
                    return;

                long totalDiskSize = drive.TotalSize;
                long freeSpace = drive.AvailableFreeSpace;
                long currentGameSize = long.TryParse(ViewModel.Game.Size, out var size) ? size : 0;

                long otherGamesSize = InstallViewModel.Instance.InstalledGames
                    .Sum(installedGame => long.TryParse(installedGame.Key.Size, out var gameSize) ? gameSize : 0) - currentGameSize;

                long unmanagedDiskSize = Math.Max(0, totalDiskSize - currentGameSize - otherGamesSize - freeSpace);

                double[] percentages =
                {
                    (double)currentGameSize / totalDiskSize * 100,
                    (double)otherGamesSize / totalDiskSize * 100,
                    (double)unmanagedDiskSize / totalDiskSize * 100,
                    (double)freeSpace / totalDiskSize * 100,
                };
                // Keep tiny slices visible
                for (int i = 0; i < percentages.Length; i++)
                {
                    if (percentages[i] > 0 && percentages[i] < 5)
                    {
                        percentages[3] -= 5 - percentages[i];
                        percentages[i] = 5;
                    }
                }
                string[] names = { Loc.F("This Game ({0})", ViewModel.Game.Title), Loc.T("Other installed GameVault Games"), Loc.T("Unmanaged Data"), Loc.T("Free Space") };
                long[] sizes = { currentGameSize, otherGamesSize, unmanagedDiskSize, freeSpace };
                Color[] colors = { Colors.DeepPink, Colors.LightSeaGreen, Colors.PaleVioletRed, Colors.DarkGray };
                var slices = new List<PieSlice>();
                for (int i = 0; i < percentages.Length; i++)
                {
                    slices.Add(new PieSlice(names[i], percentages[i], colors[i], sizes[i] == 0 ? "" : (string)gameSizeConverter.Convert(sizes[i], null, null, null)));
                }
                try
                {
                    string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) || drive.VolumeLabel == drive.Name ? "" : $"{drive.VolumeLabel} ";
                    ViewModel.DiskSize = $"{label}({drive.RootDirectory.FullName}) - {gameSizeConverter.Convert(drive.TotalSize, null, null, null)}";
                }
                catch
                {
                    ViewModel.DiskSize = $"{gameSizeConverter.Convert(drive.TotalSize, null, null, null)}";
                }
                Dispatcher.UIThread.Post(() => uiDiscUsagePieChart.Slices = slices);
            });
        }

        #endregion
        #region LAUNCH OPTIONS
        private void FindGameExecutables(string directory, bool checkForSavedExecutable)
        {
            if (!Directory.Exists(directory))
                return;

            string lastSelected = "";
            if (uiCbExecutables.SelectedItem != null)
            {
                lastSelected = ((KeyValuePair<string, string>)uiCbExecutables.SelectedItem).Key;
            }
            ViewModel.Executables.Clear();
            if (true == checkForSavedExecutable)
            {
                SavedExecutable = Preferences.Get(AppConfigKey.Executable, Path.Combine(ViewModel.Directory, "gamevault-exec"));
            }

            List<string> allExecutables = new List<string>();
            foreach (string entry in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                string fileType = Path.GetExtension(entry).TrimStart('.');
                if (Globals.SupportedExecutables.Contains(fileType.ToUpper()))
                {
                    allExecutables.Add(entry);
                }
            }
            for (int count = 0; count < allExecutables.Count; count++)
            {
                if (ContainsValueFromIgnoreList(allExecutables[count]))
                    continue;
                var currentItem = new KeyValuePair<string, string>(allExecutables[count], allExecutables[count].Substring(ViewModel.Directory.Length + 1));
                ViewModel.Executables.Add(currentItem);
                if (true == checkForSavedExecutable && allExecutables[count] == SavedExecutable)
                {
                    uiCbExecutables.SelectedItem = currentItem;
                }
                else if (true == checkForSavedExecutable && SavedExecutable == string.Empty)
                {
                    checkForSavedExecutable = false;
                    uiCbExecutables.SelectedItem = currentItem;
                }
                else if (lastSelected != string.Empty)
                {
                    var result = ViewModel.Executables.Where(e => e.Key == lastSelected).FirstOrDefault();
                    if (result.Key != null)
                    {
                        uiCbExecutables.SelectedItem = result;
                    }
                }
            }
        }
        public static bool TryPrepareLaunchExecutable(string directory)
        {
            foreach (string entry in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                string fileType = Path.GetExtension(entry).TrimStart('.');
                if (Globals.SupportedExecutables.Contains(fileType.ToUpper()))
                {
                    if (!ContainsValueFromIgnoreList(entry))
                    {
                        if (!File.Exists(Path.Combine(directory, "gamevault-exec")))
                        {
                            File.Create(Path.Combine(directory, "gamevault-exec")).Close();
                        }
                        Preferences.Set(AppConfigKey.Executable, entry, Path.Combine(directory, "gamevault-exec"));
                        return true;
                    }
                }
            }
            return false;
        }
        private static bool ContainsValueFromIgnoreList(string value)
        {
            return (SettingsViewModel.Instance.IgnoreList != null && SettingsViewModel.Instance.IgnoreList.Any(s => Path.GetFileNameWithoutExtension(value).Contains(s, StringComparison.OrdinalIgnoreCase)));
        }
        private void ExecutableSelection_Opened(object? sender, EventArgs e)
        {
            FindGameExecutables(ViewModel.Directory, false);
        }
        private void Executable_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                SavedExecutable = ((KeyValuePair<string, string>)e.AddedItems[0]!).Key;
                if (Directory.Exists(ViewModel.Directory))
                {
                    Preferences.Set(AppConfigKey.Executable, SavedExecutable, Path.Combine(ViewModel.Directory, "gamevault-exec"));
                    if (e.RemovedItems.Count > 0 && DesktopHelper.ShortcutExists(ViewModel.Game))
                    {
                        DesktopHelper.RemoveShotcut(ViewModel.Game);
                        _ = DesktopHelper.CreateShortcut(ViewModel.Game, SavedExecutable, false);
                    }
                }
            }
        }
        private async void CreateDesktopShortcut_Click(object sender, RoutedEventArgs e)
        {
            await DesktopHelper.CreateShortcut(ViewModel.Game, SavedExecutable, true);
        }

        private void LaunchParameter_Changed(object? sender, RoutedEventArgs e)
        {
            if (Directory.Exists(ViewModel.Directory))
            {
                Preferences.Set(AppConfigKey.LaunchParameter, ViewModel.LaunchParameter, Path.Combine(ViewModel.Directory, "gamevault-exec"));
            }
        }
        #endregion
        #region COMPATIBILITY (Linux)
        private GameCompatibility? gameCompatibility;
        private bool loadingCompatibility;

        private void LoadGameCompatibility()
        {
            loadingCompatibility = true;
            gameCompatibility = GameCompatibility.ForInstallation(ViewModel.Directory);
            string defaultName;
            try { defaultName = CompatibilityManager.Resolve(CompatibilitySettings.DefaultToolId).Name; }
            catch { defaultName = CompatibilityManager.DisplayName(CompatibilitySettings.DefaultToolId); }
            var tools = new List<CompatibilityTool>
            {
                new CompatibilityTool(CompatibilityToolId.Default, $"Default ({defaultName})", CompatibilityToolKind.Wine, null, "the default tool of Settings → Linux", false),
            };
            tools.AddRange(CompatibilityManager.GetTools(refresh: true).Where(t => t.Id != CompatibilityToolId.Auto));
            // Keep a tool that was deleted meanwhile visible, so the choice is not silently changed
            if (!string.IsNullOrEmpty(gameCompatibility.ToolId) && tools.All(t => t.Id != gameCompatibility.ToolId))
                tools.Add(new CompatibilityTool(gameCompatibility.ToolId, CompatibilityManager.DisplayName(gameCompatibility.ToolId), CompatibilityToolKind.Wine, null, "not found", false));
            ViewModel.GameCompatibilityTools = tools;
            ViewModel.SelectedGameCompatibilityTool = tools.First(t => t.Id == gameCompatibility.ToolId);
            ViewModel.SelectedWinePrefixMode = gameCompatibility.PrefixMode;
            ViewModel.GamePrefixPath = gameCompatibility.PrefixPath;
            string umuSetting = gameCompatibility.UmuIdSetting;
            ViewModel.UmuModeIndex = umuSetting == GameCompatibility.NoFixes ? 2 : umuSetting == "" ? 0 : 1;
            ViewModel.CustomUmuId = ViewModel.UmuModeIndex == 1 ? umuSetting : "";
            ViewModel.WinetricksVerbs = string.Join(' ', gameCompatibility.WinetricksVerbs);
            RefreshFixesStatus();
            loadingCompatibility = false;
        }
        private void RefreshFixesStatus()
        {
            if (gameCompatibility == null)
                return;
            string detected = gameCompatibility.DetectedUmuId;
            ViewModel.UmuStatus = ViewModel.UmuModeIndex switch
            {
                2 => Loc.T("No fixes are applied (umu-default)."),
                1 => Loc.F("The game starts as {0}.", gameCompatibility.EffectiveUmuId),
                _ => detected == "" ? Loc.T("The game is looked up in the umu database when it starts.")
                   : detected == "-" ? Loc.T("The umu database does not know this title: no specific fixes (umu-default). A custom id can be set.")
                   : Loc.F("Found in the umu database: {0}. Its fixes are applied with Proton.", detected),
            };
            string[] verbs = gameCompatibility.WinetricksVerbs;
            string[] pending = gameCompatibility.PendingWinetricks;
            ViewModel.WinetricksStatus = verbs.Length == 0 ? ""
                : pending.Length == 0 ? Loc.T("All components are installed in the current prefix.")
                : Loc.F("Installed at the next start: {0}", string.Join(", ", pending));
        }
        private void UmuMode_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (loadingCompatibility || gameCompatibility == null)
                return;
            gameCompatibility.SetUmuId(ViewModel.UmuModeIndex switch { 2 => GameCompatibility.NoFixes, 1 => ViewModel.CustomUmuId, _ => "" });
            RefreshFixesStatus();
        }
        private void CustomUmuId_LostFocus(object? sender, RoutedEventArgs e)
        {
            if (gameCompatibility == null || ViewModel.UmuModeIndex != 1)
                return;
            if (!string.IsNullOrWhiteSpace(ViewModel.CustomUmuId) && !GameVault.Core.Compatibility.UmuDatabase.IsValidId(ViewModel.CustomUmuId.Trim()))
            {
                ViewModel.UmuStatus = "An umu id looks like umu-271590 (see the umu database).";
                return;
            }
            gameCompatibility.SetUmuId(ViewModel.CustomUmuId);
            RefreshFixesStatus();
        }
        private async void UmuLookup_Click(object sender, RoutedEventArgs e)
        {
            if (gameCompatibility == null)
                return;
            ViewModel.UmuStatus = "Looking up...";
            string title = ViewModel.Game?.Metadata?.Title ?? ViewModel.Game?.Title ?? "";
            await GameFixes.LookUpAsync(gameCompatibility, title);
            RefreshFixesStatus();
        }
        private void WinetricksVerbs_LostFocus(object? sender, RoutedEventArgs e)
        {
            if (gameCompatibility == null)
                return;
            gameCompatibility.SetWinetricksVerbs(ViewModel.WinetricksVerbs);
            ViewModel.WinetricksVerbs = string.Join(' ', gameCompatibility.WinetricksVerbs);
            RefreshFixesStatus();
        }
        private async void WinetricksInstall_Click(object sender, RoutedEventArgs e)
        {
            if (gameCompatibility == null)
                return;
            gameCompatibility.SetWinetricksVerbs(ViewModel.WinetricksVerbs);
            string[] pending = gameCompatibility.PendingWinetricks;
            if (pending.Length == 0)
            {
                RefreshFixesStatus();
                return;
            }
            ((Control)sender).IsEnabled = false;
            ViewModel.WinetricksStatus = $"Installing {string.Join(", ", pending)}... (this can take a few minutes)";
            try
            {
                await GameFixes.RunWinetricksAsync(gameCompatibility, pending);
                RefreshFixesStatus();
            }
            catch (Exception ex)
            {
                ViewModel.WinetricksStatus = ex.Message;
            }
            ((Control)sender).IsEnabled = true;
        }
        private void GameCompatibilityTool_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // The binding also raises this when the panel opens, nothing changed then
            if (loadingCompatibility || gameCompatibility == null || ViewModel.SelectedGameCompatibilityTool == null || ViewModel.SelectedGameCompatibilityTool.Id == gameCompatibility.ToolId)
                return;
            gameCompatibility.SetTool(ViewModel.SelectedGameCompatibilityTool.Id);
            MainWindowViewModel.Instance.AppBarText = Loc.F("{0} now runs with {1}", ViewModel.Game?.Title, ViewModel.SelectedGameCompatibilityTool.Name);
        }
        private void GameWinePrefixMode_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (loadingCompatibility || gameCompatibility == null || ViewModel.SelectedWinePrefixMode == gameCompatibility.PrefixMode)
                return;
            gameCompatibility.SetPrefixMode(ViewModel.SelectedWinePrefixMode);
            ViewModel.GamePrefixPath = gameCompatibility.PrefixPath;
        }
        private void OpenGamePrefix_Click(object sender, RoutedEventArgs e)
        {
            if (gameCompatibility == null)
                return;
            Directory.CreateDirectory(gameCompatibility.PrefixPath);
            PlatformInfo.OpenFolder(gameCompatibility.PrefixPath);
        }
        private void GameWinecfg_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ProcessHelper.StartWineTool("winecfg", ViewModel.Directory);
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = ex.Message;
            }
        }
        private async void DeleteGamePrefix_Click(object sender, RoutedEventArgs e)
        {
            if (gameCompatibility == null || gameCompatibility.PrefixMode != WinePrefixMode.Game || !Directory.Exists(gameCompatibility.PrefixPath))
                return;
            if (!await DialogService.ConfirmAsync(Loc.F("Delete the prefix of {0}?\n{1}\n\nSaves stored in the prefix are deleted too (unless they are in the cloud).", ViewModel.Game?.Title, gameCompatibility.PrefixPath), Loc.T("Delete prefix")))
                return;
            try
            {
                Directory.Delete(gameCompatibility.PrefixPath, true);
                MainWindowViewModel.Instance.AppBarText = Loc.T("Prefix deleted");
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = ex.Message;
            }
        }
        #endregion
        #region EDIT IMAGE    

        private async void Image_Drop(object? sender, DragEventArgs e)
        {
            string? tag = ((Control)sender!).Tag as string;
            try
            {
                var files = e.Data.GetFiles();
                string? file = files?.Select(f => f.TryGetLocalPath()).FirstOrDefault(f => f != null);
                if (file != null)
                {
                    SetImage(tag, BitmapHelper.GetBitmapImage(file));
                    return;
                }
                // Images dragged out of a browser come as HTML or as a plain URL
                string? html = e.Data.Get("text/html") as string ?? (e.Data.Get("HTML Format") as string);
                string imagePath = html != null ? ExtractImageUrlFromHtml(html) : string.Empty;
                if (string.IsNullOrEmpty(imagePath))
                    imagePath = e.Data.GetText()?.Trim() ?? "";
                if (Uri.IsWellFormedUriString(imagePath, UriKind.Absolute))
                {
                    SetImage(tag, await BitmapHelper.GetBitmapImageAsync(imagePath));
                }
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = Loc.F("Failed to load image: {0}", ex.Message);
            }
        }
        private void SetImage(string? tag, IImage? image)
        {
            if (image == null)
                return;
            if (tag == "box")
                ViewModel.GameCoverImageSource = image;
            else
                ViewModel.BackgroundImageSource = image;
        }
        private string ExtractImageUrlFromHtml(string html)
        {
            Regex regex = new Regex("<img[^>]+?src\\s*=\\s*['\"]([^'\"]+)['\"][^>]*>");
            Match match = regex.Match(html);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return string.Empty;
        }

        private static readonly Dictionary<string, string[]> ImageFileTypes = new Dictionary<string, string[]>
        {
            { "Images", new[] { "*.jpg", "*.jpeg", "*.png", "*.gif", "*.tif", "*.tiff", "*.ico", "*.bmp", "*.webp" } },
        };
        private async void ChooseImage(object? sender, PointerReleasedEventArgs e)
        {
            try
            {
                string? tag = ((Control)sender!).Tag as string;
                string? file = await StorageHelper.PickFileAsync("Select an image", ImageFileTypes);
                if (!string.IsNullOrEmpty(file) && File.Exists(file))
                {
                    SetImage(tag, BitmapHelper.GetBitmapImage(file));
                }
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = ex.Message;
            }
        }
        private async void LoadImageUrl(string url, string tag)
        {
            try
            {
                if (tag == "box")
                {
                    ViewModel.GameCoverImageSource = await BitmapHelper.GetBitmapImageAsync(url);
                }
                else
                {
                    ViewModel.BackgroundImageSource = await BitmapHelper.GetBitmapImageAsync(url);
                }
            }
            catch (Exception ex)
            {
                if (url != string.Empty)
                    MainWindowViewModel.Instance.AppBarText = ex.Message;
            }
        }
        private void FindImages_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string query = ((Control)sender).Tag.ToString();
                string googleSearchUrl = $"https://www.google.com/search?q={Uri.EscapeDataString($"{ViewModel.Game.Title} {query}")}&tbm=isch";
                PlatformInfo.OpenUrl(googleSearchUrl);
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = ex.Message;
            }
        }
        private async Task SaveImage(string tag)
        {
            bool success = false;
            try
            {
                IImage bitmapSource = tag == "box" ? ViewModel.GameCoverImageSource : ViewModel.BackgroundImageSource;
                string resp = await WebHelper.UploadFileAsync($"{SettingsViewModel.Instance.ServerUrl}/api/media", BitmapHelper.BitmapSourceToMemoryStream(bitmapSource), "x.jpg", null);
                Media? newImage = JsonSerializer.Deserialize<Media>(resp);

                try
                {
                    UpdateGameDto updateGame = new UpdateGameDto() { UserMetadata = new UpdateGameUserMetadataDto() };
                    if (tag == "box")
                    {
                        updateGame.UserMetadata.Cover = newImage;
                    }
                    else
                    {
                        updateGame.UserMetadata.Background = newImage;
                    }

                    string changedGame = await WebHelper.PutAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games/{ViewModel.Game.ID}", JsonSerializer.Serialize(updateGame));
                    ViewModel.Game = JsonSerializer.Deserialize<Game>(changedGame);
                    success = true;
                    MainWindowViewModel.Instance.AppBarText = Loc.T("Successfully updated image");
                }
                catch (Exception ex)
                {
                    MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex);
                }

                //Update Data Context for Library. So that the images are also refreshed there directly
                if (success)
                {
                    InstallViewModel.Instance.RefreshGame(ViewModel.Game);
                    MainWindowViewModel.Instance.Library.RefreshGame(ViewModel.Game);
                    MainWindowViewModel.Instance.Downloads.RefreshGame(ViewModel.Game);
                    if (MainWindowViewModel.Instance.ActiveControl.GetType() == typeof(GameViewUserControl))
                    {
                        ((GameViewUserControl)MainWindowViewModel.Instance.ActiveControl).RefreshGame(ViewModel.Game);
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex);
            }
        }
        private async void Image_Paste(object? sender, KeyEventArgs e)
        {
            if (e.KeyModifiers != KeyModifiers.Control || e.Key != Key.V)
                return;
            try
            {
                string? tag = ((Control)sender!).Tag as string;
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard == null)
                    return;
                string[] formats = await clipboard.GetFormatsAsync();
                foreach (string format in new[] { "image/png", "PNG", "image/jpeg", "image/bmp" })
                {
                    if (formats.Contains(format) && await clipboard.GetDataAsync(format) is byte[] data)
                    {
                        SetImage(tag, new Bitmap(new MemoryStream(data)));
                        return;
                    }
                }
                string? text = (await clipboard.GetTextAsync())?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    if (File.Exists(text))
                        SetImage(tag, BitmapHelper.GetBitmapImage(text));
                    else if (Uri.IsWellFormedUriString(text, UriKind.Absolute))
                        SetImage(tag, await BitmapHelper.GetBitmapImageAsync(text));
                }
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = ex.Message;
            }
        }
        private async void CopyImageToClipboard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                IImage? image = ((Control)sender).Tag?.ToString() == "CurrentShownMappedGame"
                    ? uiImgCurrentShownMappedGame.GetImageSource()
                    : uiImgCurrentMergedGame.GetImageSource();
                if (image is not Bitmap bitmap)
                    return;
                using var png = new MemoryStream();
                bitmap.Save(png);
                var data = new DataObject();
                data.Set("image/png", png.ToArray());
                data.Set("PNG", png.ToArray());
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                    await clipboard.SetDataObjectAsync(data);
                MainWindowViewModel.Instance.AppBarText = Loc.T("Image copied to Clipboard");
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = ex.Message;
            }
        }
        #region Generic Events       
        private InputTimer backgroundImageUrldebounceTimer { get; set; }
        private InputTimer boxImageUrldebounceTimer { get; set; }
        private void InitImageUrlTimer()
        {
            if (backgroundImageUrldebounceTimer == null)
            {
                backgroundImageUrldebounceTimer = new InputTimer() { Data = string.Empty };
                backgroundImageUrldebounceTimer.Interval = TimeSpan.FromMilliseconds(400);
                backgroundImageUrldebounceTimer.Tick += BackgroundImageDebounceTimerElapsed;
            }
            if (boxImageUrldebounceTimer == null)
            {
                boxImageUrldebounceTimer = new InputTimer() { Data = string.Empty };
                boxImageUrldebounceTimer.Interval = TimeSpan.FromMilliseconds(400);
                boxImageUrldebounceTimer.Tick += BoxImageDebounceTimerElapsed;
            }
        }
        private async void BoxImage_Save(object sender, RoutedEventArgs e)
        {
            ViewModel.GameCoverImageChanged = false;
            await SaveImage("box");
        }
        private async void BackgroundImage_Save(object sender, RoutedEventArgs e)
        {
            ViewModel.BackgroundImageChanged = false;
            await SaveImage("");
        }
        private void BackgoundImageUrl_TextChanged(object? sender, TextChangedEventArgs e)
        {
            InitImageUrlTimer();
            backgroundImageUrldebounceTimer.Stop();
            backgroundImageUrldebounceTimer.Data = ((TextBox)sender).Text;
            backgroundImageUrldebounceTimer.Start();
        }
        private void BoxImageUrl_TextChanged(object? sender, TextChangedEventArgs e)
        {
            InitImageUrlTimer();
            boxImageUrldebounceTimer.Stop();
            boxImageUrldebounceTimer.Data = ((TextBox)sender).Text;
            boxImageUrldebounceTimer.Start();
        }
        private void BackgroundImageDebounceTimerElapsed(object? sender, EventArgs e)
        {
            backgroundImageUrldebounceTimer.Stop();
            LoadImageUrl(backgroundImageUrldebounceTimer.Data, "");
        }
        private void BoxImageDebounceTimerElapsed(object? sender, EventArgs e)
        {
            boxImageUrldebounceTimer.Stop();
            LoadImageUrl(boxImageUrldebounceTimer.Data, "box");
        }
        #endregion
        #endregion
        #region Metadata
        private InputTimer GameMetadataSearchTimer { get; set; }
        private void ProviderGameSearch_TextChanged(object? sender, TextChangedEventArgs e)
        {
            InitGameMetadataSearchTimer();
            GameMetadataSearchTimer.Stop();
            GameMetadataSearchTimer.Data = ((TextBox)sender).Text;
            GameMetadataSearchTimer.Start();
        }
        private void InitGameMetadataSearchTimer()
        {
            if (GameMetadataSearchTimer != null)
                return;

            GameMetadataSearchTimer = new InputTimer();
            GameMetadataSearchTimer.Interval = TimeSpan.FromMilliseconds(400);
            GameMetadataSearchTimer.Tick += GameMetadataSearchTimerElapsed!;
        }
        private async void GameMetadataSearchTimerElapsed(object sender, EventArgs e)
        {
            GameMetadataSearchTimer?.Stop();
            await GameMetadataSearch();
        }
        private async Task GameMetadataSearch()
        {
            this.Cursor = new Cursor(StandardCursorType.Wait);
            try
            {
                string currentShownUser = await WebHelper.GetAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/metadata/providers/{ViewModel.MetadataProviders?[ViewModel.SelectedMetadataProviderIndex]?.Slug}/search?query={GameMetadataSearchTimer.Data}");
                ViewModel.RemapSearchResults = JsonSerializer.Deserialize<MinimalGame[]>(currentShownUser);
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = Loc.F("Could not load metadata provider data. ({0})", ex.Message);
                ViewModel.RemapSearchResults = null;
            }
            this.Cursor = null;
        }
        private async void Recache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MetadataProviderDto currentSelectedProvider = ViewModel.MetadataProviders?[ViewModel.SelectedMetadataProviderIndex];
                string? currentProviderSlug = currentSelectedProvider?.Slug;
                string? providerId = ViewModel.CurrentShownMappedGame?.ProviderDataId;
                int gameId = ViewModel.Game.ID;
                await RemapGame(providerId, currentProviderSlug, gameId);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private async void GameRemap_Click(object sender, RoutedEventArgs e)
        {
            var providerId = ((MinimalGame)((Control)sender).DataContext).ProviderDataId;
            MetadataProviderDto? currentSelectedProvider = ViewModel.MetadataProviders?[ViewModel.SelectedMetadataProviderIndex];
            string? currentProviderSlug = currentSelectedProvider?.Slug;
            int gameId = ViewModel.Game.ID;
            await RemapGame(providerId, currentProviderSlug, gameId);
        }
        private async void Unmap_Click(object sender, RoutedEventArgs e)
        {
            MetadataProviderDto? currentSelectedProvider = ViewModel.MetadataProviders?[ViewModel.SelectedMetadataProviderIndex];
            string? currentProviderSlug = currentSelectedProvider?.Slug;
            int gameId = ViewModel.Game.ID;
            await RemapGame(null, currentProviderSlug, gameId);
        }
        private async void SavePriority_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MetadataProviderDto currentSelectedProvider = ViewModel.MetadataProviders?[ViewModel.SelectedMetadataProviderIndex];
                string? currentProviderSlug = currentSelectedProvider?.Slug;
                string? providerId = ViewModel.CurrentShownMappedGame?.ProviderDataId;
                int gameId = ViewModel.Game.ID;
                await RemapGame(providerId, currentProviderSlug, gameId, uiProviderPriority.Value == null ? null : (int?)uiProviderPriority.Value);
                await LoadGameMedatataProviders();
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private async Task RemapGame(string? providerId, string? providerSlug, int gameId, int? priority = null)
        {
            bool success = false;
            this.IsEnabled = false;
            try
            {
                UpdateGameDto updateGame = new UpdateGameDto() { MappingRequests = new List<MapGameDto>() { new MapGameDto() { ProviderSlug = providerSlug, ProviderDataId = providerId, ProviderPriority = priority } } };
                string remappedGame = await WebHelper.PutAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games/{gameId}", JsonSerializer.Serialize(updateGame));
                ViewModel.Game = JsonSerializer.Deserialize<Game>(remappedGame);
                success = true;
                MainWindowViewModel.Instance.AppBarText = Loc.F("Successfully re-mapped {0}", ViewModel.Game.Title);
            }
            catch (Exception ex)
            {
                string errMessage = WebExceptionHelper.TryGetServerMessage(ex);
                MainWindowViewModel.Instance.AppBarText = errMessage;
            }
            InstallViewModel.Instance.RefreshGame(ViewModel.Game);
            MainWindowViewModel.Instance.Library.RefreshGame(ViewModel.Game);
            if (success)
            {
                if (MainWindowViewModel.Instance.ActiveControl.GetType() == typeof(GameViewUserControl))
                {
                    ((GameViewUserControl)MainWindowViewModel.Instance.ActiveControl).RefreshGame(ViewModel.Game);
                }
                ViewModel.CurrentShownMappedGame = ViewModel.CurrentShownMappedGame;
            }
            this.IsEnabled = true;
            this.Focus();
        }
        private async Task LoadGameMedatataProviders()
        {
            try
            {
                ViewModel.MetadataProvidersLoaded = false;
                string result = await WebHelper.GetAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/metadata/providers");
                var providers = JsonSerializer.Deserialize<MetadataProviderDto[]?>(result);
                foreach (GameMetadata gmd in ViewModel.Game.ProviderMetadata)
                {
                    if (gmd.ProviderPriority != null)
                    {
                        var provider = providers?.FirstOrDefault(p => p.Slug == gmd.ProviderSlug);
                        if (provider != null)
                        {
                            provider.Priority = gmd.ProviderPriority;
                        }
                    }
                }
                providers = providers?.OrderByDescending(p => p.Priority).ToArray();
                ViewModel.MetadataProviders = providers;
                ViewModel.SelectedMetadataProviderIndex = 0;
                ViewModel.MetadataProvidersLoaded = true;
            }
            catch (Exception ex)
            {
                string message = WebExceptionHelper.TryGetServerMessage(ex);
                MainWindowViewModel.Instance.AppBarText = message;
            }
        }

        #endregion
        #region Edit Game Details
        private async void ClearUserData_Click(object sender, RoutedEventArgs e)
        {
            MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync(Loc.T("Are you sure you want to wipe all manually edited custom metadata and images?\n\nAll fields will revert to the merged provider metadata (if available).\n\nThis action cannot be undone."), "", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = Loc.T("Yes"), NegativeButtonText = Loc.T("No") });
            if (result == MessageDialogResult.Affirmative)
            {
                int gameId = ViewModel.Game.ID;
                await RemapGame(null, "user", gameId);
            }
        }
        private async void SaveGameDetails_Click(object sender, RoutedEventArgs e)
        {
            this.IsEnabled = false;
            bool success = false;

            try
            {
                string remappedGame = await WebHelper.PutAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games/{ViewModel.Game.ID}", JsonSerializer.Serialize(ViewModel.UpdateGame));
                ViewModel.Game = JsonSerializer.Deserialize<Game>(remappedGame);
                success = true;
                ViewModel.UpdateGame = new UpdateGameDto() { UserMetadata = new UpdateGameUserMetadataDto() };
                MainWindowViewModel.Instance.AppBarText = Loc.F("Successfully edited {0}", ViewModel.Game.Title);
            }
            catch (Exception ex)
            {
                string errMessage = WebExceptionHelper.TryGetServerMessage(ex);
                MainWindowViewModel.Instance.AppBarText = errMessage;
            }
            if (success)
            {
                if (MainWindowViewModel.Instance.ActiveControl.GetType() == typeof(GameViewUserControl))
                {
                    ((GameViewUserControl)MainWindowViewModel.Instance.ActiveControl).RefreshGame(ViewModel.Game);
                }
                MainWindowViewModel.Instance.Downloads.RefreshGame(ViewModel.Game);
            }
            this.IsEnabled = true;
            this.Focus();
        }
        private void KeepData_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string tag = ((Control)sender).Tag.ToString();
                if (tag == "description")
                {
                    ViewModel.UpdateGame.UserMetadata.Description = ViewModel.Game.Metadata.Description;
                }
                else if (tag == "notes")
                {
                    ViewModel.UpdateGame.UserMetadata.Notes = ViewModel.Game.Metadata.Notes;
                }
                else if (tag == "genre")
                {
                    ViewModel.UpdateGame.UserMetadata.Genres = ViewModel.Game.Metadata.Genres.Select(genre => genre.Name).ToArray();
                }
                else if (tag == "tag")
                {
                    ViewModel.UpdateGame.UserMetadata.Tags = ViewModel.Game.Metadata.Tags.Select(genre => genre.Name).ToArray();
                }
                else if (tag == "publisher")
                {
                    ViewModel.UpdateGame.UserMetadata.Publishers = ViewModel.Game.Metadata.Publishers.Select(genre => genre.Name).ToArray();
                }
                else if (tag == "developer")
                {
                    ViewModel.UpdateGame.UserMetadata.Developers = ViewModel.Game.Metadata.Developers.Select(genre => genre.Name).ToArray();
                }
                else if (tag == "trailer")
                {
                    ViewModel.UpdateGame.UserMetadata.UrlTrailers = ViewModel.Game.Metadata.Trailers;
                }
                else if (tag == "gameplays")
                {
                    ViewModel.UpdateGame.UserMetadata.UrlGameplays = ViewModel.Game.Metadata.Gameplays;
                }
                else if (tag == "screenshots")
                {
                    ViewModel.UpdateGame.UserMetadata.UrlScreenshots = ViewModel.Game.Metadata.Screenshots;
                }
                //Cheap update the UI without adding Notify property changed to the Model
                var temp = ViewModel.UpdateGame;
                ViewModel.UpdateGame = null;
                ViewModel.UpdateGame = temp;
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }

        #endregion


    }
}
