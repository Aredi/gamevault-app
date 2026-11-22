using Avalonia.Media;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using GameVault.Core.Library;

namespace gamevault.ViewModels
{
    /// <summary>The Retro page: the games of a RomM platform, filtered and sorted like the library; the selected one tints the page.</summary>
    internal sealed class RetroViewModel : ViewModelBase
    {
        public RetroViewModel()
        {
            SortOptions = new[] { Loc.T("Title"), Loc.T("Release date"), Loc.T("Rating"), Loc.T("Last played"), Loc.T("Playtime"), Loc.T("Shortest first"), Loc.T("Players' rating") };
            sortBy = SavedSort();
            collections = new() { Loc.T("All collections") };
            recent = RetroLibrary.Recent(12);
            SetAccent(null);
            // Times and favorites come from the service after the list: the cards follow
            RetroLibrary.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Recent = RetroLibrary.Recent(12);
                foreach (RetroGame game in AllGames)
                    game.RefreshPlayer();
                if (OnlyFavorites || OnlyNeverPlayed || SortBy >= 3)
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
                LoadFilterChoices();
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

        private bool onlyHidden;
        /// <summary>The games hidden from the lists, to show one again; else they are left out.</summary>
        public bool OnlyHidden { get => onlyHidden; set { onlyHidden = value; OnPropertyChanged(); ApplyFilter(); } }

        private bool onlyNeverPlayed;
        /// <summary>The games not started yet: what is left to discover.</summary>
        public bool OnlyNeverPlayed { get => onlyNeverPlayed; set { onlyNeverPlayed = value; OnPropertyChanged(); ApplyFilter(); } }

        internal static bool NeverPlayed(RetroGame game) => game.MinutesPlayed == 0 && RetroLibrary.LastPlayedOf(game) == null;

        private List<string> collections = new();
        /// <summary>"All collections", the profile's retro collections, then the player's RomM collections and the series.</summary>
        public List<string> Collections { get => collections; private set { collections = value; OnPropertyChanged(); } }
        /// <summary>The games of each collection that is not the profile's, by name as <see cref="Collections"/> shows it.</summary>
        private Dictionary<string, HashSet<int>> otherCollections = new();

        /// <summary>The player's RomM collections and the series of games, shown after the profile's.</summary>
        public void SetOtherCollections(IEnumerable<(string Name, IEnumerable<int> Games)> others)
        {
            otherCollections = new();
            foreach (var (name, games) in others)
                otherCollections.TryAdd(name, games.ToHashSet());
            LoadCollections();
        }

        /// <summary>The chosen collection is the profile's, or one of RomM: across all the consoles.</summary>
        public bool ShowsACollection => Collection > 0 && Collection < Collections.Count;

        private bool showsAll;
        /// <summary>The games of all the consoles (a collection, a search across them).</summary>
        public bool ShowsAll { get => showsAll; set { showsAll = value; OnPropertyChanged(); } }

        #region Filters
        public string[] PlayerOptions { get; } = { Loc.T("All players"), Loc.T("2 players or more"), Loc.T("4 players or more"), Loc.T("Co-op") };
        public string[] RatingOptions { get; } = { Loc.T("Any rating"), "70 %+", "80 %+", "90 %+" };

        private List<string> genres = new();
        /// <summary>"All genres", then the genres of the games shown, the most common first.</summary>
        public List<string> Genres { get => genres; private set { genres = value; OnPropertyChanged(); } }
        private List<string> regions = new(), franchises = new();
        /// <summary>"All regions", then the regions of the games shown (RomM's: Europe, USA, Japan...), the most common first.</summary>
        public List<string> Regions { get => regions; private set { regions = value; OnPropertyChanged(); } }
        /// <summary>"All franchises", then the games' franchises (Mario, Zelda...), by name.</summary>
        public List<string> Franchises { get => franchises; private set { franchises = value; OnPropertyChanged(); } }
        private int region, franchise;
        public int Region { get => region; set { if (value < 0 || value == region) return; region = value; OnPropertyChanged(); FiltersChanged(); } }
        public int Franchise { get => franchise; set { if (value < 0 || value == franchise) return; franchise = value; OnPropertyChanged(); FiltersChanged(); } }
        private List<string> decades = new();
        public List<string> Decades { get => decades; private set { decades = value; OnPropertyChanged(); } }

        private int genre, players, decade, rating;
        public int Genre { get => genre; set { if (value < 0 || value == genre) return; genre = value; OnPropertyChanged(); FiltersChanged(); } }
        public int Players { get => players; set { if (value < 0 || value == players) return; players = value; OnPropertyChanged(); FiltersChanged(); } }
        public int Decade { get => decade; set { if (value < 0 || value == decade) return; decade = value; OnPropertyChanged(); FiltersChanged(); } }
        public int Rating { get => rating; set { if (value < 0 || value == rating) return; rating = value; OnPropertyChanged(); FiltersChanged(); } }

        private bool showFilters;
        public bool ShowFilters { get => showFilters; set { showFilters = value; OnPropertyChanged(); } }
        public bool HasFilters => Genre > 0 || Players > 0 || Decade > 0 || Rating > 0 || Region > 0 || Franchise > 0;

        private void FiltersChanged()
        {
            OnPropertyChanged(nameof(HasFilters));
            ApplyFilter();
        }

        public void ClearFilters()
        {
            genre = players = decade = rating = region = franchise = 0;
            OnPropertyChanged(nameof(Region));
            OnPropertyChanged(nameof(Franchise));
            OnPropertyChanged(nameof(Genre));
            OnPropertyChanged(nameof(Players));
            OnPropertyChanged(nameof(Decade));
            OnPropertyChanged(nameof(Rating));
            FiltersChanged();
        }

        /// <summary>The most players a game takes: RomM writes "1-4".</summary>
        internal static int MaxPlayers(RetroGame game) =>
            game.Players?.Split('-').Select(p => int.TryParse(p.Trim(), out int n) ? n : 0).DefaultIfEmpty(0).Max() ?? 0;

        private static int? DecadeOf(RetroGame game) => game.ReleaseDate?.Year / 10 * 10;

        /// <summary>The genres and decades of the games of the list, kept chosen when still there.</summary>
        private void LoadFilterChoices()
        {
            string? shownGenre = Genre > 0 && Genre < Genres.Count ? Genres[Genre] : null;
            string? shownDecade = Decade > 0 && Decade < Decades.Count ? Decades[Decade] : null;
            string? shownRegion = Region > 0 && Region < Regions.Count ? Regions[Region] : null;
            string? shownFranchise = Franchise > 0 && Franchise < Franchises.Count ? Franchises[Franchise] : null;
            var regionNames = AllGames.SelectMany(g => g.Regions).GroupBy(r => r, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(r => r.Count()).Select(r => r.Key).Prepend(Loc.T("All regions")).ToList();
            var franchiseNames = AllGames.SelectMany(g => g.Franchises).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).Prepend(Loc.T("All franchises")).ToList();
            region = shownRegion != null ? Math.Max(0, regionNames.IndexOf(shownRegion)) : 0;
            franchise = shownFranchise != null ? Math.Max(0, franchiseNames.IndexOf(shownFranchise)) : 0;
            Regions = regionNames;
            Franchises = franchiseNames;
            OnPropertyChanged(nameof(Region));
            OnPropertyChanged(nameof(Franchise));
            var genreNames = AllGames.SelectMany(g => g.Genres).GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).Select(g => g.Key).Prepend(Loc.T("All genres")).ToList();
            var decadeNames = AllGames.Select(DecadeOf).OfType<int>().Distinct().OrderBy(d => d)
                .Select(d => Loc.F("{0}s", d)).Prepend(Loc.T("All decades")).ToList();
            genre = shownGenre != null ? Math.Max(0, genreNames.IndexOf(shownGenre)) : 0;
            decade = shownDecade != null ? Math.Max(0, decadeNames.IndexOf(shownDecade)) : 0;
            Genres = genreNames;
            Decades = decadeNames;
            OnPropertyChanged(nameof(Genre));
            OnPropertyChanged(nameof(Decade));
            OnPropertyChanged(nameof(HasFilters));
        }
        #endregion
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

