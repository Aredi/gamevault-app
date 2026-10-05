namespace GameVault.Core.Downloads
{
    /// <summary>
    /// Daily time window in which downloads may run, e.g. 01:00-07:00 or 22:00-06:00 (over midnight).
    /// </summary>
    public record DownloadSchedule(bool Enabled, TimeOnly Start, TimeOnly End)
    {
        public static readonly DownloadSchedule Always = new(false, new TimeOnly(0, 0), new TimeOnly(0, 0));

        public bool IsAllowed(DateTime now)
        {
            if (!Enabled || Start == End)
                return true;
            var time = TimeOnly.FromDateTime(now);
            return Start < End
                ? time >= Start && time < End
                : time >= Start || time < End;
        }

        /// <summary>When the window opens next (now if it is open).</summary>
        public DateTime NextStart(DateTime now)
        {
            if (IsAllowed(now))
                return now;
            DateTime today = now.Date + Start.ToTimeSpan();
            return today > now ? today : today.AddDays(1);
        }

        /// <summary>Parses "HH:mm"; returns false for anything else.</summary>
        public static bool TryParseTime(string? text, out TimeOnly time) =>
            TimeOnly.TryParseExact(text?.Trim() ?? "", new[] { "H:mm", "HH:mm" }, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out time);
    }
}
