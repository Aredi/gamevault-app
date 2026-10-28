using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using gamevault.Helper.Platform;

namespace gamevault.Android
{
    /// <summary>SanctuaryVault on Android: the desktop client's views in one screen, in landscape (the pages' layout).</summary>
    [Activity(Label = "SanctuaryVault", Theme = "@style/SanctuaryTheme", Icon = "@mipmap/icon", MainLauncher = true,
        LaunchMode = LaunchMode.SingleTask, ScreenOrientation = ScreenOrientation.SensorLandscape,
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
