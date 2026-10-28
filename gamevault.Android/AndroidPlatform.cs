using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.Content;
using gamevault.Helper;
using gamevault.Helper.Platform;
using GameVault.Core.Retro;
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
    /// <summary>The Android side of <see cref="IMobilePlatform"/>: the shared storage, the browser, RetroArch for Android (installed from libretro's builds).</summary>
    internal sealed class AndroidPlatform(MainActivity activity) : IMobilePlatform
    {
        /// <summary>RetroArch's packages: 64-bit (retroarch.com, F-Droid), the Play Store's, 32-bit.</summary>
        private static readonly string[] RetroArchPackages = { "com.retroarch.aarch64", "com.retroarch", "com.retroarch.ra32" };
        private const string RetroArchActivity = "com.retroarch.browser.retroactivity.RetroActivityFuture";

        /// <summary>Another app is on the screen (RetroArch, the installer, a settings page) until SanctuaryVault is back.</summary>
        private TaskCompletionSource? away;
        private bool left;

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

        public bool HasRetroArch => RetroArchPackage != null;

        /// <summary>libretro's build for the phone: 64-bit ARM (almost all), else the one with every ABI.</summary>
        private static string RetroArchApk => Build.SupportedAbis?.Contains("arm64-v8a") == true ? "RetroArch_aarch64.apk" : "RetroArch.apk";

        public async Task<bool> InstallRetroArchAsync(Action<string> status)
        {
            string folder = Path.Combine(activity.CacheDir!.AbsolutePath, "retroarch");
            string apk = Path.Combine(folder, RetroArchApk);
            if (!File.Exists(apk))
            {
                Directory.CreateDirectory(folder);
                string part = apk + ".part";
                status(Loc.F("Installing RetroArch, once for all the games: downloading {0:0}%", 0));
                await RetroArchManager.DownloadAsync($"https://buildbot.libretro.com/stable/{RetroArchLayout.Version}/android/{RetroArchApk}", part,
                    done => status(Loc.F("Installing RetroArch, once for all the games: downloading {0:0}%", done * 100)));
                File.Move(part, apk, true);
            }
            // Android 8+: SanctuaryVault must be allowed to install apps, once
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O && !activity.PackageManager!.CanRequestPackageInstalls())
            {
                status(Loc.T("Allow SanctuaryVault to install apps, then come back to it"));
                await StartAndWaitAsync(new Intent(Settings.ActionManageUnknownAppSources, AndroidUri.Parse("package:" + activity.PackageName)));
                if (!activity.PackageManager!.CanRequestPackageInstalls())
                    return false;
            }
            status(Loc.T("Installing RetroArch: confirm in Android's window"));
            var uri = FileProvider.GetUriForFile(activity, activity.PackageName + ".fileprovider", new Java.IO.File(apk));
            var install = new Intent(Intent.ActionView);
            install.SetDataAndType(uri, "application/vnd.android.package-archive");
            install.AddFlags(ActivityFlags.GrantReadUriPermission);
            await StartAndWaitAsync(install);
            if (!HasRetroArch)
                return false;
            // 200 MB less once installed
            try { File.Delete(apk); }
            catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
            return true;
        }

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

            return StartAndWaitAsync(intent);
        }

        /// <summary>Another app's page, until SanctuaryVault is back on the screen.</summary>
        private Task StartAndWaitAsync(Intent intent)
        {
            away?.TrySetResult();
            away = new TaskCompletionSource();
            left = false;
            activity.StartActivity(intent);
            return away.Task;
        }

        /// <summary>SanctuaryVault left the screen: for the app that was started.</summary>
        public void Paused()
        {
            if (away != null)
                left = true;
        }

        /// <summary>Back on the screen: the game was quit, the installer or the settings page closed.</summary>
        public void Resumed()
        {
            if (away == null || !left)
                return;
            var done = away;
            away = null;
            left = false;
            done.TrySetResult();
        }
    }
}
