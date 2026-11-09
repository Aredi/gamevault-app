using System.Globalization;
using System.IO.Compression;
using System.IO.Hashing;

namespace GameVault.Core.Downloads
{
    /// <summary>
    /// A file on the server read by parts, as a seekable stream: enough for <see cref="ZipArchive"/> to list a zip
    /// (its directory is at the end) and to read only some of its files. The end of the file is fetched once and
    /// kept; everything else is read from one connection as long as the reads follow each other.
    /// </summary>
    public sealed class RangeStream : Stream
    {
        /// <summary>A jump forward shorter than this is read and thrown away instead of asked with a new request.</summary>
        private const int SkipLimit = 256 * 1024;

        private readonly Func<long, Stream> open;
        private readonly long length;
        private readonly long tailStart;
        private byte[]? tail;
        private Stream? live;
        /// <summary>The last bytes read from the connection: a zip reader goes back over the header of a file before its content.</summary>
        private readonly byte[] window = new byte[256 * 1024];
        private long windowStart;
        private int windowLength;
        private long position;
        private long fetched;

        /// <param name="open">Opens the file from this position to its end.</param>
        public RangeStream(long length, Func<long, Stream> open, int tailSize = 4 * 1024 * 1024)
        {
            this.length = length;
            this.open = open;
            tailStart = Math.Max(0, length - tailSize);
        }

