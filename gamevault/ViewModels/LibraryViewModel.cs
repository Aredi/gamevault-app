using GameVault.Core.Library;
using Avalonia.Animation;
using Avalonia.Media;
using gamevault.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gamevault.ViewModels
{
    /// <summary>A game shown in the large banner at the top of the library.</summary>
    internal class HeroItem
    {
        public Game Game { get; init; } = null!;
        public bool IsInstalled { get; init; }
        /// <summary>"CONTINUE PLAYING", "NEW ON THE SERVER", ...</summary>
        public string Eyebrow { get; init; } = "";
        /// <summary>"34 h played · last session yesterday"</summary>
        public string Info { get; init; } = "";
    }

    /// <summary>A collection shown as a row of covers in the showcase.</summary>
    internal class CollectionRow
    {
        public string Name { get; init; } = "";
        public int Count { get; init; }
        public List<Game> Games { get; init; } = new();
    }

    internal class LibraryViewModel : ViewModelBase
    {
        public const double MinCardWidth = 120;
        public const double MaxCardWidth = 260;
        public const double DefaultCardWidth = 170;
        /// <summary>Covers in the shelf grid: small, the panel shows the selected one large.</summary>
        public const double ShelfCardWidth = 112;

        public LibraryViewModel()
        {
            AmbientBrush.Transitions = new Transitions { new ColorTransition { Property = SolidColorBrush.ColorProperty, Duration = TimeSpan.FromMilliseconds(600) } };
            foreach (SolidColorBrush brush in new[] { HeroAccentBrush, ShelfAccentBrush, ShelfAccentSoftBrush })
                brush.Transitions = new Transitions { new ColorTransition { Property = SolidColorBrush.ColorProperty, Duration = TimeSpan.FromMilliseconds(400) } };
        }

        #region Showcase
        public ObservableCollection<HeroItem> HeroItems { get; } = new();
        private int heroIndex;
        public int HeroIndex
        {
            get => heroIndex;
            set { heroIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentHero)); }
        }
        public HeroItem? CurrentHero => HeroIndex >= 0 && HeroIndex < HeroItems.Count ? HeroItems[HeroIndex] : null;
        public void HeroItemsChanged()
        {
            if (HeroIndex >= HeroItems.Count)
                HeroIndex = 0;
            OnPropertyChanged(nameof(CurrentHero));
            OnPropertyChanged(nameof(HasSeveralHeroItems));
            RefreshVisibility();
        }
        public bool HasSeveralHeroItems => HeroItems.Count > 1;

        /// <summary>The profile's collections, each as a row (the ones with games).</summary>
        public ObservableCollection<CollectionRow> CollectionRows { get; } = new();
        public bool ShowCollections => ShowcaseEnabled && IsBrowsing && !IsOffline && CollectionRows.Count > 0;

        /// <summary>Newest games of the server.</summary>
        public ObservableCollection<Game> RecentGames { get; } = new();

        private bool showcaseEnabled = true;
        /// <summary>The banner and the rows above the games (can be turned off for a plain grid).</summary>
        public bool ShowcaseEnabled
        {
            get => showcaseEnabled;
            set { showcaseEnabled = value; OnPropertyChanged(); RefreshVisibility(); }
        }

        private string searchText = "";
        public string SearchText
        {
            get => searchText;
            set { searchText = value ?? ""; OnPropertyChanged(); RefreshVisibility(); }
        }
        private bool isOffline;
        public bool IsOffline
        {
            get => isOffline;
            set { isOffline = value; OnPropertyChanged(); RefreshVisibility(); }
        }
        private int installedCount;
        /// <summary>Installed games matching the search (the "Continue playing" row).</summary>
        public int InstalledCount
        {
            get => installedCount;
            set { installedCount = value; OnPropertyChanged(); RefreshVisibility(); }
        }

        /// <summary>No search and no filter: the showcase is shown above the games.</summary>
        public bool IsBrowsing => string.IsNullOrWhiteSpace(SearchText) && string.IsNullOrEmpty(FilterCounter);
        public bool ShowHero => ShowcaseEnabled && IsBrowsing && HeroItems.Count > 0;
        public bool ShowRecent => ShowcaseEnabled && IsBrowsing && !IsOffline && RecentGames.Count > 0;
        /// <summary>Installed games stay reachable while searching and offline, even without the showcase.</summary>
        public bool ShowInstalledRow => InstalledCount > 0 && (ShowcaseEnabled && IsBrowsing || !string.IsNullOrWhiteSpace(SearchText) || IsOffline);
        public void RefreshVisibility()
        {
            OnPropertyChanged(nameof(IsBrowsing));
            OnPropertyChanged(nameof(ShowHero));
            OnPropertyChanged(nameof(ShowRecent));
            OnPropertyChanged(nameof(ShowCollections));
            OnPropertyChanged(nameof(ShowInstalledRow));
        }

        /// <summary>Top of the page, tinted with the color of the featured or hovered game.</summary>
        public SolidColorBrush AmbientBrush { get; } = new(Color.Parse("#4F46AF"));
        public SolidColorBrush HeroAccentBrush { get; } = new(Color.Parse("#4F46AF"));
        public SolidColorBrush ShelfAccentBrush { get; } = new(Color.Parse("#4F46AF"));
        public SolidColorBrush ShelfAccentSoftBrush { get; } = new(Color.Parse("#4F46AF"), 0.28);

        private double heroHeight = 360;
        public double HeroHeight
        {
            get => heroHeight;
            set { heroHeight = value; OnPropertyChanged(); }
        }
        private double heroTitleSize = 46;
        public double HeroTitleSize
        {
            get => heroTitleSize;
            set { heroTitleSize = value; OnPropertyChanged(); }
        }
        #endregion

        #region Layout
        private double cardWidth = DefaultCardWidth;
        /// <summary>Cover width in the gallery, chosen with the size slider.</summary>
        public double CardWidth
        {
            get => cardWidth;
            set { cardWidth = Math.Clamp(value, MinCardWidth, MaxCardWidth); OnPropertyChanged(); OnPropertyChanged(nameof(EffectiveCardWidth)); }
        }
        private bool isShelfMode;
        /// <summary>Small covers on the left and the selected game in a panel on the right.</summary>
        public bool IsShelfMode
        {
            get => isShelfMode;
            set { isShelfMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsGalleryMode)); OnPropertyChanged(nameof(EffectiveCardWidth)); OnPropertyChanged(nameof(ShowShelfPanel)); }
        }
        public bool IsGalleryMode => !IsShelfMode;
        public double EffectiveCardWidth => IsShelfMode ? ShelfCardWidth : CardWidth;
        private bool isWide = true;
        /// <summary>The window is wide enough for the shelf panel next to the grid.</summary>
        public bool IsWide
        {
            get => isWide;
            set { isWide = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowShelfPanel)); }
        }
        public bool ShowShelfPanel => IsShelfMode && IsWide && SelectedGame != null;
        private Game? selectedGame;
        /// <summary>The game shown in the shelf panel.</summary>
        public Game? SelectedGame
        {
            get => selectedGame;
            set { selectedGame = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowShelfPanel)); }
        }
        #endregion

        #region Quick filters
        private bool onlyInstalled;
        public bool OnlyInstalled
        {
            get => onlyInstalled;
            set { onlyInstalled = value; OnPropertyChanged(); }
        }
        private bool onlyUpdates;
        public bool OnlyUpdates
        {
            get => onlyUpdates;
            set { onlyUpdates = value; OnPropertyChanged(); }
        }
        private int updateCount;
        /// <summary>Installed games with a newer build on the server.</summary>
        public int UpdateCount
        {
            get => updateCount;
            set { updateCount = value; OnPropertyChanged(); }
        }
        #endregion
        #region PrivateMembers      
        private ObservableCollection<Game> gameCards { get; set; }
        private int totalGamesCount = -1;
        private bool scrollToTopVisibility { get; set; }
        private bool filterVisibility = false;
        private KeyValuePair<string, string> m_SelectedGameFilterSortBy { get; set; }
        private string filterCounter { get; set; } = string.Empty;
        private bool canLoadServerGames { get; set; } = true;
        #endregion
        public ObservableCollection<Game> GameCards
        {
            get
            {
                if (gameCards == null)
                {
                    gameCards = new ObservableCollection<Game>();
                }
                return gameCards;
            }
            set { gameCards = value; }
        }
        public int TotalGamesCount
        {
            get { return totalGamesCount; }
            set { totalGamesCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowNoGamesFound)); }
        }
        public bool ScrollToTopVisibility
        {
            get { return scrollToTopVisibility; }
            set { scrollToTopVisibility = value; OnPropertyChanged(); }
        }
        public bool FilterVisibility
        {
            get { return filterVisibility; }
            set { filterVisibility = value; OnPropertyChanged(); }
        }
        public string? NextPage { get; set; }
        public Dictionary<string, string> GameFilterSortByValues
        {
            get
            {
                var dict = new Dictionary<string, string>
                {
                    {"Title","sort_title"},
                    {"Size","size" },
                    {"Date Added","created_at" },
                    {"Release Date","metadata.release_date" },
                    {"Rating","metadata.rating" },
                    {"Download Count","download_count" },
                    {"Average Playtime","metadata.average_playtime" },
                    // Sorted by GameVault from the user's own play records, not by the server
                    {"Last Played", LastPlayedSort },
                    {"My Playtime", MyPlaytimeSort },
                };
                return dict;
            }
        }
        public const string LastPlayedSort = "gv:last_played";
        public const string MyPlaytimeSort = "gv:my_playtime";

        public Dictionary<string, PlayStatusFilter> PlayStatusValues { get; } = new()
        {
            { "All games", PlayStatusFilter.All },
            { "Played", PlayStatusFilter.Played },
            { "Never played", PlayStatusFilter.NeverPlayed },
        };
        private KeyValuePair<string, PlayStatusFilter> selectedPlayStatus;
        public KeyValuePair<string, PlayStatusFilter> SelectedPlayStatus
        {
            get => selectedPlayStatus;
            set { selectedPlayStatus = value; OnPropertyChanged(); }
        }

        public const string AllCollections = "All games";
        private List<string> collectionNames = new() { AllCollections };
        /// <summary>"All games" followed by the profile's collections.</summary>
        public List<string> CollectionNames
        {
            get => collectionNames;
            set { collectionNames = value; OnPropertyChanged(); }
        }
        private string? selectedCollection = AllCollections;
        public string? SelectedCollection
        {
            get => selectedCollection;
            set { selectedCollection = value; OnPropertyChanged(); }
        }

        public KeyValuePair<string, string> SelectedGameFilterSortBy
        {
            get { return m_SelectedGameFilterSortBy; }
            set { m_SelectedGameFilterSortBy = value; }
        }
        public string FilterCounter
        {
            get { return filterCounter; }
            set { filterCounter = value; OnPropertyChanged(); RefreshVisibility(); }
        }
        public bool CanLoadServerGames
        {
            get { return canLoadServerGames; }
            set { canLoadServerGames = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowNoGamesFound)); }
        }
        public bool ShowNoGamesFound
        {
            get { return TotalGamesCount == 0 && CanLoadServerGames; }
        }
    }
}
