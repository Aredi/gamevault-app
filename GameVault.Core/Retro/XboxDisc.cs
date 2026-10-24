namespace GameVault.Core.Retro
{
    /// <summary>
    /// xemu reads an XISO: the game's partition of an Xbox disc alone. A full ("redump") dump, about 7 GB, starts with the
    /// disc's video partition (387 MB); the game's is after it. Both begin with the XDVDFS volume's name, 32 sectors in.
    /// </summary>
    public static class XboxDisc
    {
        public const long VideoPartition = 387L * 1024 * 1024;
        private const long VolumeName = 32 * 2048;
        private static readonly byte[] Magic = "MICROSOFT*XBOX*MEDIA"u8.ToArray();

        private static bool HasVolume(FileStream disc, long partition)
        {
            if (disc.Length < partition + VolumeName + Magic.Length)
                return false;
            var read = new byte[Magic.Length];
            disc.Position = partition + VolumeName;
            disc.ReadExactly(read);
            return read.AsSpan().SequenceEqual(Magic);
        }

        /// <summary>A full dump, to make an XISO; an XISO, or any other file, is not one.</summary>
        public static bool IsRedump(string path)
        {
            using FileStream disc = File.OpenRead(path);
            return !HasVolume(disc, 0) && HasVolume(disc, VideoPartition);
        }

        /// <summary>
        /// The game's partition moved to the file's start, then the rest cut: no second copy of the game is needed on the
        /// disk. Each block is read 387 MB after where it is written, so none is written over before it is read.
        /// </summary>
        public static void ToXiso(string path, Action<double> progress)
        {
            using var disc = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.SequentialScan);
            long length = disc.Length - VideoPartition;
            var block = new byte[8 * 1024 * 1024];
            for (long done = 0; done < length;)
            {
                disc.Position = VideoPartition + done;
                int read = disc.Read(block, 0, (int)Math.Min(block.Length, length - done));
                if (read == 0)
                    throw new EndOfStreamException();
                disc.Position = done;
                disc.Write(block, 0, read);
                done += read;
                progress((double)done / length);
            }
            disc.SetLength(length);
        }
    }
}