        /// <summary>The lists made by themselves, first among the collections: never played, most played, added lately.</summary>
        internal static string[] SmartCollections => new[] { Loc.T("Never played"), Loc.T("Most played"), Loc.T("Recently added") };

        /// <summary>When RomM got its newest games, by id (the service's /retro/latest).</summary>
        private Dictionary<int, DateTime> recentlyAdded = new();
        internal IReadOnlyDictionary<int, DateTime> RecentlyAdded => recentlyAdded;

        public void SetRecentlyAdded(IEnumerable<(int Id, DateTime Added)> games)
        {
            recentlyAdded = games.GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.Max(x => x.Added));
            if (ShowsACollection && Collections[Collection] == SmartCollections[2])
                ApplyFilter();
        }

        /// <summary>The games of a list made by itself, in its own order; null for another collection.</summary>
        private IEnumerable<RetroGame>? SmartCollection(string name, IEnumerable<RetroGame> games)
        {
            string[] smart = SmartCollections;
            if (name == smart[0])
                return games.Where(NeverPlayed);
            if (name == smart[1])
                return games.Where(g => g.MinutesPlayed > 0).OrderByDescending(g => g.MinutesPlayed);
            if (name == smart[2])
                return games.Where(g => recentlyAdded.ContainsKey(g.Id)).OrderByDescending(g => recentlyAdded[g.Id]);
            return null;
        }

