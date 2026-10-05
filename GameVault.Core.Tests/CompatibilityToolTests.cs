using GameVault.Core.Compatibility;

namespace GameVault.Core.Tests
{
    public class CompatibilityToolTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"gv-compat-{Guid.NewGuid():N}");

        public void Dispose()
        {
            try { Directory.Delete(root, true); } catch { }
        }

        private string MakeProton(string parent, string name, string? displayName = null)
        {
            string dir = Path.Combine(parent, name);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "proton"), "#!/usr/bin/env python3");
            if (displayName != null)
                File.WriteAllText(Path.Combine(dir, "compatibilitytool.vdf"), $"\"compatibilitytools\"\n{{\n  \"compat_tools\"\n  {{\n    \"{name}\"\n    {{\n      \"display_name\" \"{displayName}\"\n    }}\n  }}\n}}");
            return dir;
        }

        private string MakeWine(string parent, string name)
        {
            string dir = Path.Combine(parent, name, "bin");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "wine"), "");
            return Path.GetDirectoryName(dir)!;
        }

        [Fact]
        public void Scan_FindsProtonAndWineBuilds_AndMarksManagedOnes()
        {
            string managed = Path.Combine(root, "gamevault");
            string steam = Path.Combine(root, "steam", "compatibilitytools.d");
            string lutris = Path.Combine(root, "lutris");
            MakeProton(managed, "GE-Proton10-9", "GE-Proton10-9");
            MakeProton(managed, "GE-Proton10-10");
            MakeProton(steam, "Proton 9.0");
            MakeWine(lutris, "wine-ge-8-26");
            MakeWine(managed, "wine-11.19-amd64-wow64");
            Directory.CreateDirectory(Path.Combine(steam, "not-a-tool"));

            var tools = CompatibilityToolScanner.Scan(new ToolSearchLocations
            {
                ManagedDirectory = managed,
                ProtonParents = { (managed, "GameVault"), (steam, "Steam") },
                WineParents = { (managed, "GameVault"), (lutris, "Lutris") },
            });

            Assert.Equal(new[] { "Proton 9.0", "GE-Proton10-10", "GE-Proton10-9" }, tools.Where(t => t.Kind == CompatibilityToolKind.Proton).Select(t => t.Name));
            Assert.Equal(2, tools.Count(t => t.Kind == CompatibilityToolKind.Wine));
            Assert.True(tools.Single(t => t.Name == "GE-Proton10-10").IsManaged);
            Assert.False(tools.Single(t => t.Name == "Proton 9.0").IsManaged);
            Assert.False(tools.Single(t => t.Name == "wine-ge-8-26").IsManaged);
            Assert.Equal("Steam", tools.Single(t => t.Name == "Proton 9.0").Source);
        }

        [Fact]
        public void Scan_ReportsATool_ReachedThroughASymlinkedSteamRoot_Once()
        {
            string real = Path.Combine(root, "local", "Steam");
            MakeProton(Path.Combine(real, "compatibilitytools.d"), "GE-Proton11-7");
            string link = Path.Combine(root, "steam-root");
            Directory.CreateSymbolicLink(link, real);

            var tools = CompatibilityToolScanner.Scan(new ToolSearchLocations
            {
                ProtonParents =
                {
                    (Path.Combine(link, "compatibilitytools.d"), "Steam"),
                    (Path.Combine(real, "compatibilitytools.d"), "Steam"),
                },
            });

            Assert.Single(tools);
        }

        [Fact]
        public void ReadProtonName_UsesDisplayName()
        {
            string dir = MakeProton(root, "proton_tkg_custom", "Proton-tkg 9.12");
            Assert.Equal("Proton-tkg 9.12", CompatibilityToolScanner.ReadProtonName(dir));
            Assert.Equal("Proton 8.0", CompatibilityToolScanner.ReadProtonName(MakeProton(root, "Proton 8.0")));
        }

        [Fact]
        public void Ids_RoundTripBuildDirectories()
        {
            string id = CompatibilityToolId.ForProton("/opt/tools/GE-Proton11-7/");
            Assert.True(CompatibilityToolId.IsProton(id, out string dir));
            Assert.Equal("/opt/tools/GE-Proton11-7", dir);
            Assert.False(CompatibilityToolId.IsWineBuild(id, out _));
            Assert.False(CompatibilityToolId.IsWineBuild(CompatibilityToolId.SystemWine, out _));
            Assert.True(CompatibilityToolId.IsWineBuild(CompatibilityToolId.ForWine("/opt/wine-11"), out string wine));
            Assert.Equal("/opt/wine-11", wine);
        }

        [Fact]
        public void NaturalComparer_ComparesNumbersByValue()
        {
            var names = new[] { "GE-Proton10-10", "GE-Proton9-27", "GE-Proton10-9" }.OrderBy(n => n, NaturalComparer.Instance);
            Assert.Equal(new[] { "GE-Proton9-27", "GE-Proton10-9", "GE-Proton10-10" }, names);
        }

        [Fact]
        public void Catalog_ParsesGeProtonReleases()
        {
            string json = """
            [
              { "tag_name": "GE-Proton11-7", "assets": [
                  { "name": "GE-Proton11-7-aarch64.tar.gz", "browser_download_url": "https://x/a.tar.gz", "size": 1 },
                  { "name": "GE-Proton11-7-x86_64.sha512sum", "browser_download_url": "https://x/sum", "size": 1 },
                  { "name": "GE-Proton11-7-x86_64.tar.gz", "browser_download_url": "https://x/x.tar.gz", "size": 563784602 } ] },
              { "tag_name": "GE-Proton10-25", "assets": [
                  { "name": "GE-Proton10-25.sha512sum", "browser_download_url": "https://x/sum2", "size": 1 },
                  { "name": "GE-Proton10-25.tar.gz", "browser_download_url": "https://x/old.tar.gz", "size": 2 } ] },
              { "tag_name": "GE-Proton12-1-rc", "prerelease": true, "assets": [] }
            ]
            """;
            var tools = ToolCatalog.Parse(ToolFlavor.GeProton, json);
            Assert.Equal(2, tools.Count);
            Assert.Equal("https://x/x.tar.gz", tools[0].DownloadUrl);
            Assert.Equal("https://x/sum", tools[0].ChecksumUrl);
            Assert.Equal("GE-Proton11-7", tools[0].FolderName);
            Assert.Equal("https://x/sum2", tools[1].ChecksumUrl);
        }

        [Fact]
        public void Catalog_ParsesWineBuilds_PickingTheWow64Archive()
        {
            string json = """
            [ { "tag_name": "11.19", "assets": [
                { "name": "sha256sums.txt", "browser_download_url": "https://x/sums", "size": 1 },
                { "name": "wine-11.19-amd64.tar.xz", "browser_download_url": "https://x/a", "size": 1 },
                { "name": "wine-11.19-amd64-wow64.tar.xz", "browser_download_url": "https://x/wow", "size": 1 },
                { "name": "wine-11.19-staging-amd64-wow64.tar.xz", "browser_download_url": "https://x/staging", "size": 1 } ] } ]
            """;
            Assert.Equal("https://x/wow", ToolCatalog.Parse(ToolFlavor.Wine, json).Single().DownloadUrl);
            var staging = ToolCatalog.Parse(ToolFlavor.WineStaging, json).Single();
            Assert.Equal("https://x/staging", staging.DownloadUrl);
            Assert.Equal("wine-11.19-staging-amd64-wow64", staging.FolderName);
            Assert.Equal("https://x/sums", staging.ChecksumUrl);
        }

        [Fact]
        public async Task Checksum_IsFoundAndVerified()
        {
            Directory.CreateDirectory(root);
            string file = Path.Combine(root, "wine-11.19-amd64-wow64.tar.xz");
            File.WriteAllText(file, "hello");
            string sha256 = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";
            string list = $"0000  wine-11.19-amd64.tar.xz\n{sha256}  ./wine-11.19-amd64-wow64.tar.xz\n";
            string? expected = ToolCatalog.FindChecksum(list, "wine-11.19-amd64-wow64.tar.xz");
            Assert.Equal(sha256, expected);
            Assert.True(await ToolCatalog.VerifyAsync(file, expected!));
            Assert.False(await ToolCatalog.VerifyAsync(file, new string('0', 64)));
        }
    }
}
