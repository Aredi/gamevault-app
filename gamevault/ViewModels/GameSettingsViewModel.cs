using GameVault.Core;
using gamevault.Models;
using gamevault.Models.Mapping;
using GameVault.Core.Compatibility;
using gamevault.Helper.Platform;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gamevault.ViewModels
{
    internal class GameSettingsViewModel : ViewModelBase
    {
        #region Privates

        private Game game { get; set; }
        private UpdateGameDto? updateGame { get; set; }
        private string directory { get; set; }
        private ObservableCollection<KeyValuePair<string, string>> m_Executables { get; set; }
        private string launchParameter { get; set; }
        private MinimalGame[]? remapSearchResults { get; set; }
        private bool backgroundImageChanged { get; set; }
        private bool boxArtImageChanged { get; set; }
        private Avalonia.Media.IImage backgroundImageSource { get; set; }
        private Avalonia.Media.IImage boxArtImageSource { get; set; }
        private string diskSize { get; set; }
        private MetadataProviderDto[]? metadataProviders { get; set; }
        private bool metadataProvidersLoaded { get; set; }
        private int selectedMetadataProviderIndex { get; set; }
        private string? installedGameVersion { get; set; }
        #endregion
        public Game Game
        {
            get { return game; }
            set { game = value; OnPropertyChanged(); }
        }
        public UpdateGameDto? UpdateGame
        {
            get { return updateGame; }
            set { updateGame = value; OnPropertyChanged(); }
        }
        public string Directory
        {
            get { return directory; }
            set { directory = value; OnPropertyChanged(); }
        }
        public ObservableCollection<KeyValuePair<string, string>> Executables
        {
            get
            {
                if (m_Executables == null)
                {
                    m_Executables = new ObservableCollection<KeyValuePair<string, string>>();
                }
                return m_Executables;
            }
            set { m_Executables = value; OnPropertyChanged(); }
        }
        #region Compatibility (Linux)
        public bool IsLinux => OperatingSystem.IsLinux();
        private List<CompatibilityTool> gameCompatibilityTools = new();
        /// <summary>"Default (…)" followed by every tool, as in Steam's "Force a specific compatibility tool".</summary>
        public List<CompatibilityTool> GameCompatibilityTools
        {
            get => gameCompatibilityTools;
            set { gameCompatibilityTools = value; OnPropertyChanged(); }
        }
        private CompatibilityTool? selectedGameCompatibilityTool;
        public CompatibilityTool? SelectedGameCompatibilityTool
        {
            get => selectedGameCompatibilityTool;
            set { selectedGameCompatibilityTool = value; OnPropertyChanged(); }
        }
        public WinePrefixMode[] WinePrefixModes => Enum.GetValues<WinePrefixMode>();
        private WinePrefixMode selectedWinePrefixMode;
        public WinePrefixMode SelectedWinePrefixMode
        {
            get => selectedWinePrefixMode;
            set { selectedWinePrefixMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(WinePrefixModeIndex)); OnPropertyChanged(nameof(IsSeparatePrefix)); }
        }
        public bool IsSeparatePrefix => selectedWinePrefixMode == WinePrefixMode.Game;
        public int WinePrefixModeIndex
        {
            get => (int)selectedWinePrefixMode;
            set => SelectedWinePrefixMode = (WinePrefixMode)Math.Max(0, value);
        }
        private int umuModeIndex;
        /// <summary>0 = automatic (umu database), 1 = custom umu id, 2 = no fixes.</summary>
        public int UmuModeIndex
        {
            get => umuModeIndex;
            set { umuModeIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsCustomUmuId)); }
        }
        public bool IsCustomUmuId => umuModeIndex == 1;
        private string customUmuId = "";
        public string CustomUmuId
        {
            get => customUmuId;
            set { customUmuId = value; OnPropertyChanged(); }
        }
        private string umuStatus = "";
        public string UmuStatus
        {
            get => umuStatus;
            set { umuStatus = value; OnPropertyChanged(); }
        }
        private string winetricksVerbs = "";
        public string WinetricksVerbs
        {
            get => winetricksVerbs;
            set { winetricksVerbs = value; OnPropertyChanged(); }
        }
        private string winetricksStatus = "";
        public string WinetricksStatus
        {
            get => winetricksStatus;
            set { winetricksStatus = value; OnPropertyChanged(); }
        }
        private string gamePrefixPath = "";
        public string GamePrefixPath
        {
            get => gamePrefixPath;
            set { gamePrefixPath = value; OnPropertyChanged(); }
        }
        #endregion
        public string LaunchParameter
        {
            get { return launchParameter; }
            set { launchParameter = value; OnPropertyChanged(); }
        }
        public MinimalGame[]? RemapSearchResults
        {
            get { return remapSearchResults; }
            set { remapSearchResults = value; OnPropertyChanged(); }
        }
        public bool BackgroundImageChanged
        {
            get { return backgroundImageChanged; }
            set { backgroundImageChanged = value; OnPropertyChanged(); }
        }
        public bool GameCoverImageChanged
        {
            get { return boxArtImageChanged; }
            set { boxArtImageChanged = value; OnPropertyChanged(); }
        }
        public Avalonia.Media.IImage BackgroundImageSource
        {
            get { return backgroundImageSource; }
            set { backgroundImageSource = value; OnPropertyChanged(); BackgroundImageChanged = true; }
        }
        public Avalonia.Media.IImage GameCoverImageSource
        {
            get { return boxArtImageSource; }
            set { boxArtImageSource = value; OnPropertyChanged(); GameCoverImageChanged = true; }
        }
        public string DiskSize
        {
            get { return diskSize; }
            set { diskSize = value; OnPropertyChanged(); }
        }
        public MetadataProviderDto[]? MetadataProviders
        {
            get { return metadataProviders; }
            set { metadataProviders = value; OnPropertyChanged(); }
        }
        public bool MetadataProvidersLoaded
        {
            get { return metadataProvidersLoaded; }
            set { metadataProvidersLoaded = value; OnPropertyChanged(); }
        }
        public int SelectedMetadataProviderIndex
        {
            get { return selectedMetadataProviderIndex; }
            set { selectedMetadataProviderIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentShownMappedGame)); RemapSearchResults = null; }
        }
        public GameMetadata? CurrentShownMappedGame
        {
            get
            {
                try
                {
                    if (MetadataProviders?.Length > 0)
                    {
                        MetadataProviderDto currentSelectedProvider = MetadataProviders?[SelectedMetadataProviderIndex];
                        return Game.ProviderMetadata.Where(m => m.ProviderSlug == currentSelectedProvider.Slug).First();
                    }
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
                return new GameMetadata();
            }
            set
            {
                OnPropertyChanged();
            }
        }
        public string? InstalledGameVersion
        {
            get { return installedGameVersion; }
            set { installedGameVersion = value; OnPropertyChanged(); }
        }
    }
}
