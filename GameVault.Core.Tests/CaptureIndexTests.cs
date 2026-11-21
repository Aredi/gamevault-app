using GameVault.Core.Captures;

namespace GameVault.Core.Tests
{
    public class CaptureIndexTests : IDisposable
    {
        private readonly string folder = Directory.CreateTempSubdirectory("gv-captures-").FullName;

        public void Dispose() => Directory.Delete(folder, true);

        private string Taken(string name)
        {
            string path = Path.Combine(folder, name);
            File.WriteAllText(path, "x");
            return path;
        }

        [Fact]
        public void AGamesCaptures_AreItsOwn_TheNewestFirst_AndKeptInTheFile()
        {
            var now = DateTime.UtcNow;
            string file = Path.Combine(folder, "captures.json");
            var index = new CaptureIndex(file);
            Assert.True(index.Add(new Capture("pc:793", Taken("a.png"), now.AddMinutes(-10))));
            Assert.True(index.Add(new Capture("pc:793", Taken("b.mp4"), now)));
            Assert.True(index.Add(new Capture("retro:psx:1", Taken("c.png"), now)));
            // Seen again as it is renamed: once
            Assert.False(index.Add(new Capture("pc:793", Path.Combine(folder, "A.PNG"), now)));

            var again = new CaptureIndex(file);
            Assert.Equal(new[] { "b.mp4", "a.png" }, again.Of("pc:793").Select(c => Path.GetFileName(c.Path)));
            Assert.True(again.Of("pc:793")[0].IsVideo);
        }

        [Fact]
        public void ADeletedFile_IsNotShown_NorKept()
        {
            string file = Path.Combine(folder, "captures.json");
            var index = new CaptureIndex(file);
            string gone = Taken("gone.png");
            index.Add(new Capture("pc:1", gone, DateTime.UtcNow));
            File.Delete(gone);
            Assert.Empty(index.Of("pc:1"));
            index.Add(new Capture("pc:1", Taken("kept.png"), DateTime.UtcNow));
            Assert.DoesNotContain("gone.png", File.ReadAllText(file));
        }

        [Fact]
        public void OnlyImagesAndVideos_AreCaptures()
        {
            Assert.True(CaptureFiles.IsCapture("Hades 2026-11-20 18-00-00.mp4"));
            Assert.True(CaptureFiles.IsCapture("shot.JXR"));
            Assert.False(CaptureFiles.IsCapture("recording.tmp"));
            Assert.False(CaptureFiles.IsShownImage("shot.jxr"));
        }
    }
}
