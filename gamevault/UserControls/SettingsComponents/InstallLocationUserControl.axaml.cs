using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Helper.Integrations;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>
    /// Interaction logic for InstallLocationUserControl.xaml
    /// </summary>
    public partial class InstallLocationUserControl : UserControl
    {
        private TaskCompletionSource<string> ResultTaskSource;
        public Dictionary<DirectoryEntry, string> RootDirectories { get; set; }
        private bool loaded = false;
        public InstallLocationUserControl()
        {
            InitializeComponent();
            ResultTaskSource = new TaskCompletionSource<string>();
            RootDirectories = new Dictionary<DirectoryEntry, string>();
            PrepareInstallLocationSelection();
            this.DataContext = this;
            // Attached rather than Loaded: the control may be hidden when there is a single root directory.
            AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(UserControl_Loaded, DispatcherPriority.Loaded);
        }
        private void UserControl_Loaded()
        {
            if (loaded) return;
            loaded = true;
            string lastSelectedRootDirectory = Preferences.Get(AppConfigKey.LastSelectedRootDirectory, LoginManager.Instance.GetUserProfile().UserConfigFile);
            if (AutoConfirmIfWindowIsHiddenOrOnlyOneEntry(lastSelectedRootDirectory))
                return;

            if (Directory.Exists(lastSelectedRootDirectory))
            {
                int index = RootDirectories.ToList().FindIndex(x => x.Key.Uri == lastSelectedRootDirectory);
                if (index != -1)
                {
                    ToggleButtonGroupBehavior.CheckToggleButtonByIndex("RootDirectorySelection", this, index);
                    return;
                }
            }
            ToggleButtonGroupBehavior.CheckToggleButtonByIndex("RootDirectorySelection", this, 0);//Default pre selects the first entry             
        }
        private void PrepareInstallLocationSelection()
        {
            // On Linux every path starts with "/", so take the most specific mount point containing the directory.
            var drives = DriveInfo.GetDrives().Where(d => { try { return d.IsReady; } catch { return false; } }).ToList();
            foreach (DirectoryEntry rootDir in SettingsViewModel.Instance.RootDirectories)
            {
                DriveInfo? drive = drives
                    .Where(d => rootDir.Uri.StartsWith(d.RootDirectory.FullName, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    .OrderByDescending(d => d.RootDirectory.FullName.Length)
                    .FirstOrDefault();
                if (drive != null && !RootDirectories.ContainsKey(rootDir))
                {
                    long freeSpace = 0;
                    try { freeSpace = drive.AvailableFreeSpace; } catch (Exception ex) { Log.Ignored(ex); }
                    RootDirectories.Add(rootDir, FormatBytes(freeSpace));
                }
            }
            if (RootDirectories.Count <= 1)
            {
                this.IsVisible = false;
            }
        }
        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
            int counter = 0;
            decimal number = bytes;

            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }

            return $"{number:n1} {suffixes[counter]}";
        }
        public Task<string> SelectInstallLocation()
        {
            return ResultTaskSource.Task;
        }
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            MainWindowViewModel.Instance.ClosePopup();
            ResultTaskSource.TrySetResult(string.Empty);
        }

        private void Install_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ToggleButton selectedButton = ToggleButtonGroupBehavior.GetCheckedToggleButton("RootDirectorySelection", this);
                if (selectedButton == null)
                    return;

                var dataContext = (KeyValuePair<DirectoryEntry, string>)selectedButton.DataContext!;
                if (selectedButton != null && selectedButton.Content != null)
                {
                    Preferences.Set(AppConfigKey.LastSelectedRootDirectory, dataContext.Key.Uri, LoginManager.Instance.GetUserProfile().UserConfigFile);
                    MainWindowViewModel.Instance.ClosePopup();
                    ResultTaskSource.TrySetResult(dataContext.Key.Uri);
                }
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }
        private bool AutoConfirmIfWindowIsHiddenOrOnlyOneEntry(string lastSelectedRootDirectory)
        {
            if (App.Instance.MainWindow.IsVisible == false && RootDirectories.Any())//possible if called by CLI
            {
                if (Directory.Exists(lastSelectedRootDirectory))
                {
                    MainWindowViewModel.Instance.ClosePopup();
                    ResultTaskSource.TrySetResult(lastSelectedRootDirectory);
                }
                else
                {
                    MainWindowViewModel.Instance.ClosePopup();
                    ResultTaskSource.TrySetResult(RootDirectories.ElementAt(0).Key.Uri);
                }
                return true;
            }
            else if (RootDirectories.Count == 1)
            {
                MainWindowViewModel.Instance.ClosePopup();
                ResultTaskSource.TrySetResult(RootDirectories.ElementAt(0).Key.Uri);
                return true;
            }
            return false;
        }
        private void DirectorySettings_Click(object sender, RoutedEventArgs e)
        {
            MainWindowViewModel.Instance.ClosePopup();
            ResultTaskSource.TrySetResult(string.Empty);
            MainWindowViewModel.Instance.SetActiveControl(MainControl.Settings);
            MainWindowViewModel.Instance.Settings.SetTabIndex(3);
        }
    }
}
