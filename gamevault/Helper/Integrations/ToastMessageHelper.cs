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
    /// Desktop notifications: org.freedesktop.Notifications (DBus) on Linux, toast notifications on Windows, macOS's on a Mac.
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
                    if (OperatingSystem.IsMacOS())
                    {
                        await ShowOnMacAsync(title, message);
                        return;
                    }
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

        /// <summary>
        /// macOS's notification, by AppleScript (the texts as its arguments, never in the script).
        /// shortcut: shown as Script Editor's, without the game's picture; the app's own notifications need it signed by Apple.
        /// </summary>
        private static async Task ShowOnMacAsync(string title, string message)
        {
            var info = new System.Diagnostics.ProcessStartInfo("osascript") { UseShellExecute = false };
            foreach (string argument in new[] { "-e", "on run argv", "-e", "display notification (item 2 of argv) with title (item 1 of argv)", "-e", "end run", title, message })
                info.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(info);
            if (process != null)
                await process.WaitForExitAsync();
        }
    }
}
