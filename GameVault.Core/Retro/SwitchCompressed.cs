using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ZstdSharp;

namespace GameVault.Core.Retro
{
    /// <summary>
    /// The compressed Switch games (.nsz, .xcz, made by the nsz tool), which Eden can not read: made a .nsp again, as
    /// nsz itself decompresses them. An .nsz is an .nsp whose contents (.nca) are compressed (.ncz); an .xcz is a game
    /// card whose secure partition holds them, and its secure partition is what Eden plays (an .nsp too).
    /// </summary>
    public static class SwitchCompressed
    {
        public static bool IsCompressed(string path) => Path.GetExtension(path).ToLowerInvariant() is ".nsz" or ".xcz";

        private const int NcaHeaderSize = 0x4000, Chunk = 0x10000;

        private sealed record Entry(string Name, long Offset, long Size);

        /// <summary>The .nsp beside the compressed game, written whole before it is named (a stopped one leaves nothing).</summary>
        /// <returns>The .nsp's path.</returns>
        public static string Decompress(string path, Action<double>? progress = null)
        {
            string target = Path.ChangeExtension(path, ".nsp"), partial = target + ".part";
            try
            {
                Write(path, partial, progress);
            }
            catch
            {
                File.Delete(partial);
                throw;
            }
            File.Move(partial, target, overwrite: true);
            return target;
        }

        private static void Write(string path, string partial, Action<double>? progress)
        {
            using (FileStream source = File.OpenRead(path))
            {
                List<Entry> entries = Path.GetExtension(path).Equals(".xcz", StringComparison.OrdinalIgnoreCase) ? SecurePartition(source) : Partition(source, 0, hashed: false);
                var sizes = entries.Select(e => e.Name.EndsWith(".ncz", StringComparison.OrdinalIgnoreCase) ? NcaSize(source, e) : e.Size).ToList();
                var names = entries.Select(e => e.Name.EndsWith(".ncz", StringComparison.OrdinalIgnoreCase) ? Path.ChangeExtension(e.Name, ".nca") : e.Name).ToList();
                long total = sizes.Sum(), done = 0;
                using (FileStream output = File.Create(partial))
                {
                    WritePfs0Header(output, names, sizes);
                    for (int i = 0; i < entries.Count; i++)
                    {
                        long before = done;
                        Action<long> written = bytes => progress?.Invoke(total == 0 ? 1 : (double)(before + bytes) / total);
                        if (entries[i].Name.EndsWith(".ncz", StringComparison.OrdinalIgnoreCase))
                            WriteNca(source, entries[i], output, written);
                        else
                            Copy(source, entries[i].Offset, entries[i].Size, output, written);
                        done += sizes[i];
                    }
                }
            }
        }

