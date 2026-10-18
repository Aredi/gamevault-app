using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GameVault.Core;
using GameVault.Core.Publishing;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace gamevault.UserControls
{
    /// <summary>The programs of an installed game, most likely first: the player picks the one Play starts.</summary>
    public partial class ChooseExecutableUserControl : UserControl
    {
        private readonly Game game;
        private readonly string installationDirectory;

        public ChooseExecutableUserControl() : this(new Game { Title = "" }, "") { }

        internal ChooseExecutableUserControl(Game game, string installationDirectory)
        {
            InitializeComponent();
            this.game = game;
            this.installationDirectory = installationDirectory;
            uiGame.Text = game.Metadata?.Title ?? game.Title;
            uiExecutables.ItemsSource = Load();
            uiMessage.IsVisible = Rows.Count == 0;
            Loaded += (_, _) => Focus();
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    MainWindowViewModel.Instance.ClosePopup();
                }
            };
        }

        internal IReadOnlyList<ExecutableRow> Rows => uiExecutables.ItemsSource as IReadOnlyList<ExecutableRow> ?? Array.Empty<ExecutableRow>();

        private string SettingsFile => Path.Combine(installationDirectory, "gamevault-exec");

        private List<ExecutableRow> Load()
        {
            try
            {
                string current = Preferences.Get(AppConfigKey.Executable, SettingsFile) ?? "";
                string title = game.Metadata?.Title ?? game.Title ?? "";
                var ignored = SettingsViewModel.Instance.IgnoreList ?? Array.Empty<string>();
                var ranked = ExecutableFinder.Rank(installationDirectory, title, ignored)
                    .Concat(ExecutableFinder.Rank(installationDirectory, title, ignored, linux: true))
                    .OrderByDescending(c => c.Score).ThenByDescending(c => c.Size).Take(25).ToList();
                var recommended = ranked.FirstOrDefault(c => c.Score > -50);
                var rows = ranked.Select(c => new ExecutableRow(Path.GetFullPath(Path.Combine(installationDirectory, c.RelativePath)), c.RelativePath, c.Size, c == recommended)).ToList();
                // A program chosen before that the ranking does not list (a script): shown too
                if (File.Exists(current) && !rows.Any(r => r.Path == current))
                    rows.Insert(0, new ExecutableRow(current, Path.GetRelativePath(installationDirectory, current).Replace('\\', '/'), new FileInfo(current).Length, false));
                foreach (var row in rows)
                    row.IsCurrent = row.Path == current;
                return rows;
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return new List<ExecutableRow>();
            }
        }

        private async void Play_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is ExecutableRow row)
                await PlayWith(row);
        }

        internal async System.Threading.Tasks.Task PlayWith(ExecutableRow row)
        {
            try
            {
                if (!File.Exists(SettingsFile))
                    File.Create(SettingsFile).Close();
                Preferences.Set(AppConfigKey.Executable, row.Path, SettingsFile);
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = ex.Message;
                return;
            }
            MainWindowViewModel.Instance.ClosePopup();
            MainWindowViewModel.Instance.AppBarText = Loc.F("{0} is now started by Play", row.Name);
            await InstallUserControl.PlayGame(game.ID);
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => MainWindowViewModel.Instance.ClosePopup();
    }

    internal sealed class ExecutableRow
    {
        public ExecutableRow(string path, string relativePath, long size, bool isRecommended)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path);
            string folder = System.IO.Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? "";
            Details = string.Join(" · ", folder == "" ? Loc.T("in the main folder") : folder, new gamevault.Converter.GameSizeConverter().Convert(size, typeof(string), null, System.Globalization.CultureInfo.CurrentCulture));
            IsRecommended = isRecommended;
        }

        public string Path { get; }
        public string Name { get; }
        public string Details { get; }
        public bool IsRecommended { get; }
        public bool IsCurrent { get; set; }
    }
}
