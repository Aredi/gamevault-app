using System.Globalization;

namespace GameVault.Core.Input
{
    /// <summary>
    /// Linux input devices describe their buttons in /sys/class/input/*/device/capabilities/key: hexadecimal words of
    /// 64 bits, the most significant first. Joystick devices (js*) also include mice and keyboards with extra axes,
    /// so only those with gamepad buttons are used.
    /// </summary>
    public static class KernelCapabilities
    {
        /// <summary>BTN_SOUTH / BTN_A: every gamepad has it.</summary>
        public const int GamepadSouthButton = 0x130;

        public static bool HasBit(string? bitmap, int bit)
        {
            if (string.IsNullOrWhiteSpace(bitmap))
                return false;
            string[] words = bitmap.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int word = bit / 64;
            if (word >= words.Length)
                return false;
            // The last word holds bits 0..63
            return ulong.TryParse(words[words.Length - 1 - word], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong value)
                && (value >> (bit % 64) & 1) == 1;
        }

        public static bool IsGamepad(string? keyBitmap) => HasBit(keyBitmap, GamepadSouthButton);
    }
}
