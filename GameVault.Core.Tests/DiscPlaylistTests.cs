using GameVault.Core.Retro;

namespace GameVault.Core.Tests
{
    public class DiscPlaylistTests
    {
        [Fact]
        public void AGameOnSeveralCds_HasItsCuesListed_InTheirOrder()
        {
            var discs = DiscPlaylist.Discs(new[]
            {
                "G/FF7 (Disc 2).cue", "G/FF7 (Disc 2).bin", "G/FF7 (Disc 10).cue", "G/FF7 (Disc 1).cue", "G/FF7 (Disc 1).bin", "G/FF7 (Disc 10).bin",
            });
            Assert.Equal(new[] { "G/FF7 (Disc 1).cue", "G/FF7 (Disc 2).cue", "G/FF7 (Disc 10).cue" }, discs);
            Assert.Equal("FF7 (Disc 1).cue\nFF7 (Disc 2).cue\nFF7 (Disc 10).cue\n", DiscPlaylist.Content("G", discs!));
        }

        [Fact]
        public void OneDisc_OrAPlaylistAlready_IsLeftAsItIs()
        {
            Assert.Null(DiscPlaylist.Discs(new[] { "G/Game.cue", "G/Game (Track 1).bin", "G/Game (Track 2).bin" }));
            Assert.Null(DiscPlaylist.Discs(new[] { "G/Game.m3u", "G/Game (Disc 1).chd", "G/Game (Disc 2).chd" }));
            Assert.Equal(new[] { "G/Game (Disc 1).chd", "G/Game (Disc 2).chd" }, DiscPlaylist.Discs(new[] { "G/Game (Disc 2).chd", "G/Game (Disc 1).chd" }));
        }
    }
}
