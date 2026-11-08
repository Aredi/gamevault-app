namespace GameVault.Core.Downloads
{
    /// <summary>A download's time left, shown while it runs.</summary>
    public static class TimeLeft
    {
        /// <summary>Beyond this the speed says nothing (a download nearly stopped): not a time.</summary>
        private static readonly TimeSpan Longest = TimeSpan.FromHours(100);

        /// <summary>
        /// "hh:mm:ss", hours past a day included; "--:--:--" without a speed. A speed near zero once gave seconds no
        /// TimeSpan holds: the download stopped on an overflow.
        /// </summary>
        public static string Text(long bytesLeft, double bytesPerSecond)
        {
            double seconds = Math.Max(0, bytesLeft) / bytesPerSecond;
            if (!(bytesPerSecond > 0) || double.IsNaN(seconds) || seconds >= Longest.TotalSeconds)
                return "--:--:--";
            var left = TimeSpan.FromSeconds(seconds);
            return $"{(int)left.TotalHours:00}:{left.Minutes:00}:{left.Seconds:00}";
        }
    }
}
