using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GameVault.Core;
using gamevault.Converter;
using gamevault.Helper;
using gamevault.Helper.Integrations;
using gamevault.Helper.Platform;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>Asks where a game is installed: one of the game folders (with their free space), or a new one.</summary>
    public partial class InstallLocationUserControl : UserControl
    {
        private readonly TaskCompletionSource<string> ResultTaskSource = new();
        private readonly long requiredBytes;

        public InstallLocationUserControl() : this(0) { }

        /// <param name="requiredBytes">The size of the game: folders without that much room can not be chosen.</param>
        public InstallLocationUserControl(long requiredBytes)
        {
            InitializeComponent();
            this.requiredBytes = requiredBytes;
            uiNeeded.Text = requiredBytes > 0 ? Loc.F("The game needs {0}", Size(requiredBytes)) : "";
            uiNeeded.IsVisible = requiredBytes > 0;
            Load();
            // Attached rather than Loaded: started without a visible window (command line), nobody can answer
            AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (App.Instance.MainWindow.IsVisible == false && Rows.Count > 0)
                    Choose(DefaultLocation());
                else
                    Focus();
            }, DispatcherPriority.Loaded);
            // Closed another way (Escape closes popups): the download that waits for the answer is not started
            DetachedFromVisualTree += (_, _) => ResultTaskSource.TrySetResult(string.Empty);
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Choose(string.Empty);
                }
            };
        }

        internal IReadOnlyList<InstallLocationRow> Rows => uiLocations.ItemsSource as IReadOnlyList<InstallLocationRow> ?? Array.Empty<InstallLocationRow>();

        private static string Size(long bytes) => new GameSizeConverter().Convert(bytes, typeof(string), null, CultureInfo.CurrentCulture) as string ?? "";

        private void Load()
        {
            string last = Preferences.Get(AppConfigKey.LastSelectedRootDirectory, LoginManager.Instance.GetUserProfile().UserConfigFile);
            var rows = new List<InstallLocationRow>();
            foreach (DirectoryEntry root in SettingsViewModel.Instance.RootDirectories)
            {
                long? free = null;
                try { free = PlatformInfo.GetDriveForPath(root.Uri)?.AvailableFreeSpace; }
                catch (Exception ex) { Log.Ignored(ex); }
                bool exists = Directory.Exists(root.Uri);
                string details = !exists ? Loc.T("Not found (disk unplugged?)")
                    : free == null ? "" : Loc.F("{0} free", Size(free.Value));
                bool hasRoom = exists && (free == null || requiredBytes <= 0 || free.Value >= requiredBytes);
                if (exists && !hasRoom)
                    details += " · " + Loc.T("not enough room");
                rows.Add(new InstallLocationRow(root.Uri, details, root.Uri == last, hasRoom));
            }
            uiLocations.ItemsSource = rows;
        }

        /// <summary>Without asking: the last install location if it is still a root directory, else the first one.</summary>
        public static string DefaultLocation()
        {
            string last = Preferences.Get(AppConfigKey.LastSelectedRootDirectory, LoginManager.Instance.GetUserProfile().UserConfigFile);
            var roots = SettingsViewModel.Instance.RootDirectories;
            return roots.Any(r => r.Uri == last) && Directory.Exists(last) ? last : roots.FirstOrDefault()?.Uri ?? string.Empty;
        }

        public Task<string> SelectInstallLocation() => ResultTaskSource.Task;

        private void Choose(string directory)
        {
            if (directory != string.Empty)
            {
                Preferences.Set(AppConfigKey.LastSelectedRootDirectory, directory, LoginManager.Instance.GetUserProfile().UserConfigFile);
                if (uiDontAsk.IsChecked == true)
                    SettingsViewModel.Instance.AskInstallLocation = false;
            }
            ResultTaskSource.TrySetResult(directory);
            MainWindowViewModel.Instance.ClosePopup();
        }

        private void Install_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is InstallLocationRow row)
                Choose(row.Path);
        }

        private async void OtherFolder_Click(object? sender, RoutedEventArgs e)
        {
            string directory = await SettingsViewModel.Instance.SelectDownloadPath();
            if (directory != "")
                await UseNewFolderAsync(directory);
        }

        /// <summary>The folder becomes a game folder, and the game is installed in it.</summary>
        internal async Task UseNewFolderAsync(string directory)
        {
            try
            {
                if (requiredBytes > 0 && PlatformInfo.GetDriveForPath(directory)?.AvailableFreeSpace < requiredBytes)
                {
                    MainWindowViewModel.Instance.AppBarText = Loc.F("Not enough space available for drive {0}", PlatformInfo.GetDriveForPath(directory)?.Name);
                    return;
                }
                IsEnabled = false;
                await SettingsViewModel.Instance.AddRootDirectoryAsync(directory);
                Choose(directory);
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = ex.Message;
                IsEnabled = true;
            }
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => Choose(string.Empty);
    }

    internal sealed class InstallLocationRow
    {
        public InstallLocationRow(string path, string details, bool isLast, bool hasRoom)
        {
            Path = path;
            Details = details;
            IsLast = isLast;
            HasRoom = hasRoom;
        }

        public string Path { get; }
        public string Details { get; }
        public bool IsLast { get; }
        public bool HasRoom { get; }
    }
}
