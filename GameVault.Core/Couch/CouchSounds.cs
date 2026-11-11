namespace GameVault.Core.Couch
{
    /// <summary>The sounds of the living-room mode.</summary>
    public enum CouchSound
    {
        /// <summary>Another game, another line.</summary>
        Move,
        /// <summary>Another tab, the strip or the grid.</summary>
        Tab,
        /// <summary>Opened, chosen.</summary>
        Accept,
        Back,
    }

    /// <summary>
    /// The sounds, made here instead of kept in files: one or two short soft notes each (16 bits, one channel), quiet
    /// enough to be heard under a TV's sound without covering it.
    /// </summary>
    public static class CouchSounds
    {
        public const int Rate = 44100;
        /// <summary>The length of a WAV file's header before its samples.</summary>
        public const int Header = 44;

        /// <summary>The notes of a sound: their pitch at their start and at their end, their length and how loud.</summary>
        private static (double From, double To, double Seconds, double Volume)[] Notes(CouchSound sound) => sound switch
        {
            CouchSound.Move => new[] { (1150.0, 1150.0, 0.035, 0.14) },
            CouchSound.Tab => new[] { (720.0, 1040.0, 0.075, 0.15) },
            CouchSound.Accept => new[] { (880.0, 880.0, 0.055, 0.18), (1320.0, 1320.0, 0.085, 0.18) },
            _ => new[] { (660.0, 660.0, 0.055, 0.16), (440.0, 440.0, 0.085, 0.16) },
        };

        /// <summary>The samples of a sound.</summary>
        public static short[] Samples(CouchSound sound)
        {
            var samples = new List<short>();
            foreach (var (from, to, seconds, volume) in Notes(sound))
            {
                int count = (int)(seconds * Rate);
                double phase = 0;
                for (int i = 0; i < count; i++)
                {
                    double t = (double)i / count;
                    phase += 2 * Math.PI * (from + (to - from) * t) / Rate;
                    // Up in 3 ms, then down to nothing: no click at either end
                    double envelope = Math.Min(1, i / (0.003 * Rate)) * Math.Pow(1 - t, 1.6);
                    samples.Add((short)(Math.Sin(phase) * envelope * volume * short.MaxValue));
                }
            }
            return samples.ToArray();
        }

        /// <summary>A sound as a WAV file, as Windows plays it from memory.</summary>
        public static byte[] Wave(CouchSound sound)
        {
            short[] samples = Samples(sound);
            using var bytes = new MemoryStream();
            using var writer = new BinaryWriter(bytes);
            int data = samples.Length * 2;
            writer.Write("RIFF"u8);
            writer.Write(36 + data);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(Rate);
            writer.Write(Rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(data);
            foreach (short sample in samples)
                writer.Write(sample);
            writer.Flush();
            return bytes.ToArray();
        }
    }
}