        #region Containers
        /// <summary>A PFS0 (an .nsp) or an HFS0 (a game card's partition) at an offset: its files, their offsets in the file.</summary>
        private static List<Entry> Partition(Stream file, long start, bool hashed)
        {
            byte[] head = ReadAt(file, start, 0x10);
            string magic = Encoding.ASCII.GetString(head, 0, 4);
            if (magic != (hashed ? "HFS0" : "PFS0"))
                throw new InvalidDataException($"Not a Switch package ({magic})");
            int count = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(4)), strings = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(8));
            int entrySize = hashed ? 0x40 : 0x18;
            if (count is < 0 or > 10000 || strings is < 0 or > 1 << 20)
                throw new InvalidDataException("Damaged Switch package header");
            byte[] table = ReadAt(file, start + 0x10, count * entrySize + strings);
            long data = start + 0x10 + table.Length;
            var entries = new List<Entry>();
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> entry = table.AsSpan(i * entrySize, entrySize);
                int nameAt = count * entrySize + BinaryPrimitives.ReadInt32LittleEndian(entry[16..]);
                int nameEnd = Array.IndexOf(table, (byte)0, nameAt);
                // A name from the file stays a name: nothing is written elsewhere than the package
                string name = Path.GetFileName(Encoding.UTF8.GetString(table, nameAt, (nameEnd < 0 ? table.Length : nameEnd) - nameAt));
                entries.Add(new Entry(name, data + BinaryPrimitives.ReadInt64LittleEndian(entry), BinaryPrimitives.ReadInt64LittleEndian(entry[8..])));
            }
            return entries;
        }

        /// <summary>A game card's secure partition (its header at 0, or at 0x1000 in a dump with its key area).</summary>
        private static List<Entry> SecurePartition(Stream file)
        {
            long header = Encoding.ASCII.GetString(ReadAt(file, 0x100, 4)) == "HEAD" ? 0 : 0x1000;
            long root = header + BinaryPrimitives.ReadInt64LittleEndian(ReadAt(file, header + 0x130, 8));
            Entry secure = Partition(file, root, hashed: true).FirstOrDefault(e => e.Name == "secure") ?? throw new InvalidDataException("No secure partition in the game card");
            return Partition(file, secure.Offset, hashed: true);
        }

        private static void WritePfs0Header(Stream output, List<string> names, List<long> sizes)
        {
            byte[] strings = names.SelectMany(n => Encoding.UTF8.GetBytes(n).Append((byte)0)).ToArray();
            // The header ends on 0x20, as nsz writes it
            int headerSize = 0x10 + names.Count * 0x18 + strings.Length, padded = (headerSize + 0x1F) & ~0x1F;
            var header = new byte[padded];
            Encoding.ASCII.GetBytes("PFS0").CopyTo(header, 0);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), names.Count);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), strings.Length + padded - headerSize);
            long offset = 0;
            int nameAt = 0;
            for (int i = 0; i < names.Count; i++)
            {
                Span<byte> entry = header.AsSpan(0x10 + i * 0x18, 0x18);
                BinaryPrimitives.WriteInt64LittleEndian(entry, offset);
                BinaryPrimitives.WriteInt64LittleEndian(entry[8..], sizes[i]);
                BinaryPrimitives.WriteInt32LittleEndian(entry[16..], nameAt);
                offset += sizes[i];
                nameAt += Encoding.UTF8.GetByteCount(names[i]) + 1;
            }
            strings.CopyTo(header, 0x10 + names.Count * 0x18);
            output.Write(header);
        }
        #endregion

        #region NCZ
        private sealed record Section(long Offset, long Size, long CryptoType, byte[] Key, byte[] Counter);

        /// <summary>Its sections, with the gap after the header (not encrypted) as one when there is one.</summary>
        private static List<Section> Sections(Stream file, Entry ncz, out long dataStart)
        {
            byte[] head = ReadAt(file, ncz.Offset + NcaHeaderSize, 16);
            if (Encoding.ASCII.GetString(head, 0, 8) != "NCZSECTN")
                throw new InvalidDataException($"{ncz.Name} is not a compressed Switch content");
            long count = BinaryPrimitives.ReadInt64LittleEndian(head.AsSpan(8));
            if (count is < 1 or > 64)
                throw new InvalidDataException($"{ncz.Name}: damaged sections");
            byte[] table = ReadAt(file, ncz.Offset + NcaHeaderSize + 16, (int)count * 0x40);
            var sections = new List<Section>();
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> s = table.AsSpan(i * 0x40, 0x40);
                sections.Add(new Section(BinaryPrimitives.ReadInt64LittleEndian(s), BinaryPrimitives.ReadInt64LittleEndian(s[8..]),
                    BinaryPrimitives.ReadInt64LittleEndian(s[16..]), s[32..48].ToArray(), s[48..64].ToArray()));
            }
            if (sections[0].Offset > NcaHeaderSize)
                sections.Insert(0, new Section(NcaHeaderSize, sections[0].Offset - NcaHeaderSize, 1, Array.Empty<byte>(), Array.Empty<byte>()));
            dataStart = ncz.Offset + NcaHeaderSize + 16 + table.Length;
            return sections;
        }

        private static long NcaSize(Stream file, Entry ncz) => NcaHeaderSize + Sections(file, ncz, out _).Sum(s => s.Size);

        /// <summary>The .nca again: its header as it is, its sections decompressed then encrypted again (AES-CTR) as they were.</summary>
        private static void WriteNca(Stream file, Entry ncz, Stream output, Action<long> written)
        {
            List<Section> sections = Sections(file, ncz, out long dataStart);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] header = ReadAt(file, ncz.Offset, NcaHeaderSize);
            output.Write(header);
            hash.AppendData(header);
            long count = header.Length;

            byte[] magic = ReadAt(file, dataStart, 8);
            using Stream body = Encoding.ASCII.GetString(magic) == "NCZBLOCK"
                ? new BlockReader(file, dataStart, ncz.Offset + ncz.Size)
                : new DecompressionStream(new Window(file, dataStart, ncz.Offset + ncz.Size - dataStart));
            byte[] buffer = new byte[Chunk];
            using var aes = Aes.Create();
            for (int s = 0; s < sections.Count; s++)
            {
                Section section = sections[s];
                bool encrypted = section.CryptoType is 3 or 4;
                if (encrypted)
                    aes.Key = section.Key;
                long at = section.Offset, end = section.Offset + section.Size;
                // A first section starting inside the header: that part came with it
                if (s == 0 && section.Offset < NcaHeaderSize)
                    at = NcaHeaderSize;
                while (at < end)
                {
                    int read = ReadFull(body, buffer, (int)Math.Min(Chunk, end - at));
                    if (read == 0)
                        throw new InvalidDataException($"{ncz.Name} ends early");
                    if (encrypted)
                        Ctr(aes, section.Counter, at, buffer.AsSpan(0, read));
                    output.Write(buffer, 0, read);
                    hash.AppendData(buffer, 0, read);
                    at += read;
                    count += read;
                    written(count);
                }
            }
            // Its name is the start of its hash: a damaged download is not given to Eden
            string stem = Path.GetFileNameWithoutExtension(ncz.Name);
            if (stem.Length == 32 && stem.All(Uri.IsHexDigit) && !Convert.ToHexString(hash.GetHashAndReset())[..32].Equals(stem, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{ncz.Name}: its content does not match its hash");
        }

        /// <summary>AES-CTR as the NCA's: the counter's first 8 bytes, then the offset / 16 (big endian).</summary>
        internal static void Ctr(Aes aes, byte[] counter, long offset, Span<byte> data)
        {
            int skip = (int)(offset & 0xF);
            int blocks = (skip + data.Length + 15) / 16;
            byte[] stream = new byte[blocks * 16];
            for (int b = 0; b < blocks; b++)
            {
                counter.AsSpan(0, 8).CopyTo(stream.AsSpan(b * 16));
                BinaryPrimitives.WriteUInt64BigEndian(stream.AsSpan(b * 16 + 8), (ulong)(offset >> 4) + (ulong)b);
            }
            byte[] key = aes.EncryptEcb(stream, PaddingMode.None);
            for (int i = 0; i < data.Length; i++)
                data[i] ^= key[skip + i];
        }

        /// <summary>The NCZBLOCK form: blocks compressed one by one (stored as they are when that was not smaller).</summary>
        private sealed class BlockReader : Stream
        {
            private readonly Stream file;
            private readonly long[] offsets;
            private readonly int[] sizes;
            private readonly long blockSize, decompressed;
            private long position;
            private byte[] current = Array.Empty<byte>();
            private int currentId = -1;

            public BlockReader(Stream file, long start, long end)
            {
                this.file = file;
                byte[] head = ReadAt(file, start, 24);
                int exponent = head[11], count = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(12));
                if (exponent is < 14 or > 32 || count < 0)
                    throw new InvalidDataException("Damaged NCZBLOCK header");
                blockSize = 1L << exponent;
                decompressed = BinaryPrimitives.ReadInt64LittleEndian(head.AsSpan(16));
                byte[] list = ReadAt(file, start + 24, count * 4);
                sizes = Enumerable.Range(0, count).Select(i => BinaryPrimitives.ReadInt32LittleEndian(list.AsSpan(i * 4))).ToArray();
                offsets = new long[count];
                long at = start + 24 + list.Length;
                for (int i = 0; i < count; i++)
                {
                    offsets[i] = at;
                    at += sizes[i];
                }
                if (count > 0 && offsets[^1] + sizes[^1] > end)
                    throw new InvalidDataException("Damaged NCZBLOCK sizes");
            }

            private byte[] Block(int id)
            {
                if (id == currentId)
                    return current;
                long size = blockSize;
                if (id == offsets.Length - 1 && decompressed % blockSize > 0)
                    size = decompressed % blockSize;
                byte[] stored = ReadAt(file, offsets[id], sizes[id]);
                using (var zstd = new Decompressor())
                    current = sizes[id] < size ? zstd.Unwrap(stored).ToArray() : stored;
                currentId = id;
                return current;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int id = (int)(position / blockSize);
                if (id >= offsets.Length)
                    return 0;
                byte[] block = Block(id);
                int at = (int)(position % blockSize), n = Math.Min(count, block.Length - at);
                if (n <= 0)
                    return 0;
                Array.Copy(block, at, buffer, offset, n);
                position += n;
                return n;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => decompressed;
            public override long Position { get => position; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
        #endregion

        /// <summary>A part of the file, read from its start: what the zstd stream is given.</summary>
        private sealed class Window(Stream file, long start, long length) : Stream
        {
            private long position;

            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = (int)Math.Min(count, length - position);
                if (n <= 0)
                    return 0;
                file.Position = start + position;
                n = file.Read(buffer, offset, n);
                position += n;
                return n;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => length;
            public override long Position { get => position; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private static byte[] ReadAt(Stream file, long offset, int count)
        {
            var bytes = new byte[count];
            file.Position = offset;
            if (ReadFull(file, bytes, count) != count)
                throw new InvalidDataException("The Switch package ends early");
            return bytes;
        }

        private static int ReadFull(Stream stream, byte[] buffer, int count)
        {
            int total = 0, read;
            while (total < count && (read = stream.Read(buffer, total, count - total)) > 0)
                total += read;
            return total;
        }

        private static void Copy(Stream file, long offset, long size, Stream output, Action<long> written)
        {
            byte[] buffer = new byte[Chunk];
            long done = 0;
            file.Position = offset;
            while (done < size)
            {
                int read = file.Read(buffer, 0, (int)Math.Min(Chunk, size - done));
                if (read == 0)
                    throw new InvalidDataException("The Switch package ends early");
                output.Write(buffer, 0, read);
                done += read;
                written(done);
            }
        }
    }
}
