using gamevault.Converter;
using gamevault.Helper;
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
    internal class GameDownloadViewModel : ViewModelBase
    {
        #region PrivateMembers
        private Game m_Game { get; set; }
        private string m_State { get; set; }
        private int m_GameDownloadProgress { get; set; }
        private int m_GameExtractionProgress { get; set; }
        private int installationStepperProgress { get; set; } = -1;
        private string m_DownloadInfo { get; set; }
        private string m_ExtractionInfo { get; set; }
        private string m_InstallPath { get; set; }
        private double totalDataSize { get; set; }
        private bool m_IsDownloadPaused { get; set; }
        private bool m_IsDownloadResumed { get; set; } = true;
        private bool m_DownloadUIVisibility { get; set; }
        private bool m_ExtractionUIVisibility { get; set; }
        private bool m_DownloadFailedVisibility { get; set; }
        private bool? createShortcut { get; set; }

        #endregion

        public Game Game
        {
            get { return m_Game; }
            set { m_Game = value; OnPropertyChanged(); }
        }
        public string State
        {
            get { return m_State; }
            set { m_State = value; OnPropertyChanged(); }
        }
        public int GameDownloadProgress
        {
            get { return m_GameDownloadProgress; }
            set { m_GameDownloadProgress = value; OnPropertyChanged(); }
        }
        public int GameExtractionProgress
        {
            get { return m_GameExtractionProgress; }
            set { m_GameExtractionProgress = value; OnPropertyChanged(); }
        }
        public int InstallationStepperProgress
        {
            get { return installationStepperProgress; }
            set { installationStepperProgress = value; OnPropertyChanged(); }
        }
        public string DownloadInfo
        {
            get { return m_DownloadInfo; }
            set { m_DownloadInfo = value; OnPropertyChanged(); }
        }
        public string ExtractionInfo
        {
            get { return m_ExtractionInfo; }
            set { m_ExtractionInfo = value; OnPropertyChanged(); }
        }

        public bool DownloadUIVisibility
        {
            get { return m_DownloadUIVisibility; }
            set { m_DownloadUIVisibility = value; OnPropertyChanged(); }
        }
        public bool ExtractionUIVisibility
        {
            get { return m_ExtractionUIVisibility; }
            set { m_ExtractionUIVisibility = value; OnPropertyChanged(); }
        }
        public bool DownloadFailedVisibility
        {
            get { return m_DownloadFailedVisibility; }
            set { m_DownloadFailedVisibility = value; OnPropertyChanged(); }
        }
        public string InstallPath
        {
            get { return m_InstallPath; }
            set { m_InstallPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(InstallerInstallPath)); }
        }
        /// <summary>
        /// The installation folder as a Windows installer sees it (a Z:\ path under Wine/Proton on Linux).
        /// </summary>
        public string InstallerInstallPath => OperatingSystem.IsWindows() || string.IsNullOrEmpty(m_InstallPath)
            ? m_InstallPath
            : "Z:" + m_InstallPath.Replace('/', '\\');
        public double TotalDataSize
        {
            get { return totalDataSize; }
            set { totalDataSize = value; OnPropertyChanged(); }
        }
        public bool IsDownloadPaused
        {
            get { return m_IsDownloadPaused; }
            set { m_IsDownloadPaused = value; OnPropertyChanged(); }
        }
        public bool IsDownloadResumed
        {
            get { return m_IsDownloadResumed; }
            set { m_IsDownloadResumed = value; OnPropertyChanged(); }
        }
        public bool? CreateShortcut
        {
            get
            {
                if (createShortcut == null)
                {
                    createShortcut = Preferences.Get(AppConfigKey.CreateDesktopShortcut, LoginManager.Instance.GetUserProfile().UserConfigFile) == "1";
                }
                return createShortcut;
            }

            set
            {
                createShortcut = value; OnPropertyChanged();
                Preferences.Set(AppConfigKey.CreateDesktopShortcut, createShortcut == true ? "1" : "0", LoginManager.Instance.GetUserProfile().UserConfigFile);
            }
        }
        public string[] SupportedArchives
        {
            get
            {
                return Globals.SupportedArchives;
            }
        }
        public Dictionary<GameType, string?> GameTypes
        {
            get
            {
                return Enum.GetValues(typeof(GameType)).Cast<GameType>().Where(v => v != GameType.UNDETECTABLE).ToDictionary(v => v, v => new EnumDescriptionConverter().Convert(v, null, null, null) as string);
            }
        }
    }
}
