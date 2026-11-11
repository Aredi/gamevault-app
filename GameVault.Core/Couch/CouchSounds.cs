namespace GameVault.Core.Couch
{
    /// <summary>The sounds of the living-room mode, one by event, as a console's menus have them.</summary>
    public enum CouchSound
    {
        /// <summary>Another game, another line.</summary>
        Move,
        /// <summary>Another tab, the strip, the grid or the list.</summary>
        Tab,
        /// <summary>Chosen.</summary>
        Accept,
        Back,
        /// <summary>A game's screen opened.</summary>
        Open,
        /// <summary>A game's screen left for the list.</summary>
        Close,
        /// <summary>A menu over the screen (the menu, the quick access, the filters).</summary>
        Panel,
        PanelClose,
        /// <summary>A game started.</summary>
        Launch,
        /// <summary>Nothing further that way: the end of a list.</summary>
        Edge,
        /// <summary>The login screen comes: a slow chord rising, as a console that wakes.</summary>
        Login,
    }

    /// <summary>
    /// The sounds, made here instead of kept in files: soft mallet notes (a few partials that fade out), airy sweeps of
    /// filtered noise, and a little room around them. Quiet enough to be heard under a TV's sound without covering it.
    /// </summary>
    public static class CouchSounds
    {
        public const int Rate = 44100;
        /// <summary>The length of a WAV file's header before its samples.</summary>
        public const int Header = 44;

        // A soft mallet (marimba-like): its fundamental, an octave, and a little of the bright partial
        private static readonly (double Ratio, double Amp, double Decay)[] Mallet = { (1, 1, 1), (2, 0.28, 0.55), (3.98, 0.08, 0.3) };
        // A glassy bell for the bigger moments
        private static readonly (double Ratio, double Amp, double Decay)[] Bell = { (1, 1, 1), (2.01, 0.42, 0.7), (2.76, 0.22, 0.45), (5.4, 0.06, 0.25) };

        /// <summary>A note added at <paramref name="at"/> seconds: its partials, each fading with its own speed.</summary>
        private static void Note(double[] buffer, double at, double frequency, double volume, double decay, (double Ratio, double Amp, double Decay)[] partials, double glideTo = 0)
        {
            int start = (int)(at * Rate);
            int length = Math.Min(buffer.Length - start, (int)(decay * 6 * Rate));
            foreach (var (ratio, amp, partialDecay) in partials)
            {
                double phase = 0, tau = decay * partialDecay;
                for (int i = 0; i < length; i++)
                {
                    double t = (double)i / Rate;
                    double f = (glideTo > 0 ? frequency + (glideTo - frequency) * Math.Min(1, t / 0.06) : frequency) * ratio;
                    phase += 2 * Math.PI * f / Rate;
                    // Up in 2 ms (no click), then down
                    double envelope = Math.Min(1, t / 0.002) * Math.Exp(-t / tau);
                    buffer[start + i] += Math.Sin(phase) * amp * envelope * volume;
                }
            }
        }

        /// <summary>A breath of noise whose brightness sweeps (a swoosh), rising then falling in loudness.</summary>
        private static void Sweep(double[] buffer, double at, double seconds, double volume, double fromHz, double toHz, int seed)
        {
            var random = new Random(seed);
            int start = (int)(at * Rate), length = Math.Min(buffer.Length - start, (int)(seconds * Rate));
            double low = 0, lower = 0;
            for (int i = 0; i < length; i++)
            {
                double t = (double)i / length;
                double cutoff = fromHz + (toHz - fromHz) * t;
                double a = 1 - Math.Exp(-2 * Math.PI * cutoff / Rate);
                double noise = random.NextDouble() * 2 - 1;
                // Two gentle low-passes, minus the second: a soft band around the cutoff
                low += a * (noise - low);
                lower += a * 0.5 * (low - lower);
                buffer[start + i] += (low - lower) * Math.Sin(Math.PI * t) * volume;
            }
        }

        /// <summary>A small room: a few short echoes, so the sounds are not dry beeps.</summary>
        private static void Room(double[] buffer, double mix)
        {
            int[] delays = { (int)(0.029 * Rate), (int)(0.041 * Rate), (int)(0.053 * Rate) };
            var wet = new double[buffer.Length];
            foreach (int delay in delays)
            {
                var line = new double[buffer.Length];
                for (int i = 0; i < buffer.Length; i++)
                    line[i] = buffer[i] + (i >= delay ? line[i - delay] * 0.38 : 0);
                for (int i = delay; i < buffer.Length; i++)
                    wet[i] += line[i - delay] / delays.Length;
            }
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] += wet[i] * mix;
        }

        private static (double Seconds, double Peak, Action<double[]> Draw, double Room) Recipe(CouchSound sound, Timbre t) => sound switch
        {
            CouchSound.Move => (0.07, 0.12, b => { Note(b, 0, 1568*t.Pitch, 1, (0.012) * t.Decay, t.Main); Sweep(b, 0, 0.012, (0.5) * t.Sweep, 6000, 3000, 1); }, 0.12),
            CouchSound.Tab => (0.16, 0.13, b => { Sweep(b, 0, 0.13, (1) * t.Sweep, 700, 5200, 2); Note(b, 0.02, 988*t.Pitch, 0.35, (0.03) * t.Decay, t.Main); }, 0.2),
            CouchSound.Accept => (0.3, 0.17, b => { Note(b, 0, 1046.5*t.Pitch, 1, (0.05) * t.Decay, t.Main); Note(b, 0.05, 1568*t.Pitch, 0.9, (0.07) * t.Decay, t.Main); }, 0.3),
            CouchSound.Back => (0.26, 0.14, b => { Note(b, 0, 1318.5*t.Pitch, 0.9, (0.045) * t.Decay, t.Main); Note(b, 0.045, 880*t.Pitch, 1, (0.06) * t.Decay, t.Main); }, 0.25),
            CouchSound.Open => (0.42, 0.17, b =>
            {
                Sweep(b, 0, 0.16, (0.7) * t.Sweep, 600, 4500, 3);
                Note(b, 0.06, 880*t.Pitch, 0.7, (0.09) * t.Decay, t.Bright);
                Note(b, 0.06, 1108.7*t.Pitch, 0.5, (0.09) * t.Decay, t.Bright);
                Note(b, 0.06, 1318.5*t.Pitch, 0.5, (0.11) * t.Decay, t.Bright);
            }, 0.35),
            CouchSound.Close => (0.22, 0.12, b => { Sweep(b, 0, 0.16, (0.9) * t.Sweep, 4200, 600, 4); Note(b, 0.03, 659.3*t.Pitch, 0.5, (0.05) * t.Decay, t.Main); }, 0.25),
            CouchSound.Panel => (0.26, 0.14, b => { Sweep(b, 0, 0.18, (0.8) * t.Sweep, 400, 3000, 5); Note(b, 0.02, 392*t.Pitch, 0.8, (0.07) * t.Decay, t.Main, glideTo: 523.3 * t.Pitch); }, 0.3),
            CouchSound.PanelClose => (0.22, 0.12, b => { Sweep(b, 0, 0.15, (0.8) * t.Sweep, 3000, 400, 6); Note(b, 0.02, 523.3*t.Pitch, 0.7, (0.05) * t.Decay, t.Main, glideTo: 392 * t.Pitch); }, 0.25),
            CouchSound.Launch => (0.95, 0.18, b =>
            {
                // Up a major chord, then all of it ringing: the game starts
                double[] notes = { 523.3, 659.3, 784, 1046.5, 1568 };
                for (int i = 0; i < notes.Length; i++)
                    Note(b, i * 0.065, notes[i]*t.Pitch, i == notes.Length - 1 ? 0.6 : 0.8, (0.18 + i * 0.03) * t.Decay, t.Bright);
                Sweep(b, 0.2, 0.5, (0.25) * t.Sweep, 2000, 9000, 7);
            }, 0.45),
            CouchSound.Login => (0.95, 0.16, b =>
            {
                Sweep(b, 0, 0.7, (0.5) * t.Sweep, 300, 6000, 8);
                double[] notes = { 261.6, 392, 523.3, 659.3, 784 };
                for (int i = 0; i < notes.Length; i++)
                    Note(b, 0.08 + i * 0.09, notes[i] * t.Pitch, 0.55, (0.3 + i * 0.04) * t.Decay, t.Bright);
            }, 0.5),
            _ => (0.12, 0.09, b => { Note(b, 0, 220*t.Pitch, 1, (0.025) * t.Decay, t.Main); Note(b, 0.035, 196*t.Pitch, 0.8, (0.03) * t.Decay, t.Main); }, 0.1),
        };

        /// <summary>How a pack of sounds plays the same recipes: its instrument, its room, its pitch.</summary>
        private sealed record Timbre((double Ratio, double Amp, double Decay)[] Main, (double Ratio, double Amp, double Decay)[] Bright, double Room, double Decay, double Sweep, double Pitch);

        // An old console's square wave: its odd partials, held a little longer, no room
        private static readonly (double Ratio, double Amp, double Decay)[] Square = { (1, 1, 1), (3, 0.33, 1), (5, 0.2, 1), (7, 0.14, 1), (9, 0.11, 1) };

        private static Timbre TimbreOf(string pack) => pack switch
        {
            "retro" => new Timbre(Square, Square, 0, 1.6, 0.35, 0.5),
            "crystal" => new Timbre(Bell, Bell, 1.8, 1.4, 0.6, 1),
            _ => new Timbre(Mallet, Bell, 1, 1, 1, 1),
        };

        /// <summary>The samples of a sound.</summary>
        /// <param name="pack">"soft" (mallets), "retro" (square waves of an old console) or "crystal" (bells).</param>
        /// <param name="volume">Its loudness in percent of the usual one.</param>
        public static short[] Samples(CouchSound sound, string pack = "soft", int volume = 100)
        {
            Timbre timbre = TimbreOf(pack);
            var (seconds, peak, draw, room) = Recipe(sound, timbre);
            var buffer = new double[(int)(seconds * Rate)];
            draw(buffer);
            Room(buffer, Math.Min(0.6, room * timbre.Room));
            return Finish(buffer, peak * Math.Clamp(volume, 0, 100) / 100.0);
        }

        /// <summary>To 16 bits at the loudness asked, with the last milliseconds faded out: no click at either end.</summary>
        internal static short[] Finish(double[] buffer, double peak)
        {
            double max = buffer.Max(Math.Abs);
            double gain = max > 0 ? peak / max : 0;
            int fade = Math.Min(buffer.Length / 4, (int)(0.008 * Rate));
            var samples = new short[buffer.Length];
            for (int i = 0; i < buffer.Length; i++)
            {
                double edge = Math.Min(1, Math.Min(i, buffer.Length - 1 - i) / (double)fade);
                samples[i] = (short)Math.Round(buffer[i] * gain * edge * short.MaxValue);
            }
            return samples;
        }

        /// <summary>A sound as a WAV file, as Windows plays it from memory.</summary>
        public static byte[] Wave(CouchSound sound, string pack = "soft", int volume = 100) => Wave(Samples(sound, pack, volume), Rate);

        /// <summary>Samples of one channel as a WAV file.</summary>
        public static byte[] Wave(short[] samples, int rate)
        {
            using var bytes = new MemoryStream();
            using var writer = new BinaryWriter(bytes);
            int data = samples.Length * 2;
            writer.Write("RIFF"u8);
            writer.Write(36 + data);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(rate);
            writer.Write(rate * 2);
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
