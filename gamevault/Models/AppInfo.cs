using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace gamevault.Models
{
    public enum AppConfigKey
    {
        Username,
        Password,
        RootDirectories,
        LastSelectedRootDirectory,
        Executable,
        BackgroundStart,
        ServerUrl,
        LibStartup,
        AutoExtract,
        AutoInstallPortable,
        AutoDeletePortable,
        ExtractionFinished,
        RunningInTrayMessage,
        DownloadLimit,
        InstalledGamesOpen,
        LaunchParameter,
        ShowMappedTitle,
        LastPlayed,
        Theme,
        ExtractionPassword,
        DownloadProgress,
        InstalledGamesRows,
        CreateDesktopShortcut,
        MediaSliderVolume,
        UnreadNews,
        NewsHash,
        RetainLibarySortByAndOrderBy,
        LastLibrarySortBy,
        LastLibraryOrderBy,
        SendAnonymousAnalytics,
        LastCommunitySortBy,
        ForcedInstallationType,
        SyncDiscordPresence,
        CloudSaves,
        //DevMode
        DevModeEnabled,
        DevTargetPhalcodeTestBackend,
        //
        InstallationId,
        CustomCloudSaveManifests,
        UsePrimaryCloudSaveManifest,
        MountIso,
        UserID,
        LoginRememberMe,
        LastUserProfile,
        IsLoggedInWithSSO,
        LastImageOptimization,
        SessionToken,
        InstalledGameVersion,
        //Server file of the installed build (gamevault-exec), detects updates
        InstalledGameFile,
        InstalledGameSize,
        //When the cloud save of the installation was last uploaded or restored (gamevault-exec)
        LastCloudSync,
        //Updates already announced ("id:file;...")
        UpdatesNotified,
        AdditionalRequestHeaders,
        //Linux compatibility layer
        LinuxRunner,//Legacy, replaced by LinuxCompatibilityTool
        LinuxWinePrefix,
        LinuxProtonPath,//Legacy, replaced by LinuxCompatibilityTool
        LinuxCustomRunner,
        LinuxCompatibilityTool,
        //Per game (gamevault-exec)
        GameCompatibilityTool,
        GameWinePrefixMode,
        GameUmuId,//"" = look up automatically, "none" = no fixes, else the umu id to use
        GameUmuIdDetected,//cached lookup result, "-" when the umu database does not know the game
        GameWinetricks,
        GameWinetricksApplied,
        //New game notifications
        NotifyNewGames,
        //"0": games go to the last game folder without asking where
        AskInstallLocation,
        LastSeenGameId,
        LastSeenRetroGameId,
        //Status of each game request last seen (id -> status), to tell when one is added or refused
        GameRequestStatuses,
        //This PC for "Install on my PC" (profileconfig): the id the web page asks for
        RemoteDeviceId,
        RommToken,
        RommUsername,
        RommDeviceId,
        //RetroAchievements (profileconfig): the account's name and token, never its password
        RetroAchievementsUser,
        RetroAchievementsToken,
        //Retro consoles shown with the CRT look (profileconfig): "nes,snes"
        RetroCrtSystems,
        //Definition of the 3D consoles (profileconfig): "dolphin=2;eden=1"
        RetroResolution,
        //Language of the consoles that have one (profileconfig): RomM's name, "French"; "" for the client's
        RetroLanguage,
        //Language chosen for games (profileconfig): "559=French;12=Japanese"
        RetroGameLanguages,
        //Controller of the consoles that have several (profileconfig), RetroArch's ids: "wii=769;psx=261"
        RetroControllers,
        //Controller chosen for games (profileconfig): "559=1025"
        RetroGameControllers,
        //Keyboard of the consoles played in RetroArch (profileconfig), RetroArch's key names by RetroPad button: {"snes":{"a":{"Name":"x","Label":"X"}}}
        RetroKeyboard,
        //Download queue
        MaxConcurrentDownloads,
        //Interface language (profileconfig): "", "en", "fr"
        Language,
        DownloadConnections,
        //Progress of each range of a parallel download (gamevault-metadata of the download)
        DownloadParts,
        DownloadFileName,
        DownloadScheduleEnabled,
        DownloadScheduleStart,
        DownloadScheduleEnd,
        //Admin: game publishing
        PublishTargetDirectory,
        PublishUploaderUrl,
        ShareActivity,
        PublishDestination,
        //Library layout: cover size, "gallery" / "shelf", showcase on ("1") or off ("0")
        LibraryCardWidth,
        LibraryLayout,
        LibraryShowcase,
        //Navigation bar folded to its icons ("1", profileconfig)
        SidebarFolded,
        //Server news: when they were last opened (the newer ones are counted on the badge)
        NewsLastSeen
    }
    public static class Globals
    {
        internal static string[] SupportedExecutables = new string[] { "EXE", "BAT", "VBS", "COM", "CMD", "INF", "IPA", "OSX", "PIF", "RUN", "WSH", "LNK", "SH", "MSI" };
        internal static string[] SupportedArchives = new string[] { ".7z", ".xz", ".bz2", ".gz", ".tar", ".zip", ".wim", ".ar", ".arj", ".cab", ".chm", ".cpio", ".cramfs", ".dmg", ".ext", ".fat", ".gpt", ".hfs", ".ihex", ".iso", ".lzh", ".lzma", ".mbr", ".msi", ".nsis", ".ntfs", ".qcow2", ".rar", ".rpm", ".squashfs", ".udf", ".uefi", ".vdi", ".vhd", ".vmdk", ".wim", ".xar", ".z" };
    }
}
