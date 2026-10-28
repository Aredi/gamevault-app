using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using gamevault.Localization;

namespace gamevault.Android
{
    /// <summary>
    /// Keeps the app running while games download: without it, Android cuts the network of an app a few seconds after
    /// another app comes to the screen ("Software caused connection abort"). Shown as a notification.
    /// </summary>
    [Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
    internal sealed class DownloadService : Service
    {
        private const string Channel = "downloads";

        public override IBinder? OnBind(Intent? intent) => null;

        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        {
            var notifications = (NotificationManager)GetSystemService(NotificationService)!;
            notifications.CreateNotificationChannel(new NotificationChannel(Channel, Loc.T("Downloads"), NotificationImportance.Low));
            var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable);
            Notification notification = new Notification.Builder(this, Channel)
                .SetContentTitle("SanctuaryVault")!
                .SetContentText(Loc.T("Downloading games..."))!
                .SetSmallIcon(global::Android.Resource.Drawable.StatSysDownload)!
                .SetOngoing(true)!
                .SetContentIntent(open)!
                .Build()!;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                StartForeground(1, notification, ForegroundService.TypeDataSync);
            else
                StartForeground(1, notification);
            return StartCommandResult.NotSticky;
        }
    }
}
