namespace GameVault.Core.Couch
{
    /// <summary>
    /// The quiet music behind the living-room mode's menus, made here (no file to ship or license): four soft chords
    /// of a slow pad, a few bell notes over them, and an echo. It loops without a seam: everything that rings past
    /// the end is added back at the start.
    /// </summary>
    public static class CouchMusic
    {
        public const int Rate = 22050;
        public const double ChordSeconds = 8;

        // F maj7, A min9, C maj7, G 6/9: open, a little dreamy, never resolving hard
        private static readonly double[][] Calm =
        {
            new[] { 174.61, 220.00, 261.63, 329.63 },
            new[] { 110.00, 164.81, 196.00, 246.94, 261.63 },
            new[] { 130.81, 196.00, 246.94, 329.63 },
            new[] { 98.00, 146.83, 164.81, 220.00, 246.94 },
        };

        // A major pentatonic, two octaves up: the bells never clash with the chords
        private static readonly double[] CalmBells = { 659.26, 739.99, 880.00, 987.77, 1108.73, 1318.51 };

        // The night: A min9, F maj7, D min9, E min7, lower and darker; A minor pentatonic bells, an octave lower
        private static readonly double[][] Night =
        {
            new[] { 110.00, 164.81, 196.00, 246.94, 261.63 },
            new[] { 87.31, 130.81, 164.81, 220.00 },
            new[] { 73.42, 110.00, 130.81, 174.61, 164.81 },
            new[] { 82.41, 123.47, 146.83, 196.00 },
        };
        private static readonly double[] NightBells = { 440.00, 523.25, 587.33, 659.26, 783.99 };

        public static double Seconds => Calm.Length * ChordSeconds;

        /// <summary>The loop, 16 bits, one channel, at <see cref="Rate"/>.</summary>
        /// <param name="theme">"calm" (the usual one) or "night" (lower, darker).</param>
        public static short[] Samples(string theme = "calm")
        {
            bool night = theme == "night";
            double[][] Chords = night ? Night : Calm;
            double[] Bells = night ? NightBells : CalmBells;
            int length = (int)(Seconds * Rate);
            var buffer = new double[length];
            int chordLength = (int)(ChordSeconds * Rate);
            double fade = 2.5 * Rate;
            for (int c = 0; c < Chords.Length; c++)
            {
                // Each chord sounds a little past its time and fades into the next, around the loop
                int start = c * chordLength;
                int span = chordLength + (int)fade;
                foreach (double note in Chords[c])
                {
                    double phaseA = 0, phaseB = 0;
                    for (int i = 0; i < span; i++)
                    {
                        double swell = Math.Min(1, i / fade) * Math.Min(1, (span - i) / fade);
                        swell = swell * swell * (3 - 2 * swell);
                        // Two voices a little out of tune: a slow, wide shimmer
                        phaseA += 2 * Math.PI * note * 1.0012 / Rate;
                        phaseB += 2 * Math.PI * note * 0.9988 / Rate;
                        double voice = 0;
                        for (int h = 1; h <= 4; h++)
                            voice += (Math.Sin(phaseA * h) + Math.Sin(phaseB * h)) / Math.Pow(h, 1.8);
                        buffer[(start + i) % length] += voice * swell * 0.08;
                    }
                }
            }
            // The bells: at fixed moments, so the loop is the same each time
            var random = new Random(night ? 20261112 : 20261111);
            for (double at = 1.3; at < Seconds; at += 1.7 + random.NextDouble() * 2.4)
            {
                double note = Bells[random.Next(Bells.Length)];
                double volume = 0.05 + random.NextDouble() * 0.04;
                int start = (int)(at * Rate);
                double phase = 0;
                for (int i = 0; i < (int)(3 * Rate); i++)
                {
                    double t = (double)i / Rate;
                    phase += 2 * Math.PI * note / Rate;
                    double ring = Math.Min(1, t / 0.004) * Math.Exp(-t / 0.7);
                    buffer[(start + i) % length] += (Math.Sin(phase) + 0.3 * Math.Sin(phase * 2.01) * Math.Exp(-t / 0.25)) * ring * volume;
                }
            }
            // A soft low-pass, then an echo around the loop (twice: the echo of the end reaches the start)
            double low = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < length; i++)
                    buffer[i] = low += (night ? 0.22 : 0.35) * (buffer[i] - low);
            int delay = (int)(0.42 * Rate);
            var echoed = (double[])buffer.Clone();
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < length; i++)
                    echoed[i] = buffer[i] + 0.32 * echoed[(i - delay + length) % length];
            // Quiet: under the menus' sounds, under a conversation
            double max = echoed.Max(Math.Abs);
            var samples = new short[length];
            for (int i = 0; i < length; i++)
                samples[i] = (short)Math.Round(echoed[i] / max * 0.16 * short.MaxValue);
            return samples;
        }
    }
}
