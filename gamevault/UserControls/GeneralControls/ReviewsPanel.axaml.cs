using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using gamevault.Converter;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    internal sealed record ReviewRow(SocialReview Review, bool CanRemove)
    {
        public string Name => Review.Username;
        public string Stars => new string('★', Review.Stars) + new string('☆', 5 - Review.Stars);
        public string When => RelativeDate.Format(Review.At);
        public string? Text => Review.Text;
    }

    /// <summary>A game's reviews by the players of the server; not shown when the service keeps none (an older one, offline).</summary>
    public partial class ReviewsPanel : UserControl
    {
        private string? game;
        private int stars;
        private int load;

        public ReviewsPanel()
        {
            InitializeComponent();
        }

        /// <summary>"pc:793", "retro:psx:2161".</summary>
        public string? Game
        {
            get => game;
            set
            {
                game = value;
                _ = LoadAsync();
            }
        }

        private static int Me => LoginManager.Instance.GetCurrentUser()?.ID ?? 0;
        private static bool IsAdmin => LoginManager.Instance.GetCurrentUser()?.Role == PERMISSION_ROLE.ADMIN;

        internal async Task LoadAsync()
        {
            int mine = ++load;
            if (game is not string key)
                return;
            List<SocialReview>? reviews = await SocialService.ReviewsAsync(key);
            if (mine != load)
                return;
            IsVisible = reviews != null;
            if (reviews == null)
                return;
            SocialReview? own = reviews.FirstOrDefault(r => r.UserId == Me);
            var others = reviews.Where(r => r.UserId != Me).ToList();
            uiReviews.ItemsSource = others.Select(r => new ReviewRow(r, IsAdmin)).ToList();
            uiSummary.Text = reviews.Count == 0 ? Loc.T("No review yet")
                : Loc.F(reviews.Count == 1 ? "★ {0} · {1} review" : "★ {0} · {1} reviews", reviews.Average(r => r.Stars).ToString("0.0", CultureInfo.CurrentCulture), reviews.Count);
            SetStars(own?.Stars ?? 0);
            uiText.Text = own?.Text ?? "";
            uiRemove.IsVisible = own != null;
            uiPublish.Content = Loc.T(own != null ? "Update my review" : "Publish");
        }

        private void SetStars(int count)
        {
            stars = count;
            foreach (ToggleButton star in uiStars.Children.OfType<ToggleButton>())
                star.IsChecked = int.Parse((string)star.Tag!) <= count;
            uiPublish.IsEnabled = count > 0;
        }

        private void Star_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton { Tag: string tag })
                SetStars(int.Parse(tag));
        }

        private async void Publish_Click(object? sender, RoutedEventArgs e) => await PublishAsync();

        internal async Task PublishAsync()
        {
            if (game == null || stars == 0)
                return;
            uiPublish.IsEnabled = false;
            try
            {
                await SocialService.SetReviewAsync(game, stars, uiText.Text?.Trim() is { Length: > 0 } text ? text : null);
                uiStatus.Text = Loc.T("Your review is published.");
                await LoadAsync();
            }
            catch (Exception ex) { uiStatus.Text = WebExceptionHelper.TryGetServerMessage(ex); }
            finally { uiPublish.IsEnabled = stars > 0; }
        }

        private async void Remove_Click(object? sender, RoutedEventArgs e)
        {
            if (game == null)
                return;
            try
            {
                await SocialService.RemoveReviewAsync(game);
                uiStatus.Text = "";
                await LoadAsync();
            }
            catch (Exception ex) { uiStatus.Text = WebExceptionHelper.TryGetServerMessage(ex); }
        }

        /// <summary>An administrator takes away another player's review.</summary>
        private async void RemoveOther_Click(object? sender, RoutedEventArgs e)
        {
            if (game == null || (sender as Control)?.DataContext is not ReviewRow row)
                return;
            try
            {
                await SocialService.RemoveReviewAsync(game, row.Review.UserId);
                await LoadAsync();
            }
            catch (Exception ex) { uiStatus.Text = WebExceptionHelper.TryGetServerMessage(ex); }
        }
    }
}
