using gamevault.Converter;
using gamevault.Localization;
using GameVault.Core.Downloads;
using System;
using System.Collections.Generic;

namespace gamevault.Helper
{
    /// <summary>How a running download reads, the same everywhere: "2,1 Go sur 12 Go · 18,4 Mo/s · encore 9 min 05".</summary>
    internal static class DownloadText
    {
        /// <summary>"2 min 30", "55 s", "1 h 05".</summary>
        public static string Duration(TimeSpan time) =>
            time.TotalHours >= 1 ? Loc.F("{0} h {1}", (int)time.TotalHours, time.Minutes.ToString("00"))
            : time.TotalMinutes >= 1 ? Loc.F("{0} min {1}", time.Minutes, time.Seconds.ToString("00"))
            : Loc.F("{0} s", Math.Max(1, (int)Math.Ceiling(time.TotalSeconds)));

        /// <summary>What came down of how much, the speed, and the time left when the speed tells one.</summary>
        public static string Line(long done, long total, double bytesPerSecond)
        {
            var parts = new List<string> { Loc.F("{0} of {1}", GameSizeConverter.Text(done), GameSizeConverter.Text(total)) };
            if (bytesPerSecond > 0)
                parts.Add($"{GameSizeConverter.Text(bytesPerSecond, 1000)}/s");
            if (TimeLeft.Of(total - done, bytesPerSecond) is TimeSpan left)
                parts.Add(Loc.F("{0} left", Duration(left)));
            return string.Join(" · ", parts);
        }
    }
}
