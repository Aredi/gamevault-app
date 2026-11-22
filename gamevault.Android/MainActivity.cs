using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
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
            if (!platform.HasStorageAccess)
                platform.RequestStorageAccess();
            // Android draws the app behind its bars and its keyboard: their room, in the app's units, each time the layout changes
            Window!.DecorView.ViewTreeObserver!.GlobalLayout += (_, _) => ApplySafeArea();
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
            if (e != null && FromController(e.Source, e.Device) && CouchButtonOf(e.KeyCode) is CouchButtons button)
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
