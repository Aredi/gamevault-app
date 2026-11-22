using GameVault.Core;
using GameVault.Core.Couch;
using gamevault.Helper.Platform;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace gamevault.Helper
{
    internal enum CouchPower { Sleep, Restart, ShutDown }

    /// <summary>What the living-room mode asks the PC itself: its sound's volume, its sleep and its power, another program.</summary>
    internal static class CouchSystem
    {
        /// <summary>Starts a program (the tests look at what would be started instead).</summary>
        internal static Action<string, string[]> Start { get; set; } = (file, arguments) =>
        {
            var info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true };
            foreach (string argument in arguments)
                info.ArgumentList.Add(argument);
            Process.Start(info)?.Dispose();
        };

        /// <summary>Opens an application of the home, as a double click on it would.</summary>
        internal static Action<CouchApp> Open { get; set; } = app =>
            Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true, Arguments = app.Arguments ?? "", WorkingDirectory = Path.GetDirectoryName(app.Path) ?? "" })?.Dispose();

        internal static (string File, string[] Arguments) PowerCommand(CouchPower power, bool windows, bool mac) => (power, windows, mac) switch
        {
            (CouchPower.Sleep, true, _) => ("rundll32.exe", new[] { "powrprof.dll,SetSuspendState", "0,1,0" }),
            (CouchPower.Restart, true, _) => ("shutdown.exe", new[] { "/r", "/t", "0" }),
            (CouchPower.ShutDown, true, _) => ("shutdown.exe", new[] { "/s", "/t", "0" }),
            (CouchPower.Sleep, _, true) => ("pmset", new[] { "sleepnow" }),
            (CouchPower.Restart, _, true) => ("osascript", new[] { "-e", "tell application \"System Events\" to restart" }),
            (CouchPower.ShutDown, _, true) => ("osascript", new[] { "-e", "tell application \"System Events\" to shut down" }),
            (CouchPower.Sleep, _, _) => ("systemctl", new[] { "suspend" }),
            (CouchPower.Restart, _, _) => ("systemctl", new[] { "reboot" }),
            _ => ("systemctl", new[] { "poweroff" }),
        };

        /// <summary>Null on Windows: its volume keys are pressed instead.</summary>
        /// <param name="direction">1: louder, -1: lower, 0: the sound cut or back.</param>
        internal static (string File, string[] Arguments)? VolumeCommand(int direction, bool windows, bool mac)
        {
            if (windows)
                return null;
            if (mac)
                return ("osascript", new[] { "-e", direction == 0
                    ? "set volume output muted not (output muted of (get volume settings))"
                    : $"set volume output volume ((output volume of (get volume settings)) {(direction > 0 ? "+" : "-")} 6)" });
            return direction == 0
                ? ("pactl", new[] { "set-sink-mute", "@DEFAULT_SINK@", "toggle" })
                : ("pactl", new[] { "set-sink-volume", "@DEFAULT_SINK@", direction > 0 ? "+5%" : "-5%" });
        }

        [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);

        /// <summary>The window is still open (Windows); the tests say it themselves.</summary>
        internal static Func<IntPtr, bool> Exists { get; set; } = window => OperatingSystem.IsWindows() && window != IntPtr.Zero && IsWindow(window);

        /// <summary>A key pressed and let go, as on the keyboard (Windows).</summary>
        internal static void PressKey(byte key)
        {
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, 2, UIntPtr.Zero);
        }

        /// <summary>
        /// A window in front (Windows). Windows refuses the foreground to a program the player did not touch last: a press
        /// of Alt (nothing else happens with it) makes it accept, as when the player clicks the window.
        /// </summary>
        internal static void ToFront(IntPtr window)
        {
            if (IsIconic(window))
                ShowWindow(window, 9);
            PressKey(0x12);
            SetForegroundWindow(window);
        }

        /// <summary>A window given back the foreground it had (a game's, under the menu over it).</summary>
        internal static void GiveFront(IntPtr window) => SetForegroundWindow(window);

        public static void Volume(int direction)
        {
            try
            {
                if (Platform.MobilePlatform.Current is Platform.IMobilePlatform mobile)
                {
                    mobile.Volume(direction);
                    return;
                }
                if (VolumeCommand(direction, PlatformInfo.IsWindows, PlatformInfo.IsMac) is var (file, arguments))
                {
                    Start(file, arguments);
                    return;
                }
                // The keyboard's volume keys: louder, lower, mute
                PressKey(direction > 0 ? (byte)0xAF : direction < 0 ? (byte)0xAE : (byte)0xAD);
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        public static void Power(CouchPower power)
        {
            var (file, arguments) = PowerCommand(power, PlatformInfo.IsWindows, PlatformInfo.IsMac);
            Start(file, arguments);
        }
    }
}
