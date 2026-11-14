namespace GameVault.Core.Couch
{
    /// <summary>
    /// Where a game's theme starts to be heard, without decoding it: in YouTube's WebM sound (Opus), a frame of silence
    /// is a few bytes, a frame of music hundreds. The frames' sizes are read from the file's beginning; the theme starts
    /// a little before the first ones that are loud, when that is later than a second.
    /// </summary>
    public static class QuietStart
    {
        private const uint Segment = 0x18538067, Cluster = 0x1F43B675, Info = 0x1549A966, TimecodeScale = 0x2AD7B1,
            Timecode = 0xE7, SimpleBlock = 0xA3, BlockGroup = 0xA0, Block = 0xA1;
        /// <summary>Not skipped for less: a second of silence is the theme's own.</summary>
        public static readonly TimeSpan Least = TimeSpan.FromSeconds(1);
        /// <summary>Started this much before the sound, so its first note is not cut.</summary>
        public static readonly TimeSpan Before = TimeSpan.FromMilliseconds(300);

        /// <summary>The frames read: their time from the start, and their size.</summary>
        public static List<(TimeSpan At, int Bytes)> Frames(ReadOnlySpan<byte> webm)
        {
            var frames = new List<(TimeSpan, int)>();
            long scale = 1_000_000, cluster = 0;
            int at = 0;
            Read(webm, ref at, webm.Length, frames, ref scale, ref cluster);
            return frames;
        }

        private static void Read(ReadOnlySpan<byte> data, ref int at, int end, List<(TimeSpan, int)> frames, ref long scale, ref long cluster)
        {
            while (at < end)
            {
                int start = at;
                if (!Id(data, ref at, out uint id) || !Size(data, ref at, out long size))
                    return;
                // Unknown size (a live stream's segment and clusters): to the end of what was read
                int stop = size < 0 || at + size > data.Length ? data.Length : at + (int)size;
                switch (id)
                {
                    case Segment or Cluster or Info or BlockGroup:
                        Read(data, ref at, stop, frames, ref scale, ref cluster);
                        at = Math.Max(at, Math.Min(stop, data.Length));
                        break;
                    case TimecodeScale:
                        scale = Unsigned(data[at..stop]);
                        at = stop;
                        break;
                    case Timecode:
                        cluster = Unsigned(data[at..stop]);
                        at = stop;
                        break;
                    case SimpleBlock or Block:
                        // Track number (a small number), then the time from the cluster's, then the flags
                        int header = at;
                        if (Size(data, ref header, out _) && header + 3 <= stop && stop <= data.Length && size >= 0)
                        {
                            short relative = (short)(data[header] << 8 | data[header + 1]);
                            long ticks = (cluster + relative) * scale / 100;
                            frames.Add((TimeSpan.FromTicks(ticks), stop - (header + 3)));
                        }
                        at = stop;
                        break;
                    default:
                        at = stop;
                        break;
                }
                if (at <= start)
                    return;
            }
        }

        /// <summary>Where the theme starts: a little before its first loud frames, or null when it is heard from the start.</summary>
        public static TimeSpan? Of(ReadOnlySpan<byte> webm)
        {
            var frames = Frames(webm);
            if (frames.Count < 20)
                return null;
            // Loud: a third of the usual frame's size; three in a row (not a click)
            var sizes = frames.Select(f => f.Bytes).OrderBy(b => b).ToList();
            int usual = sizes[sizes.Count * 3 / 4];
            int loud = Math.Max(24, usual / 3);
            for (int i = 0; i + 2 < frames.Count; i++)
            {
                if (frames[i].Bytes < loud || frames[i + 1].Bytes < loud || frames[i + 2].Bytes < loud)
                    continue;
                TimeSpan start = frames[i].At - Before;
                return start >= Least ? start : null;
            }
            return null;
        }

        private static bool Id(ReadOnlySpan<byte> data, ref int at, out uint id)
        {
            id = 0;
            if (at >= data.Length || data[at] == 0)
                return false;
            int length = 1;
            for (byte mask = 0x80; (data[at] & mask) == 0; mask >>= 1)
                length++;
            if (length > 4 || at + length > data.Length)
                return false;
            for (int i = 0; i < length; i++)
                id = id << 8 | data[at + i];
            at += length;
            return true;
        }

        /// <param name="size">-1 when unknown (all its bits set).</param>
        private static bool Size(ReadOnlySpan<byte> data, ref int at, out long size)
        {
            size = 0;
            if (at >= data.Length || data[at] == 0)
                return false;
            int length = 1;
            byte mask = 0x80;
            for (; (data[at] & mask) == 0; mask >>= 1)
                length++;
            if (at + length > data.Length)
                return false;
            long value = data[at] & (mask - 1);
            bool unknown = value == mask - 1;
            for (int i = 1; i < length; i++)
            {
                value = value << 8 | data[at + i];
                unknown &= data[at + i] == 0xFF;
            }
            at += length;
            size = unknown ? -1 : value;
            return true;
        }

        private static long Unsigned(ReadOnlySpan<byte> data)
        {
            long value = 0;
            foreach (byte b in data)
                value = value << 8 | b;
            return value;
        }
    }
}
