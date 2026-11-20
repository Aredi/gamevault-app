using GameVault.Core.Retro;
using static GameVault.Core.Retro.RetroUpdates;

namespace GameVault.Core.Tests
{
    public class RetroUpdatesTests
    {
        private static readonly RemoteFile Base = new(1, "Zelda TOTK [0100F2C0115B6000].nsp", 16_000_000_000, "game");
        private static readonly RemoteFile Update = new(2, "update/Zelda TOTK [0100F2C0115B6800][v65536].nsp", 1_200_000_000, "update");

        [Fact]
        public void AnUpdateAddedToRomm_AfterTheInstall_IsMissing()
        {
            var missing = Missing(new[] { Base, Update }, new[] { "Zelda TOTK [0100F2C0115B6000].nsp" });
            Assert.Equal(new[] { Update }, missing);
        }

        [Fact]
        public void AnUpdateAlreadyHere_OrDecompressed_IsNotMissing()
        {
            Assert.Empty(Missing(new[] { Base, Update }, new[] { "Zelda TOTK [0100F2C0115B6000].nsp", @"update\Zelda TOTK [0100F2C0115B6800][v65536].nsp" }));
            // Eden's .nsp of RomM's .nsz
            var compressed = Update with { Path = "update/Zelda TOTK [0100F2C0115B6800][v65536].nsz" };
            Assert.Empty(Missing(new[] { compressed }, new[] { "update/Zelda TOTK [0100F2C0115B6800][v65536].nsp" }));
        }

        [Fact]
        public void OnlyUpdates_AndNoPathOutOfTheGamesFolder()
        {
            Assert.Empty(Missing(new[] { Base, new RemoteFile(3, "dlc/X.nsp", 1, "dlc"), new RemoteFile(4, "../../x.nsp", 1, "update"), new RemoteFile(5, "C:/x.nsp", 1, "update") }, Array.Empty<string>()));
        }
    }
}
