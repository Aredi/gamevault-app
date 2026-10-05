using GameVault.Core;
using gamevault.Helper.Platform;
using System;
using System.IO;

namespace gamevault.Helper
{
    /// <summary>
    /// Start GameVault with the session: HKCU Run key on Windows, XDG autostart entry on Linux.
    /// </summary>
    internal static class AutostartHelper
    {
        private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        private static string LinuxAutostartFile => Path.Combine(XdgConfigHome, "autostart", "gamevault.desktop");

        internal static string XdgConfigHome
        {
            get
            {
                string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                return string.IsNullOrEmpty(xdg) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config") : xdg;
            }
        }

        internal static bool IsEnabled()
        {
            if (OperatingSystem.IsWindows())
            {
                using var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, false);
                return rk?.GetValue("GameVault") != null;
            }
            return File.Exists(LinuxAutostartFile);
        }

        internal static void Enable()
        {
            if (OperatingSystem.IsWindows())
            {
                using var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true);
                rk?.SetValue("GameVault", $"\"{PlatformInfo.ExecutablePath}\"");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(LinuxAutostartFile)!);
            File.WriteAllText(LinuxAutostartFile, DesktopEntry.Create(
                name: "GameVault",
                exec: $"{DesktopEntry.Quote(PlatformInfo.ExecutablePath)} show --minimized=true",
                comment: "Start GameVault in the background",
                extra: "X-GNOME-Autostart-enabled=true\n"));
        }

        internal static void Disable()
        {
            if (OperatingSystem.IsWindows())
            {
                using var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (rk?.GetValue("GameVault") != null)
                    rk.DeleteValue("GameVault");
                return;
            }
            try
            {
                if (File.Exists(LinuxAutostartFile))
                    File.Delete(LinuxAutostartFile);
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }
    }

    /// <summary>
    /// Helpers to write freedesktop.org .desktop files.
    /// </summary>
    internal static class DesktopEntry
    {
        public static string IconPath
        {
            get
            {
                // The icon is exported once next to the other user data so .desktop files can reference it.
                string icon = Path.Combine(PlatformInfo.LocalDataDirectory, "gamevault.png");
                if (!File.Exists(icon))
                {
                    try
                    {
                        Directory.CreateDirectory(PlatformInfo.LocalDataDirectory);
                        using Stream source = Avalonia.Platform.AssetLoader.Open(new Uri("avares://gamevault/Resources/Images/icon.png"));
                        using FileStream target = File.Create(icon);
                        source.CopyTo(target);
                    }
                    catch (Exception ex) { Log.Ignored(ex); }
                }
                return icon;
            }
        }

        public static string Create(string name, string exec, string comment = "", string? icon = null, string extra = "")
        {
            return "[Desktop Entry]\n" +
                   "Type=Application\n" +
                   $"Name={name}\n" +
                   (string.IsNullOrEmpty(comment) ? "" : $"Comment={comment}\n") +
                   $"Exec={exec}\n" +
                   $"Icon={icon ?? IconPath}\n" +
                   "Terminal=false\n" +
                   extra;
        }

        /// <summary>
        /// Quotes an argument for the Exec key (see the Desktop Entry Specification).
        /// </summary>
        public static string Quote(string value)
        {
            if (value.IndexOfAny(new[] { ' ', '\t', '"', '\'', '\\', '>', '<', '~', '|', '&', ';', '$', '*', '?', '#', '(', ')', '`' }) < 0)
                return value;
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$") + "\"";
        }
    }
}
