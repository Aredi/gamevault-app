using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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
        internal List<SocialRecommendation> Recommendations { get; private set; } = new();

        public ActivityUserControl()
        {
            InitializeComponent();
            // The lines as wide as the window lets them, up to a readable width
            uiScroll.SizeChanged += (_, e) => uiPage.Width = Math.Clamp(e.NewSize.Width - 80, 0, 760);
            // Fresh each time the page is opened (the page host attaches the page it shows)
            // A friend's recommendation, or a reaction on the player's line: shown at once
            SocialService.RecommendationReceived += received => { if (IsAttached) _ = LoadAsync(); };
            SocialService.ReactionReceived += (_, _, _) => { if (IsAttached) _ = LoadAsync(); };
            DetachedFromVisualTree += (_, _) => IsAttached = false;
            AttachedToVisualTree += (_, _) =>
            {
                IsAttached = true;
                Focus();
                uiLoading.IsActive = uiLoading.IsVisible = Items.Count == 0;
                _ = LoadAsync();
            };
        }

        /// <summary>The games played lately by all the players.</summary>
        private int loading;

        internal async Task LoadAsync()
        {
            // Each load makes its own list; a newer load wins (the page opened, then a reaction told)
            int mine = ++loading;
            var items = new List<ActivityFeedItem>();
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
                items = recent
                    .Select(p =>
                    {
                        if (games.TryGetValue(p.Game!.ID, out Game? whole))
                            p.Game = whole;
                        string game = p.Game!.Metadata?.Title ?? p.Game.Title;
                        string text = string.Equals(p.State, "COMPLETED", StringComparison.OrdinalIgnoreCase)
                            ? Loc.F("{0} completed {1}", p.User!.Username, game)
                            : Loc.F("{0} played {1}", p.User!.Username, game);
                        return new ActivityFeedItem(p.User, p.Game, text, Converter.RelativeDate.FormatMoment(p.LastPlayedAt!.Value), p.LastPlayedAt!.Value.ToUniversalTime())
                        {
                            Key = SocialService.ItemKey("pc", p.User.ID, p.Game.ID.ToString(), p.LastPlayedAt!.Value),
                            Owner = p.User.ID,
                        };
                    })
                    .ToList();
                // The retro games played lately (kept by the SanctuaryVault service), among them by date
                try
                {
                    var retro = (await RetroLibrary.ActivityAsync()).Select(a => new ActivityFeedItem(null, null,
                        Loc.F("{0} played {1}", a.Username, $"{a.Name} ({a.PlatformName})"), Converter.RelativeDate.FormatMoment(a.LastPlayed), a.LastPlayed.ToUniversalTime(),
                        RetroLibrary.GameOf(a.RomId, a.Platform, a.PlatformName, a.Name, a.Cover))
                    {
                        Key = SocialService.ItemKey("retro", a.UserId, $"{a.Platform}:{a.RomId}", a.LastPlayed),
                        Owner = a.UserId,
                    });
                    items = items.Concat(retro).OrderByDescending(i => i.At).Take(30).ToList();
                }
                catch (Exception ex) { Log.Ignored(ex); }
                // The achievements earned: one line per player, game and day (a session can earn a dozen)
                try
                {
                    var unlocks = (await RetroLibrary.UnlocksAsync())
                        .GroupBy(u => (u.UserId, u.Platform, u.RomId, u.Date.ToUniversalTime().Date))
                        .Select(g =>
                        {
                            var last = g.MaxBy(u => u.Date)!;
                            // The day of the line is a UTC one, as its key: the same on every PC, whatever its time zone
                            string game = $"{last.Name} ({last.PlatformName})";
                            string text = g.Count() == 1 ? Loc.F("{0} earned \"{1}\" in {2}", last.Username, last.Title, game)
                                : Loc.F("{0} earned {1} achievements in {2}", last.Username, g.Count(), game);
                            return new ActivityFeedItem(null, null, text, Converter.RelativeDate.FormatMoment(last.Date), last.Date.ToUniversalTime(),
                                RetroLibrary.GameOf(last.RomId, last.Platform, last.PlatformName, last.Name, last.Cover))
                            {
                                Key = SocialService.ItemKey("ach", last.UserId, $"{last.Platform}:{last.RomId}", last.Date),
                                Owner = last.UserId,
                            };
                        });
                    items = items.Concat(unlocks).OrderByDescending(i => i.At).Take(30).ToList();
                }
                catch (Exception ex) { Log.Ignored(ex); }
            }
            catch (Exception ex) { Log.Ignored(ex); }
            // The reactions on these lines, and the games the friends recommend
            var reactions = await SocialService.ReactionsAsync(items.Where(i => i.Key != null).Select(i => i.Key!));
            int me = LoginManager.Instance.GetCurrentUser()?.ID ?? 0;
            foreach (ActivityFeedItem item in items)
                if (item.Key != null && reactions.TryGetValue(item.Key, out var on))
                    item.SetReactions(on, me);
            var recommendations = await SocialService.MyRecommendationsAsync();
            if (mine != loading)
                return;
            Items = items;
            Recommendations = recommendations;
            uiRecommendations.ItemsSource = Recommendations;
            uiRecommendationsSection.IsVisible = Recommendations.Count > 0;
            uiItems.ItemsSource = Items;
            uiEmpty.IsVisible = Items.Count == 0;
            uiLoading.IsActive = uiLoading.IsVisible = false;
        }

        private bool IsAttached;

        /// <summary>A chip "🔥 2": the player's reaction changed to it, or taken away when it was theirs.</summary>
        private async void Reaction_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control control && control.DataContext is ReactionChip chip && LineOf(control) is ActivityFeedItem item)
                await ReactAsync(item, chip.Emoji);
        }

        /// <summary>The emojis offered last (the tests look at it).</summary>
        internal Flyout? Picker { get; private set; }

        /// <summary>"Réagir": the emojis offered in a row, the player's own lit (picked again: taken away).</summary>
        private void AddReaction_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || LineOf(button) is not ActivityFeedItem item)
                return;
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 2 };
            var flyout = Picker = new Flyout { Content = row, Placement = PlacementMode.BottomEdgeAlignedRight };
            foreach (string emoji in SocialService.Emojis)
            {
                var choice = new Button { Content = emoji };
                choice.Classes.Add("pick");
                if (emoji.All(char.IsAsciiLetterOrDigit))
                    choice.Classes.Add("text");
                if (item.MyEmoji == emoji)
                    choice.Classes.Add("mine");
                choice.Click += async (_, _) =>
                {
                    flyout.Hide();
                    await ReactAsync(item, emoji);
                };
                row.Children.Add(choice);
            }
            flyout.ShowAt(button);
        }

        internal static async Task ReactAsync(ActivityFeedItem item, string emoji)
        {
            if (!item.CanReact)
                return;
            try { item.SetReactions(await SocialService.ReactAsync(item.Key!, item.MyEmoji == emoji ? null : emoji), LoginManager.Instance.GetCurrentUser()?.ID ?? 0); }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
        }

        private static ActivityFeedItem? LineOf(Control control) =>
            control.GetVisualAncestors().OfType<Control>().Prepend(control).Select(c => c.DataContext).OfType<ActivityFeedItem>().FirstOrDefault();

        private async void Recommendation_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is SocialRecommendation recommendation)
                await OpenAsync(recommendation.Game);
        }

        private async void Dismiss_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not SocialRecommendation recommendation)
                return;
            try
            {
                await SocialService.DismissAsync(recommendation);
                Recommendations = Recommendations.Where(r => r != recommendation).ToList();
                uiRecommendations.ItemsSource = Recommendations;
                uiRecommendationsSection.IsVisible = Recommendations.Count > 0;
            }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
        }

        /// <summary>The game's page: to play it, or to install it.</summary>
        internal static async Task OpenAsync(ChatGame game)
        {
            if (game.IsRetro)
                await MainWindowViewModel.Instance.Retro.ShowGameAsync(game.Platform ?? "", game.Id);
            else
                MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(new Game { ID = game.Id }));
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