        /// <summary>The collections again (one was created from a game).</summary>
        public void LoadCollections()
        {
            string? shown = Collection > 0 && Collection < Collections.Count ? Collections[Collection] : null;
            var names = new List<string> { Loc.T("All collections") };
            names.AddRange(SmartCollections);
            try { names.AddRange(RetroLibrary.Collections.Load().Select(c => c.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)); }
            catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
            names.AddRange(otherCollections.Keys.Where(n => !names.Contains(n)));
            collection = shown != null ? Math.Max(0, names.IndexOf(shown)) : 0;
            if (!names.SequenceEqual(Collections))
                Collections = names;
            OnPropertyChanged(nameof(Collection));
        }

        public string[] SortOptions { get; }
        private int sortBy;
        public int SortBy
        {
            get => sortBy;
            set
            {
                if (value < 0 || value >= SortOptions.Length)
                    return;
                bool changed = value != sortBy;
                if (changed && value == ByPlayersRating)
                    ratings = null;
                sortBy = value;
                OnPropertyChanged();
                ApplyFilter();
                if (changed)
                    SaveSort();
            }
        }

        /// <summary>The sorts as saved: by name, so a sort added to the list does not change the one kept.</summary>
        internal static readonly string[] SortKeys = { "title", "released", "rating", "lastPlayed", "playtime", "shortest", "players" };
        private const int ByPlayersRating = 6;

        /// <summary>The players' reviews of every game, asked for when sorting by them.</summary>
        private Dictionary<string, SocialRating>? ratings;
        private bool loadingRatings;

        private async void LoadRatings()
        {
            if (loadingRatings)
                return;
            loadingRatings = true;
            try { ratings = await SocialService.RatingsAsync(); }
            finally { loadingRatings = false; }
            if (SortBy == ByPlayersRating)
                ApplyFilter();
        }

        /// <summary>The sort chosen last time, kept for the next start; the title when none.</summary>
        private static int SavedSort()
        {
            try { return Math.Max(0, Array.IndexOf(SortKeys, Preferences.Get(AppConfigKey.LastRetroSortBy, LoginManager.Instance.GetUserProfile().UserConfigFile))); }
            catch (Exception ex) { GameVault.Core.Log.Ignored(ex); return 0; }
        }

        private void SaveSort()
        {
            try { Preferences.Set(AppConfigKey.LastRetroSortBy, SortKeys[sortBy], LoginManager.Instance.GetUserProfile().UserConfigFile); }
            catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
        }

