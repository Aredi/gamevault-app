using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GameVault.Core;
using GameVault.Core.Storage;
using gamevault.Converter;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>The cloud saves the service keeps for a retro game (the current one first); one can be put back.</summary>
    public partial class RetroSaveHistoryUserControl : UserControl
    {
        private readonly RetroGame game;
        private readonly Action? changed;

        public RetroSaveHistoryUserControl() : this(new RetroGame { Platform = new RetroPlatform() }, null) { }

        internal RetroSaveHistoryUserControl(RetroGame game, Action? changed)
        {
            InitializeComponent();
            this.game = game;
            this.changed = changed;
            uiGame.Text = game.Name;
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

        internal IReadOnlyList<RetroSaveVersionRow> Rows => uiVersions.ItemsSource as IReadOnlyList<RetroSaveVersionRow> ?? Array.Empty<RetroSaveVersionRow>();

        internal async Task LoadAsync()
        {
            uiLoading.IsVisible = true;
            uiMessage.IsVisible = false;
            try
            {
                var versions = await RetroCloudSaves.HistoryAsync(game);
                uiVersions.ItemsSource = versions?.Select(v => new RetroSaveVersionRow(v)).ToList();
                if (versions == null || versions.Count == 0)
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

        private async void Restore_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not RetroSaveVersionRow row)
                return;
            var answer = await App.Instance.MainWindow.ShowMessageAsync(
                Loc.F("Restore the save of {0}?", row.Date),
                Loc.T("A copy of the current save is kept on this computer."),
                MessageDialogStyle.AffirmativeAndNegative,
                new MetroDialogSettings { AffirmativeButtonText = Loc.T("Restore"), NegativeButtonText = Loc.T("Cancel") });
            if (answer != MessageDialogResult.Affirmative)
                return;
            await RestoreAsync(row);
        }

        internal async Task RestoreAsync(RetroSaveVersionRow row)
        {
            IsEnabled = false;
            uiLoading.IsVisible = true;
            MainWindowViewModel.Instance.AppBarText = await RetroCloudSaves.RestoreAsync(game, row.Version.Saves);
            IsEnabled = true;
            changed?.Invoke();
            await LoadAsync();
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => MainWindowViewModel.Instance.ClosePopup();
    }

    internal sealed class RetroSaveVersionRow
    {
        public RetroSaveVersionRow(RetroCloudSaves.Version version)
        {
            Version = version;
            DateTime local = DateTimeOffset.FromUnixTimeMilliseconds(version.SavedAt).LocalDateTime;
            Date = local.ToString("f", CultureInfo.CurrentCulture);
            Details = version.Size > 0 ? $"{RelativeDate.Format(local)} · {StorageCleanup.FormatSize(version.Size)}" : RelativeDate.Format(local);
        }

        public RetroCloudSaves.Version Version { get; }
        public bool IsCurrent => Version.IsCurrent;
        public string Date { get; }
        public string Details { get; }
    }
}
