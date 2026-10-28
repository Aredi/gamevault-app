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

        /// <summary>Opens Android's page that gives the storage access.</summary>
        void RequestStorageAccess();

        /// <summary>A web page in the browser, or a downloaded file in its app.</summary>
        void Open(string target);

        /// <summary>Why RetroArch can not start this core (not installed, core missing), or null.</summary>
        string? CannotPlay(string core);

        /// <summary>Starts the game in RetroArch; completes once SanctuaryVault is back on the screen.</summary>
        Task PlayAsync(string core, string rom);
    }

    internal static class MobilePlatform
    {
        public static IMobilePlatform? Current { get; set; }
    }
}
