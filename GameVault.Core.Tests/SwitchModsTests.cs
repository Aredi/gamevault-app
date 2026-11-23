using System.Buffers.Binary;
using System.Text;
using GameVault.Core.Retro;
using static GameVault.Core.Retro.SwitchMods;

namespace GameVault.Core.Tests
{
    public class SwitchModsTests
    {
        [Fact]
        public void TheGamesId_IsInItsName_OrInItsFirstContentsHeader()
        {
            Assert.Equal("0100F2C0115B6000", TitleIdFromName("The Legend of Zelda Tears of the Kingdom [0100F2C0115B6000][v0].xci"));
            // An update's id gives its game's
            Assert.Equal("0100F2C0115B6000", TitleIdFromName("TOTK v1.2.1[0100F2C0115B6800][v393216].nsp"));
            Assert.Null(TitleIdFromName("Bayonetta 2.xci"));
            // A DLC's id: 0x1000 after its game's
            Assert.Equal("0100670014482000", TitleIdFromName("Assassins Creed - Brotherhood [DLC][0100670014483001].nsp"));

            // IEEE 1619's first XTS-AES-128 vector: the cipher is the one Nintendo uses
            Assert.Equal("917CF69EBD68B2EC9B9FE9A3EADDA692CD43D2F59598ED858C02C2652FBF922E",
                Convert.ToHexString(DecipherXts(new byte[32], new byte[32], 0, decipher: false)));

            // An .nsp whose only content's header (ciphered) holds the id of the game's second program
            var key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
            var header = new byte[0x400];
            Encoding.ASCII.GetBytes("NCA3").CopyTo(header, 0x200);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(0x210), 0x0100F2C0115B6001);
            byte[] ciphered = DecipherXts(key, header, 0, decipher: false);
            using var nsp = new MemoryStream();
            byte[] name = Encoding.ASCII.GetBytes("1234.nca\0\0\0\0");
            nsp.Write(Encoding.ASCII.GetBytes("PFS0"));
            nsp.Write(BitConverter.GetBytes(1));
            nsp.Write(BitConverter.GetBytes(name.Length));
            nsp.Write(new byte[4]);
            nsp.Write(BitConverter.GetBytes(0L));
            nsp.Write(BitConverter.GetBytes((long)ciphered.Length));
            nsp.Write(BitConverter.GetBytes(0));
            nsp.Write(new byte[4]);
            nsp.Write(name);
            nsp.Write(ciphered);
            Assert.Equal("0100F2C0115B6000", TitleIdOf(nsp, card: false, key));
            // The wrong key reads no id
            Assert.Null(TitleIdOf(nsp, card: false, new byte[32]));
        }

