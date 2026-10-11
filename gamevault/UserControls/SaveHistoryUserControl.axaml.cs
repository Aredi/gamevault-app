using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GameVault.Core;
using GameVault.Core.Storage;
using gamevault.Converter;
using gamevault.Helper;
using gamevault.Helper.Integrations;
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
    /// <summary>The cloud saves the server keeps for a game (the newest first); an older one can be restored.</summary>
    public partial class SaveHistoryUserControl : UserControl
    {
        private readonly Game game;
        private readonly string installationDirectory;

        public SaveHistoryUserControl() : this(new Game { Title = "" }, "") { }

        internal SaveHistoryUserControl(Game game, string installationDirectory)
        {
            InitializeComponent();
            this.game = game;
            this.installationDirectory = installationDirectory;
            uiGame.Text = game.Metadata?.Title ?? game.Title;
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

        internal IReadOnlyList<SaveVersionRow> Rows => uiVersions.ItemsSource as IReadOnlyList<SaveVersionRow> ?? Array.Empty<SaveVersionRow>();

        internal async Task LoadAsync()
        {
            uiLoading.IsVisible = true;
            uiMessage.IsVisible = false;
            try
            {
                List<SaveVersion>? versions = await SaveHistoryService.ListAsync(game.ID);
                string thisComputer = InstallationIdOf(installationDirectory);
                uiVersions.ItemsSource = versions?.Select((v, i) => new SaveVersionRow(v, i == 0, v.InstallationId.Equals(thisComputer, StringComparison.OrdinalIgnoreCase))).ToList();
                if (versions == null)
                    Message(Loc.T("The save history is not available on this server."));
                else if (versions.Count == 0)
                    Message(Loc.T("No cloud save of this game yet. It is uploaded when the game is closed."));
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                Message(Loc.T("The SanctuaryVault service can not be reached for the moment. Try again later."));
            }
            uiLoading.IsVisible = false;
        }

        private void Message(string text)
        {
            uiMessage.Text = text;
            uiMessage.IsVisible = true;
        }

        private static string InstallationIdOf(string directory)
        {
            try { return Preferences.Get(AppConfigKey.InstallationId, Path.Combine(directory, "gamevault-exec")) ?? ""; }
            catch (Exception ex) { Log.Ignored(ex); return ""; }
        }

        private async void Restore_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not SaveVersionRow row)
                return;
            var answer = await App.Instance.MainWindow.ShowMessageAsync(
                Loc.F("Restore the save of {0}?", row.Date),
                Loc.T("A copy of the current save is kept on this computer."),
                MessageDialogStyle.AffirmativeAndNegative,
                new MetroDialogSettings { AffirmativeButtonText = Loc.T("Restore"), NegativeButtonText = Loc.T("Cancel") });
            if (answer != MessageDialogResult.Affirmative)
                return;
            IsEnabled = false;
            uiLoading.IsVisible = true;
            string status = await SaveGameHelper.Instance.RestoreVersion(game.ID, installationDirectory, row.Version.Name);
            MainWindowViewModel.Instance.AppBarText = status;
            IsEnabled = true;
            await LoadAsync();
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => MainWindowViewModel.Instance.ClosePopup();
    }

    internal sealed class SaveVersionRow
    {
        public SaveVersionRow(SaveVersion version, bool isCurrent, bool fromThisComputer)
        {
            Version = version;
            IsCurrent = isCurrent;
            DateTime local = version.SavedAtUtc.ToLocalTime();
            Date = local.ToString("f", CultureInfo.CurrentCulture);
            Details = string.Join(" · ", RelativeDate.Format(local), StorageCleanup.FormatSize(version.Size),
                fromThisComputer ? Loc.T("from this computer") : Loc.T("from another computer"));
        }

        public SaveVersion Version { get; }
        public bool IsCurrent { get; }
        public string Date { get; }
        public string Details { get; }
    }
}
