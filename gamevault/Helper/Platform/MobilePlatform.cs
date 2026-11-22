using System;
using System.Threading.Tasks;

namespace gamevault.Helper.Platform
{
    /// <summary>
    /// What the Android app does itself: no other programs are started there, no shell opens links, the games go
    /// where RetroArch can read them. Set by the Android activity; none on a PC.
    /// </summary>
    internal interface IMobilePlatform
    {
        /// <summary>The games' folder, in the shared storage (RetroArch for Android reads it).</summary>
        string GamesDirectory { get; }

        /// <summary>The shared storage may be written ("All files access", asked once).</summary>
        bool HasStorageAccess { get; }

        /// <summary>
        /// Keeps the app running (and its network) while another app is on the screen, until disposed: the downloads.
        /// </summary>
        IDisposable KeepRunning();

        /// <summary>Opens Android's page that gives the storage access.</summary>
        void RequestStorageAccess();

        /// <summary>A web page in the browser, or a downloaded file in its app.</summary>
        void Open(string target);

        /// <summary>RetroArch for Android is installed.</summary>
        bool HasRetroArch { get; }

        /// <summary>
        /// RetroArch for Android downloaded from libretro, then Android's installer (and, the first time, the permission
        /// to install apps). True once it is installed.
        /// </summary>
        Task<bool> InstallRetroArchAsync(Action<string> status);

        /// <summary>Why RetroArch can not start the game now (no access to the files), or null.</summary>
        string? CannotPlay(string core);

        /// <summary>Starts the game in RetroArch; completes once SanctuaryVault is back on the screen.</summary>
        Task PlayAsync(string core, string rom);

        /// <summary>Why this device can not play this core's games at all (its GPU), or null: asked before a download.</summary>
        Task<string?> UnsupportedAsync(string core);

        /// <summary>The app plays this core's games itself (its pictures are drawn without the GPU): no RetroArch.</summary>
        bool PlaysBuiltIn(string core);

        /// <summary>The living-room mode opened or closed: the whole screen (in landscape on a phone) while it is.</summary>
        void SetCouch(bool on);

        /// <summary>The device's sound: louder (1), lower (-1), on or off (0), with Android's own volume panel.</summary>
        void Volume(int direction);

        /// <summary>The battery's charge (0 to 100) and whether it charges; null without a battery (a TV box).</summary>
        (int Percent, bool Charging)? Battery { get; }

        /// <summary>Android's panel of the Wi-Fi ("wifi") or the Bluetooth ("bluetooth"), over the app.</summary>
        void OpenConnections(string kind);

        /// <summary>The names of the controllers plugged now.</summary>
        System.Collections.Generic.IReadOnlyList<string> Controllers { get; }

        /// <summary>A short vibration (the living-room mode's moves): a tick, or a firmer one to confirm.</summary>
        void Vibrate(bool firm);

        /// <summary>The device's other apps (the ones of its app drawer): their name and package, by name.</summary>
        System.Collections.Generic.IReadOnlyList<(string Name, string Package)> Apps { get; }

        void OpenApp(string package);

        /// <summary>The device's pictures folder for the games' screenshots (Pictures/SanctuaryVault).</summary>
        string CapturesDirectory { get; }

        /// <summary>One of the emulator's packages is installed.</summary>
        bool IsInstalled(AndroidEmulator emulator);

        /// <summary>The game in the emulator app; completes once SanctuaryVault is back on the screen.</summary>
        Task PlayExternalAsync(AndroidEmulator emulator, string rom);

        /// <summary>The core of the app's own player, downloaded once from libretro.</summary>
        Task EnsureCoreAsync(string core, Action<string> status);

        /// <summary>Plays the game in the app, full screen; completes once the player quits and its saves are written.</summary>
        Task PlayBuiltInAsync(BuiltInGame game);
    }

    /// <param name="Name">Shown in the player's menu.</param>
    /// <param name="Platform">The platform's slug: the touch controls of its console.</param>
    /// <param name="SystemDirectory">The BIOS and the cores' files.</param>
    /// <param name="SaveFile">The cartridge's or memory card's save, read before and written after.</param>
    /// <param name="Language">RetroArch's name of the console's language ("French"...).</param>
    /// <param name="StateFile">Where the game's state is kept when it is quit (quick resume, RetroArch's ".auto" state), or none.</param>
    /// <param name="StartState">A save state the game starts from, or none.</param>
    /// <param name="SlotFile">The save state of the in-game menu (RetroArch's first slot: the game's page lists it).</param>
    /// <param name="CaptureFolder">Where the in-game menu's screenshots go; <paramref name="Captured"/> is told each one.</param>
    internal sealed record BuiltInGame(string Name, string Platform, string Core, string Rom, string SystemDirectory, string SaveFile, string Language,
        string? StateFile = null, string? StartState = null, string? SlotFile = null, string? CaptureFolder = null, System.Action<string>? Captured = null);

    internal static class MobilePlatform
    {
        /// <summary>"Battery 82 %", "Battery 82 %, charging"; empty without a battery.</summary>
        public static string BatteryText => Current?.Battery is var (percent, charging)
            ? charging ? Localization.Loc.F("Battery {0} %, charging", percent) : Localization.Loc.F("Battery {0} %", percent) : "";

        public static IMobilePlatform? Current { get; set; }

        /// <summary>The app is on the screen (not behind another app): Android's own "window is active".</summary>
        public static bool InFront { get; set; } = true;
    }
}