        [Fact]
        public void TheFileForTheGamesVersion_IsChosen_AndTheOnesForAnotherManagerLast()
        {
            Assert.Equal("1.2.1", GameVersion(new[] { "TOTK [0100F2C0115B6000][v0].xci", "update/TOTK v1.2.1[0100F2C0115B6800][v393216].nsp", "TOTK v1.1.0.nsp" }));
            Assert.Null(GameVersion(new[] { "Bayonetta 2.xci", "x [v393216].nsp" }));

            Assert.True(Names("unbreakable_v121_33b15.7z", "1.2.1"));
            Assert.True(Names("For game version 1.2.1", "1.2.1"));
            Assert.False(Names("Version 2.1.4", "1.2.1"));
            Assert.False(Names("v1.2.10", "1.2.1"));

            var versions = new[]
            {
                new ModFile(1, "unbreakable_v100.7z", 1, "v1.0.0", 100),
                new ModFile(2, "unbreakable_v121.7z", 1, "v1.2.1", 50),
                new ModFile(3, "unbreakable_v141.7z", 1, "v1.4.1", 200),
            };
            Assert.Equal(2, BestFile(versions, "1.2.1")!.Id);
            // The game's version unknown: the newest
            Assert.Equal(3, BestFile(versions, null)!.Id);
            var challenge = new[]
            {
                new ModFile(1, "challenge_mode_180_only_for_tkmm.rar", 1, "ONLY USE WITH TKMM", 300),
                new ModFile(2, "challenge_mode_180_recommended.rar", 1, "Recommended version of the mod", 100),
            };
            Assert.Equal(2, BestFile(challenge, "1.2.1")!.Id);
            // Challenge Mode's files for Eden: its data is the game's 1.4.2, the game in 1.2.1 stops as it starts
            var forAnother = new[]
            {
                new ModFile(1, "challenge_mode_180_only_for_tkmm_86a6a.rar", 1, "ONLY USE WITH TKMM, AND ONLY USE THIS FILE WITH TKMM, NOT THE TWO BELOW!!!!!", 300),
                new ModFile(2, "challenge_mode_180_recommended.rar", 1, "Recommended version (all recommended options enabled) of the mod, for game version 1.4.2", 100),
                new ModFile(3, "cool_v110.zip", 1, "For v1.1.0", 50),
            };
            Assert.Equal("1.4.2", MadeForAnother(forAnother[1], "1.2.1"));
            Assert.Null(MadeForAnother(forAnother[1], "1.4.2"));
            Assert.Null(MadeForAnother(forAnother[1], null));
            // The mod's own version (1.8.0, v1.1.0) is not the game's
            Assert.Null(MadeForAnother(forAnother[0], "1.2.1"));
            Assert.Null(MadeForAnother(forAnother[2], "1.2.1"));
            Assert.Null(MadeForAnother(new ModFile(4, "x.zip", 1, "For game version 1.2.1 and 1.4.2", 1), "1.2.1"));
            Assert.Equal("1.4.2", MadeForAnother(new ModFile(5, "x.zip", 1, "Works on version 1.4.2 of the game", 1), "1.1.0"));
            Assert.Equal(3, BestFile(forAnother, "1.2.1")!.Id);
            var defogger = new[]
            {
                new ModFile(1, "interior_addon.zip", 1, "Only affects Interiors", 300),
                new ModFile(2, "serfrosts_base_defogger.zip", 1, "Base Defogger, All Game Update Versions", 100),
            };
            Assert.Equal(2, BestFile(defogger, "1.2.1")!.Id);
        }

