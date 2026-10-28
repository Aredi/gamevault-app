using Android.Content;
using Android.Content.PM;
using Android.OS;
using gamevault.Helper.Platform;
using gamevault.Localization;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AndroidEnvironment = Android.OS.Environment;
using AndroidUri = Android.Net.Uri;
using Settings = Android.Provider.Settings;

namespace gamevault.Android
{
    /// <summary>The Android side of <see cref="IMobilePlatform"/>: the shared storage, the browser, RetroArch for Android.</summary>
    internal sealed class AndroidPlatform(MainActivity activity) : IMobilePlatform
    {
        /// <summary>RetroArch's packages: 64-bit (retroarch.com, F-Droid), the Play Store's, 32-bit.</summary>
        private static readonly string[] RetroArchPackages = { "com.retroarch.aarch64", "com.retroarch", "com.retroarch.ra32" };
        private const string RetroArchActivity = "com.retroarch.browser.retroactivity.RetroActivityFuture";

        private TaskCompletionSource? playing;
        private bool leftForTheGame;

        public string GamesDirectory => Path.Combine(AndroidEnvironment.ExternalStorageDirectory!.AbsolutePath, "SanctuaryVault");

        public bool HasStorageAccess => Build.VERSION.SdkInt >= BuildVersionCodes.R
            ? AndroidEnvironment.IsExternalStorageManager
            : activity.CheckSelfPermission(global::Android.Manifest.Permission.WriteExternalStorage) == Permission.Granted;

        public void RequestStorageAccess()
        {
            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
                    activity.StartActivity(new Intent(Settings.ActionManageAppAllFilesAccessPermission, AndroidUri.Parse("package:" + activity.PackageName)));
                else
                    activity.RequestPermissions(new[] { global::Android.Manifest.Permission.WriteExternalStorage, global::Android.Manifest.Permission.ReadExternalStorage }, 1);
            }
            catch (Exception ex) { GameVault.Core.Log.Error(ex, "Storage access not asked"); }
        }

        public void Open(string target)
        {
            if (!PlatformInfo.IsWebAddress(target))
            {
                GameVault.Core.Log.Info($"Not opened on Android, not a web address: {target}");
                return;
            }
            try { activity.StartActivity(new Intent(Intent.ActionView, AndroidUri.Parse(target)).AddFlags(ActivityFlags.NewTask)); }
            catch (Exception ex) { GameVault.Core.Log.Error(ex, $"Could not open {target}"); }
        }

        private string? RetroArchPackage => RetroArchPackages.FirstOrDefault(IsInstalled);

        private bool IsInstalled(string package)
        {
            try
            {
                activity.PackageManager!.GetPackageInfo(package, 0);
                return true;
            }
            catch (PackageManager.NameNotFoundException) { return false; }
        }

        public string? CannotPlay(string core)
        {
            if (RetroArchPackage == null)
                return Loc.T("Install RetroArch for Android (retroarch.com or F-Droid) to play the retro games.");
            if (!HasStorageAccess)
            {
                RequestStorageAccess();
                return Loc.T("Allow SanctuaryVault to access the files, so that RetroArch reads the game.");
            }
            return null;
        }

        public Task PlayAsync(string core, string rom)
        {
            string package = RetroArchPackage ?? throw new InvalidOperationException(CannotPlay(core));
            var intent = new Intent(Intent.ActionMain);
            intent.SetComponent(new ComponentName(package, RetroArchActivity));
            intent.PutExtra("ROM", rom);
            // Downloaded once in RetroArch (Online Updater > Core Downloader): RetroArch says so when it is missing
            intent.PutExtra("LIBRETRO", $"/data/data/{package}/cores/{core}_libretro_android.so");
            // RetroArch's own settings (controllers, video), as when it is opened from its icon
            intent.PutExtra("CONFIGFILE", Path.Combine(AndroidEnvironment.ExternalStorageDirectory!.AbsolutePath, "Android", "data", package, "files", "retroarch.cfg"));
            intent.PutExtra("QUITFOCUS", "");
            intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask | ActivityFlags.ClearTop | ActivityFlags.NoHistory);

            playing?.TrySetResult();
            playing = new TaskCompletionSource();
            leftForTheGame = false;
            activity.StartActivity(intent);
            return playing.Task;
        }

        /// <summary>SanctuaryVault left the screen: for RetroArch when a game was started.</summary>
        public void Paused()
        {
            if (playing != null)
                leftForTheGame = true;
        }

        /// <summary>Back on the screen: the game was quit.</summary>
        public void Resumed()
        {
            if (playing == null || !leftForTheGame)
                return;
            var done = playing;
            playing = null;
            leftForTheGame = false;
            done.TrySetResult();
        }
    }
}
