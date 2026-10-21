using Avalonia.Media;
using gamevault.Helper;
using gamevault.Localization;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace gamevault.ViewModels
{
    /// <summary>The Retro page: the games of a RomM platform, filtered and sorted like the library; the selected one tints the page.</summary>
    internal sealed class RetroViewModel : ViewModelBase
    {
        public RetroViewModel()
        {
            SortOptions = new[] { Loc.T("Title"), Loc.T("Release date"), Loc.T("Rating"), Loc.T("Last played"), Loc.T("Playtime") };
            collections = new() { Loc.T("All collections") };
            recent = RetroLibrary.Recent(12);
            SetAccent(null);
            // Times and favorites come from the service after the list: the cards follow
            RetroLibrary.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Recent = RetroLibrary.Recent(12);
                foreach (RetroGame game in AllGames)
                    game.RefreshPlayer();
                if (OnlyFavorites || SortBy >= 3)
                    ApplyFilter();
            });
        }

        private List<RetroGame> recent = new();
        /// <summary>"Continue playing": the games played last, whatever the console.</summary>
        public List<RetroGame> Recent { get => recent; set { recent = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRecent)); } }
        public bool HasRecent => Recent.Count > 0;

        private List<RetroPlatform> platforms = new();
        public List<RetroPlatform> Platforms { get => platforms; set { platforms = value; OnPropertyChanged(); } }

        private List<RetroGame> allGames = new();
        /// <summary>The selected platform's games, as the service sent them.</summary>
        public List<RetroGame> AllGames
        {
            get => allGames;
            set
            {
                allGames = value;
                // The cards show the downloads already going on (started before a reload or a change of page)
                RetroDownloads.Attach(allGames);
                RetroLibrary.Describe(allGames.Where(g => g.Installed));
                ApplyFilter();
            }
        }

        private List<RetroGame> games = new();
        public List<RetroGame> Games { get => games; private set { games = value; OnPropertyChanged(); OnPropertyChanged(nameof(CountText)); } }

        public string CountText => AllGames.Count == 0 ? "" : Games.Count == AllGames.Count
            ? new Converter.CountConverter().Convert(AllGames.Count, typeof(string), "{0} game|{0} games", System.Globalization.CultureInfo.CurrentCulture) as string ?? ""
            : Loc.F("{0} of {1}", Games.Count, AllGames.Count);

        private string search = "";
        public string Search { get => search; set { search = value ?? ""; ApplyFilter(); } }

        private bool onlyInstalled;
        public bool OnlyInstalled { get => onlyInstalled; set { onlyInstalled = value; OnPropertyChanged(); ApplyFilter(); } }

        private bool onlyFavorites;
        public bool OnlyFavorites { get => onlyFavorites; set { onlyFavorites = value; OnPropertyChanged(); ApplyFilter(); } }

        private List<string> collections = new();
        /// <summary>"All collections", then the profile's retro collections.</summary>
        public List<string> Collections { get => collections; private set { collections = value; OnPropertyChanged(); } }
        private int collection;
        public int Collection
        {
            get => collection;
            set
            {
                // -1: the list emptied its selection while its items changed
                if (value < 0 || value == collection)
                    return;
                collection = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        /// <summary>The collections again (one was created from a game).</summary>
        public void LoadCollections()
        {
            string? shown = Collection > 0 && Collection < Collections.Count ? Collections[Collection] : null;
            var names = new List<string> { Loc.T("All collections") };
            try { names.AddRange(RetroLibrary.Collections.Load().Select(c => c.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)); }
            catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
            collection = shown != null ? Math.Max(0, names.IndexOf(shown)) : 0;
            if (!names.SequenceEqual(Collections))
                Collections = names;
            OnPropertyChanged(nameof(Collection));
        }

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
            if (OnlyFavorites)
                shown = shown.Where(g => g.IsFavorite);
            if (Collection > 0 && Collection < Collections.Count)
            {
                var members = RetroLibrary.Collections.Load().FirstOrDefault(c => c.Name == Collections[Collection])?.GameIds.ToHashSet() ?? new();
                shown = shown.Where(g => members.Contains(g.Id));
            }
            shown = SortBy switch
            {
                1 => shown.OrderByDescending(g => g.Released ?? long.MinValue),
                2 => shown.OrderByDescending(g => g.Rating ?? -1),
                3 => shown.OrderByDescending(g => RetroLibrary.LastPlayedOf(g) ?? DateTime.MinValue),
                4 => shown.OrderByDescending(g => g.MinutesPlayed),
                _ => shown.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
            };
            Games = shown.ToList();
        }

        private RetroGame? selectedGame;
        /// <summary>The game selected with the keyboard (Enter opens its page).</summary>
        public RetroGame? SelectedGame
        {
            get => selectedGame;
            set
            {
                if (selectedGame != null)
                {
                    selectedGame.PropertyChanged -= SelectedGame_PropertyChanged;
                    selectedGame.IsSelected = false;
                }
                selectedGame = value;
                if (selectedGame != null)
                {
                    selectedGame.IsSelected = true;
                    selectedGame.PropertyChanged += SelectedGame_PropertyChanged;
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

        /// <summary>The top of the page takes the color of the selected game, as in the library.</summary>
        public RadialGradientBrush AmbientBrush { get; } = new()
        {
            Center = new Avalonia.RelativePoint(0.5, 0, Avalonia.RelativeUnit.Relative),
            GradientOrigin = new Avalonia.RelativePoint(0.5, 0, Avalonia.RelativeUnit.Relative),
            RadiusX = new Avalonia.RelativeScalar(0.9, Avalonia.RelativeUnit.Relative),
            RadiusY = new Avalonia.RelativeScalar(1, Avalonia.RelativeUnit.Relative),
        };

        private void SetAccent(Color? color)
        {
            Color accent = color ?? CoverColors.ThemeAccent;
            AmbientBrush.GradientStops = new GradientStops
            {
                new GradientStop(Color.FromArgb(82, accent.R, accent.G, accent.B), 0),
                new GradientStop(Color.FromArgb(20, accent.R, accent.G, accent.B), 0.55),
                new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1),
            };
        }
    }
}
