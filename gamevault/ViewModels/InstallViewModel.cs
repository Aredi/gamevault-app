using gamevault.Helper;
using gamevault.Models;
using gamevault.UserControls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gamevault.ViewModels
{
    internal class InstallViewModel : ViewModelBase
    {
        #region Singleton
        private static InstallViewModel instance = null;
        private static readonly object padlock = new object();

        public static InstallViewModel Instance
        {
            get
            {
                lock (padlock)
                {
                    if (instance == null)
                    {
                        instance = new InstallViewModel();
                    }
                    return instance;
                }
            }
        }
        #endregion
        #region PrivateMembers      
        private ObservableCollection<KeyValuePair<Game, string>> m_InstalledGames { get; set; }
        public FilteredCollectionView<KeyValuePair<Game, string>>? installedGamesFilter { get; set; }
        private int rows { get; set; } = 0;
        private int colums { get; set; } = 0;
        #endregion      
        public ObservableCollection<KeyValuePair<Game, string>> InstalledGames
        {
            get
            {
                if (m_InstalledGames == null)
                {
                    m_InstalledGames = new ObservableCollection<KeyValuePair<Game, string>>();
                }
                return m_InstalledGames;
            }
            set { m_InstalledGames = value; OnPropertyChanged(); }
        }
        /// <summary>Puts the server's current game object in the list (after an update), so cards and badges refresh.</summary>
        public void ReplaceInstalledGame(Game game)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                int index = InstalledGames.ToList().FindIndex(g => g.Key.ID == game.ID);
                if (index >= 0)
                    InstalledGames[index] = new KeyValuePair<Game, string>(game, InstalledGames[index].Value);
            });
        }
        public Dictionary<int,string> InstalledGamesDuplicates= new Dictionary<int, string>();
        public FilteredCollectionView<KeyValuePair<Game, string>>? InstalledGamesFilter
        {
            get { return installedGamesFilter; }
            set { installedGamesFilter = value; OnPropertyChanged(); }
        }
        public void RefreshGame(Game gameToRefreshParam)
        {
            KeyValuePair<Game, string> gameToRefresh = InstalledGames.Where(g => g.Key.ID == gameToRefreshParam.ID).FirstOrDefault();
            if (!gameToRefresh.Equals(default(KeyValuePair<Game, string>)))
            {
                int index = InstalledGames.IndexOf(gameToRefresh);
                InstalledGames[index] = new KeyValuePair<Game, string>(gameToRefreshParam, gameToRefresh.Value);
            }
        }
        /// <summary>The current user's play time and last session of each game (from the server's progresses).</summary>
        public Dictionary<int, GameVault.Core.Library.PlayRecord> PlayRecords { get; private set; } = new();
        private int playRecordsVersion;
        /// <summary>Changes with <see cref="PlayRecords"/>: bindings showing play data refresh on it.</summary>
        public int PlayRecordsVersion
        {
            get => playRecordsVersion;
            private set { playRecordsVersion = value; OnPropertyChanged(); }
        }
        public void SetPlayRecords(IEnumerable<GameVault.Core.Library.PlayRecord> records)
        {
            PlayRecords = records.GroupBy(r => r.GameId).ToDictionary(g => g.Key, g => new GameVault.Core.Library.PlayRecord(g.Key, g.Max(r => r.LastPlayedAt), g.Sum(r => r.MinutesPlayed)));
            PlayRecordsVersion++;
        }
        private int visibleInstalledCount;
        /// <summary>Installed games matching the library search.</summary>
        public int VisibleInstalledCount
        {
            get => visibleInstalledCount;
            set { visibleInstalledCount = value; OnPropertyChanged(); }
        }
        public int Rows
        {
            get { return rows; }

            set
            {
                rows = value; OnPropertyChanged();
            }
        }
        public int Colums
        {
            get { return colums; }

            set
            {
                colums = value; OnPropertyChanged();
            }
        }
    }
}
