using gamevault.Models;
using gamevault.Localization;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace gamevault.ViewModels
{
    internal class GameViewViewModel : ViewModelBase
    {
        #region PrivateMembers
        private Game? game { get; set; }
        private Progress? currentUserProgress { get; set; }
        private Progress[]? userProgresses { get; set; }
        private Dictionary<string, string> gameStates { get; set; }
        private bool isInstalled { get; set; }
        private bool? isDownloaded { get; set; }
        private string? descriptionMarkdown { get; set; }
        private string? notesMarkdown { get; set; }
        private string cloudSaveMatchTitle { get; set; }
        #endregion
        public Game? Game
        {
            get { return game; }
            set
            {
                game = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(ReleaseText));
                OnPropertyChanged(nameof(HasScreenshots));
                OnPropertyChanged(nameof(PosterUrl));
            }
        }

        public GameViewViewModel()
        {
            foreach (var brush in new[] { AccentBrush, AccentSoftBrush })
                brush.Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.ColorTransition { Property = Avalonia.Media.SolidColorBrush.ColorProperty, Duration = TimeSpan.FromMilliseconds(450) } };
        }

        /// <summary>The color of the game's cover: play button, glow under the artwork, genre chips.</summary>
        public Avalonia.Media.SolidColorBrush AccentBrush { get; } = new(gamevault.Helper.CoverColors.ThemeAccent);
        public Avalonia.Media.SolidColorBrush AccentSoftBrush { get; } = new(gamevault.Helper.CoverColors.ThemeAccent, 0.3);
        public void SetAccent(Avalonia.Media.Color? color)
        {
            var accent = color ?? gamevault.Helper.CoverColors.ThemeAccent;
            AccentBrush.Color = accent;
            AccentSoftBrush.Color = accent;
        }

        /// <summary>"Supergiant Games · 2020 · v1.38"</summary>
        public string Subtitle => string.Join(" · ", new[]
        {
            Game?.Metadata?.Developers?.FirstOrDefault()?.Name,
            Game?.Metadata?.ReleaseDate?.Year.ToString(),
            Game?.Version,
        }.Where(p => !string.IsNullOrEmpty(p)));

        public string ReleaseText => Game?.Metadata?.ReleaseDate is DateTime date ? date.ToString("d MMMM yyyy", System.Globalization.CultureInfo.CurrentUICulture) : "—";
        public bool HasScreenshots => Game?.Metadata?.Screenshots?.Length > 0;
        /// <summary>Shown in the player's place until it has loaded (and when it cannot).</summary>
        public string? PosterUrl => Game?.Metadata?.Screenshots?.FirstOrDefault();
        public bool HasOtherPlayers => UserProgresses?.Length > 0;
        public Progress? CurrentUserProgress
        {
            get { return currentUserProgress; }
            set { currentUserProgress = value; OnPropertyChanged(); }
        }
        public Progress[]? UserProgresses
        {
            get { return userProgresses; }
            set { userProgresses = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasOtherPlayers)); }
        }
        public Dictionary<string, string>? GameStates
        {
            get => gameStates ?? (gameStates = Enum.GetValues(typeof(State))
                .Cast<State>()
                .ToDictionary(state => GetEnumDescription(state), state => state.ToString()));
            set { gameStates = value; OnPropertyChanged(); }
        }

        private static string GetEnumDescription(State value) =>
            (Attribute.GetCustomAttribute(value.GetType().GetField(value.ToString()), typeof(DescriptionAttribute)) is DescriptionAttribute attribute) ? Loc.T(attribute.Description) : value.ToString();
        public bool IsInstalled
        {
            get { return isInstalled; }
            set { isInstalled = value; OnPropertyChanged(); }
        }
        private bool isUpdateAvailable;
        /// <summary>Installed, and the server has another build (a new version or a replaced file).</summary>
        public bool IsUpdateAvailable
        {
            get { return isUpdateAvailable; }
            set { isUpdateAvailable = value; OnPropertyChanged(); }
        }
        public bool? IsDownloaded
        {
            get { return isDownloaded; }
            set { isDownloaded = value; OnPropertyChanged(); }
        }
        public string? DescriptionMarkdown
        {
            get { return descriptionMarkdown; }
            set { descriptionMarkdown = value; OnPropertyChanged(); }
        }
        public string? NotesMarkdown
        {
            get { return notesMarkdown; }
            set { notesMarkdown = value; OnPropertyChanged(); }
        }
        public string CloudSaveMatchTitle
        {
            get { return cloudSaveMatchTitle; }
            set { cloudSaveMatchTitle = value; OnPropertyChanged(); }
        }
    }
}
