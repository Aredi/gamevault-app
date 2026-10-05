namespace GameVault.Core.Tests
{
    public class UpdateCheckerTests
    {
        private const string Release = """
        {
          "tag_name": "1.18.0.0",
          "html_url": "https://github.com/Aredi/gamevault-app/releases/tag/1.18.0.0",
          "assets": [
            { "name": "GameVault-linux-x64.tar.gz", "browser_download_url": "https://example/linux.tar.gz" },
            { "name": "GameVault-win-x64.zip", "browser_download_url": "https://example/win.zip" }
          ]
        }
        """;

        [Fact]
        public void NewerRelease_PicksMatchingAsset()
        {
            ReleaseInfo? info = UpdateChecker.ParseRelease(Release, "1.17.2.0", "win");
            Assert.NotNull(info);
            Assert.Equal("1.18.0.0", info!.Version);
            Assert.Equal("https://example/win.zip", info.DownloadUrl);
        }

        [Fact]
        public void SameOrOlderRelease_ReturnsNull()
        {
            Assert.Null(UpdateChecker.ParseRelease(Release, "1.18.0.0", null));
            Assert.Null(UpdateChecker.ParseRelease(Release, "1.20.0", null));
        }

        [Fact]
        public void ReleaseWithoutAssets_StillReportsPage()
        {
            ReleaseInfo? info = UpdateChecker.ParseRelease("""{ "tag_name": "9.0.0" }""", "1.0.0", null);
            Assert.NotNull(info);
            Assert.Null(info!.DownloadUrl);
            Assert.Equal(AppRepository.ReleasesPage, info.PageUrl);
        }

        [Fact]
        public void EmptyReleaseList_MeansNoUpdate()
        {
            Assert.Null(UpdateChecker.ParseReleaseList("[]", "1.0.0", null));
        }

        [Fact]
        public void ReleaseList_SkipsDraftsAndPreReleases()
        {
            string list = """
            [
              { "tag_name": "3.0.0", "draft": true },
              { "tag_name": "2.5.0", "prerelease": true },
              { "tag_name": "2.0.0", "draft": false, "prerelease": false }
            ]
            """;
            Assert.Equal("2.0.0", UpdateChecker.ParseReleaseList(list, "1.0.0", null)?.Version);
            Assert.Null(UpdateChecker.ParseReleaseList(list, "2.0.0", null));
        }
    }
}
