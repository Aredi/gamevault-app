using GameVault.Core.Couch;

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

        public static void Key(CouchButtons button, bool down, string? deviceName)
        {
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
