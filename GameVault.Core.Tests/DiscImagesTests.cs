using GameVault.Core.Library;

namespace GameVault.Core.Tests
{
    public class DiscImagesTests
    {
        [Fact]
        public void AnArchiveOfAnIsoOnly_HasItOpened_NotOneWithAProgramBeside()
        {
            Assert.Equal(new[] { "Extract/Queens Domain/Queens Domain.ISO" }, DiscImages.ToOpen(new[] { "Extract/Queens Domain/Queens Domain.ISO", "Extract/gamevault-metadata" }));
            Assert.Empty(DiscImages.ToOpen(new[] { "Extract/disc.iso", "Extract/setup.exe" }));
            Assert.Empty(DiscImages.ToOpen(new[] { "Extract/game.exe", "Extract/data.bin" }));
        }

        [Fact]
        public void TheSetup_IsTheDiscsOwn()
        {
            Assert.Equal("D/setup.exe", DiscImages.Setup(new[] { "D/QueensDomain-2.ico", "D/autorun.inf", "D/elamigos-1.bin", "D/setup.exe" }));
            Assert.Equal("D/Setup_Game.exe", DiscImages.Setup(new[] { "D/Redist/vcredist.exe", "D/Setup_Game.exe" }));
            Assert.Equal("D/SETUP.EXE", DiscImages.Setup(new[] { "D/Redist/DirectX/setup.exe", "D/SETUP.EXE" }));
            Assert.Null(DiscImages.Setup(new[] { "D/Game.exe", "D/data.pak" }));
        }
    }
}
