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
        /// <summary>Moving an installed game: its installation directory, else null.</summary>
        private readonly string? movingFrom;

        public InstallLocationUserControl() : this(0) { }

        /// <param name="requiredBytes">The size of the game: folders without that much room can not be chosen.</param>
        /// <param name="movingFrom">The installation of a game to move instead of a game to install.</param>
        public InstallLocationUserControl(long requiredBytes, string? movingFrom = null)
        {
            InitializeComponent();
            this.requiredBytes = requiredBytes;
            this.movingFrom = movingFrom;
            if (movingFrom != null)
            {
                uiTitle.Text = Loc.T("Where should the game be moved?");
                uiDontAsk.IsVisible = false;
            }
            uiNeeded.Text = requiredBytes > 0 ? Loc.F("The game needs {0}", Size(requiredBytes)) : "";
            uiNeeded.IsVisible = requiredBytes > 0;
            Load();
            // Attached rather than Loaded: started without a visible window (command line), nobody can answer
            AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (App.Instance.MainWindow.IsVisible == false && Rows.Count > 0 && movingFrom == null)
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
            string? currentDrive = movingFrom == null ? null : DriveName(movingFrom);
            foreach (DirectoryEntry root in SettingsViewModel.Instance.RootDirectories)
            {
                long? free = null;
                try { free = PlatformInfo.GetDriveForPath(root.Uri)?.AvailableFreeSpace; }
                catch (Exception ex) { Log.Ignored(ex); }
                bool exists = Directory.Exists(root.Uri);
                string details = !exists ? Loc.T("Not found (disk unplugged?)")
                    : free == null ? "" : Loc.F("{0} free", Size(free.Value));
                // A move on the same disk takes no room
                bool sameDisk = currentDrive != null && DriveName(root.Uri) == currentDrive;
                bool hasRoom = exists && (sameDisk || free == null || requiredBytes <= 0 || free.Value >= requiredBytes);
                if (exists && !hasRoom)
                    details += " · " + Loc.T("not enough room");
                if (movingFrom != null && IsCurrentLocation(root.Uri))
                {
                    details = Loc.T("The game is here");
                    hasRoom = false;
                }
                rows.Add(new InstallLocationRow(root.Uri, details, movingFrom == null && root.Uri == last, hasRoom, movingFrom == null ? Loc.T("Install here") : Loc.T("Move here")));
            }
            uiLocations.ItemsSource = rows;
        }

        private static string? DriveName(string path)
        {
            try { return PlatformInfo.GetDriveForPath(path)?.Name; }
            catch (Exception ex) { Log.Ignored(ex); return null; }
        }

        private bool IsCurrentLocation(string root) => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(root, "GameVault", "Installations"))),
            Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(movingFrom!))),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

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
            if (directory != string.Empty && movingFrom == null)
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
                if (requiredBytes > 0 && PlatformInfo.GetDriveForPath(directory)?.AvailableFreeSpace < requiredBytes
                    && (movingFrom == null || DriveName(directory) != DriveName(movingFrom)))
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
                MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex);
                IsEnabled = true;
            }
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => Choose(string.Empty);
    }

    internal sealed class InstallLocationRow
    {
        public InstallLocationRow(string path, string details, bool isLast, bool hasRoom, string action)
        {
            Path = path;
            Details = details;
            IsLast = isLast;
            HasRoom = hasRoom;
            Action = action;
        }

        public string Path { get; }
        public string Details { get; }
        public bool IsLast { get; }
        public bool HasRoom { get; }
        public string Action { get; }
    }
}
