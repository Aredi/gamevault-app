using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.Content;
using gamevault.Helper;
using gamevault.Helper.Platform;
using GameVault.Core.Retro;
using gamevault.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AndroidEnvironment = Android.OS.Environment;
using AndroidUri = Android.Net.Uri;
using Settings = Android.Provider.Settings;

namespace gamevault.Android
{
    /// <summary>The Android side of <see cref="IMobilePlatform"/>: the shared storage, the browser, RetroArch for Android (installed from libretro's builds).</summary>
    internal sealed class AndroidPlatform(MainActivity activity) : IMobilePlatform
    {
        /// <summary>The cores of the app's own player (Dolphin, LRPS2 and Citra draw with the GPU, in the player's OpenGL ES context).</summary>
        private static readonly HashSet<string> BuiltInCores = new() { "mesen", "snes9x", "mgba", "melondsds", "swanstation", "fbneo", "dolphin", "pcsx2", "citra" };

        private Retro.Gamepads? gamepads;
        private Retro.RetroPlayer? player;
        private Retro.RetroPlayerView? playerView;

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

        public bool PlaysBuiltIn(string core) => BuiltInCores.Contains(core);

        // Asked away from the screen's thread: the question makes an OpenGL context current, which would take Avalonia's
        public async Task<string?> UnsupportedAsync(string core) =>
            core == "citra" && !await Task.Run(() => Retro.GlContext.IsModern) ? Loc.T("The 3DS needs OpenGL ES 3.2, which this device's GPU does not have.") : null;

        private int running;