        /// <summary>The bytes received from the server so far.</summary>
        public long Fetched => Interlocked.Read(ref fetched);

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => position; set => position = value; }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (position >= length || buffer.Length == 0)
                return 0;
            int read;
            if (position >= tailStart)
            {
                tail ??= ReadTail();
                read = (int)Math.Min(buffer.Length, length - position);
                tail.AsSpan((int)(position - tailStart), read).CopyTo(buffer);
            }
            else
            {
                if (position < windowStart || position >= windowStart + windowLength)
                    Fill();
                int from = (int)(position - windowStart);
                read = Math.Min(buffer.Length, windowLength - from);
                window.AsSpan(from, read).CopyTo(buffer);
            }
            position += read;
            return read;
        }

        /// <summary>Reads the bytes at <see cref="position"/> from the connection: it goes on, skips a short gap, or asks again from there.</summary>
        private void Fill()
        {
            long next = windowStart + windowLength;
            if (live != null && position >= next && position - next <= SkipLimit)
            {
                while (next < position)
                    next += Receive((int)Math.Min(window.Length, position - next));
            }
            else
            {
                live?.Dispose();
                live = open(position);
            }
            windowStart = position;
            windowLength = 0;
            windowLength = Receive(window.Length);
        }

        private int Receive(int count)
        {
            int read = live!.Read(window, 0, count);
            if (read == 0)
                throw new EndOfStreamException("The server ended the file early");
            Interlocked.Add(ref fetched, read);
            return read;
        }

        private byte[] ReadTail()
        {
            byte[] bytes = new byte[length - tailStart];
            using Stream stream = open(tailStart);
            stream.ReadExactly(bytes);
            Interlocked.Add(ref fetched, bytes.Length);
            return bytes;
        }

        public override long Seek(long offset, SeekOrigin origin) => position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            _ => length + offset,
        };

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                live?.Dispose();
            base.Dispose(disposing);
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// The checksums of the installed files ("gamevault-sums" next to the installation's settings), with the size
    /// and date each one had: a file that still has both is not read again at the next update.
    /// </summary>
    public sealed class FileSums
    {
        public const string FileName = "gamevault-sums";

        private readonly Dictionary<string, (long Size, long Written, uint Crc)> known = new(StringComparer.Ordinal);

        public static FileSums Read(string installationDirectory)
        {
            var sums = new FileSums();
            string file = Path.Combine(installationDirectory, FileName);
            if (!File.Exists(file))
                return sums;
            foreach (string line in File.ReadLines(file))
            {
                // crc size date path
                string[] parts = line.Split(' ', 4);
                if (parts.Length == 4 && uint.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint crc)
                    && long.TryParse(parts[1], out long size) && long.TryParse(parts[2], out long written))
                    sums.known[parts[3]] = (size, written, crc);
            }
            return sums;
        }

        /// <summary>The checksum of this installed file: the one kept if the file did not change since, else read from the file.</summary>
        public uint Of(string relative, FileInfo file, CancellationToken cancel = default)
        {
            if (known.TryGetValue(relative, out var sum) && sum.Size == file.Length && sum.Written == file.LastWriteTimeUtc.Ticks)
                return sum.Crc;
            uint crc = ZipDelta.Crc(file.FullName, cancel);
            known[relative] = (file.Length, file.LastWriteTimeUtc.Ticks, crc);
            return crc;
        }

        /// <summary>Keeps the checksums of these files of the installation (the zip gave them) and forgets every other.</summary>
        public static void Write(string installationDirectory, string filesDirectory, IEnumerable<(string Path, uint Crc)> files)
        {
            var lines = new List<string>();
            foreach ((string relative, uint crc) in files)
            {
                var file = new FileInfo(Path.Combine(filesDirectory, relative));
                if (file.Exists)
                    lines.Add($"{crc:x8} {file.Length} {file.LastWriteTimeUtc.Ticks} {relative}");
            }
            string target = Path.Combine(installationDirectory, FileName);
            File.WriteAllLines(target + ".tmp", lines);
            File.Move(target + ".tmp", target, true);
        }
    }

    /// <summary>What an update by pieces fetches: the files of the new zip the installation does not have yet.</summary>
    public sealed class DeltaPlan
    {
        /// <summary>The files to fetch, in the order of the zip.</summary>
        public List<ZipArchiveEntry> Changed { get; } = new();
        /// <summary>Every file of the new version, relative to the installation's files, with its checksum.</summary>
        public List<(string Path, uint Crc)> Files { get; } = new();
        /// <summary>The size of <see cref="Changed"/> in the zip (what the network carries).</summary>
        public long DownloadSize => Changed.Sum(entry => entry.CompressedLength);
        /// <summary>The size of <see cref="Changed"/> on the disk.</summary>
        public long ExtractedSize => Changed.Sum(entry => entry.Length);
    }

    /// <summary>
    /// Updates an installed game from the zip of its new version without downloading the zip: the files that are
    /// the same (size and checksum) stay, the others are read out of the zip on the server.
    /// </summary>
    public static class ZipDelta
    {
        public static uint Crc(string file, CancellationToken cancel = default)
        {
            var crc = new Crc32();
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, FileOptions.SequentialScan);
            byte[] buffer = new byte[1 << 20];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancel.ThrowIfCancellationRequested();
                crc.Append(buffer.AsSpan(0, read));
            }
            return crc.GetCurrentHashAsUInt32();
        }

        /// <summary>
        /// Compares the zip with the installed files. Null when the zip cannot be applied file by file: a
        /// password, links, or names that could land elsewhere than where the extraction put them.
        /// </summary>
        /// <param name="compared">Called with the bytes compared so far and the total.</param>
        public static DeltaPlan? Plan(ZipArchive zip, string filesDirectory, FileSums sums, Action<long, long>? compared = null, CancellationToken cancel = default)
        {
            var plan = new DeltaPlan();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<ZipArchiveEntry> files = zip.Entries.Where(entry => !entry.FullName.EndsWith('/')).ToList();
            long total = files.Sum(entry => entry.Length);
            long done = 0;
            foreach (ZipArchiveEntry entry in files)
            {
                cancel.ThrowIfCancellationRequested();
                string relative = entry.FullName;
                bool link = ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;
                if (entry.IsEncrypted || link || !IsPlain(relative) || !seen.Add(relative))
                    return null;
                var installed = new FileInfo(Path.Combine(filesDirectory, relative));
                if (!installed.Exists || installed.Length != entry.Length || sums.Of(relative, installed, cancel) != entry.Crc32)
                    plan.Changed.Add(entry);
                plan.Files.Add((relative, entry.Crc32));
                done += entry.Length;
                compared?.Invoke(done, total);
            }
            return plan.Files.Count == 0 ? null : plan;
        }

        /// <summary>A relative path with "/" that stays inside the folder and that every tool reads the same way.</summary>
        internal static bool IsPlain(string path) =>
            path.Length > 0 && !path.StartsWith('/') && !path.Contains('\\') && !path.Contains(':') && !path.Contains('�')
            && !path.Split('/').Any(part => part is "" or "." or "..");

        /// <summary>
        /// Reads the changed files out of the zip into <paramref name="staging"/> and checks each one. Files already
        /// there and whole (an interrupted attempt) are not fetched again. The installation is not touched.
        /// </summary>
        /// <param name="progress">Called while the bytes arrive.</param>
        public static void Fetch(DeltaPlan plan, string staging, string filesDirectory, Action? progress = null, CancellationToken cancel = default)
        {
            byte[] buffer = new byte[1 << 20];
            foreach (ZipArchiveEntry entry in plan.Changed)
            {
                cancel.ThrowIfCancellationRequested();
                string target = Path.Combine(staging, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!(File.Exists(target) && new FileInfo(target).Length == entry.Length && Crc(target, cancel) == entry.Crc32))
                {
                    var crc = new Crc32();
                    try
                    {
                        using (Stream source = entry.Open())
                        using (var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                        {
                            int read;
                            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                cancel.ThrowIfCancellationRequested();
                                crc.Append(buffer.AsSpan(0, read));
                                file.Write(buffer, 0, read);
                                progress?.Invoke();
                            }
                        }
                        if (new FileInfo(target).Length != entry.Length || crc.GetCurrentHashAsUInt32() != entry.Crc32)
                            throw new InvalidDataException($"'{entry.FullName}' arrived damaged");
                    }
                    catch
                    {
                        // A part of a file is of no use: the next attempt fetches it whole
                        File.Delete(target);
                        throw;
                    }
                }
                KeepRights(entry, target, Path.Combine(filesDirectory, entry.FullName));
                progress?.Invoke();
            }
        }

        /// <summary>
        /// The rights the zip gives the file (zips made on Linux), and a file that replaces a program stays one:
        /// the launcher or the player made it executable.
        /// </summary>
        private static void KeepRights(ZipArchiveEntry entry, string target, string replaced)
        {
            if (OperatingSystem.IsWindows())
                return;
            const UnixFileMode Execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            var given = (UnixFileMode)((entry.ExternalAttributes >> 16) & 0x1FF);
            UnixFileMode mode = given != 0 ? given : File.GetUnixFileMode(target);
            if (File.Exists(replaced))
                mode |= File.GetUnixFileMode(replaced) & Execute;
            File.SetUnixFileMode(target, mode | UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
