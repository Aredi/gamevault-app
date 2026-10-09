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
                selectedScreenshot = null;
                OnPropertyChanged(nameof(HasScreenshots));
                OnPropertyChanged(nameof(SelectedScreenshot));
                OnPropertyChanged(nameof(ScreenshotCounter));
                OnPropertyChanged(nameof(HasSeveralScreenshots));
                OnPropertyChanged(nameof(HasVideos));
                OnPropertyChanged(nameof(HasMedia));
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
        private string? selectedScreenshot;
        /// <summary>The screenshot shown large; the first one by default.</summary>
        public string? SelectedScreenshot
        {
            get => selectedScreenshot ?? Game?.Metadata?.Screenshots?.FirstOrDefault();
            set { selectedScreenshot = value; OnPropertyChanged(); OnPropertyChanged(nameof(ScreenshotCounter)); }
        }
        public void MoveScreenshot(int offset)
        {
            var shots = Game?.Metadata?.Screenshots;
            if (shots == null || shots.Length == 0)
                return;
            int index = Math.Max(0, Array.IndexOf(shots, SelectedScreenshot));
            SelectedScreenshot = shots[(index + offset + shots.Length) % shots.Length];
        }
        /// <summary>"2 / 6"</summary>
        public string ScreenshotCounter
        {
            get
            {
                var shots = Game?.Metadata?.Screenshots;
                return shots == null || shots.Length < 2 ? "" : $"{Math.Max(0, Array.IndexOf(shots, SelectedScreenshot)) + 1} / {shots.Length}";
            }
        }
        public bool HasSeveralScreenshots => Game?.Metadata?.Screenshots?.Length > 1;
        /// <summary>Trailers or gameplay videos to play.</summary>
        public bool HasVideos => gamevault.UserControls.TrailerPopup.HasVideos(Game?.Metadata);
        /// <summary>A gallery (screenshots, or the artwork with the play button) is shown.</summary>
        public bool HasMedia => HasScreenshots || HasVideos;
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