        [Fact]
        public void AMod_IsPutWhereEdenReadsIt_TurnedOff_AndTakenAway()
        {
            string root = Path.Combine(Path.GetTempPath(), "gv-mods-" + Guid.NewGuid().ToString("N"));
            try
            {
                var eden = new EdenLayout(Path.Combine(root, "eden"), windows: false);
                const string game = "0100F2C0115B6000";
                // UltraCam's zip: UltraCam/exefs and UltraCam/romfs
                string extracted = Path.Combine(root, "x1");
                Directory.CreateDirectory(Path.Combine(extracted, "UltraCam", "exefs"));
                File.WriteAllText(Path.Combine(extracted, "UltraCam", "exefs", "main.npdm"), "npdm");
                Directory.CreateDirectory(Path.Combine(extracted, "UltraCam", "romfs"));

                Assert.Equal(new[] { "UltraCam: 4K" }.Select(FolderName), Install(eden, game, extracted, "UltraCam: 4K", 480138, 1349811));
                string on = Path.Combine(eden.UserDirectory, "load", game, "UltraCam 4K");
                Assert.True(File.Exists(Path.Combine(on, "exefs", "main.npdm")));
                InstalledMod mod = Assert.Single(Installed(eden, game));
                Assert.Equal((true, 480138), (mod.Enabled, mod.ModId));

                SetEnabled(eden, game, mod.Folder, false);
                Assert.False(Directory.Exists(on));
                Assert.False(Assert.Single(Installed(eden, game)).Enabled);
                SetEnabled(eden, game, mod.Folder, true);
                Assert.True(Directory.Exists(on));

                // Installed again (another file): it takes the place of the previous one
                string again = Path.Combine(root, "x2");
                Directory.CreateDirectory(Path.Combine(again, "atmosphere", "contents", game, "romfs"));
                Install(eden, game, again, "UltraCam: 4K", 480138, 1);
                Assert.Equal("UltraCam 4K", Assert.Single(Installed(eden, game)).Folder);

                // Several in one archive: all on, named after the mod
                string variants = Path.Combine(root, "x3");
                Directory.CreateDirectory(Path.Combine(variants, "Mod", "30 FPS", "exefs"));
                Directory.CreateDirectory(Path.Combine(variants, "Mod", "60 FPS", "exefs"));
                Assert.Equal(new[] { "FPS · 30 FPS", "FPS · 60 FPS" }, Install(eden, game, variants, "FPS", 2, 2));
                Assert.Equal(new[] { true, true }, Installed(eden, game).Where(m => m.ModId == 2).Select(m => m.Enabled));

                // Two folders of one name in an archive: both kept; another mod of the same name takes the place
                string versions = Path.Combine(root, "x5");
                Directory.CreateDirectory(Path.Combine(versions, "1.2.1", "Cam", "exefs"));
                Directory.CreateDirectory(Path.Combine(versions, "1.4.2", "Cam", "exefs"));
                Assert.Equal(new[] { "Free Cam · Cam", "Free Cam · Cam 2" }, Install(eden, game, versions, "Free Cam", 5, 5));
                string other = Path.Combine(root, "x6");
                Directory.CreateDirectory(Path.Combine(other, "romfs"));
                Assert.Equal(new[] { "Free Cam · Cam" }, Install(eden, game, other, "Free Cam · Cam", 6, 6));
                Assert.Equal(6, Assert.Single(Installed(eden, game), m => m.Folder == "Free Cam · Cam").ModId);

                // Nothing Eden reads: nothing put, and the mod's file installed before stays
                string none = Path.Combine(root, "x4");
                Directory.CreateDirectory(Path.Combine(none, "mod.tkcl"));
                Assert.Empty(Install(eden, game, none, "TKMM only", 3, 3));
                Assert.Empty(Install(eden, game, none, "UltraCam: 4K", 480138, 2));
                Assert.Contains(Installed(eden, game), m => m.ModId == 480138 && m.Enabled);

                // A folder put by hand of the same name stays: the mod gets another
                Directory.CreateDirectory(Path.Combine(eden.UserDirectory, "load", game, "60 FPS", "exefs"));
                string sixty = Path.Combine(root, "x7");
                Directory.CreateDirectory(Path.Combine(sixty, "romfs"));
                Assert.Equal(new[] { "60 FPS 2" }, Install(eden, game, sixty, "60 FPS", 7, 7));
                Assert.Contains(Installed(eden, game), m => m.Folder == "60 FPS" && m.ModId == null);

                // A folder per version of the game: the game's on, the others off
                string perVersion = Path.Combine(root, "x8");
                Directory.CreateDirectory(Path.Combine(perVersion, "1.2.1", "Cam", "exefs"));
                Directory.CreateDirectory(Path.Combine(perVersion, "v142", "Cam", "exefs"));
                Assert.Equal(new[] { "Cams · Cam", "Cams · Cam 2" }, Install(eden, game, perVersion, "Cams", 8, 8, "1.2.1"));
                Assert.Equal(new[] { true, false }, Installed(eden, game).Where(m => m.ModId == 8).OrderBy(m => m.Folder).Select(m => m.Enabled));

                // Put by hand with a long name, of two spaces: turned off and taken away as it is named
                string hand = "TOTK 60fps + Dynamic Resolution  + No blur v1.2.1 by ChucksFeedAndSeed (exefs and romfs together)";
                Directory.CreateDirectory(Path.Combine(eden.UserDirectory, "load", game, hand, "exefs"));
                SetEnabled(eden, game, hand, false);
                Assert.Contains(Installed(eden, game), m => m.Folder == hand && !m.Enabled);
                Remove(eden, game, hand);
                Assert.DoesNotContain(Installed(eden, game), m => m.Folder == hand);
                Assert.Throws<ArgumentException>(() => Remove(eden, game, ".."));

                Remove(eden, game, "UltraCam 4K");
                Assert.DoesNotContain(Installed(eden, game), m => m.ModId == 480138);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }
    }
}
