using GameVault.Core.Library;

namespace GameVault.Core.Tests
{
    public class LibraryTests : IDisposable
    {
        private readonly string file = Path.Combine(Path.GetTempPath(), $"gv-collections-{Guid.NewGuid():N}", "collections.json");

        public void Dispose()
        {
            try { Directory.Delete(Path.GetDirectoryName(file)!, true); } catch { }
        }

        [Fact]
        public void Collections_AreCreatedRenamedDeleted_AndPersisted()
        {
            var store = new GameCollectionStore(file);
            store.Create("Co-op");
            store.Create(" co-op ");// same name
            store.Create("Favourites");
            store.SetMembership("Co-op", 3, true);
            store.SetMembership("Co-op", 3, true);
            store.SetMembership("favourites", 3, true);
            store.SetMembership("Favourites", 7, true);

            var reloaded = new GameCollectionStore(file);
            Assert.Equal(2, reloaded.Load().Count);
            Assert.Equal(new[] { "Co-op", "Favourites" }, reloaded.CollectionsOf(3));
            Assert.Single(reloaded.Load().First(c => c.Name == "Co-op").GameIds);

            reloaded.Rename("Co-op", "Local co-op");
            Assert.Throws<ArgumentException>(() => reloaded.Rename("Local co-op", "FAVOURITES"));
            reloaded.SetMembership("Favourites", 3, false);
            Assert.Equal(new[] { "Local co-op" }, reloaded.CollectionsOf(3));
            reloaded.Delete("local co-op");
            Assert.Equal(new[] { "Favourites" }, reloaded.Load().Select(c => c.Name));
        }

        [Fact]
        public void IdFilter_CombinesCollectionAndPlayStatus()
        {
            var played = new[] { 1, 3 };
            Assert.Equal("", LibraryQuery.IdFilter(null, PlayStatusFilter.All, played));
            Assert.Equal("&filter.id=$in:1,3", LibraryQuery.IdFilter(null, PlayStatusFilter.Played, played));
            Assert.Equal("&filter.id=$not:$in:1,3", LibraryQuery.IdFilter(null, PlayStatusFilter.NeverPlayed, played));
            Assert.Equal("", LibraryQuery.IdFilter(null, PlayStatusFilter.NeverPlayed, Array.Empty<int>()));
            Assert.Equal("&filter.id=$in:2,3", LibraryQuery.IdFilter(new[] { 3, 2 }, PlayStatusFilter.All, played));
            Assert.Equal("&filter.id=$in:3", LibraryQuery.IdFilter(new[] { 3, 2 }, PlayStatusFilter.Played, played));
            Assert.Equal("&filter.id=$in:2", LibraryQuery.IdFilter(new[] { 3, 2 }, PlayStatusFilter.NeverPlayed, played));
            Assert.Equal("&filter.id=$eq:-1", LibraryQuery.IdFilter(Array.Empty<int>(), PlayStatusFilter.All, played));
            Assert.Equal("&filter.id=$eq:-1", LibraryQuery.IdFilter(new[] { 2 }, PlayStatusFilter.Played, played));
        }

        [Fact]
        public void OrderByPlay_SortsByLastPlayedOrPlaytime()
        {
            var records = new[]
            {
                new PlayRecord(1, new DateTime(2026, 1, 1), 300),
                new PlayRecord(2, new DateTime(2026, 3, 1), 10),
                new PlayRecord(3, null, 50),
            };
            Assert.Equal(new[] { 2, 1, 3 }, LibraryQuery.OrderByPlay(records, byPlaytime: false, ascending: false));
            Assert.Equal(new[] { 3, 1, 2 }, LibraryQuery.OrderByPlay(records, byPlaytime: false, ascending: true));
            Assert.Equal(new[] { 1, 3, 2 }, LibraryQuery.OrderByPlay(records, byPlaytime: true, ascending: false));
        }
    }
}
