using System.Globalization;
using System.Text;

namespace GameVault.Core.Downloads
{
    /// <summary>One byte range of a download fetched by its own connection. <see cref="Position"/> is the next byte to fetch.</summary>
    public sealed class DownloadPart
    {
        public long Start { get; }
        /// <summary>Last byte, inclusive (like the HTTP Range header).</summary>
        public long End { get; }
        public long Position { get; set; }

        public DownloadPart(long start, long end, long position)
        {
            Start = start;
            End = end;
            Position = Math.Clamp(position, start, end + 1);
        }

        public bool IsComplete => Position > End;
        public long Downloaded => Position - Start;
        public long Length => End - Start + 1;
        public string RangeHeader => $"bytes={Position}-{End}";
    }

    /// <summary>
    /// Splits a download into ranges fetched in parallel and keeps the progress of each one, so a paused or
    /// interrupted download continues every range where it stopped.
    /// </summary>
    public static class DownloadParts
    {
        /// <summary>Below this size one connection is as fast: the extra requests are not worth it.</summary>
        public const long MinimumPartSize = 32L * 1024 * 1024;

        public static List<DownloadPart> Plan(long totalSize, int connections)
        {
            int count = (int)Math.Clamp(Math.Min(connections, totalSize / MinimumPartSize), 1, 16);
            long size = totalSize / count;
            var parts = new List<DownloadPart>();
            for (int i = 0; i < count; i++)
            {
                long start = i * size;
                long end = i == count - 1 ? totalSize - 1 : start + size - 1;
                parts.Add(new DownloadPart(start, end, start));
            }
            return parts;
        }

        /// <summary>"total|start-end@position,..."</summary>
        public static string Serialize(long totalSize, IEnumerable<DownloadPart> parts)
        {
            var text = new StringBuilder(totalSize.ToString(CultureInfo.InvariantCulture)).Append('|');
            text.AppendJoin(',', parts.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Start}-{p.End}@{p.Position}")));
            return text.ToString();
        }

        /// <summary>The saved ranges, or null when <paramref name="text"/> is not a valid checkpoint of a download of that size.</summary>
        public static List<DownloadPart>? Parse(string? text, out long totalSize)
        {
            totalSize = 0;
            if (string.IsNullOrWhiteSpace(text))
                return null;
            string[] halves = text.Split('|');
            if (halves.Length != 2 || !long.TryParse(halves[0], NumberStyles.None, CultureInfo.InvariantCulture, out totalSize) || totalSize <= 0)
                return null;
            var parts = new List<DownloadPart>();
            long expectedStart = 0;
            foreach (string entry in halves[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] rangeAndPosition = entry.Split('@');
                string[] range = rangeAndPosition[0].Split('-');
                if (rangeAndPosition.Length != 2 || range.Length != 2
                    || !long.TryParse(range[0], NumberStyles.None, CultureInfo.InvariantCulture, out long start)
                    || !long.TryParse(range[1], NumberStyles.None, CultureInfo.InvariantCulture, out long end)
                    || !long.TryParse(rangeAndPosition[1], NumberStyles.None, CultureInfo.InvariantCulture, out long position)
                    || start != expectedStart || end < start)
                    return null;
                parts.Add(new DownloadPart(start, end, position));
                expectedStart = end + 1;
            }
            // The ranges must cover the whole file without gaps
            return parts.Count > 0 && expectedStart == totalSize ? parts : null;
        }

        public static long Downloaded(IEnumerable<DownloadPart> parts) => parts.Sum(p => p.Downloaded);
    }
}
