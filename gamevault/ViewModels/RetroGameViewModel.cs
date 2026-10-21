using Avalonia.Media;
using Avalonia.Media.Imaging;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace gamevault.ViewModels
{
    /// <summary>A friend on a retro game's page: their time and last session.</summary>
    internal sealed record RetroPlayerRow(User? User, string Username, string TimeText, string WhenText);

    /// <summary>The page of a retro game, as the library's game page: the game, its screenshots, its saves, who played it.</summary>
    internal sealed class RetroGameViewModel : ViewModelBase
    {
        public RetroGameViewModel(RetroGame game)
        {
            Game = game;
            SetAccent(game.Accent);
            game.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RetroGame.Accent))
                    SetAccent(game.Accent);
                else if (e.PropertyName == nameof(RetroGame.ScreenshotImages))
                    SelectedScreenshot = game.ScreenshotImages?.FirstOrDefault();
            };
        }

        public RetroGame Game { get; }

        public SolidColorBrush AccentBrush { get; } = new();
        public SolidColorBrush AccentSoftBrush { get; } = new() { Opacity = 0.3 };

        private void SetAccent(Color? color)
        {
            Color accent = color ?? CoverColors.ThemeAccent;
            AccentBrush.Color = accent;
            AccentSoftBrush.Color = accent;
        }

        /// <summary>"Konami · 2002": under the title.</summary>
        public string Subtitle => string.Join(" · ", new[] { Game.Companies.FirstOrDefault(), Game.ReleaseDate?.Year.ToString(), Game.Platform.Name }.Where(p => !string.IsNullOrEmpty(p)));
        public string ReleaseText => Game.ReleaseDate?.ToString("D", CultureInfo.CurrentCulture) ?? "—";
        public string LastPlayedText => RetroLibrary.LastPlayedOf(Game) is DateTime last ? Converter.RelativeDate.Format(last) : "—";
        public string Join(string[] values) => values.Length > 0 ? string.Join(", ", values) : "—";
        public string CompaniesText => Join(Game.Companies);
        public string SeriesText => Join(Game.Franchises);
        public string ModesText => Join(Game.Modes);
        public string RegionsText => Join(Game.Regions);
        public string LanguagesText => Join(Game.Languages);
        public string AgeRatingsText => Join(Game.AgeRatings);
        /// <summary>The settings of the game's emulator.</summary>
        public string MenuText => Game.Platform.System.IsRpcs3 ? Loc.T("Controls and picture: RPCS3 settings") : Loc.T("Controls and picture: RetroArch settings");
        public string PlayersText => string.IsNullOrEmpty(Game.Players) || Game.Players == "null" ? "—" : Game.Players;

        private Bitmap? selectedScreenshot;
        public Bitmap? SelectedScreenshot
        {
            get => selectedScreenshot;
            set { selectedScreenshot = value; OnPropertyChanged(); OnPropertyChanged(nameof(ScreenshotCounter)); OnPropertyChanged(nameof(HasScreenshots)); OnPropertyChanged(nameof(HasSeveralScreenshots)); }
        }
        public bool HasScreenshots => Game.ScreenshotImages?.Count > 0;
        public bool HasSeveralScreenshots => Game.ScreenshotImages?.Count > 1;
        public string ScreenshotCounter => Game.ScreenshotImages is { Count: > 0 } shots && selectedScreenshot != null
            ? $"{shots.IndexOf(selectedScreenshot) + 1} / {shots.Count}" : "";

        public void MoveScreenshot(int offset)
        {
            if (Game.ScreenshotImages is not { Count: > 0 } shots)
                return;
            int index = selectedScreenshot == null ? 0 : shots.IndexOf(selectedScreenshot);
            SelectedScreenshot = shots[(index + offset + shots.Count) % shots.Count];
        }

        private string savesText = "";
        /// <summary>Where the game's cloud saves are: "Sent 2 hours ago", "Not sent yet".</summary>
        public string SavesText { get => savesText; set { savesText = value; OnPropertyChanged(); } }

        private List<RetroPlayerRow> players = new();
        public List<RetroPlayerRow> Players { get => players; set { players = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPlayers)); } }
        public bool HasPlayers => Players.Count > 0;

        /// <summary>The time played changed (a session ended): the strip shows it again.</summary>
        public void RefreshPlayer() => OnPropertyChanged(nameof(LastPlayedText));
    }
}
