using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GameVault.Core;
using GameVault.Core.Storage;
using gamevault.Converter;
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
    /// <summary>
    /// What the installed games take on the disks, and the ones not played for months, to free room in a click.
    /// </summary>
    public partial class StorageUserControl : UserControl
    {
        private List<(InstalledGameUsage Usage, Game Game)> games = new();

        public StorageUserControl()
        {
            InitializeComponent();
            Loaded += async (_, _) =>
            {
                Focus();
                await LoadAsync();
            };
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    MainWindowViewModel.Instance.ClosePopup();
                }
            };
        }

        internal IReadOnlyList<StorageGameRow> Rows => uiGames.ItemsSource as IReadOnlyList<StorageGameRow> ?? Array.Empty<StorageGameRow>();

        /// <summary>Sizes the installation folders (in the background: large games have many files).</summary>
        internal async Task LoadAsync()
        {
            uiLoading.IsVisible = true;
            var installed = InstallViewModel.Instance.InstalledGames.Where(g => g.Key != null && !string.IsNullOrEmpty(g.Value)).ToList();
            var records = InstallViewModel.Instance.PlayRecords;
            games = await Task.Run(() => installed.Select(install =>
            {
                records.TryGetValue(install.Key.ID, out var record);
                long size = 0;
                DateTime installedAt = DateTime.UtcNow;
                try
                {
                    size = StorageCleanup.DirectorySize(install.Value);
                    installedAt = Directory.GetCreationTimeUtc(install.Value);
                }
                catch (Exception ex) { Log.Ignored(ex); }
                var usage = new InstalledGameUsage(install.Key.ID, install.Key.Metadata?.Title ?? install.Key.Title ?? "", install.Value, size,
                    record?.MinutesPlayed ?? 0, record?.LastPlayedAt?.ToUniversalTime(), installedAt);
                return (usage, install.Key);
            }).ToList());
            uiDrives.ItemsSource = DriveRow.For(games.Select(g => g.Usage));
            uiLoading.IsVisible = false;
            Show();
        }

        private void Show()
        {
            DateTime now = DateTime.UtcNow;
            var summary = InstalledGamesUsage.Summarize(games.Select(g => g.Usage).ToList(), now);
            uiSummary.Text = summary.IdleCount > 0
                ? Loc.F("{0} installed games take {1}. {2} were not played for 3 months: {3} can be freed.", summary.Count, StorageCleanup.FormatSize(summary.Size), summary.IdleCount, StorageCleanup.FormatSize(summary.IdleSize))
                : Loc.F("{0} installed games take {1}.", summary.Count, StorageCleanup.FormatSize(summary.Size));
            var sort = uiSort.SelectedIndex switch { 1 => InstalledGamesSort.LastPlayed, 2 => InstalledGamesSort.Title, _ => InstalledGamesSort.Size };
            var byId = games.ToDictionary(g => g.Usage.Id, g => g.Game);
            var shown = InstalledGamesUsage.Sort(games.Select(g => g.Usage), sort, now)
                .Where(u => uiIdleOnly.IsChecked != true || InstalledGamesUsage.IsIdle(u, now))
                .Select(u => new StorageGameRow(u, byId[u.Id], now))
                .ToList();
            uiGames.ItemsSource = shown;
            uiEmpty.Text = games.Count == 0 ? Loc.T("No game is installed.") : Loc.T("Every installed game was played lately.");
            uiEmpty.IsVisible = shown.Count == 0 && !uiLoading.IsVisible;
        }

        private void IdleOnly_Changed(object? sender, RoutedEventArgs e)
        {
            if (IsLoaded)
                Show();
        }

        private void Sort_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
                Show();
        }

        private void OpenFolder_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is StorageGameRow row)
                PlatformInfo.OpenFolder(row.Usage.Directory);
        }

        /// <summary>The usual uninstallation (with its confirmation and the uninstaller of setups), then back here.</summary>
        private async void Uninstall_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not StorageGameRow row)
                return;
            var settings = new GameSettingsUserControl(row.Game);
            MainWindowViewModel.Instance.OpenPopup(settings);
            await settings.UninstallGame();
            MainWindowViewModel.Instance.OpenPopup(new StorageUserControl());
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => MainWindowViewModel.Instance.ClosePopup();
    }

    /// <summary>A line of the list: an installed game, its size and its last session.</summary>
    internal sealed class StorageGameRow
    {
        public StorageGameRow(InstalledGameUsage usage, Game game, DateTime nowUtc)
        {
            Usage = usage;
            Game = game;
            IsIdle = InstalledGamesUsage.IsIdle(usage, nowUtc);
            string time = usage.MinutesPlayed > 0 ? (string)new GameTimeConverter().Convert(usage.MinutesPlayed, typeof(string), null, CultureInfo.CurrentCulture)! : "";
            PlayText = usage.LastPlayedUtc is DateTime last
                ? Loc.F("Played {0} · {1}", RelativeDate.Format(last), time)
                : Loc.F("Never started · installed {0}", RelativeDate.Format(usage.InstalledUtc));
        }

        public InstalledGameUsage Usage { get; }
        public Game Game { get; }
        public string Title => Usage.Title;
        public string SizeText => StorageCleanup.FormatSize(Usage.Size);
        public bool IsIdle { get; }
        public string PlayText { get; }
    }

    /// <summary>A disk of the library: what SanctuaryVault takes on it, what the rest takes, what is free.</summary>
    internal sealed class DriveRow
    {
        public string Name { get; init; } = "";
        public string FreeText { get; init; } = "";
        public string UsageText { get; init; } = "";
        /// <summary>Widths in the bar (the free room is what remains).</summary>
        public double OursWidth { get; init; }
        public double OthersWidth { get; init; }
        private const double BarWidth = 302;

        public static List<DriveRow> For(IEnumerable<InstalledGameUsage> games)
        {
            var rows = new List<DriveRow>();
            foreach (var group in games.GroupBy(g => DriveOf(g.Directory)).Where(g => g.Key != null))
            {
                try
                {
                    var drive = new DriveInfo(group.Key!);
                    long total = drive.TotalSize, free = drive.AvailableFreeSpace, ours = group.Sum(g => g.Size);
                    long others = Math.Max(0, total - free - ours);
                    rows.Add(new DriveRow
                    {
                        Name = drive.Name,
                        FreeText = Loc.F("{0} free of {1}", StorageCleanup.FormatSize(free), StorageCleanup.FormatSize(total)),
                        UsageText = Loc.F("Games: {0} · other files: {1}", StorageCleanup.FormatSize(ours), StorageCleanup.FormatSize(others)),
                        OursWidth = total > 0 ? Math.Max(2, BarWidth * ours / total) : 0,
                        OthersWidth = total > 0 ? BarWidth * others / total : 0,
                    });
                }
                catch (Exception ex) { Log.Ignored(ex); }
            }
            return rows;
        }

        /// <summary>The mount point (Linux) or drive (Windows) holding the folder.</summary>
        private static string? DriveOf(string directory)
        {
            try
            {
                string full = Path.GetFullPath(directory);
                return DriveInfo.GetDrives().Where(d => full.StartsWith(d.Name, StringComparison.Ordinal))
                    .OrderByDescending(d => d.Name.Length).FirstOrDefault()?.Name;
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return null;
            }
        }
    }
}
