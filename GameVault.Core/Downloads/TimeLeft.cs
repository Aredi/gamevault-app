namespace GameVault.Core.Downloads
{
    /// <summary>A download's time left, shown while it runs.</summary>
    public static class TimeLeft
    {
        /// <summary>Beyond this the speed says nothing (a download nearly stopped): not a time.</summary>
        private static readonly TimeSpan Longest = TimeSpan.FromHours(100);

        /// <summary>The time left, null without a speed that says one.</summary>
        public static TimeSpan? Of(long bytesLeft, double bytesPerSecond)
        {
            double seconds = Math.Max(0, bytesLeft) / bytesPerSecond;
            if (!(bytesPerSecond > 0) || double.IsNaN(seconds) || seconds >= Longest.TotalSeconds)
                return null;
            return TimeSpan.FromSeconds(seconds);
        }
    }
}
