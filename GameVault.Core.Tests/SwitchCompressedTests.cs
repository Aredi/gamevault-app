using GameVault.Core.Retro;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace GameVault.Core.Tests
{
    public class SwitchCompressedTests
    {
        private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Switch", name);

        /// <summary>The files of an .nsp: name and content.</summary>
        private static Dictionary<string, byte[]> Files(string nsp)
        {
            byte[] bytes = File.ReadAllBytes(nsp);
            Assert.Equal("PFS0", Encoding.ASCII.GetString(bytes, 0, 4));
            int count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)), strings = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
            int table = 0x10 + count * 0x18, data = table + strings;
            var files = new Dictionary<string, byte[]>();
            for (int i = 0; i < count; i++)
            {
                var entry = bytes.AsSpan(0x10 + i * 0x18);
                int name = table + BinaryPrimitives.ReadInt32LittleEndian(entry[16..]);
                long offset = data + BinaryPrimitives.ReadInt64LittleEndian(entry), size = BinaryPrimitives.ReadInt64LittleEndian(entry[8..]);
                files[Encoding.UTF8.GetString(bytes, name, Array.IndexOf(bytes, (byte)0, name) - name)] = bytes[(int)offset..(int)(offset + size)];
            }
            return files;
        }

        private static string Copy(string name)
        {
            string folder = Path.Combine(Path.GetTempPath(), "switch-compressed-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.Copy(Fixture(name), Path.Combine(folder, name));
            return Path.Combine(folder, name);
        }

        [Fact]
        public void AnNsz_IsAnNspAgain_ItsContentsAsTheyWere()
        {
            string nsz = Copy("game.nsz");
            var progress = new List<double>();
            string nsp = SwitchCompressed.Decompress(nsz, progress.Add);
            Assert.Equal(Path.ChangeExtension(nsz, ".nsp"), nsp);
            var files = Files(nsp);
            // Solid then block compression (a gap before the first section, a block stored as it is); the ticket as it is
            Assert.Equal(new[] { "4cbd541d6b127879e0ebab5b0e738daa.nca", "0100000000001000.tik", "abca7a2d4a8516c7e2c08ad25154c30e.nca" }, files.Keys);
            foreach (var (name, content) in files.Where(f => f.Key.EndsWith(".nca")))
                Assert.StartsWith(Path.GetFileNameWithoutExtension(name), Convert.ToHexStringLower(SHA256.HashData(content)));
            Assert.Equal(0x2C0, files["0100000000001000.tik"].Length);
            Assert.Equal(1, progress[^1], 3);
            Assert.False(File.Exists(nsp + ".part"));
            Directory.Delete(Path.GetDirectoryName(nsz)!, true);
        }

        [Fact]
        public void AnXcz_IsItsSecurePartition_AsAnNsp()
        {
            string xcz = Copy("card.xcz");
            var files = Files(SwitchCompressed.Decompress(xcz));
            Assert.Equal(new[] { "4cbd541d6b127879e0ebab5b0e738daa.nca", "abca7a2d4a8516c7e2c08ad25154c30e.nca" }, files.Keys);
            Directory.Delete(Path.GetDirectoryName(xcz)!, true);
        }

        [Fact]
        public void ADamagedContent_IsRefused_NothingLeft()
        {
            string nsz = Copy("game.nsz");
            byte[] bytes = File.ReadAllBytes(nsz);
            // A byte of the first content's compressed data changed: its hash no longer matches its name
            bytes[0x8000] ^= 0xFF;
            File.WriteAllBytes(nsz, bytes);
            Assert.ThrowsAny<Exception>(() => SwitchCompressed.Decompress(nsz));
            Assert.False(File.Exists(Path.ChangeExtension(nsz, ".nsp")));
            Assert.False(File.Exists(Path.ChangeExtension(nsz, ".nsp") + ".part"));
            Directory.Delete(Path.GetDirectoryName(nsz)!, true);
        }

        [Fact]
        public void TheCounter_IsTheNcasOne()
        {
            using var aes = Aes.Create();
            aes.Key = new byte[16];
            byte[] counter = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
            byte[] whole = new byte[64], part = new byte[40];
            SwitchCompressed.Ctr(aes, counter, 0x1000, whole);
            // From inside a block: the same stream
            SwitchCompressed.Ctr(aes, counter, 0x1000 + 24, part);
            Assert.Equal(whole[24..], part);
        }
    }
}
