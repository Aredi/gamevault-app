using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using gamevault.Helper.Platform;

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
        }

        /// <summary>A controller's buttons and sticks go to the game being played.</summary>
        public override bool DispatchKeyEvent(global::Android.Views.KeyEvent? e)
        {
            if (e != null && platform?.OnKey(e) == true)
                return true;
            return base.DispatchKeyEvent(e);
        }

        public override bool DispatchGenericMotionEvent(global::Android.Views.MotionEvent? e)
        {
            if (e != null && platform?.OnMotion(e) == true)
                return true;
            return base.DispatchGenericMotionEvent(e);
        }

        protected override void OnPause()
        {
            base.OnPause();
            platform?.Paused();
        }

        protected override void OnResume()
        {
            base.OnResume();
            platform?.Resumed();
        }
    }
}
