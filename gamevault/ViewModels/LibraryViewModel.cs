using GameVault.Core.Library;
using gamevault.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gamevault.ViewModels
{
    internal class LibraryViewModel : ViewModelBase
    {
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
            set { filterCounter = value; OnPropertyChanged(); }
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
