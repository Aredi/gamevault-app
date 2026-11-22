using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.UserControls.Mobile
{
    /// <summary>The phone's home: the game to resume in large, the ones played lately, the challenge, the newest games, the friends.</summary>
    public partial class MobileHomeUserControl : UserControl
    {
        private RetroGame? hero;

        public MobileHomeUserControl()
        {
            InitializeComponent();
            DataContext = MainWindowViewModel.Instance;
            AttachedToVisualTree += (_, _) => _ = RefreshAsync();
            RetroLibrary.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (IsVisible) _ = RefreshAsync(); });
        }

        private bool refreshing;

        internal async Task RefreshAsync()
        {
            if (refreshing)
                return;
            refreshing = true;
            try
            {
                int hour = DateTime.Now.Hour;
                uiGreeting.Text = hour < 5 || hour >= 18 ? Loc.T("Good evening") : Loc.T("Hello");
                List<RetroGame> all = new();
                try { all = await MainWindowViewModel.Instance.Retro.AllGamesAsync(); }
                catch (Exception ex) { Log.Ignored(ex); }
                var byKey = all.GroupBy(RetroLibrary.Key).ToDictionary(g => g.Key, g => g.First());
                var recent = RetroLibrary.Recent(14).Select(g => byKey.GetValueOrDefault(RetroLibrary.Key(g)) ?? g).Where(g => !g.IsHidden).ToList();
                ShowHero(recent.FirstOrDefault());
                Row(uiContinue, recent.Skip(1).Take(12), uiContinueHeader, uiContinueScroll);
                var added = MainWindowViewModel.Instance.Retro.RecentlyAdded;
                Row(uiLatest, all.Where(g => added.ContainsKey(g.Id) && !g.IsHidden).OrderByDescending(g => added[g.Id]).Take(12), uiLatestHeader, uiLatestScroll);
                ShowFriends();
                await ShowChallengeAsync();
            }
            finally { refreshing = false; }
        }

        private void ShowHero(RetroGame? game)
        {
            if (hero != null)
                hero.PropertyChanged -= Hero_PropertyChanged;
            hero = game;
            uiHero.IsVisible = game != null;
            uiStart.IsVisible = game == null;
            if (game == null)
                return;
            game.PropertyChanged += Hero_PropertyChanged;
            uiHeroEyebrow.Text = (Loc.T("Resume") + " · " + game.Platform.Name).ToUpperInvariant();
            uiHeroTitle.Text = game.Name;
            uiHeroInfo.Text = game.ContinueInfo;
            ShowHeroState();
            uiHeroImage.Source = game.Banner;
            // Its screenshot when it has one, else its cover
            _ = LoadBannerAsync(game);
        }

        private async Task LoadBannerAsync(RetroGame game)
        {
            try
            {
                await game.LoadScreenshotsAsync();
                if (hero == game)
                    uiHeroImage.Source = game.Banner;
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private void Hero_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(RetroGame.Status) or nameof(RetroGame.Installed) or nameof(RetroGame.Busy) or nameof(RetroGame.CoverImage) or nameof(RetroGame.Banner))
                Avalonia.Threading.Dispatcher.UIThread.Post(ShowHeroState);
        }

        private void ShowHeroState()
        {
            if (hero == null)
                return;
            uiHeroPlayText.Text = hero.Installed ? Loc.T("Resume") : Loc.T("Install and play");
            uiHeroPlay.IsEnabled = !hero.Busy;
            uiHeroStatus.Text = hero.Status;
            uiHeroStatus.IsVisible = !string.IsNullOrEmpty(hero.Status);
            if (uiHeroImage.Source == null)
                uiHeroImage.Source = hero.Banner;
        }

        private static void Row(ItemsControl row, IEnumerable<RetroGame> games, Control header, Control scroll)
        {
            var cards = games.Select(g => MobileCards.Cover(g, 116, RetroUserControl.OpenGame)).ToList();
            row.ItemsSource = cards;
            header.IsVisible = scroll.IsVisible = cards.Count > 0;
        }

        private void ShowFriends()
        {
            var friends = Windows.MainView.Current?.ChatFriends.Where(f => f.Rank < 3).Take(12).ToList() ?? new();
            uiFriends.ItemsSource = friends.Select(Friend).ToList();
            uiFriendsHeader.IsVisible = uiFriendsScroll.IsVisible = friends.Count > 0;
        }

        /// <summary>A friend online: their avatar in a ring (lit while they play), their name, what they do.</summary>
        private static Control Friend(ChatEntry entry)
        {
            var avatar = new CacheImage { ImageCacheType = Models.ImageCache.UserAvatar, Stretch = Stretch.UniformToFill, Data = entry.User };
            var ring = new Border
            {
                Width = 62, Height = 62, CornerRadius = new CornerRadius(31), Padding = new Thickness(3),
                Background = entry.Rank == 0 ? new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#7C73E6"), 0), new GradientStop(Color.Parse("#3DD68C"), 1) },
                } : entry.Dot ?? Brushes.Gray,
                Child = new Border { CornerRadius = new CornerRadius(28), ClipToBounds = true, BorderThickness = new Thickness(2), Child = avatar },
            };
            ((Border)ring.Child).Bind(Border.BorderBrushProperty, ring.GetResourceObservable("Brush.Background"));
            var card = new StackPanel
            {
                Width = 72, Spacing = 4,
                Children =
                {
                    ring,
                    new TextBlock { Text = entry.Title, FontSize = 12.5, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = entry.Subtitle, FontSize = 11, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 72 },
                },
            };
            ring.HorizontalAlignment = HorizontalAlignment.Center;
            return MobileCards.Pressable(card, () => Windows.MainView.Current?.ShowSocial(entry), null);
        }

        private async Task ShowChallengeAsync()
        {
            ChallengeState? state = null;
            try { state = await ChallengesService.GetAsync(); }
            catch (Exception ex) { Log.Ignored(ex); }
            uiChallenge.IsVisible = uiChallengeHeader.IsVisible = state != null;
            if (state == null)
                return;
            int me = LoginManager.Instance.GetCurrentUser()?.ID ?? -1;
            int mine = state.Board.FirstOrDefault(b => b.UserId == me)?.Value ?? 0;
            int place = state.Board.FindIndex(b => b.UserId == me) + 1;
            int days = Math.Max(0, (int)Math.Ceiling((state.EndsAt - DateTime.UtcNow).TotalDays));
            uiChallengeText.Text = state.Challenge.Text;
            var parts = new List<string> { Loc.F("{0} of {1}", state.Challenge.Amount(mine), state.Challenge.Amount(state.Challenge.Goal)) };
            if (place > 0)
                parts.Add(Loc.F("#{0}", place));
            parts.Add(Loc.F("Ends in {0} days", days));
            uiChallengeMine.Text = string.Join(" · ", parts);
            uiChallengeBar.Value = state.Challenge.Goal > 0 ? Math.Min(1, (double)mine / state.Challenge.Goal) : 0;
        }

        private async void HeroPlay_Click(object? sender, RoutedEventArgs e)
        {
            if (hero == null)
                return;
            if (hero.Installed)
                await RetroPlay.PlayAsync(hero);
            else
                await RetroPlay.InstallAsync(hero, playWhenDone: true);
        }

        private void HeroOpen_Click(object? sender, RoutedEventArgs e)
        {
            if (hero != null)
                RetroUserControl.OpenGame(hero);
        }

        private void Hero_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            // The buttons have their own taps
            if (hero != null && (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) == null)
                RetroUserControl.OpenGame(hero);
        }

        private void Search_Click(object? sender, RoutedEventArgs e) => Windows.MainView.Current?.OpenQuickSearch();
        private void News_Click(object? sender, RoutedEventArgs e) => Windows.MainView.Current?.OpenNews();
        private void Games_Click(object? sender, RoutedEventArgs e) => Windows.MainView.Current?.ShowMobileTab(Windows.MobileTab.Games);
        private void Challenges_Click(object? sender, RoutedEventArgs e) => Windows.MainView.Current?.ShowMobileTab(Windows.MobileTab.Challenges);
        private void Challenge_PointerReleased(object? sender, PointerReleasedEventArgs e) => Windows.MainView.Current?.ShowMobileTab(Windows.MobileTab.Challenges);
        private void Social_Click(object? sender, RoutedEventArgs e) => Windows.MainView.Current?.ShowMobileTab(Windows.MobileTab.Social);
    }
}
