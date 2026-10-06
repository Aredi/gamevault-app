using DesktopNotifications;
using GameVault.Core;
using gamevault.Localization;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// Desktop notifications: org.freedesktop.Notifications (DBus) on Linux, toast notifications on Windows.
    /// </summary>
    public class ToastMessageHelper
    {
        private static INotificationManager? manager;
        private static readonly SemaphoreSlim initLock = new SemaphoreSlim(1, 1);
        private static bool initFailed;

        private static async Task<INotificationManager?> GetManager()
        {
            if (manager != null || initFailed)
                return manager;
            await initLock.WaitAsync();
            try
            {
                if (manager != null || initFailed)
                    return manager;
                INotificationManager created;
                if (OperatingSystem.IsWindows())
                {
                    created = new DesktopNotifications.Windows.WindowsNotificationManager(
                        DesktopNotifications.Windows.WindowsApplicationContext.FromCurrentProcess("GameVault"));
                }
                else
                {
                    created = new DesktopNotifications.FreeDesktop.FreeDesktopNotificationManager(
                        DesktopNotifications.FreeDesktop.FreeDesktopApplicationContext.FromCurrentProcess(DesktopEntry.IconPath));
                }
                await created.Initialize();
                manager = created;
            }
            catch (Exception ex)
            {
                initFailed = true;
                Log.Error(ex, "Desktop notifications unavailable");
            }
            finally
            {
                initLock.Release();
            }
            return manager;
        }

        public static void CreateToastMessage(string title, string message, string imageUri = "")
        {
            title = Loc.T(title);
            message = Loc.T(message);
            _ = Task.Run(async () =>
            {
                try
                {
                    INotificationManager? notifications = await GetManager();
                    if (notifications == null)
                        return;
                    var notification = new Notification { Title = title, Body = message };
                    if (!string.IsNullOrEmpty(imageUri) && File.Exists(imageUri))
                        notification.BodyImagePath = imageUri;
                    await notifications.ShowNotification(notification);
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
            });
        }
    }
}
