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

    /// <summary>An achievement of the game: its badge (grey until earned), what it asks, its points.</summary>
    internal sealed class RetroAchievementRow(GameVault.Core.Retro.RommProgress.Achievement achievement) : ViewModelBase
    {
        public string Title => achievement.Title;
        public string Description => achievement.Description;
        public string PointsText => Loc.F("{0} pts", achievement.Points);
        public bool Earned => achievement.Earned;
        public double Opacity => achievement.Earned ? 1 : 0.45;
        public string? BadgeUrl => achievement.Earned ? achievement.Badge : achievement.LockedBadge ?? achievement.Badge;
        private Bitmap? badge;
        public Bitmap? Badge { get => badge; set { badge = value; OnPropertyChanged(); } }
    }

    /// <summary>A save state of the game: its picture, its slot, when; "Resume" starts the game from it.</summary>
    internal sealed record RetroStateRow(int Slot, Bitmap? Picture, string SlotText, string WhenText);

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
        /// <summary>"EN · FR · DE": the header's chip of the languages the game has (RomM's, from its file's name).</summary>
        public string LanguageCodes => string.Join(" · ", Game.Languages.Select(name =>
            CultureInfo.GetCultures(CultureTypes.NeutralCultures).FirstOrDefault(c => c.EnglishName == name)?.TwoLetterISOLanguageName.ToUpperInvariant() ?? name));
        public bool HasLanguages => Game.Languages.Length > 0;

        /// <summary>Its console has a language (Settings > Retro): the page can choose the game's.</summary>
        public bool HasConsoleLanguage => GameVault.Core.Retro.RetroLanguage.Cores.Contains(Game.Platform.System.Core);
        /// <summary>The ones the game has (all when RomM does not know them), after "Automatic".</summary>
        private string[] LanguageChoices => GameVault.Core.Retro.RetroLanguage.All.Where(l => Game.Languages.Length == 0 || Game.Languages.Contains(l)).ToArray();
        public List<string> LanguageOptions => LanguageChoices.Select(LanguageName)
            .Prepend(Loc.F("Automatic ({0})", Lower(LanguageName(GameVault.Core.Retro.RetroLanguage.Pick(null, RetroArchManager.Language, Game.Languages))))).ToList();
        public int LanguageIndex
        {
            get => RetroArchManager.GameLanguages.TryGetValue(Game.Id, out string? chosen) ? Array.IndexOf(LanguageChoices, chosen) + 1 : 0;
            set
            {
                if (value < 0 || value == LanguageIndex)
                    return;
                var games = RetroArchManager.GameLanguages;
                if (value == 0)
                    games.Remove(Game.Id);
                else
                    games[Game.Id] = LanguageChoices[value - 1];
                RetroArchManager.GameLanguages = games;
                OnPropertyChanged();
            }
        }
        /// <summary>Played in RetroArch: its page opens Régler mes touches.</summary>
        public bool HasControls => GameVault.Core.Retro.RetroKeyboard.Of(Game.Platform.Slug).Length > 0;
        /// <summary>What it is played with: its controller (a console with several), and the buttons set for its console.</summary>
        public string ControlsSummary
        {
            get
            {
                var parts = new List<string>();
                if (GameVault.Core.Retro.RetroControllers.BySystem.TryGetValue(Game.Platform.Slug, out var controllers))
                    parts.Add(ControllerName(controllers.FirstOrDefault(c => c.Device == RetroArchManager.ControllerOf(Game))?.Name ?? controllers[0].Name));
                if (RetroArchManager.Pad.ContainsKey(Game.Platform.Slug))
                    parts.Add(Loc.T("gamepad set"));
                if (RetroArchManager.Keyboard.ContainsKey(Game.Platform.Slug))
                    parts.Add(Loc.T("keyboard set"));
                return parts.Count > 0 ? string.Join(" · ", parts) : Loc.T("RetroArch's default buttons");
            }
        }
        /// <summary>Régler mes touches closed: what it is played with may have changed.</summary>
        public void ControlsChanged() => OnPropertyChanged(nameof(ControlsSummary));
        /// <summary>A controller of <see cref="GameVault.Core.Retro.RetroControllers"/>, in the client's language.</summary>
        public static string ControllerName(string name) => name switch
        {
            "Wiimote + Nunchuk" => Loc.T("Wiimote + Nunchuk"),
            "Wiimote" => Loc.T("Wiimote"),
            "Sideways Wiimote" => Loc.T("Sideways Wiimote"),
            "Classic Controller" => Loc.T("Classic Controller"),
            "GameCube Controller" => Loc.T("GameCube Controller"),
            "DualShock (analog)" => Loc.T("DualShock (analog)"),
            "Digital controller" => Loc.T("Digital controller"),
            _ => name,
        };

        /// <summary>"in French", inside a sentence.</summary>
        public static string Lower(string text) => text.Length > 0 ? char.ToLower(text[0], CultureInfo.CurrentCulture) + text[1..] : text;
        /// <summary>"In French": RomM's name, in the client's language.</summary>
        public static string LanguageName(string language) => language switch
        {
            "French" => Loc.T("In French"),
            "German" => Loc.T("In German"),
            "Spanish" => Loc.T("In Spanish"),
            "Italian" => Loc.T("In Italian"),
            "Japanese" => Loc.T("In Japanese"),
            _ => Loc.T("In English"),
        };
        /// <summary>The settings of the game's emulator.</summary>
        public string MenuText => Loc.F("Controls and picture: {0} settings", StandaloneEmulator.Of(Game.Platform.System)?.Name ?? "RetroArch");
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

        #region RomM: the player's status and rating, the achievements
        public List<string> StatusOptions { get; } = new()
        {
            "—", Loc.T("Started"), Loc.T("Finished"), Loc.T("Completed 100%"), Loc.T("Retired"), Loc.T("Never playing"),
        };
        public List<string> RatingOptions { get; } = new[] { "—" }.Concat(Enumerable.Range(1, 10).Select(n => $"{n} / 10")).ToList();

        private bool hasRomm;
        /// <summary>The player's RomM gave its side of the game: the status and the rating show.</summary>
        public bool HasRomm { get => hasRomm; set { hasRomm = value; OnPropertyChanged(); } }

        private string rommNote = "";
        /// <summary>Why the status, rating and achievements are not there (not linked, linked before they existed).</summary>
        public string RommNote { get => rommNote; set { rommNote = value; OnPropertyChanged(); } }

        /// <summary>The player changed the status or the rating (not when the page sets it).</summary>
        public event Action<object>? MineChanged;
        private bool loadingMine;

        private int statusIndex;
        public int StatusIndex
        {
            get => statusIndex;
            set
            {
                if (value < 0 || value == statusIndex)
                    return;
                statusIndex = value;
                OnPropertyChanged();
                if (!loadingMine)
                    MineChanged?.Invoke(new { status = value == 0 ? null : GameVault.Core.Retro.RommProgress.Statuses[value] });
            }
        }

        private int ratingIndex;
        public int RatingIndex
        {
            get => ratingIndex;
            set
            {
                if (value < 0 || value == ratingIndex)
                    return;
                ratingIndex = value;
                OnPropertyChanged();
                if (!loadingMine)
                    MineChanged?.Invoke(new { rating = value });
            }
        }

        public void SetMine(GameVault.Core.Retro.RommProgress.Mine mine)
        {
            loadingMine = true;
            StatusIndex = Math.Max(0, GameVault.Core.Retro.RommProgress.Statuses.ToList().IndexOf(mine.Status));
            RatingIndex = mine.Rating;
            loadingMine = false;
            HasRomm = true;
        }

        private List<RetroAchievementRow> achievements = new();
        public List<RetroAchievementRow> Achievements
        {
            get => achievements;
            set { achievements = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAchievements)); OnPropertyChanged(nameof(AchievementsText)); }
        }
        public bool HasAchievements => Achievements.Count > 0;
        /// <summary>"12 / 126 achievements · 40 / 1200 points", and how to earn them when not signed in.</summary>
        public string AchievementsText
        {
            get
            {
                if (Achievements.Count == 0)
                    return "";
                string text = Loc.F("{0} / {1} achievements earned", Achievements.Count(a => a.Earned), Achievements.Count);
                return RetroAchievementsAccount.IsSignedIn ? text
                    : $"{text} · {Loc.T("Sign in to RetroAchievements (Settings > Retro) to earn them.")}";
            }
        }
        #endregion

        private List<RetroStateRow> states = new();
        /// <summary>The game's save states (RetroArch's games), the newest first.</summary>
        public List<RetroStateRow> States { get => states; set { states = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasStates)); } }
        public bool HasStates => States.Count > 0;

        /// <summary>The time played changed (a session ended): the strip shows it again.</summary>
        public void RefreshPlayer() => OnPropertyChanged(nameof(LastPlayedText));
    }
}
