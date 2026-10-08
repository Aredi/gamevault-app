using gamevault.Helper;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace gamevault.ViewModels
{
    public enum MainControl
    {
        Library = 0,
        Downloads = 1,
        Community = 2,
        Settings = 3,
        AdminConsole = 4
    }
    internal class MainWindowViewModel : ViewModelBase
    {
        public MainWindowViewModel()
        {
            m_Settings = new SettingsUserControl();
            m_Downloads = new DownloadsUserControl();
            m_Library = new LibraryUserControl();
            m_Community = new CommunityUserControl();
            m_AdminConsole = new AdminConsoleUserControl();
        }
        public void UpdateTaskbarProgress()
        {
            List<int> downloadProgresses = new List<int>();
            foreach (var download in DownloadsViewModel.Instance.DownloadedGames)
            {
                if (download.IsDownloading())
                {
                    downloadProgresses.Add(download.GetDownloadProgress());
                }
            }
            TaskbarProgress = downloadProgresses.Count > 0 ? (downloadProgresses.Average() / 100) : 0.0;
        }
        #region Singleton
        private static MainWindowViewModel instance = null;
        private static readonly object padlock = new object();

        public static MainWindowViewModel Instance
        {
            get
            {
                lock (padlock)
                {
                    if (instance == null)
                    {
                        instance = new MainWindowViewModel();
                    }
                    return instance;
                }
            }
        }
        #endregion
        #region AppBarProperties 
        private bool m_IsAppBarOpen { get; set; }
        public bool IsAppBarOpen
        {
            get { return m_IsAppBarOpen; }
            set { m_IsAppBarOpen = value; OnPropertyChanged(); }
        }
        private string m_AppBarText { get; set; }
        public string AppBarText
        {
            get { return m_AppBarText; }
            // Fixed texts and statuses passed around are translated here; texts with values use Loc.F
            set { m_AppBarText = value == null ? value : Loc.T(value); OnPropertyChanged(); IsAppBarOpen = true; Avalonia.Threading.Dispatcher.UIThread.Post(RestartAppBarTimer); }
        }
        private Avalonia.Threading.DispatcherTimer? appBarTimer;
        private bool appBarHeld;
        private void RestartAppBarTimer()
        {
            if (appBarTimer == null)
            {
                appBarTimer = new Avalonia.Threading.DispatcherTimer();
                appBarTimer.Tick += (_, _) => { appBarTimer.Stop(); IsAppBarOpen = false; };
            }
            // Long messages stay longer: 4 s, plus about one second per line
            appBarTimer.Interval = System.TimeSpan.FromSeconds(System.Math.Clamp(4 + (m_AppBarText?.Length ?? 0) / 40.0, 4, 10));
            appBarTimer.Stop();
            if (!appBarHeld)
                appBarTimer.Start();
        }
        /// <summary>The message stays while the pointer is on it.</summary>
        public void HoldAppBar(bool hold)
        {
            appBarHeld = hold;
            if (hold)
                appBarTimer?.Stop();
            else if (IsAppBarOpen)
                RestartAppBarTimer();
        }
        private User? m_UserAvatar { get; set; }
        public User? UserAvatar
        {
            get { return m_UserAvatar; }
            set
            {
                m_UserAvatar = value; if (m_UserAvatar == null)
                { m_UserAvatar = new User(); }
                OnPropertyChanged();
            }
        }
        #endregion
        #region PrivateMembers     
        private double m_TaskbarProgress = 0;
        private int m_ActiveControlIndex = -1;
        private bool isOffline = false;
        private UserControl m_ActiveControl { get; set; }
        private Control m_Popup { get; set; }
        private SettingsUserControl m_Settings { get; set; }
        private DownloadsUserControl m_Downloads { get; set; }
        private LibraryUserControl m_Library { get; set; }
        private CommunityUserControl m_Community { get; set; }
        private AdminConsoleUserControl m_AdminConsole { get; set; }
        #endregion
        public MainControl LastMainControl { get; set; }
        public double TaskbarProgress
        {
            get { return m_TaskbarProgress; }
            set { m_TaskbarProgress = value; OnPropertyChanged(); }
        }
        public int ActiveControlIndex
        {
            get { return m_ActiveControlIndex; }
            set { m_ActiveControlIndex = value; OnPropertyChanged(); }
        }
        public bool IsOffline
        {
            get { return isOffline; }
            set { isOffline = value; OnPropertyChanged(); }
        }
        public UserControl ActiveControl
        {
            get { return m_ActiveControl; }
            set
            {
                if (m_ActiveControl != null) { m_ActiveControl.IsVisible = false; }
                m_ActiveControl = value;
                if (m_ActiveControl != null)
                {
                    m_ActiveControl.IsVisible = true;
                }
                OnPropertyChanged();
            }
        }
        public Control Popup
        {
            get { return m_Popup; }
            set
            {
                m_Popup = value;
                OnPropertyChanged();
            }
        }
        public void OpenPopup(Control userControl)
        {
            if (MainWindowViewModel.Instance.ActiveControl is GameViewUserControl gameView && userControl is not MediaSlider)//Else the popup would be rendered below the native trailer view (airspace problem)
            {
                gameView.uiMediaSlider.UnloadMediaSlider();
            }
            Popup = userControl;
        }
        public void ClosePopup()
        {
            Popup = null;
            if (ActiveControl != null)
            {
                ActiveControl.Focus();//Bring back focus to the current page to restore keyboard key press.
            }
        }
        internal SettingsUserControl Settings
        {
            get { return m_Settings; }
            private set { m_Settings = value; }
        }
        internal DownloadsUserControl Downloads
        {
            get { return m_Downloads; }
            private set { m_Downloads = value; }
        }
        internal LibraryUserControl Library
        {
            get { return m_Library; }
            private set { m_Library = value; }
        }
        internal CommunityUserControl Community
        {
            get { return m_Community; }
            private set { m_Community = value; }
        }
        internal AdminConsoleUserControl AdminConsole
        {
            get { return m_AdminConsole; }
            private set { m_AdminConsole = value; }
        }
        public void SetActiveControl(MainControl mainControl)
        {
            ActiveControlIndex = (int)mainControl;
        }
        public void SetActiveControl(UserControl userControl)
        {
            ActiveControl = userControl;
            ActiveControlIndex = -1;
        }
        public void UndoActiveControl()
        {
            ActiveControlIndex = (int)LastMainControl;
        }
    }
}
