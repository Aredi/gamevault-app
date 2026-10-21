using Avalonia.Media;
using gamevault.Helper;
using gamevault.Localization;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace gamevault.ViewModels
{
    /// <summary>The Retro page: the games of a RomM platform, filtered and sorted like the library, and the one shown beside them.</summary>
    internal sealed class RetroViewModel : ViewModelBase
    {
        public RetroViewModel()
        {
            SortOptions = new[] { Loc.T("Title"), Loc.T("Release date"), Loc.T("Rating") };
            SetAccent(null);
        }

        private List<RetroPlatform> platforms = new();
        public List<RetroPlatform> Platforms { get => platforms; set { platforms = value; OnPropertyChanged(); } }

        private List<RetroGame> allGames = new();
        /// <summary>The selected platform's games, as the service sent them.</summary>
        public List<RetroGame> AllGames { get => allGames; set { allGames = value; ApplyFilter(); } }

        private List<RetroGame> games = new();
        public List<RetroGame> Games { get => games; private set { games = value; OnPropertyChanged(); OnPropertyChanged(nameof(CountText)); } }

        public string CountText => AllGames.Count == 0 ? "" : Games.Count == AllGames.Count
            ? Loc.F("{0} games", AllGames.Count)
            : Loc.F("{0} of {1}", Games.Count, AllGames.Count);

        private string search = "";
        public string Search { get => search; set { search = value ?? ""; ApplyFilter(); } }

        private bool onlyInstalled;
        public bool OnlyInstalled { get => onlyInstalled; set { onlyInstalled = value; OnPropertyChanged(); ApplyFilter(); } }

        public string[] SortOptions { get; }
        private int sortBy;
        public int SortBy { get => sortBy; set { sortBy = value; OnPropertyChanged(); ApplyFilter(); } }

        private string? message;
        /// <summary>Why there are no games (no service, no platform, an error).</summary>
        public string? Message { get => message; set { message = value; OnPropertyChanged(); } }

        private bool loading;
        public bool Loading { get => loading; set { loading = value; OnPropertyChanged(); } }

        public void ApplyFilter()
        {
            IEnumerable<RetroGame> shown = AllGames;
            if (Search.Trim() is { Length: > 0 } text)
                shown = shown.Where(g => g.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || g.FileName.Contains(text, StringComparison.OrdinalIgnoreCase));
            if (OnlyInstalled)
                shown = shown.Where(g => g.Installed);
            shown = SortBy switch
            {
                1 => shown.OrderByDescending(g => g.Released ?? long.MinValue),
                2 => shown.OrderByDescending(g => g.Rating ?? -1),
                _ => shown.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
            };
            Games = shown.ToList();
        }

        private RetroGame? selectedGame;
        /// <summary>The game shown in the details panel; null closes it.</summary>
        public RetroGame? SelectedGame
        {
            get => selectedGame;
            set
            {
                if (selectedGame != null)
                    selectedGame.PropertyChanged -= SelectedGame_PropertyChanged;
                selectedGame = value;
                if (selectedGame != null)
                {
                    selectedGame.PropertyChanged += SelectedGame_PropertyChanged;
                    _ = selectedGame.LoadScreenshotsAsync();
                }
                SetAccent(selectedGame?.Accent);
                OnPropertyChanged();
            }
        }

        private void SelectedGame_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RetroGame.Accent))
                SetAccent(selectedGame?.Accent);
        }

        /// <summary>The top of the page and the details take the color of the shown game, as in the library.</summary>
        public RadialGradientBrush AmbientBrush { get; } = new()
        {
            Center = new Avalonia.RelativePoint(0.5, 0, Avalonia.RelativeUnit.Relative),
            GradientOrigin = new Avalonia.RelativePoint(0.5, 0, Avalonia.RelativeUnit.Relative),
            RadiusX = new Avalonia.RelativeScalar(0.9, Avalonia.RelativeUnit.Relative),
            RadiusY = new Avalonia.RelativeScalar(1, Avalonia.RelativeUnit.Relative),
        };
        public SolidColorBrush AccentBrush { get; } = new();
        public SolidColorBrush AccentSoftBrush { get; } = new() { Opacity = 0.28 };

        private void SetAccent(Color? color)
        {
            Color accent = color ?? CoverColors.ThemeAccent;
            AccentBrush.Color = accent;
            AccentSoftBrush.Color = accent;
            AmbientBrush.GradientStops = new GradientStops
            {
                new GradientStop(Color.FromArgb(82, accent.R, accent.G, accent.B), 0),
                new GradientStop(Color.FromArgb(20, accent.R, accent.G, accent.B), 0.55),
                new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1),
            };
        }
    }
}
