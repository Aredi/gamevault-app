using gamevault.Models;
using gamevault.UserControls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gamevault.ViewModels
{
    internal class DownloadsViewModel : ViewModelBase
    {
        #region Singleton
        private static DownloadsViewModel instance = null;
        private static readonly object padlock = new object();

        public static DownloadsViewModel Instance
        {
            get
            {
                lock (padlock)
                {
                    if (instance == null)
                    {
                        instance = new DownloadsViewModel();
                    }
                    return instance;
                }
            }
        }
        #endregion
        #region PrivateMembers       
        private ObservableCollection<GameDownloadUserControl> m_DownloadedGames { get; set; }

        #endregion
       
        public ObservableCollection<GameDownloadUserControl> DownloadedGames
        {
            get
            {
                if (m_DownloadedGames == null)
                {
                    m_DownloadedGames = new ObservableCollection<GameDownloadUserControl>();
                }
                return m_DownloadedGames;
            }
            set { m_DownloadedGames = value; OnPropertyChanged(); }
        }

        private DownloadsViewModel()
        {
            // The counters of the page header and the navigation bar
            var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) => RefreshCounts();
            timer.Start();
        }

        private int activeCount, queuedCount, readyCount;
        /// <summary>Downloads and extractions running.</summary>
        public int ActiveCount { get => activeCount; private set { if (activeCount != value) { activeCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(BadgeText)); } } }
        public int QueuedCount { get => queuedCount; private set { if (queuedCount != value) { queuedCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(BadgeText)); } } }
        public int ReadyCount { get => readyCount; private set { if (readyCount != value) { readyCount = value; OnPropertyChanged(); } } }
        /// <summary>Shown on the navigation bar: what is running or waiting.</summary>
        public string BadgeText => ActiveCount + QueuedCount > 0 ? (ActiveCount + QueuedCount).ToString() : "";

        public void RefreshCounts()
        {
            try
            {
                ActiveCount = DownloadedGames.Count(d => d.IsBusy());
                QueuedCount = DownloadedGames.Count(d => Helper.DownloadQueue.IsWaiting(d));
                ReadyCount = DownloadedGames.Count(d => d.IsReadyToInstall());
            }
            catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
        }
    }
}