        public IDisposable KeepRunning()
        {
            if (Interlocked.Increment(ref running) == 1)
            {
                try
                {
                    // The notification of the service shows from Android 13 once allowed (asked once; the downloads go on without)
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
                        && activity.CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications) != Permission.Granted)
                        activity.RequestPermissions(new[] { global::Android.Manifest.Permission.PostNotifications }, 2);
                    activity.StartForegroundService(new Intent(activity, typeof(DownloadService)));
                }
                catch (Exception ex) { GameVault.Core.Log.Error(ex, "The downloads' service did not start"); }
            }
            return new Release(this);
        }

        private sealed class Release(AndroidPlatform platform) : IDisposable
        {
            private int done;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref done, 1) == 1 || Interlocked.Decrement(ref platform.running) > 0)
                    return;
                platform.StopDownloadService();
            }
        }

        private void StopDownloadService()
        {
            try { activity.StopService(new Intent(activity, typeof(DownloadService))); }
            catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
        }

        private static string Abi => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x86_64",
            System.Runtime.InteropServices.Architecture.Arm => "armeabi-v7a",
            _ => "arm64-v8a",
        };

        private string CoreFile(string core) => Path.Combine(activity.FilesDir!.AbsolutePath, "cores", Abi, $"{core}_libretro_android.so");

        public async Task EnsureCoreAsync(string core, Action<string> status)
        {
            string file = CoreFile(core);
            if (File.Exists(file))
                return;
            status(Loc.T("Downloading the emulator..."));
            string folder = Path.GetDirectoryName(file)!;
            Directory.CreateDirectory(folder);
            string zip = file + ".zip";
            await RetroArchManager.DownloadAsync($"https://buildbot.libretro.com/nightly/android/latest/{Abi}/{core}_libretro_android.so.zip", zip,
                done => status(Loc.F("Downloading the emulator... {0:0}%", done * 100)));
            await Task.Run(() =>
            {
                using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
                    archive.GetEntry(Path.GetFileName(file))!.ExtractToFile(file + ".part", true);
                File.Move(file + ".part", file, true);
                File.Delete(zip);
            });
        }

        public async Task PlayBuiltInAsync(BuiltInGame game)
        {
            gamepads ??= CreateGamepads();
            gamepads.Reset();
            var started = new Retro.RetroPlayer(game, CoreFile(game.Core));
            player = started;
            var view = new Retro.RetroPlayerView(started, game.Platform, game.Name);
            playerView = view;
            view.ShowTouchPad(!gamepads.Any);
            view.Quit += () => started.Stop();
            started.QuitRequested += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => started.Stop());
            App.Instance.OverScreen = view;
            Immersive(true);
            gamepads.Player = started;
            try
            {
                if (!await started.StartAsync())
                    throw new InvalidOperationException(Loc.F("The game could not be started: {0}", started.Error));
                await started.Completion;
            }
            finally
            {
                gamepads.Player = null;
                player = null;
                playerView = null;
                App.Instance.OverScreen = null;
                Immersive(false);
            }
        }

        private Retro.Gamepads CreateGamepads()
        {
            var created = new Retro.Gamepads(activity);
            created.PresenceChanged += any => Avalonia.Threading.Dispatcher.UIThread.Post(() => playerView?.ShowTouchPad(!any));
            created.ControllerUsed += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => playerView?.ShowTouchPad(false));
            return created;
        }

        private bool couch;

        public (int Percent, bool Charging)? Battery
        {
            get
            {
                if (activity.GetSystemService(Context.BatteryService) is not global::Android.OS.BatteryManager battery)
                    return null;
                int percent = battery.GetIntProperty((int)global::Android.OS.BatteryProperty.Capacity);
                // A TV box answers 0, or the minimum int, or 100 while it is plugged without a battery
                if (percent <= 0 || percent > 100)
                    return null;
                return (percent, battery.IsCharging);
            }
        }

        public IReadOnlyList<string> Controllers => (global::Android.Views.InputDevice.GetDeviceIds() ?? Array.Empty<int>())
            .Select(global::Android.Views.InputDevice.GetDevice)
            .Where(d => d != null && !d.IsVirtual && ((d.Sources & global::Android.Views.InputSourceType.Gamepad) == global::Android.Views.InputSourceType.Gamepad
                || (d.Sources & global::Android.Views.InputSourceType.Joystick) == global::Android.Views.InputSourceType.Joystick))
            .Select(d => d!.Name ?? "").Where(n => n.Length > 0).Distinct().ToList();

        public void Vibrate(bool firm)
        {
            try
            {
                // The window's own haptics (no permission): Android plays them only when the system's touch vibrations are on
                activity.Window?.DecorView.PerformHapticFeedback(firm ? global::Android.Views.FeedbackConstants.Confirm : global::Android.Views.FeedbackConstants.ClockTick,
                    global::Android.Views.FeedbackFlags.IgnoreGlobalSetting);
            }
            catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
        }

        public void OpenConnections(string kind)
        {
            try
            {
                string action = kind == "wifi"
                    ? Build.VERSION.SdkInt >= BuildVersionCodes.Q ? global::Android.Provider.Settings.Panel.ActionWifi : global::Android.Provider.Settings.ActionWifiSettings
                    : global::Android.Provider.Settings.ActionBluetoothSettings;
                activity.StartActivity(new Intent(action));
            }
            catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
        }

        public void Volume(int direction)
        {
            if (activity.GetSystemService(Context.AudioService) is global::Android.Media.AudioManager audio)
                audio.AdjustStreamVolume(global::Android.Media.Stream.Music,
                    direction > 0 ? global::Android.Media.Adjust.Raise : direction < 0 ? global::Android.Media.Adjust.Lower : global::Android.Media.Adjust.ToggleMute,
                    global::Android.Media.VolumeNotificationFlags.ShowUi);
        }

        public void SetCouch(bool on)
        {
            couch = on;
            Immersive(on);
        }

        /// <summary>The game takes the whole screen (no status or navigation bar), the screen stays on; the living-room mode too.</summary>
        private void Immersive(bool on)
        {
            // A game closed in the living-room mode: back to it, still on the whole screen
            on |= couch;
            try
            {
                // A phone: landscape while a game is played. A large screen (a foldable open, a tablet) stays as it is, the
                // player lays out upright too (asked, Android would show the game in a band across it)
                if (activity.Resources!.Configuration!.SmallestScreenWidthDp < 600)
                    activity.RequestedOrientation = on ? global::Android.Content.PM.ScreenOrientation.SensorLandscape : global::Android.Content.PM.ScreenOrientation.Unspecified;
                var window = activity.Window!;
                if (on)
                    window.AddFlags(global::Android.Views.WindowManagerFlags.KeepScreenOn);
                else
                    window.ClearFlags(global::Android.Views.WindowManagerFlags.KeepScreenOn);
                if (Build.VERSION.SdkInt >= BuildVersionCodes.R && window.InsetsController is global::Android.Views.IWindowInsetsController insets)
                {
                    if (on)
                    {
                        insets.Hide(global::Android.Views.WindowInsets.Type.SystemBars());
                        insets.SystemBarsBehavior = (int)global::Android.Views.WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
                    }
                    else
                        insets.Show(global::Android.Views.WindowInsets.Type.SystemBars());
                }
            }
            catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
        }

        /// <summary>A controller's button: the game's while one is played (or its menu's while open).</summary>
        public bool OnKey(global::Android.Views.KeyEvent e)
        {
            if (player == null || gamepads == null)
                return false;
            if (playerView?.IsMenuOpen == true)
            {
                if (e.Action == global::Android.Views.KeyEventActions.Down && Retro.Gamepads.MenuButtonOf(e) is int button)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => playerView?.MenuButtonPressed(button));
                    return true;
                }
                return false;
            }
            return gamepads.OnKey(e);
        }

        public bool OnMotion(global::Android.Views.MotionEvent e) => player != null && playerView?.IsMenuOpen != true && gamepads != null && gamepads.OnMotion(e);

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
            player?.Pause(true);
            if (away != null)
                left = true;
        }

        /// <summary>Back on the screen: the game was quit, the installer or the settings page closed.</summary>
        public void Resumed()
        {
            if (player != null && playerView?.IsMenuOpen != true)
                player.Pause(false);
            if (away == null || !left)
                return;
            var done = away;
            away = null;
            left = false;
            done.TrySetResult();
        }
    }
}
