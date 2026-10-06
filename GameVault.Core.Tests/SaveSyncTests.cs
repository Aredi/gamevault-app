using GameVault.Core.CloudSaves;

namespace GameVault.Core.Tests
{
    public class SaveSyncTests : IDisposable
    {
        private const string Mine = "6f2d1c3e-1111-4a5b-9c8d-000000000001";
        private const string Other = "6f2d1c3e-2222-4a5b-9c8d-000000000002";
        private static readonly DateTime T0 = new(2026, 10, 1, 20, 0, 0, DateTimeKind.Utc);
        private readonly string root = Path.Combine(Path.GetTempPath(), $"gv-saves-{Guid.NewGuid():N}");

        public void Dispose()
        {
            try { Directory.Delete(root, true); } catch { }
        }

        [Fact]
        public void ServerFileNames_AreParsed()
        {
            var save = ServerSave.FromFileName($"1759348800000_{Other}.zip");
            Assert.Equal(Other, save!.InstallationId);
            Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1759348800000).UtcDateTime, save.UploadedAt);
            Assert.Null(ServerSave.FromFileName("x.zip"));
            Assert.Null(ServerSave.FromFileName(null));
        }

        [Fact]
        public void TheServerHasThisComputersSave_NothingToDo()
        {
            Assert.Equal(SaveSyncAction.UpToDate, SaveSync.BeforePlaying(new ServerSave(T0, Mine), Mine, T0, T0.AddHours(1)));
            Assert.Equal(SaveSyncAction.UpToDate, SaveSync.BeforePlaying(null, Mine, null, T0));
        }

        [Fact]
        public void AnotherComputerPlayed_AndNothingChangedHere_Restores()
        {
            // Synced at T0, the other computer uploaded at T0+2h, local files untouched since T0
            Assert.Equal(SaveSyncAction.Restore, SaveSync.BeforePlaying(new ServerSave(T0.AddHours(2), Other), Mine, T0, T0));
            Assert.Equal(SaveSyncAction.Restore, SaveSync.BeforePlaying(new ServerSave(T0, Other), Mine, null, null));
        }

        [Fact]
        public void BothPlayed_IsAConflict()
        {
            // Played offline here at T0+1h, the other computer uploaded at T0+2h
            Assert.Equal(SaveSyncAction.Conflict, SaveSync.BeforePlaying(new ServerSave(T0.AddHours(2), Other), Mine, T0, T0.AddHours(1)));
            // Never synced here, local saves newer than the server's
            Assert.Equal(SaveSyncAction.Conflict, SaveSync.BeforePlaying(new ServerSave(T0, Other), Mine, null, T0.AddMinutes(10)));
            // Within the tolerance: the same change
            Assert.Equal(SaveSyncAction.Restore, SaveSync.BeforePlaying(new ServerSave(T0.AddHours(2), Other), Mine, T0, T0.AddSeconds(3)));
        }

        [Fact]
        public void AnUploadAfterAnotherComputersUpload_IsDetected()
        {
            Assert.True(SaveSync.ServerChangedMeanwhile(new ServerSave(T0.AddHours(1), Other), Mine, T0));
            Assert.False(SaveSync.ServerChangedMeanwhile(new ServerSave(T0.AddHours(1), Mine), Mine, T0));
            Assert.False(SaveSync.ServerChangedMeanwhile(new ServerSave(T0, Other), Mine, T0.AddHours(1)));
            Assert.False(SaveSync.ServerChangedMeanwhile(null, Mine, T0));
        }

        [Fact]
        public void LudusaviPreview_ListsTheSaveFiles_AndTheirLastChange()
        {
            Directory.CreateDirectory(root);
            string a = Path.Combine(root, "slot1.sav"), b = Path.Combine(root, "slot2.sav");
            File.WriteAllText(a, "1");
            File.WriteAllText(b, "2");
            File.SetLastWriteTimeUtc(a, T0);
            File.SetLastWriteTimeUtc(b, T0.AddHours(3));
            var entries = new Dictionary<string, object>
            {
                [a.Replace("\\", "/")] = new { change = "Same", bytes = 1 },
                [b.Replace("\\", "/")] = new { change = "Different", bytes = 1 },
                ["/gone/file.sav"] = new { change = "New", bytes = 1 },
                ["/skipped.sav"] = new { ignored = true, bytes = 1 },
            };
            string json = System.Text.Json.JsonSerializer.Serialize(new
            {
                overall = new { totalGames = 1 },
                games = new Dictionary<string, object> { ["Celeste"] = new { decision = "Processed", change = "Same", files = entries, registry = new { } } },
            });
            var files = SaveSync.SaveFilesFromLudusaviPreview(json);
            Assert.Equal(3, files.Count);
            Assert.Equal(T0.AddHours(3), SaveSync.LastChange(files));
            Assert.Null(SaveSync.LastChange(new[] { "/gone/file.sav" }));
            Assert.Empty(SaveSync.SaveFilesFromLudusaviPreview("""{"overall":{},"games":{}}"""));
        }
    }
}
