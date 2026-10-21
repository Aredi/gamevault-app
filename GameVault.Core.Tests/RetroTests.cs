using System.Buffers.Binary;
using System.IO.Compression;
using GameVault.Core.Retro;

namespace GameVault.Core.Tests
{
    public class RetroTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"gv-retro-{Guid.NewGuid():N}");

        public void Dispose()
        {
            try { Directory.Delete(root, true); } catch { }
        }

        [Fact]
        public void TheLaunchFile_IsTheBestExtensionOfTheSystem()
        {
            RetroSystem psx = RetroSystems.All["psx"];
            Assert.Equal("d/Game.cue", RetroSystems.LaunchFile(psx, new[] { "d/Game.img", "d/Game.sub", "d/Game.ccd", "d/Game.cue" }));
            Assert.Equal("Game (Disc 1).m3u", RetroSystems.LaunchFile(psx, new[] { "Game (Disc 1).cue", "Game (Disc 1).m3u" }));
            Assert.Null(RetroSystems.LaunchFile(psx, new[] { "readme.txt" }));
            Assert.Equal("RBLP8P.WBFS", RetroSystems.LaunchFile(RetroSystems.All["wii"], new[] { "RBLP8P.WBFS" }));
        }

        /// <summary>A CIA as a 3DS dump tool writes it: header, certificates, ticket, TMD, then the main content (an NCCH).</summary>
        private static byte[] Cia(bool titleKeyEncrypted, bool noCrypto, int contentSize = 0x400)
        {
            const int certificates = 0xA00, ticket = 0x350, tmdSize = 0xB34;
            static int Align(int value) => (value + 63) / 64 * 64;
            int tmd = Align(0x2020) + Align(certificates) + Align(ticket);
            int content = Align(tmd + tmdSize);
            byte[] cia = new byte[content + contentSize];
            BinaryPrimitives.WriteUInt32LittleEndian(cia, 0x2020);
            BinaryPrimitives.WriteUInt32LittleEndian(cia.AsSpan(0x08), certificates);
            BinaryPrimitives.WriteUInt32LittleEndian(cia.AsSpan(0x0C), ticket);
            BinaryPrimitives.WriteUInt32LittleEndian(cia.AsSpan(0x10), tmdSize);
            BinaryPrimitives.WriteUInt32BigEndian(cia.AsSpan(tmd), 0x10004);
            int chunk = tmd + 4 + 0x13C + 0xC4 + 64 * 0x24;
            BinaryPrimitives.WriteUInt16BigEndian(cia.AsSpan(chunk + 6), (ushort)(titleKeyEncrypted ? 1 : 0));
            BinaryPrimitives.WriteUInt64BigEndian(cia.AsSpan(chunk + 8), (ulong)contentSize);
            "NCCH"u8.CopyTo(cia.AsSpan(content + 0x100));
            cia[content + 0x188 + 7] = (byte)(noCrypto ? 0x04 : 0);
            cia[content + contentSize - 1] = 0x42;
            return cia;
        }

        [Fact]
        public async Task ADecryptedCia_GivesItsMainContent_AndAnEncryptedOneIsSaidSo()
        {
            byte[] playable = Cia(titleKeyEncrypted: false, noCrypto: true);
            CiaReader.Content content = CiaReader.Inspect(playable.AsSpan(0, Math.Min(playable.Length, CiaReader.HeaderBytes)), playable.Length);
            Assert.Equal(CiaReader.Verdict.Playable, content.Verdict);
            Assert.Equal(CiaReader.Verdict.Encrypted, CiaReader.Inspect(Cia(false, noCrypto: false), playable.Length).Verdict);
            Assert.Equal(CiaReader.Verdict.Encrypted, CiaReader.Inspect(Cia(titleKeyEncrypted: true, true), playable.Length).Verdict);
            Assert.Equal(CiaReader.Verdict.NotACia, CiaReader.Inspect(new byte[0x400], 0x400).Verdict);
            Assert.Equal(CiaReader.Verdict.TooShort, CiaReader.Inspect(playable.AsSpan(0, 0x100), playable.Length).Verdict);

            Directory.CreateDirectory(root);
            string cia = Path.Combine(root, "Game.cia");
            File.WriteAllBytes(cia, playable);
            string cxi = await CiaReader.ExtractAsync(cia, content);
            Assert.Equal(Path.Combine(root, "Game.cxi"), cxi);
            Assert.False(File.Exists(cia));
            byte[] main = File.ReadAllBytes(cxi);
            Assert.Equal(0x400, main.Length);
            Assert.Equal("NCCH"u8.ToArray(), main[0x100..0x104]);
            Assert.Equal(0x42, main[^1]);
        }

        [Fact]
        public void Saves_GoUpWhenChanged_AndComeDownWhenNewer_KeepingABackup()
        {
            string pc1 = Path.Combine(root, "pc1"), pc2 = Path.Combine(root, "pc2");
            Directory.CreateDirectory(Path.Combine(pc1, "mGBA"));
            File.WriteAllText(Path.Combine(pc1, "mGBA", "zelda.srm"), "pc1 save");
            Directory.CreateDirectory(Path.Combine(pc1, "dolphin-emu", "User", "Cache"));
            File.WriteAllText(Path.Combine(pc1, "dolphin-emu", "User", "Cache", "big.cache"), "rebuilt by Dolphin");
            var first = new RetroSaveSync(pc1, Path.Combine(root, "backups1"));
            Assert.True(first.ChangedSinceSync);

            var server = new MemoryStream();
            first.Pack(server);
            first.Remember(1000);
            Assert.False(first.ChangedSinceSync);
            using (var zip = new ZipArchive(new MemoryStream(server.ToArray())))
                Assert.Equal(new[] { "mGBA/zelda.srm" }, zip.Entries.Select(e => e.FullName));

            // The other PC played without network: its save is kept aside, the server's is taken
            Directory.CreateDirectory(Path.Combine(pc2, "mGBA"));
            File.WriteAllText(Path.Combine(pc2, "mGBA", "zelda.srm"), "pc2 save");
            var second = new RetroSaveSync(pc2, Path.Combine(root, "backups2"));
            Assert.True(second.IsOlderThan(1000));
            string? backup = second.Unpack(new MemoryStream(server.ToArray()));
            second.Remember(1000);
            Assert.Equal("pc1 save", File.ReadAllText(Path.Combine(pc2, "mGBA", "zelda.srm")));
            using (var kept = ZipFile.OpenRead(backup!))
                Assert.Equal("pc2 save", new StreamReader(kept.GetEntry("mGBA/zelda.srm")!.Open()).ReadToEnd());
            Assert.False(second.IsOlderThan(1000));
            Assert.False(second.ChangedSinceSync);
        }

        [Fact]
        public void ASavesZip_DoesNotWriteOutsideTheSavesFolder()
        {
            var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(zip.CreateEntry("../outside.txt").Open()))
                writer.Write("no");
            stream.Position = 0;
            new RetroSaveSync(Path.Combine(root, "saves"), Path.Combine(root, "backups")).Unpack(stream);
            Assert.False(File.Exists(Path.Combine(root, "outside.txt")));
        }

        [Fact]
        public void TheSharedSavesOfTheFirstVersion_GoToTheFirstPlayer()
        {
            var layout = new RetroArchLayout(root, windows: false);
            Directory.CreateDirectory(Path.Combine(root, "saves", "mGBA"));
            File.WriteAllText(Path.Combine(root, "saves", "mGBA", "game.srm"), "save");
            layout.AdoptSharedSaves("7");
            Assert.Equal("save", File.ReadAllText(Path.Combine(layout.SavesDirectory("7"), "mGBA", "game.srm")));
            layout.AdoptSharedSaves("8");// the next player starts with none
            Assert.Empty(Directory.EnumerateFileSystemEntries(layout.SavesDirectory("8")));
        }
    }
}
