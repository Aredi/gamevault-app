using GameVault.Core.News;

namespace GameVault.Core.Tests
{
    public class ServerNewsTests
    {
        private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void ANewFile_IsAnUpdate_TheFirstLookIsNot()
        {
            var snapshot = new NewsSnapshot();
            ServerNews.Track(snapshot, new[] { new NewsGame(1, null, "/files/Hades (v1.38).zip", "100", "v1.38") }, Now.AddDays(-3));
            Assert.Empty(snapshot.Updates);

            ServerNews.Track(snapshot, new[] { new NewsGame(1, null, "/files/Hades (v1.38).zip", "100", "v1.38") }, Now.AddDays(-2));
            Assert.Empty(snapshot.Updates);

            ServerNews.Track(snapshot, new[] { new NewsGame(1, null, "/files/Hades (v1.40).zip", "120", "v1.40") }, Now);
            var update = Assert.Single(snapshot.Updates);
            Assert.Equal((NewsKind.Updated, 1, Now, "v1.40"), (update.Kind, update.GameId, update.DateUtc, update.Version));
        }

        [Fact]
        public void Feed_HasRecentAdditions_AndUpdates_NewestFirst()
        {
            var snapshot = new NewsSnapshot { Updates = { new NewsEntry(NewsKind.Updated, 1, Now.AddDays(-1), "v2") } };
            var added = new[]
            {
                new NewsGame(2, Now.AddDays(-5), "/files/b.zip", "1", null),
                new NewsGame(3, Now.AddHours(-2), "/files/c.zip", "1", null),
                new NewsGame(4, Now.AddDays(-90), "/files/old.zip", "1", null),// out of the window
            };
            var feed = ServerNews.Feed(added, snapshot, Now);
            Assert.Equal(new[] { 3, 1, 2 }, feed.Select(e => e.GameId));
            Assert.Equal(new[] { NewsKind.Added, NewsKind.Updated, NewsKind.Added }, feed.Select(e => e.Kind));
            Assert.Equal(2, ServerNews.Unread(feed, Now.AddDays(-2)));
            Assert.Equal(3, ServerNews.Unread(feed, null));
        }

        [Fact]
        public void AGameAddedAndUpdatedTheSameDay_IsOnlyNew()
        {
            var snapshot = new NewsSnapshot { Updates = { new NewsEntry(NewsKind.Updated, 7, Now, "v1") } };
            var feed = ServerNews.Feed(new[] { new NewsGame(7, Now.AddHours(-3), "/files/g.zip", "1", "v1") }, snapshot, Now);
            Assert.Equal(NewsKind.Added, Assert.Single(feed).Kind);
        }

        [Fact]
        public void Snapshot_RoundTrips_AndOldUpdatesExpire()
        {
            string file = Path.Combine(Path.GetTempPath(), $"news-{Guid.NewGuid():N}.json");
            try
            {
                var snapshot = new NewsSnapshot { Files = { [1] = "/files/a.zip|1" }, Updates = { new NewsEntry(NewsKind.Updated, 1, Now.AddDays(-100), "v1") } };
                ServerNews.Track(snapshot, Array.Empty<NewsGame>(), Now);
                Assert.Empty(snapshot.Updates);
                snapshot.Save(file);
                Assert.Equal("/files/a.zip|1", NewsSnapshot.Load(file).Files[1]);
                Assert.Empty(NewsSnapshot.Load(file + ".missing").Files);
            }
            finally { File.Delete(file); }
        }

        [Theory]
        [InlineData(2026, 10, 6, ServerNews.Period.ThisWeek)]   // Monday of this week
        [InlineData(2026, 10, 2, ServerNews.Period.LastWeek)]
        [InlineData(2026, 9, 20, ServerNews.Period.ThisMonth)]
        [InlineData(2026, 8, 1, ServerNews.Period.Earlier)]
        public void Periods(int y, int m, int d, ServerNews.Period expected) =>
            Assert.Equal(expected, ServerNews.PeriodOf(new DateTime(y, m, d, 10, 0, 0), new DateTime(2026, 10, 9, 12, 0, 0)));
    }
}
