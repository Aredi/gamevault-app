using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using System;
using System.Linq;
using Avalonia;
using Avalonia.Android;
using gamevault.Helper.Platform;
using GameVault.Core.Couch;

namespace gamevault.Android
{
    /// <summary>SanctuaryVault on Android: the desktop client's views in one screen, turned as the phone is (a game asks for landscape).</summary>
    [Activity(Label = "SanctuaryVault", Theme = "@style/SanctuaryTheme", Icon = "@mipmap/icon", MainLauncher = true,
        LaunchMode = LaunchMode.SingleTask, ScreenOrientation = ScreenOrientation.Unspecified,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout
            | ConfigChanges.SmallestScreenSize | ConfigChanges.Density | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation)]
    public class MainActivity : AvaloniaMainActivity<App>
    {
        private AndroidPlatform? platform;

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder).WithInterFont();

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            // Before the app starts: the settings read the games' folder from it
            platform = new AndroidPlatform(this);
            MobilePlatform.Current = platform;
            base.OnCreate(savedInstanceState);
            // A crash outside the UI thread (it ends the app) kept for the next problem report; then why Android ended the
            // app last time (a crash in native code, too little memory, frozen): nothing in the app sees those
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    App.WriteErrorLog(ex);
            };
            global::Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => App.WriteErrorLog(e.Exception);
            Avalonia.Threading.Dispatcher.UIThread.Post(LogLastExit, Avalonia.Threading.DispatcherPriority.Background);
            if (!platform.HasStorageAccess)
                platform.RequestStorageAccess();
            // Android draws the app behind its bars and its keyboard: their room, in the app's units, each time the layout changes
            Window!.DecorView.ViewTreeObserver!.GlobalLayout += (_, _) => ApplySafeArea();
        }


        /// <summary>The app's last end, when it was not a normal one, in the log (a problem report sends it).</summary>
        private void LogLastExit()
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.R || GetSystemService(ActivityService) is not ActivityManager manager)
                return;
            try
            {
                var exit = manager.GetHistoricalProcessExitReasons(PackageName, 0, 5)?.FirstOrDefault(e => e.ProcessName == PackageName);
                // The app's own process (not its web pages'), told once
                var seen = GetSharedPreferences("exits", global::Android.Content.FileCreationMode.Private)!;
                if (exit == null || seen.GetLong("last", 0) == exit.Timestamp)
                    return;
                seen.Edit()!.PutLong("last", exit.Timestamp)!.Apply();
                // Too little memory, a crash (.NET's or native), frozen, failed to start, too much of the device used
                string? reason = exit.Reason switch { 3 => "low memory", 4 => "crash", 5 => "native crash", 6 => "not responding", 7 => "failed to start", 9 => "too many resources", _ => null };
                if (reason == null)
                    return;
                string text = $"Last exit: {reason} at {DateTimeOffset.FromUnixTimeMilliseconds(exit.Timestamp).LocalDateTime}, "
                    + $"status {exit.Status}, memory {exit.Pss / 1024} MB (rss {exit.Rss / 1024} MB): {exit.Description}";
                // The native crash's tombstone, or the frozen app's threads (Android 11 and later)
                using var trace = exit.TraceInputStream;
                if (trace != null)
                {
                    using var reader = new System.IO.StreamReader(trace);
                    char[] buffer = new char[100_000];
                    int read = reader.ReadBlock(buffer, 0, buffer.Length);
                    // A native crash's tombstone is binary: its readable parts (the functions, the libraries) kept
                    text += "\n" + new string(buffer.Take(read).Select(c => c == '\n' || !char.IsControl(c) ? c : ' ').ToArray());
                }
                GameVault.Core.Log.Info(text);
                App.WriteErrorLog(new Exception(text));
            }
            catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
        }

        private Avalonia.Thickness safeArea;
        private void ApplySafeArea()
        {
            var insets = Window?.DecorView.RootWindowInsets;
            if (insets == null || Build.VERSION.SdkInt < BuildVersionCodes.R)
                return;
            // The keyboard too: the text being typed (a chat message) stays above it
            var bars = insets.GetInsets(global::Android.Views.WindowInsets.Type.SystemBars() | global::Android.Views.WindowInsets.Type.DisplayCutout()
                | global::Android.Views.WindowInsets.Type.Ime());
            double density = Resources!.DisplayMetrics!.Density;
            var padding = new Avalonia.Thickness(bars.Left / density, bars.Top / density, bars.Right / density, bars.Bottom / density);
            if (padding == safeArea)
                return;
            safeArea = padding;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => App.Instance.SetSafeArea(padding));
        }

        /// <summary>A controller's buttons and sticks go to the game being played.</summary>
        public override bool DispatchKeyEvent(global::Android.Views.KeyEvent? e)
        {
            if (e != null && platform?.OnKey(e) == true)
                return true;
            // Outside a game, a controller's buttons are the living-room mode's (it reads them as a PC's gamepad)
            if (e != null && FromController(e.Source, e.Device) && CouchButtonOf(Retro.Gamepads.Positional(e)) is CouchButtons button)
            {
                if (e.Action is KeyEventActions.Down or KeyEventActions.Up)
                    MobileGamepad.Key(button, e.Action == KeyEventActions.Down, e.Device?.Name);
                if (gamevault.Windows.MainView.Current?.IsCouchShown == true)
                    return true;
            }
            return base.DispatchKeyEvent(e);
        }

        public override bool DispatchGenericMotionEvent(global::Android.Views.MotionEvent? e)
        {
            if (e != null && platform?.OnMotion(e) == true)
                return true;
            if (e != null && e.Action == MotionEventActions.Move && FromController(e.Source, e.Device))
            {
                MobileGamepad.Axes(e.GetAxisValue(Axis.X), e.GetAxisValue(Axis.Y), e.GetAxisValue(Axis.HatX), e.GetAxisValue(Axis.HatY),
                    e.GetAxisValue(Axis.Z), e.GetAxisValue(Axis.Rz));
                if (gamevault.Windows.MainView.Current?.IsCouchShown == true)
                    return true;
            }
            return base.DispatchGenericMotionEvent(e);
        }

        private static bool FromController(InputSourceType source, InputDevice? device) =>
            (source & InputSourceType.Gamepad) == InputSourceType.Gamepad || (source & InputSourceType.Joystick) == InputSourceType.Joystick
            || (source & InputSourceType.Dpad) == InputSourceType.Dpad && device is { IsVirtual: false } && ((device.Sources & InputSourceType.Gamepad) == InputSourceType.Gamepad);

        private static CouchButtons? CouchButtonOf(Keycode key) => key switch
        {
            Keycode.ButtonA => CouchButtons.A,
            Keycode.ButtonB => CouchButtons.B,
            Keycode.ButtonX => CouchButtons.X,
            Keycode.ButtonY => CouchButtons.Y,
            Keycode.ButtonL1 => CouchButtons.LeftShoulder,
            Keycode.ButtonR1 => CouchButtons.RightShoulder,
            Keycode.ButtonL2 => CouchButtons.LeftTrigger,
            Keycode.ButtonR2 => CouchButtons.RightTrigger,
            Keycode.ButtonStart => CouchButtons.Start,
            Keycode.ButtonSelect => CouchButtons.Back,
            Keycode.ButtonThumbl => CouchButtons.LeftStick,
            Keycode.ButtonThumbr => CouchButtons.RightStick,
            Keycode.DpadUp => CouchButtons.Up,
            Keycode.DpadDown => CouchButtons.Down,
            Keycode.DpadLeft => CouchButtons.Left,
            Keycode.DpadRight => CouchButtons.Right,
            _ => null,
        };

        protected override void OnPause()
        {
            base.OnPause();
            MobilePlatform.InFront = false;
            platform?.Paused();
        }

        protected override void OnResume()
        {
            base.OnResume();
            MobilePlatform.InFront = true;
            platform?.Resumed();
        }
    }
}
