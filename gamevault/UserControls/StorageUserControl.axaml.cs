using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GameVault.Core;
using GameVault.Core.Storage;
using gamevault.Converter;
using gamevault.Helper;
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
        private List<(InstalledGameUsage Usage, Game? Game, RetroGame? Retro)> games = new();
        /// <summary>RetroArch and its cores, BIOS (not a game: never "not played for a while").</summary>
        private InstalledGameUsage? emulator;
        /// <summary>The retro games checked for deletion, by folder: kept when the list is sorted or filtered again.</summary>
        private readonly HashSet<string> selected = new();

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
            await RetroLibrary.LoadAsync();
            games = await Task.Run(() =>
            {
                static (long Size, DateTime InstalledAt) Measure(string folder)
                {
                    try { return (StorageCleanup.DirectorySize(folder), Directory.GetCreationTimeUtc(folder)); }
                    catch (Exception ex) { Log.Ignored(ex); return (0, DateTime.UtcNow); }
                }
                var all = installed.Select(install =>
                {
                    records.TryGetValue(install.Key.ID, out var record);
                    var (size, installedAt) = Measure(install.Value);
                    var usage = new InstalledGameUsage(install.Key.ID, install.Key.Metadata?.Title ?? install.Key.Title ?? "", install.Value, size,
                        record?.MinutesPlayed ?? 0, record?.LastPlayedAt?.ToUniversalTime(), installedAt);
                    return (usage, (Game?)install.Key, (RetroGame?)null);
                }).ToList();
                // The retro games too: negative ids, apart from the server's games
                int retroId = 0;
                foreach (RetroGame retro in RetroLibrary.InstalledGames())
                {
                    if (RetroService.InstalledFolder(retro) is not string folder)
                        continue;
                    var (size, installedAt) = Measure(folder);
                    all.Add((new InstalledGameUsage(--retroId, retro.Name, folder, size, retro.MinutesPlayed, RetroLibrary.LastPlayedOf(retro), installedAt), null, retro));
                }
                return all;
            });
            long emulatorSize = await Task.Run(RetroArchManager.InstalledSize);
            emulator = emulatorSize > 0 ? new InstalledGameUsage(int.MinValue, string.Join(" · ", StandaloneEmulator.All.Where(e => e.Layout.IsInstalled).Select(e => e.Name).Prepend("RetroArch")), RetroArchManager.Layout.Directory, emulatorSize, 0, null, DateTime.UtcNow) : null;
            uiDrives.ItemsSource = DriveRow.For(games.Select(g => g.Usage).Concat(emulator != null ? new[] { emulator } : Array.Empty<InstalledGameUsage>()));
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
            var byId = games.ToDictionary(g => g.Usage.Id);
            var shown = InstalledGamesUsage.Sort(games.Select(g => g.Usage), sort, now)
                .Where(u => uiIdleOnly.IsChecked != true || InstalledGamesUsage.IsIdle(u, now))
                .Where(u => uiRetroOnly.IsChecked != true || byId[u.Id].Retro != null)
                .Select(u => new StorageGameRow(u, byId[u.Id].Game, now, byId[u.Id].Retro) { IsSelected = selected.Contains(u.Directory) })
                .ToList();
            if (emulator != null && uiIdleOnly.IsChecked != true)
                shown.Add(new StorageGameRow(emulator, null, now, isEmulator: true));
            uiGames.ItemsSource = shown;
            uiEmpty.Text = games.Count == 0 ? Loc.T("No game is installed.") : Loc.T("Every installed game was played lately.");
            uiEmpty.IsVisible = shown.Count == 0 && !uiLoading.IsVisible;
            ShowSelection();
        }

        /// <summary>The button of the checked retro games: how many, how much room.</summary>
        private void ShowSelection()
        {
            selected.IntersectWith(games.Where(g => g.Retro != null).Select(g => g.Usage.Directory));
            long size = games.Where(g => selected.Contains(g.Usage.Directory)).Sum(g => g.Usage.Size);
            uiDeleteSelected.IsVisible = selected.Count > 0;
            uiDeleteSelectedText.Text = Loc.F("Delete the {0} selected retro games ({1})", selected.Count, StorageCleanup.FormatSize(size));
        }

        private void Selected_Changed(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is StorageGameRow row)
                Select(row, row.IsSelected);
        }

        internal void Select(StorageGameRow row, bool on)
        {
            if (row.Retro == null)
                return;
            row.IsSelected = on;
            if (on)
                selected.Add(row.Usage.Directory);
            else
                selected.Remove(row.Usage.Directory);
            ShowSelection();
        }

        private List<(InstalledGameUsage Usage, Game? Game, RetroGame? Retro)> Chosen => games.Where(g => g.Retro != null && selected.Contains(g.Usage.Directory)).ToList();

        private async void DeleteSelected_Click(object? sender, RoutedEventArgs e)
        {
            var chosen = Chosen;
            if (chosen.Count > 0 && await DialogService.ConfirmAsync(Loc.F("Delete {0} retro games from this PC ({1})? Their saves are kept.", chosen.Count, StorageCleanup.FormatSize(chosen.Sum(g => g.Usage.Size))), Loc.T("Storage")))
                await DeleteSelectedAsync();
        }

        /// <summary>The checked retro games deleted (their saves stay), then the list again.</summary>
        internal async Task DeleteSelectedAsync()
        {
            foreach (var game in Chosen)
            {
                try { await Task.Run(() => Directory.Delete(game.Usage.Directory, true)); }
                catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = ex.Message; }
            }
            selected.Clear();
            await LoadAsync();
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
            if (row.Retro != null || row.IsEmulator)
            {
                string question = row.IsEmulator
                    ? Loc.T("Uninstall the emulators, the BIOS and the firmware? The saves are kept.")
                    : Loc.F("Delete '{0}' from this PC? Its saves are kept.", row.Title);
                if (!await DialogService.ConfirmAsync(question, Loc.T("Storage")))
                    return;
                try
                {
                    if (row.IsEmulator)
                        await RetroArchManager.UninstallAsync();
                    else
                        Directory.Delete(row.Usage.Directory, true);
                }
                catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = ex.Message; }
                await LoadAsync();
                return;
            }
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
        public StorageGameRow(InstalledGameUsage usage, Game? game, DateTime nowUtc, RetroGame? retro = null, bool isEmulator = false)
        {
            Usage = usage;
            Game = game;
            Retro = retro;
            IsEmulator = isEmulator;
            IsIdle = !isEmulator && InstalledGamesUsage.IsIdle(usage, nowUtc);
            string time = usage.MinutesPlayed > 0 ? (string)new GameTimeConverter().Convert(usage.MinutesPlayed, typeof(string), null, CultureInfo.CurrentCulture)! : "";
            PlayText = isEmulator ? Loc.T("The emulators of the retro games, their files and the BIOS (the saves are kept)")
                : usage.LastPlayedUtc is DateTime last
                ? Loc.F("Played {0} · {1}", RelativeDate.Format(last), time)
                : Loc.F("Never started · installed {0}", RelativeDate.Format(usage.InstalledUtc));
            if (retro != null)
                PlayText = $"{retro.Platform.Name} · {PlayText}";
        }

        public InstalledGameUsage Usage { get; }
        /// <summary>Checked for deletion (the retro games).</summary>
        public bool IsSelected { get; set; }
        public Game? Game { get; }
        public RetroGame? Retro { get; }
        public bool IsEmulator { get; }
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
                // "/mnt/pool" must not hold "/mnt/pool2/games"
                return PlatformInfo.GetDriveForPath(directory)?.Name;
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return null;
            }
        }
    }
}
