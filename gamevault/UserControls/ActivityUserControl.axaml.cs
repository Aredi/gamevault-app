using Avalonia.Controls;
using Avalonia.Interactivity;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    public partial class ActivityUserControl : UserControl
    {
        internal List<ActivityFeedItem> Items { get; private set; } = new();

        public ActivityUserControl()
        {
            InitializeComponent();
            // Fresh each time the page is opened (the page host attaches the page it shows)
            AttachedToVisualTree += (_, _) =>
            {
                Focus();
                uiLoading.IsActive = uiLoading.IsVisible = Items.Count == 0;
                _ = LoadAsync();
            };
        }

        /// <summary>The games played lately by all the players.</summary>
        internal async Task LoadAsync()
        {
            try
            {
                string json = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/progresses?sortBy=updated_at:DESC&limit=30");
                Progress[] progresses = JsonSerializer.Deserialize<PaginatedData<Progress>>(json)?.Data ?? Array.Empty<Progress>();
                var recent = progresses
                    .Where(p => p.Game != null && p.User != null && p.LastPlayedAt != null && p.Game.DeletedAt == null && p.User.DeletedAt == null)
                    .OrderByDescending(p => p.LastPlayedAt)
                    .Take(30)
                    .ToList();
                // The progresses bring their game without its metadata (no cover): the games are asked for whole
                var games = new Dictionary<int, Game>();
                string ids = string.Join(",", recent.Select(p => p.Game!.ID).Distinct());
                if (ids.Length > 0)
                {
                    try
                    {
                        string gamesJson = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games?filter.id=$in:{ids}&limit=-1");
                        foreach (Game game in JsonSerializer.Deserialize<PaginatedData<Game>>(gamesJson)?.Data ?? Array.Empty<Game>())
                            games[game.ID] = game;
                    }
                    catch (Exception ex) { Log.Ignored(ex); }
                }
                Items = recent
                    .Select(p =>
                    {
                        if (games.TryGetValue(p.Game!.ID, out Game? whole))
                            p.Game = whole;
                        string game = p.Game!.Metadata?.Title ?? p.Game.Title;
                        string text = string.Equals(p.State, "COMPLETED", StringComparison.OrdinalIgnoreCase)
                            ? Loc.F("{0} completed {1}", p.User!.Username, game)
                            : Loc.F("{0} played {1}", p.User!.Username, game);
                        return new ActivityFeedItem(p.User, p.Game, text, Converter.RelativeDate.Format(p.LastPlayedAt!.Value), p.LastPlayedAt!.Value.ToUniversalTime());
                    })
                    .ToList();
                // The retro games played lately (kept by the SanctuaryVault service), among them by date
                try
                {
                    var retro = (await RetroLibrary.ActivityAsync()).Select(a => new ActivityFeedItem(null, null,
                        Loc.F("{0} played {1}", a.Username, $"{a.Name} ({a.PlatformName})"), Converter.RelativeDate.Format(a.LastPlayed), a.LastPlayed.ToUniversalTime(),
                        RetroLibrary.GameOf(a.RomId, a.Platform, a.PlatformName, a.Name, a.Cover)));
                    Items = Items.Concat(retro).OrderByDescending(i => i.At).Take(30).ToList();
                }
                catch (Exception ex) { Log.Ignored(ex); }
            }
            catch (Exception ex) { Log.Ignored(ex); }
            uiItems.ItemsSource = Items;
            uiEmpty.IsVisible = Items.Count == 0;
            uiLoading.IsActive = uiLoading.IsVisible = false;
        }

        private void Item_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not ActivityFeedItem item)
                return;
            if (item.Retro is Helper.RetroGame retro)
                _ = MainWindowViewModel.Instance.Retro.ShowGameAsync(retro.Platform.Slug, retro.Id);
            else if (item.Game != null)
                MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(item.Game));
        }
    }
}
