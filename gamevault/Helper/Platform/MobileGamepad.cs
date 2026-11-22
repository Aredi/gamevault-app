using GameVault.Core;
using GameVault.Core.Couch;
using gamevault.Helper;
using gamevault.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace gamevault.Helper.Platform
{
    /// <summary>
    /// Android's controllers outside a game, as the living-room mode reads them: the activity gives their buttons and
    /// sticks as they change (Android has no SDL to ask).
    /// </summary>
    internal static class MobileGamepad
    {
        private static CouchButtons keys, stick, hat, pressed;

        /// <summary>
        /// The buttons held, the left stick and the cross as the directions; a button pressed and let go since the last
        /// read is held once, so that a quick press is not missed.
        /// </summary>
        public static CouchButtons TakeHeld()
        {
            CouchButtons held = keys | stick | hat | pressed;
            pressed = CouchButtons.None;
            return held;
        }

        /// <summary>Where the right stick points, from -1 to 1 each way (down is positive).</summary>
        public static (double X, double Y) RightStick { get; private set; }

        /// <summary>The kind of the controller last used (CouchSettings.Xbox, PlayStation, Switch), from its name.</summary>
        public static string? Kind { get; private set; }

        /// <summary>The name of the controller last used, for the living-room mode's "Nintendo layout" of it.</summary>
        public static string? LastDevice { get; private set; }

        private static HashSet<string>? nintendo;

        /// <summary>
        /// The controllers with Nintendo's layout (A on the right, B at the bottom): Android names their buttons as printed,
        /// so the bottom one is B. Swapped back, the bottom button confirms on every controller, as on an Xbox's.
        /// </summary>
        public static bool IsNintendo(string? deviceName)
        {
            nintendo ??= Preferences.Get(AppConfigKey.NintendoControllers, ProfileManager.ProfileConfigFile)
                .Split('|', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            return deviceName != null && nintendo.Contains(deviceName);
        }

        public static void SetNintendo(string deviceName, bool on)
        {
            IsNintendo(deviceName);
            if (on) nintendo!.Add(deviceName); else nintendo!.Remove(deviceName);
            Preferences.Set(AppConfigKey.NintendoControllers, string.Join("|", nintendo!), ProfileManager.ProfileConfigFile);
        }

        public static void Key(CouchButtons button, bool down, string? deviceName)
        {
            if (deviceName != null)
                LastDevice = deviceName;
            keys = down ? keys | button : keys & ~button;
            if (down)
            {
                pressed |= button;
                Kind = KindOf(deviceName);
            }
        }

        public static void Axes(float leftX, float leftY, float hatX, float hatY, float rightX, float rightY)
        {
            stick = Directions(leftX, leftY, 0.5f);
            hat = Directions(hatX, hatY, 0.5f);
            RightStick = (rightX, rightY);
        }

        private static CouchButtons Directions(float x, float y, float dead) =>
            (x < -dead ? CouchButtons.Left : x > dead ? CouchButtons.Right : CouchButtons.None)
            | (y < -dead ? CouchButtons.Up : y > dead ? CouchButtons.Down : CouchButtons.None);

        private static string KindOf(string? name)
        {
            string lower = name?.ToLowerInvariant() ?? "";
            if (lower.Contains("dualsense") || lower.Contains("dualshock") || lower.Contains("wireless controller") || lower.Contains("playstation"))
                return CouchSettings.PlayStation;
            if (lower.Contains("pro controller") || lower.Contains("joy-con") || lower.Contains("nintendo"))
                return CouchSettings.Switch;
            return CouchSettings.Xbox;
        }
    }
}
