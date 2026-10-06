using GameVault.Core;
using GameVault.Core.Compatibility;
using gamevault.Helper.Platform;
using gamevault.Helper;
using gamevault.Helper.Integrations;
using gamevault.Models;
using gamevault.UserControls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.ViewModels
{
    internal class SettingsViewModel : ViewModelBase
    {
        #region Singleton
        private static SettingsViewModel instance = null;
        private static readonly object padlock = new object();

        public static SettingsViewModel Instance
        {
            get
            {
                lock (padlock)
                {
                    if (instance == null)
                    {
                        instance = new SettingsViewModel();
                    }
                    return instance;
                }
            }
        }
        #endregion
        #region PrivateMembers       
        private string m_UserName { get; set; }
        private bool m_BackgroundStart { get; set; }
        private bool m_LibStartup { get; set; }
        private bool m_AutoExtract { get; set; }
        private bool autoInstallPortable { get; set; }
        private bool autoDeletePortableGameFiles { get; set; }
        private bool retainLibarySortByAndOrderBy { get; set; }
        private bool sendAnonymousAnalytics { get; set; }
        private string m_ServerUrl { get; set; }
        private float m_ImageCacheSize { get; set; }
        private float m_OfflineCacheSize { get; set; }
        private long m_DownloadLimit { get; set; }
        private long m_DownloadLimitUIValue { get; set; }
        private string[] ignoreList { get; set; }
        private ObservableCollection<ThemeItem> themes { get; set; }
        private ObservableCollection<ThemeItem> communityThemes { get; set; }
        private bool showMappedTitle { get; set; }
        private bool syncSteamShortcuts { get; set; }
        private bool syncDiscordPresence { get; set; }
        private bool cloudSaves { get; set; }
        private bool isCommunityThemeSelected { get; set; }
        private bool usePrimaryCloudSaveManifest { get; set; }
        private ObservableCollection<DirectoryEntry> customCloudSaveManifests;
        private ObservableCollection<DirectoryEntry> rootDirectories;
        private bool mountIso { get; set; }
        //DevMode
        private bool devModeEnabled { get; set; }
        private bool devTargetPhalcodeTestBackend { get; set; }
        //

        #endregion

        public SettingsViewModel()
        {

        }
        private string userConfigFile;
        public void Init()
        {
            userConfigFile = LoginManager.Instance.GetUserProfile().UserConfigFile;

            UserName = Preferences.Get(AppConfigKey.Username, userConfigFile);
            ServerUrl = Preferences.Get(AppConfigKey.ServerUrl, userConfigFile, true);

            string rootDirectoriesString = Preferences.Get(AppConfigKey.RootDirectories, userConfigFile);
            RootDirectories = string.IsNullOrWhiteSpace(rootDirectoriesString) ? null! : new ObservableCollection<DirectoryEntry>(rootDirectoriesString.Split(';').Select(part => new DirectoryEntry { Uri = part }).ToList());

            string showMappedTitleString = Preferences.Get(AppConfigKey.ShowMappedTitle, userConfigFile);
            showMappedTitle = showMappedTitleString == "1" || showMappedTitleString == "";
            //Setting the private members to avoid writing to the user config file over and over again
            m_BackgroundStart = (Preferences.Get(AppConfigKey.BackgroundStart, userConfigFile) == "1"); OnPropertyChanged(nameof(BackgroundStart));
            notifyNewGames = Preferences.Get(AppConfigKey.NotifyNewGames, userConfigFile) != "0"; OnPropertyChanged(nameof(NotifyNewGames));
            maxConcurrentDownloads = int.TryParse(Preferences.Get(AppConfigKey.MaxConcurrentDownloads, userConfigFile), out int maxDownloads) ? Math.Clamp(maxDownloads, 0, 10) : 0;
            downloadScheduleEnabled = Preferences.Get(AppConfigKey.DownloadScheduleEnabled, userConfigFile) == "1";
            string scheduleStart = Preferences.Get(AppConfigKey.DownloadScheduleStart, userConfigFile);
            string scheduleEnd = Preferences.Get(AppConfigKey.DownloadScheduleEnd, userConfigFile);
            downloadScheduleStart = string.IsNullOrEmpty(scheduleStart) ? "01:00" : scheduleStart;
            downloadScheduleEnd = string.IsNullOrEmpty(scheduleEnd) ? "07:00" : scheduleEnd;
            OnPropertyChanged(nameof(MaxConcurrentDownloadsIndex)); OnPropertyChanged(nameof(DownloadScheduleEnabled)); OnPropertyChanged(nameof(DownloadScheduleStart)); OnPropertyChanged(nameof(DownloadScheduleEnd));
            // On unless the user turned it off: downloaded games are ready without an extra click
            m_AutoExtract = (Preferences.Get(AppConfigKey.AutoExtract, userConfigFile) != "0"); OnPropertyChanged(nameof(AutoExtract));
            autoDeletePortableGameFiles = Preferences.Get(AppConfigKey.AutoDeletePortable, userConfigFile) == "1"; OnPropertyChanged(nameof(AutoDeletePortableGameFiles));
            retainLibarySortByAndOrderBy = Preferences.Get(AppConfigKey.RetainLibarySortByAndOrderBy, userConfigFile) == "1"; OnPropertyChanged(nameof(RetainLibarySortByAndOrderBy));

            string analyticsPreference = Preferences.Get(AppConfigKey.SendAnonymousAnalytics, userConfigFile);
            sendAnonymousAnalytics = analyticsPreference == "1"; OnPropertyChanged(nameof(SendAnonymousAnalytics));

            syncSteamShortcuts = Preferences.Get(AppConfigKey.SyncSteamShortcuts, userConfigFile) == "1"; OnPropertyChanged(nameof(SyncSteamShortcuts));
            syncDiscordPresence = Preferences.Get(AppConfigKey.SyncDiscordPresence, userConfigFile) == "1"; OnPropertyChanged(nameof(SyncDiscordPresence));
            cloudSaves = Preferences.Get(AppConfigKey.CloudSaves, userConfigFile) == "1"; OnPropertyChanged(nameof(CloudSaves));

            string autoInstallPortableStr = Preferences.Get(AppConfigKey.AutoInstallPortable, userConfigFile);
            if (string.IsNullOrWhiteSpace(autoInstallPortableStr) || autoInstallPortableStr == "1")
            {
                autoInstallPortable = true; OnPropertyChanged(nameof(AutoInstallPortable));
            }

            string libstartupStr = Preferences.Get(AppConfigKey.LibStartup, userConfigFile);
            if (libstartupStr == string.Empty)
            {
                LibStartup = true;
            }
            else
            {
                m_LibStartup = (libstartupStr == "1"); OnPropertyChanged(nameof(LibStartup));
            }
            if (long.TryParse(Preferences.Get(AppConfigKey.DownloadLimit, userConfigFile), out long downloadLimitResult))
            {
                DownloadLimit = downloadLimitResult;
                DownloadLimitUIValue = DownloadLimit;
            }
            else
            {
                DownloadLimit = 0;
                DownloadLimitUIValue = 0;
            }
            string usePrimaryCloudSaveManifestString = Preferences.Get(AppConfigKey.UsePrimaryCloudSaveManifest, userConfigFile);
            usePrimaryCloudSaveManifest = usePrimaryCloudSaveManifestString == "1" || usePrimaryCloudSaveManifestString == "";

            string customCloudSaveManifestsString = Preferences.Get(AppConfigKey.CustomCloudSaveManifests, userConfigFile);
            customCloudSaveManifests = string.IsNullOrWhiteSpace(customCloudSaveManifestsString) ? null! : new ObservableCollection<DirectoryEntry>(customCloudSaveManifestsString.Split(';').Select(part => new DirectoryEntry { Uri = part }).ToList());

            string mountIsoString = Preferences.Get(AppConfigKey.MountIso, userConfigFile);
            mountIso = mountIsoString == "1";

            //Linux compatibility layer
            CompatibilitySettings.DefaultToolId = ReadDefaultCompatibilityTool();
            CompatibilitySettings.WinePrefix = Preferences.Get(AppConfigKey.LinuxWinePrefix, userConfigFile);
            CompatibilitySettings.CustomCommand = Preferences.Get(AppConfigKey.LinuxCustomRunner, userConfigFile);
            if (OperatingSystem.IsLinux())
                RefreshCompatibilityTools();
            OnPropertyChanged(nameof(WinePrefix)); OnPropertyChanged(nameof(CustomRunnerCommand));

            //DevMode
            devModeEnabled = Preferences.Get(AppConfigKey.DevModeEnabled, userConfigFile) == "1"; OnPropertyChanged(nameof(DevModeEnabled));
            devTargetPhalcodeTestBackend = Preferences.Get(AppConfigKey.DevTargetPhalcodeTestBackend, userConfigFile) == "1"; OnPropertyChanged(nameof(DevTargetPhalcodeTestBackend));
            //          
        }
        public async Task InitIgnoreList()
        {
            string ignoreListFile = LoginManager.Instance.GetUserProfile().IgnoreList;
            try
            {
                if (!File.Exists(ignoreListFile))
                {
                    string response = await WebHelper.GetAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/progresses/ignorefile");
                    string[] ignoreList = JsonSerializer.Deserialize<string[]>(response);
                    if (ignoreList != null || ignoreList?.Length > 0)
                    {
                        IgnoreList = ignoreList.Where(s => !string.IsNullOrEmpty(s)).ToArray(); //Make sure server ignore list don't contain empty strings, because this will exclude any file which is compared to the ignore list
                        Preferences.Set("IL", response.Replace("\n", ""), ignoreListFile);
                    }
                }
                else
                {
                    string result = Preferences.Get("IL", ignoreListFile);
                    IgnoreList = JsonSerializer.Deserialize<string[]>(result);
                }
            }
            catch
            {
                try
                {
                    string result = Preferences.Get("IL", ignoreListFile);
                    IgnoreList = JsonSerializer.Deserialize<string[]>(result);
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
            }
        }

        public string UserName
        {
            get { return m_UserName; }
            set { m_UserName = value; OnPropertyChanged(); }
        }
        #region Storage cleanup
        public ObservableCollection<StorageCleanupItem> StorageCleanupItems { get; } = new();
        private string storageCleanupSummary = "";
        public string StorageCleanupSummary
        {
            get => storageCleanupSummary;
            set { storageCleanupSummary = value; OnPropertyChanged(); }
        }
        private bool isStorageCleanupRunning;
        public bool IsStorageCleanupRunning
        {
            get => isStorageCleanupRunning;
            set { isStorageCleanupRunning = value; OnPropertyChanged(); }
        }
        public void RefreshStorageCleanupSummary()
        {
            var selected = StorageCleanupItems.Where(i => i.IsSelected).ToList();
            StorageCleanupSummary = StorageCleanupItems.Count == 0
                ? "Nothing to clean up."
                : $"{selected.Count} of {StorageCleanupItems.Count} selected: {GameVault.Core.Storage.StorageCleanup.FormatSize(selected.Sum(i => i.Candidate.Size))} can be freed.";
        }
        #endregion
        #region Download queue
        private int maxConcurrentDownloads;
        /// <summary>0 = no limit (every download starts at once).</summary>
        public int MaxConcurrentDownloads => maxConcurrentDownloads;
        public string[] MaxConcurrentDownloadsValues { get; } = { "Unlimited", "1", "2", "3", "4", "5" };
        public int MaxConcurrentDownloadsIndex
        {
            get => maxConcurrentDownloads;
            set
            {
                maxConcurrentDownloads = Math.Clamp(value, 0, 5);
                Preferences.Set(AppConfigKey.MaxConcurrentDownloads, maxConcurrentDownloads.ToString(), userConfigFile);
                OnPropertyChanged();
                Helper.DownloadQueue.Advance();
            }
        }
        private bool downloadScheduleEnabled;
        public bool DownloadScheduleEnabled
        {
            get => downloadScheduleEnabled;
            set { downloadScheduleEnabled = value; Preferences.Set(AppConfigKey.DownloadScheduleEnabled, value ? "1" : "0", userConfigFile); OnPropertyChanged(); Helper.DownloadQueue.Advance(); }
        }
        private string downloadScheduleStart = "01:00";
        public string DownloadScheduleStart
        {
            get => downloadScheduleStart;
            set
            {
                downloadScheduleStart = value;
                if (GameVault.Core.Downloads.DownloadSchedule.TryParseTime(value, out _))
                {
                    Preferences.Set(AppConfigKey.DownloadScheduleStart, value.Trim(), userConfigFile);
                    Helper.DownloadQueue.Advance();
                }
                OnPropertyChanged();
            }
        }
        private string downloadScheduleEnd = "07:00";
        public string DownloadScheduleEnd
        {
            get => downloadScheduleEnd;
            set
            {
                downloadScheduleEnd = value;
                if (GameVault.Core.Downloads.DownloadSchedule.TryParseTime(value, out _))
                {
                    Preferences.Set(AppConfigKey.DownloadScheduleEnd, value.Trim(), userConfigFile);
                    Helper.DownloadQueue.Advance();
                }
                OnPropertyChanged();
            }
        }
        public GameVault.Core.Downloads.DownloadSchedule DownloadSchedule =>
            GameVault.Core.Downloads.DownloadSchedule.TryParseTime(downloadScheduleStart, out TimeOnly start) && GameVault.Core.Downloads.DownloadSchedule.TryParseTime(downloadScheduleEnd, out TimeOnly end)
                ? new GameVault.Core.Downloads.DownloadSchedule(downloadScheduleEnabled, start, end)
                : GameVault.Core.Downloads.DownloadSchedule.Always;
        #endregion
        private bool notifyNewGames = true;
        /// <summary>Desktop notification when games are added to the server (checked every 15 minutes).</summary>
        public bool NotifyNewGames
        {
            get => notifyNewGames;
            set { notifyNewGames = value; Preferences.Set(AppConfigKey.NotifyNewGames, value ? "1" : "0", userConfigFile); OnPropertyChanged(); }
        }
        public bool BackgroundStart
        {
            get { return m_BackgroundStart; }
            set
            {
                m_BackgroundStart = value;
                OnPropertyChanged();
                string stringValue = "1";
                if (!m_BackgroundStart)
                {
                    stringValue = "0";
                }
                Preferences.Set(AppConfigKey.BackgroundStart, stringValue, userConfigFile);
            }
        }
        public bool LibStartup
        {
            get { return m_LibStartup; }
            set
            {
                m_LibStartup = value;
                OnPropertyChanged();
                string stringValue = "1";
                if (!m_LibStartup)
                {
                    stringValue = "0";
                }
                Preferences.Set(AppConfigKey.LibStartup, stringValue, userConfigFile);
            }
        }
        public bool AutoExtract
        {
            get { return m_AutoExtract; }
            set
            {
                m_AutoExtract = value;
                OnPropertyChanged();
                string stringValue = "1";
                if (!m_AutoExtract)
                {
                    stringValue = "0";
                }
                Preferences.Set(AppConfigKey.AutoExtract, stringValue, userConfigFile);
            }
        }
        public bool AutoInstallPortable
        {
            get { return autoInstallPortable; }
            set
            {
                autoInstallPortable = value;
                OnPropertyChanged();
                string stringValue = "1";
                if (!autoInstallPortable)
                {
                    stringValue = "0";
                }
                Preferences.Set(AppConfigKey.AutoInstallPortable, stringValue, userConfigFile);
            }
        }
        public bool AutoDeletePortableGameFiles
        {
            get { return autoDeletePortableGameFiles; }
            set
            {
                autoDeletePortableGameFiles = value;
                OnPropertyChanged();
                string stringValue = "1";
                if (!autoDeletePortableGameFiles)
                {
                    stringValue = "0";
                }
                Preferences.Set(AppConfigKey.AutoDeletePortable, stringValue, userConfigFile);
            }
        }
        public bool RetainLibarySortByAndOrderBy
        {
            get { return retainLibarySortByAndOrderBy; }
            set
            {
                retainLibarySortByAndOrderBy = value;
                OnPropertyChanged();
                string stringValue = "1";
                if (!retainLibarySortByAndOrderBy)
                {
                    stringValue = "0";
                }
                Preferences.Set(AppConfigKey.RetainLibarySortByAndOrderBy, stringValue, userConfigFile);
            }
        }
        public bool SendAnonymousAnalytics
        {
            get { return sendAnonymousAnalytics; }
            set
            {
                sendAnonymousAnalytics = value;
                OnPropertyChanged();
                string stringValue = "1";
                if (!sendAnonymousAnalytics)
                {
                    stringValue = "0";
                }
                Preferences.Set(AppConfigKey.SendAnonymousAnalytics, stringValue, userConfigFile);
            }
        }

        public string ServerUrl
        {
            get { return m_ServerUrl; }
            set { m_ServerUrl = value; OnPropertyChanged(); }
        }
        public float ImageCacheSize
        {
            get { return m_ImageCacheSize; }
            set { m_ImageCacheSize = value; OnPropertyChanged(); }
        }
        public float OfflineCacheSize
        {
            get { return m_OfflineCacheSize; }
            set { m_OfflineCacheSize = value; OnPropertyChanged(); }
        }
        public long DownloadLimit
        {
            get { return m_DownloadLimit; }
            set { m_DownloadLimit = value; OnPropertyChanged(); }
        }
        public long DownloadLimitUIValue
        {
            get { return m_DownloadLimitUIValue; }
            set { m_DownloadLimitUIValue = value; OnPropertyChanged(); }
        }
        public string[] IgnoreList
        {
            get
            {
                if (ignoreList == null)
                {
                    ignoreList = new string[0];
                }
                return ignoreList;
            }
            set { ignoreList = value; OnPropertyChanged(); }
        }
        public bool ShowMappedTitle
        {
            get
            {
                return showMappedTitle;
            }
            set { showMappedTitle = value; Preferences.Set(AppConfigKey.ShowMappedTitle, showMappedTitle ? "1" : "0", userConfigFile); OnPropertyChanged(); }
        }
        public bool SyncSteamShortcuts
        {
            get
            {
                return syncSteamShortcuts;
            }
            set { syncSteamShortcuts = value; Preferences.Set(AppConfigKey.SyncSteamShortcuts, syncSteamShortcuts ? "1" : "0", userConfigFile); OnPropertyChanged(); }
        }
        public bool SyncDiscordPresence
        {
            get
            {
                return syncDiscordPresence;
            }
            set { syncDiscordPresence = value; Preferences.Set(AppConfigKey.SyncDiscordPresence, syncDiscordPresence ? "1" : "0", userConfigFile); OnPropertyChanged(); }
        }
        public bool CloudSaves
        {
            get
            {
                return cloudSaves;
            }
            set { cloudSaves = value; Preferences.Set(AppConfigKey.CloudSaves, cloudSaves ? "1" : "0", userConfigFile); OnPropertyChanged(); }
        }
        public bool IsCommunityThemeSelected
        {
            get
            {
                return isCommunityThemeSelected;
            }
            set { isCommunityThemeSelected = value; OnPropertyChanged(); }
        }
        public ObservableCollection<ThemeItem> Themes
        {
            get { return themes; }
            set { themes = value; OnPropertyChanged(); }
        }
        public ObservableCollection<ThemeItem> CommunityThemes
        {
            get { return communityThemes; }
            set { communityThemes = value; OnPropertyChanged(); }
        }

        public bool UsePrimaryCloudSaveManifest
        {
            get
            {
                return usePrimaryCloudSaveManifest;
            }
            set { usePrimaryCloudSaveManifest = value; Preferences.Set(AppConfigKey.UsePrimaryCloudSaveManifest, usePrimaryCloudSaveManifest ? "1" : "0", userConfigFile); OnPropertyChanged(); }
        }
        public ObservableCollection<DirectoryEntry> CustomCloudSaveManifests
        {
            get
            {
                if (customCloudSaveManifests == null)
                {
                    customCloudSaveManifests = new ObservableCollection<DirectoryEntry>();
                }
                return customCloudSaveManifests;
            }
            set { customCloudSaveManifests = value; OnPropertyChanged(); }
        }
        public ObservableCollection<DirectoryEntry> RootDirectories
        {
            get
            {
                if (rootDirectories == null)
                {
                    rootDirectories = new ObservableCollection<DirectoryEntry>();
                }
                return rootDirectories;
            }
            set { rootDirectories = value; OnPropertyChanged(); }
        }
        public bool MountIso
        {
            get
            {
                return mountIso;
            }
            set { mountIso = value; Preferences.Set(AppConfigKey.MountIso, mountIso ? "1" : "0", userConfigFile); OnPropertyChanged(); }
        }
        public async Task<string> SelectDownloadPath()
        {
            string? selectedPath = await StorageHelper.PickFolderAsync("Select a folder");
            if (string.IsNullOrEmpty(selectedPath) || !Directory.Exists(selectedPath))
                return "";
            try
            {
                string accessCheck = Path.Combine(selectedPath, "accesscheck.file");
                File.Create(accessCheck).Close();
                File.Delete(accessCheck);
            }
            catch (Exception)
            {
                MainWindowViewModel.Instance.AppBarText = $"Access to the path {selectedPath} is denied";
            }
            return selectedPath;
        }
        public string Version
        {
            get
            {
                return Assembly.GetExecutingAssembly().GetName().Version.ToString();
            }
        }
        #region Linux
        public bool IsLinux => OperatingSystem.IsLinux();

        /// <summary>
        /// Default tool of this profile. Profiles of older versions stored a runner (Auto/Proton/Wine/Custom)
        /// and an optional Proton path instead.
        /// </summary>
        private string ReadDefaultCompatibilityTool()
        {
            string toolId = Preferences.Get(AppConfigKey.LinuxCompatibilityTool, userConfigFile);
            if (!string.IsNullOrEmpty(toolId))
                return toolId;
            string protonPath = Preferences.Get(AppConfigKey.LinuxProtonPath, userConfigFile);
            return Preferences.Get(AppConfigKey.LinuxRunner, userConfigFile) switch
            {
                "Proton" => string.IsNullOrWhiteSpace(protonPath) ? CompatibilityToolId.UmuLatest : CompatibilityToolId.ForProton(protonPath),
                "Wine" => CompatibilityToolId.SystemWine,
                "Custom" => CompatibilityToolId.Custom,
                _ => CompatibilityToolId.Auto,
            };
        }

        private ObservableCollection<CompatibilityTool> compatibilityTools = new();
        /// <summary>Every tool a game can use (automatic choice, umu, system Wine, detected builds, custom command).</summary>
        public ObservableCollection<CompatibilityTool> CompatibilityTools
        {
            get => compatibilityTools;
            private set { compatibilityTools = value; OnPropertyChanged(); }
        }
        /// <summary>Proton / Wine builds found on this computer (the "manager" list).</summary>
        public List<CompatibilityTool> InstalledCompatibilityBuilds => CompatibilityTools.Where(t => t.Path != null && t.Id != CompatibilityToolId.SystemWine).ToList();

        public void RefreshCompatibilityTools()
        {
            CompatibilityTools = new ObservableCollection<CompatibilityTool>(CompatibilityManager.GetTools(refresh: true));
            OnPropertyChanged(nameof(InstalledCompatibilityBuilds));
            OnPropertyChanged(nameof(DefaultCompatibilityTool));
            OnPropertyChanged(nameof(DetectedCompatibilityTools));
        }

        public CompatibilityTool? DefaultCompatibilityTool
        {
            get => CompatibilityTools.FirstOrDefault(t => t.Id == CompatibilitySettings.DefaultToolId);
            set
            {
                if (value == null)
                    return;
                CompatibilitySettings.DefaultToolId = value.Id;
                Preferences.Set(AppConfigKey.LinuxCompatibilityTool, value.Id, userConfigFile);
                OnPropertyChanged(); OnPropertyChanged(nameof(IsCustomRunner)); OnPropertyChanged(nameof(DetectedCompatibilityTools));
            }
        }
        public bool IsCustomRunner => CompatibilitySettings.DefaultToolId == CompatibilityToolId.Custom;
        public string WinePrefix
        {
            get => CompatibilitySettings.WinePrefix;
            set { CompatibilitySettings.WinePrefix = value?.Trim() ?? ""; Preferences.Set(AppConfigKey.LinuxWinePrefix, CompatibilitySettings.WinePrefix, userConfigFile); OnPropertyChanged(); OnPropertyChanged(nameof(EffectiveWinePrefix)); }
        }
        public string EffectiveWinePrefix => CompatibilitySettings.EffectiveWinePrefix;
        public string CustomRunnerCommand
        {
            get => CompatibilitySettings.CustomCommand;
            set { CompatibilitySettings.CustomCommand = value ?? ""; Preferences.Set(AppConfigKey.LinuxCustomRunner, CompatibilitySettings.CustomCommand, userConfigFile); OnPropertyChanged(); }
        }
        public string DetectedCompatibilityTools
        {
            get
            {
                if (!OperatingSystem.IsLinux())
                    return "";
                string Found(string? path) => path ?? "not found";
                string startsWith;
                try { startsWith = CompatibilityManager.Resolve(CompatibilitySettings.DefaultToolId).Name; }
                catch (Exception ex) { startsWith = ex.Message; }
                return $"umu-run: {Found(Helper.Platform.PlatformInfo.FindInPath("umu-run"))}\n" +
                       $"wine: {Found(Helper.Platform.PlatformInfo.FindInPath("wine"))}\n" +
                       $"7-Zip: {Found(Helper.Platform.ToolLocator.SevenZip())}\n" +
                       $"Ludusavi: {Helper.Platform.ToolLocator.Ludusavi()?.FileName ?? "not found"}\n" +
                       $"Games without a tool of their own start with: {startsWith}";
            }
        }

        // Download manager
        public ToolFlavor[] ToolFlavors => Enum.GetValues<ToolFlavor>();
        private ToolFlavor selectedToolFlavor = ToolFlavor.GeProton;
        public ToolFlavor SelectedToolFlavor
        {
            get => selectedToolFlavor;
            set { selectedToolFlavor = value; OnPropertyChanged(); AvailableToolDownloads = new List<DownloadableTool>(); }
        }
        private List<DownloadableTool> availableToolDownloads = new();
        public List<DownloadableTool> AvailableToolDownloads
        {
            get => availableToolDownloads;
            set { availableToolDownloads = value; OnPropertyChanged(); SelectedToolDownload = value.FirstOrDefault(); }
        }
        private DownloadableTool? selectedToolDownload;
        public DownloadableTool? SelectedToolDownload
        {
            get => selectedToolDownload;
            set { selectedToolDownload = value; OnPropertyChanged(); }
        }
        private bool isToolDownloadRunning;
        public bool IsToolDownloadRunning
        {
            get => isToolDownloadRunning;
            set { isToolDownloadRunning = value; OnPropertyChanged(); }
        }
        private double toolDownloadProgress;
        public double ToolDownloadProgress
        {
            get => toolDownloadProgress;
            set { toolDownloadProgress = value; OnPropertyChanged(); }
        }
        private string toolDownloadStatus = "";
        public string ToolDownloadStatus
        {
            get => toolDownloadStatus;
            set { toolDownloadStatus = value; OnPropertyChanged(); }
        }
        #endregion
        //DevMode
        public bool DevModeEnabled
        {
            get
            {
                return devModeEnabled;
            }
            set { devModeEnabled = value; Preferences.Set(AppConfigKey.DevModeEnabled, devModeEnabled ? "1" : "0", userConfigFile); OnPropertyChanged(); }
        }
        public bool DevTargetPhalcodeTestBackend
        {
            get
            {
                return devTargetPhalcodeTestBackend;
            }
            set { devTargetPhalcodeTestBackend = value; Preferences.Set(AppConfigKey.DevTargetPhalcodeTestBackend, devTargetPhalcodeTestBackend ? "1" : "0", userConfigFile); OnPropertyChanged(); }
        }
        //
    }
    public class ThemeItem
    {
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string Author { get; set; }
        public string Path { get; set; }
        public bool IsPlus { get; set; }
    }
}