        private string? message;
        /// <summary>Why there are no games (no service, no platform, an error).</summary>
        public string? Message { get => message; set { message = value; OnPropertyChanged(); } }

        private bool loading;
        public bool Loading { get => loading; set { loading = value; OnPropertyChanged(); } }

        public void ApplyFilter()
        {
            IEnumerable<RetroGame> shown = AllGames;
            // Accents and case ignored, as the quick search: "pokemon" finds "Pokémon"
            if (GameVault.Core.Library.QuickMatch.Normalize(Search) is { Length: > 0 } text)
                shown = shown.Where(g => GameVault.Core.Library.QuickMatch.Normalize(g.Name).Contains(text, StringComparison.Ordinal)
                    || GameVault.Core.Library.QuickMatch.Normalize(g.FileName).Contains(text, StringComparison.Ordinal));
            if (OnlyInstalled)
                shown = shown.Where(g => g.Installed);
            shown = shown.Where(g => g.IsHidden == OnlyHidden);
            if (OnlyFavorites)
                shown = shown.Where(g => g.IsFavorite);
            if (OnlyNeverPlayed)
                shown = shown.Where(NeverPlayed);
            IEnumerable<RetroGame>? smart = ShowsACollection ? SmartCollection(Collections[Collection], shown) : null;
            if (smart != null)
                shown = smart;
            else if (ShowsACollection)
            {
                var members = otherCollections.TryGetValue(Collections[Collection], out HashSet<int>? others) ? others
                    : RetroLibrary.Collections.Load().FirstOrDefault(c => c.Name == Collections[Collection])?.GameIds.ToHashSet() ?? new();
                shown = shown.Where(g => members.Contains(g.Id));
            }
            if (Genre > 0 && Genre < Genres.Count)
                shown = shown.Where(g => g.Genres.Contains(Genres[Genre], StringComparer.OrdinalIgnoreCase));
            shown = Players switch
            {
                1 => shown.Where(g => MaxPlayers(g) >= 2),
                2 => shown.Where(g => MaxPlayers(g) >= 4),
                3 => shown.Where(g => g.Modes.Contains("Co-operative", StringComparer.OrdinalIgnoreCase)),
                _ => shown,
            };
            if (Region > 0 && Region < Regions.Count)
                shown = shown.Where(g => g.Regions.Contains(Regions[Region], StringComparer.OrdinalIgnoreCase));
            if (Franchise > 0 && Franchise < Franchises.Count)
                shown = shown.Where(g => g.Franchises.Contains(Franchises[Franchise], StringComparer.OrdinalIgnoreCase));
            if (Decade > 0 && Decade < Decades.Count)
                shown = shown.Where(g => DecadeOf(g) is int d && Loc.F("{0}s", d) == Decades[Decade]);
            if (Rating > 0)
                shown = shown.Where(g => g.Rating >= 60 + 10 * Rating);
            // Asked again each time this sort is chosen: the reviews change while the page is open
            if (SortBy == ByPlayersRating && ratings == null)
                LoadRatings();
            // A list made by itself keeps its own order (the most played first, the newest first), unless another sort is chosen
            if (smart != null && SortBy == 0 && Collections[Collection] != SmartCollections[0])
            {
                Games = shown.ToList();
                return;
            }
            shown = SortBy switch
            {
                1 => shown.OrderByDescending(g => g.Released ?? long.MinValue),
                2 => shown.OrderByDescending(g => g.Rating ?? -1),
                3 => shown.OrderByDescending(g => RetroLibrary.LastPlayedOf(g) ?? DateTime.MinValue),
                4 => shown.OrderByDescending(g => g.MinutesPlayed),
                // The time to finish its story: the ones without it last
                5 => shown.OrderBy(g => g.MainStory ?? long.MaxValue).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
                ByPlayersRating => LibraryQuery.OrderByPlayersRating(shown.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
                    g => ratings != null && ratings.TryGetValue(SocialService.GameKey(g), out SocialRating? r) ? (r.Stars, r.Count) : null, ascending: false),
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
