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
        SyncSteamShortcuts,
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
        LastSeenGameId,
        //Download queue
        MaxConcurrentDownloads,
        DownloadScheduleEnabled,
        DownloadScheduleStart,
        DownloadScheduleEnd,
        //Admin: game publishing
        PublishTargetDirectory
    }
    public static class Globals
    {
        internal static string[] SupportedExecutables = new string[] { "EXE", "BAT", "VBS", "COM", "CMD", "INF", "IPA", "OSX", "PIF", "RUN", "WSH", "LNK", "SH", "MSI" };
        internal static string[] SupportedArchives = new string[] { ".7z", ".xz", ".bz2", ".gz", ".tar", ".zip", ".wim", ".ar", ".arj", ".cab", ".chm", ".cpio", ".cramfs", ".dmg", ".ext", ".fat", ".gpt", ".hfs", ".ihex", ".iso", ".lzh", ".lzma", ".mbr", ".msi", ".nsis", ".ntfs", ".qcow2", ".rar", ".rpm", ".squashfs", ".udf", ".uefi", ".vdi", ".vhd", ".vmdk", ".wim", ".xar", ".z" };
    }
}
