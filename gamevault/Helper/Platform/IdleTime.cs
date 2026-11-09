using GameVault.Core;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Tmds.DBus;

namespace gamevault.Helper.Platform
{
    /// <summary>[GNOME] Mutter's idle monitor: the time since the last input of the session, Wayland or X11.</summary>
    [DBusInterface("org.gnome.Mutter.IdleMonitor")]
    public interface IMutterIdleMonitor : IDBusObject
    {
        Task<ulong> GetIdletimeAsync();
    }

    /// <summary>
    /// How long nobody touched the keyboard or the mouse of this PC (the chat's "Away"): Windows and macOS say it, GNOME
    /// too through DBus. Elsewhere (KDE, other Wayland desktops) only the inputs in SanctuaryVault's window count.
    /// </summary>
    internal static class IdleTime
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LastInputInfo
        {
            public uint Size;
            public uint Time;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LastInputInfo info);

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern double CGEventSourceSecondsSinceLastEventType(int stateId, uint eventType);

        private static DateTime lastInputHere = DateTime.UtcNow;
        private static IMutterIdleMonitor? mutter;
        private static bool mutterTried, mutterFailed;

        /// <summary>An input in SanctuaryVault's window (the fallback where the system does not say it).</summary>
        public static void InputHere() => lastInputHere = DateTime.UtcNow;

        public static async Task<TimeSpan> GetAsync()
        {
            TimeSpan here = DateTime.UtcNow - lastInputHere;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
                    // The tick count of the last input is 32 bits: compared on 32 bits (it wraps after 49 days)
                    return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)GetTickCount64() - info.Time)) : here;
                }
                if (OperatingSystem.IsMacOS())
                    return TimeSpan.FromSeconds(CGEventSourceSecondsSinceLastEventType(1 /* HID system state */, uint.MaxValue /* any input */));
                if (OperatingSystem.IsLinux() && !mutterFailed)
                {
                    if (!mutterTried)
                    {
                        mutterTried = true;
                        mutter = Connection.Session.CreateProxy<IMutterIdleMonitor>("org.gnome.Mutter.IdleMonitor", "/org/gnome/Mutter/IdleMonitor/Core");
                    }
                    if (mutter != null)
                        return TimeSpan.FromMilliseconds(await mutter.GetIdletimeAsync());
                }
            }
            catch (Exception ex)
            {
                // Not GNOME (or no session bus): the window's inputs from now on
                mutterFailed = OperatingSystem.IsLinux();
                Log.Ignored(ex);
            }
            return here;
        }
    }
}
