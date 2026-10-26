using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>One game the providers know, with its cover once it is downloaded.</summary>
    internal sealed class RetroMatchRow : ViewModelBase
    {
        public RetroMatchRow(RommAccount.MatchCandidate game, bool current)
        {
            Game = game;
            Details = string.Join(" · ", new[] { game.IgdbId is string igdb ? $"IGDB {igdb}" : game.Source, current ? Loc.T("current") : null }.Where(t => t != null));
        }

        public RommAccount.MatchCandidate Game { get; }
        public string Details { get; }
        private Bitmap? image;
        public Bitmap? Image { get => image; set { image = value; OnPropertyChanged(); } }
    }

    /// <summary>An admin matches a retro game to the right game of IGDB, ScreenScraper...: RomM takes its details from it.</summary>
    public partial class RetroMatchPopup : UserControl
    {
        private readonly RetroGame game;
        private string? currentIgdb;

        public RetroMatchPopup() : this(new RetroGame()) { }

        internal RetroMatchPopup(RetroGame game)
        {
            InitializeComponent();
            this.game = game;
            uiFile.Text = Loc.F("File: {0}", game.FileName);
            uiCurrent.Text = Loc.F("Matched to: {0}", game.Name);
            uiTerm.Text = game.Name;
            AttachedToVisualTree += async (_, _) =>
            {
                try
                {
                    JsonElement rom = await RommAccount.RomAsync(game.Id);
                    currentIgdb = rom.TryGetProperty("igdb_id", out JsonElement igdb) && igdb.ValueKind == JsonValueKind.Number ? igdb.ToString() : null;
                }
                catch (Exception ex) { Log.Ignored(ex); }
                await SearchAsync(game.Name, byId: false);
            };
        }

        internal IReadOnlyList<RetroMatchRow> Rows => uiResults.ItemsSource as IReadOnlyList<RetroMatchRow> ?? Array.Empty<RetroMatchRow>();

        private void Close_Click(object? sender, RoutedEventArgs e) => MainWindowViewModel.Instance.ClosePopup();

        private async void Search_Click(object? sender, RoutedEventArgs e) => await SearchAsync(uiTerm.Text?.Trim() ?? "", uiByName.IsChecked != true);

        private async void Term_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                await SearchAsync(uiTerm.Text?.Trim() ?? "", uiByName.IsChecked != true);
        }

        internal async Task SearchAsync(string term, bool byId)
        {
            if (term.Length == 0 || (byId && !int.TryParse(term, out _)))
            {
                uiStatus.Text = byId ? Loc.T("An IGDB id is a number (in the address of the game's page on igdb.com).") : Loc.T("Enter the name of the game.");
                return;
            }
            uiSearch.IsEnabled = false;
            uiStatus.Text = Loc.T("Searching...");
            try
            {
                var rows = (await RommAccount.SearchMatchesAsync(game.Id, term, byId)).Select(c => new RetroMatchRow(c, c.IgdbId != null && c.IgdbId == currentIgdb)).ToList();
                uiResults.ItemsSource = rows;
                uiStatus.Text = rows.Count == 0 ? Loc.T("No game found: try another name, or its IGDB id.") : "";
                foreach (RetroMatchRow row in rows.Where(r => r.Game.Cover != null))
                    _ = LoadCoverAsync(row);
            }
            catch (Exception ex) { uiStatus.Text = WebExceptionHelper.TryGetServerMessage(ex); }
            finally { uiSearch.IsEnabled = true; }
        }

        private static async Task LoadCoverAsync(RetroMatchRow row)
        {
            try
            {
                byte[] picture = await HttpClients.Shared.GetByteArrayAsync(row.Game.Cover);
                row.Image = await Task.Run(() => Bitmap.DecodeToWidth(new MemoryStream(picture), 120));
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private async void Match_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is RetroMatchRow row)
                await MatchAsync(row);
        }

        internal async Task MatchAsync(RetroMatchRow row)
        {
            IsEnabled = false;
            uiStatus.Text = Loc.T("RomM takes the game's details again...");
            try
            {
                game.Rematched(await RommAccount.MatchAsync(game.Id, row.Game));
                MainWindowViewModel.Instance.ClosePopup();
                MainWindowViewModel.Instance.AppBarText = Loc.F("'{0}' is now matched to {1}. The other players see it within 5 minutes.", game.FileName, game.Name);
            }
            catch (Exception ex) { uiStatus.Text = WebExceptionHelper.TryGetServerMessage(ex); }
            finally { IsEnabled = true; }
        }
    }
}
