using System.Buffers.Binary;

namespace GameVault.Core.Retro
{
    /// <summary>
    /// A 3DS game as a CIA (an installable package): Azahar plays its main content (the .cxi inside), and only when it
    /// is not encrypted. The header (the first kilobytes) is enough to know where that content is and whether it plays.
    /// </summary>
    public static class CiaReader
    {
        public enum Verdict { Playable, Encrypted, NotACia, TooShort }

        public sealed record Content(Verdict Verdict, long Offset = 0, long Size = 0);

        /// <summary>Enough for the header, the certificates, the ticket, the TMD and the first NCCH header.</summary>
        public const int HeaderBytes = 64 * 1024;

        private static long Align(long value) => (value + 63) / 64 * 64;

        /// <param name="start">The beginning of the file, <see cref="HeaderBytes"/> or the whole file when it is shorter.</param>
        public static Content Inspect(ReadOnlySpan<byte> start, long fileSize)
        {
            if (start.Length < 0x20)
                return new Content(Verdict.TooShort);
            uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(start);
            uint certificates = BinaryPrimitives.ReadUInt32LittleEndian(start[0x08..]);
            uint ticket = BinaryPrimitives.ReadUInt32LittleEndian(start[0x0C..]);
            uint tmdSize = BinaryPrimitives.ReadUInt32LittleEndian(start[0x10..]);
            if (headerSize != 0x2020)
                return new Content(Verdict.NotACia);
            long tmd = Align(headerSize) + Align(certificates) + Align(ticket);
            long content = Align(tmd + tmdSize);
            if (start.Length < tmd + 4)
                return new Content(Verdict.TooShort);
            // The TMD starts with its signature, whose size depends on its type
            long body = tmd + 4 + BinaryPrimitives.ReadUInt32BigEndian(start[(int)tmd..]) switch
            {
                0x10003 => 0x200 + 0x3C,
                0x10004 => 0x100 + 0x3C,
                0x10005 => 0x3C + 0x40,
                _ => -1,
            };
            if (body < tmd + 4)
                return new Content(Verdict.NotACia);
            long chunk = body + 0xC4 + 64 * 0x24;// the first content's record, after the header and the info records
            if (start.Length < chunk + 16 || start.Length < content + 0x190)
                return new Content(Verdict.TooShort);
            ushort type = BinaryPrimitives.ReadUInt16BigEndian(start[(int)(chunk + 6)..]);
            long size = (long)BinaryPrimitives.ReadUInt64BigEndian(start[(int)(chunk + 8)..]);
            if (content + size > fileSize)
                return new Content(Verdict.NotACia);
            // Encrypted with the title key (the content is then unreadable), or an NCCH without the NoCrypto flag
            bool readable = (type & 1) == 0 && start.Slice((int)content + 0x100, 4).SequenceEqual("NCCH"u8);
            bool noCrypto = readable && (start[(int)content + 0x188 + 7] & 0x04) != 0;
            return new Content(noCrypto ? Verdict.Playable : Verdict.Encrypted, content, size);
        }

        /// <summary>Writes the main content of a playable CIA as a .cxi file next to it, then deletes the CIA.</summary>
        public static async Task<string> ExtractAsync(string cia, Content content, CancellationToken cancellationToken = default)
        {
            string cxi = Path.ChangeExtension(cia, ".cxi");
            await using (FileStream source = File.OpenRead(cia))
            await using (FileStream target = File.Create(cxi + ".part"))
            {
                source.Seek(content.Offset, SeekOrigin.Begin);
                byte[] buffer = new byte[1 << 20];
                long left = content.Size;
                while (left > 0)
                {
                    int read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), cancellationToken);
                    if (read == 0)
                        throw new EndOfStreamException("The CIA ends before its content");
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    left -= read;
                }
            }
            File.Move(cxi + ".part", cxi, overwrite: true);
            File.Delete(cia);
            return cxi;
        }
    }
}
