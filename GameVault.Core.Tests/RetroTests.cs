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
        public void DiscordShows_TheSmallCoverOfRomm()
        {
            string? url = RetroSystems.PublicCoverUrl("https://romm.example.com/", "roms/7/235/cover/big.png");
            Assert.Equal("https://romm.example.com/assets/romm/resources/roms/7/235/cover/small.png", url);
            Assert.True(GameVault.Core.Integrations.DiscordActivity.IsPublicImage(url));
            Assert.Null(RetroSystems.PublicCoverUrl(null, "roms/7/235/cover/big.png"));
            Assert.Null(RetroSystems.PublicCoverUrl("https://romm.example.com", null));
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

        private RetroGameSaves Saves(string pc, string set = "nes-40", string stem = "Zelda (E)") =>
            new(Path.Combine(root, pc, "saves"), Path.Combine(root, pc, "states"), Path.Combine(root, pc, "backups"), set, stem);

        private string Write(string pc, string relative, string text)
        {
            string file = Path.Combine(root, pc, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
            return file;
        }

        [Fact]
        public void AGamesSaves_AreItsNamedFiles_AndWhatItWroteWhileRunning()
        {
            var zelda = Saves("pc1");
            Write("pc1", "saves/Mesen/Zelda (E).srm", "zelda");
            Write("pc1", "states/Mesen/Zelda (E).state1", "state");
            Write("pc1", "saves/Mesen/Metroid.srm", "another game");
            Write("pc1", "saves/dolphin-emu/User/Cache/big.cache", "rebuilt by Dolphin");
            // A memory card the game writes while it runs: its saves too, from then on
            var before = zelda.Snapshot();
            Write("pc1", "saves/pcsx2/memcards/Mcd001.ps2", "card");
            zelda.Track(before);

            var zip = new MemoryStream();
            zelda.Pack(zip);
            using var archive = new ZipArchive(new MemoryStream(zip.ToArray()));
            Assert.Equal(new[] { "saves/Mesen/Zelda (E).srm", "saves/pcsx2/memcards/Mcd001.ps2", "states/Mesen/Zelda (E).state1" }, archive.Entries.Select(e => e.FullName).Order());
            Assert.DoesNotContain(Saves("pc1", "nes-41", "Metroid").Files(), f => f.Contains("Mcd001"));
        }

        [Fact]
        public void AGamesSaves_GoUpWhenChanged_AndComeDownWhenNewer_KeepingABackup()
        {
            var first = Saves("pc1");
            Write("pc1", "saves/Mesen/Zelda (E).srm", "pc1 save");
            Assert.True(first.ChangedSinceSync);
            var server = new MemoryStream();
            first.Pack(server);
            first.Remember(1000);
            Assert.False(first.ChangedSinceSync);

            // The other PC played without network: its save is kept aside, the server's is taken
            Write("pc2", "saves/Mesen/Zelda (E).srm", "pc2 save");
            Write("pc2", "saves/Mesen/Metroid.srm", "not this game");
            var second = Saves("pc2");
            Assert.True(second.IsOlderThan(1000));
            string? backup = second.Unpack(new MemoryStream(server.ToArray()));
            second.Remember(1000);
            Assert.Equal("pc1 save", File.ReadAllText(Path.Combine(root, "pc2", "saves", "Mesen", "Zelda (E).srm")));
            Assert.Equal("not this game", File.ReadAllText(Path.Combine(root, "pc2", "saves", "Mesen", "Metroid.srm")));
            using (var kept = ZipFile.OpenRead(backup!))
                Assert.Equal("pc2 save", new StreamReader(kept.GetEntry("saves/Mesen/Zelda (E).srm")!.Open()).ReadToEnd());
            Assert.False(second.IsOlderThan(1000));
            Assert.False(second.ChangedSinceSync);
        }

        [Fact]
        public void ForRomm_TheMainSaveGoesAlone_AndComesBackWhereTheEmulatorReadsIt()
        {
            Write("pc1", "saves/Mesen/Zelda (E).srm", "cartridge");
            Write("pc1", "states/Mesen/Zelda (E).state1", "state");
            var sv = Saves("pc1");
            var romm = new RetroGameSaves(Path.Combine(root, "pc1", "saves"), Path.Combine(root, "pc1", "states"), Path.Combine(root, "pc1", "backups"), "nes-40", "Zelda (E)", "romm");
            Assert.EndsWith(Path.Combine("Mesen", "Zelda (E).srm"), romm.MainSave());
            var rest = new MemoryStream();
            Assert.True(romm.PackRest(rest));
            using (var archive = new ZipArchive(new MemoryStream(rest.ToArray())))
                Assert.Equal(new[] { "states/Mesen/Zelda (E).state1" }, archive.Entries.Select(e => e.FullName));

            // Each destination has its own state: synced to the service is not synced to RomM
            sv.Remember(1000);
            Assert.False(sv.ChangedSinceSync);
            Assert.True(romm.ChangedSinceSync);

            // A save of RomM (its web page's emulator) comes in place of this PC's, which is kept
            string? backup = romm.Replace(null, ("Mesen", ".srm", System.Text.Encoding.ASCII.GetBytes("from RomM")));
            Assert.Equal("from RomM", File.ReadAllText(Path.Combine(root, "pc1", "saves", "Mesen", "Zelda (E).srm")));
            Assert.NotNull(backup);
            // A folder from the network stays inside the saves folder
            romm.Replace(null, ("../..", ".srm", new byte[] { 1 }));
            Assert.False(File.Exists(Path.Combine(root, "Zelda (E).srm")));
        }

        [Fact]
        public void ACoresSaves_AreInTheFolderOfItsName()
        {
            var layout = new RetroArchLayout(root, windows: true);
            Directory.CreateDirectory(Path.Combine(layout.AppDirectory, "info"));
            File.WriteAllText(Path.Combine(layout.AppDirectory, "info", "mgba_libretro.info"), "display_name = \"Nintendo - Game Boy Advance (mGBA)\"\ncorename = \"mGBA\"\n");
            Assert.Equal("mGBA", layout.SaveFolderName("mgba"));
            Assert.Equal("azahar", layout.SaveFolderName("azahar"));
        }

        [Fact]
        public void ASavesZip_DoesNotWriteOutsideTheSavesFolders()
        {
            var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("saves/../../outside.txt").Open()))
                    writer.Write("no");
                using (var writer = new StreamWriter(zip.CreateEntry("elsewhere/file.txt").Open()))
                    writer.Write("no");
            }
            stream.Position = 0;
            Saves("pc1").Unpack(stream);
            Assert.False(File.Exists(Path.Combine(root, "outside.txt")));
            Assert.False(Directory.Exists(Path.Combine(root, "pc1", "elsewhere")));
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
