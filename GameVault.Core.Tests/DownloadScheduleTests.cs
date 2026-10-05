using GameVault.Core.Downloads;

namespace GameVault.Core.Tests
{
    public class DownloadScheduleTests
    {
        private static DateTime At(int hour, int minute = 0) => new DateTime(2026, 10, 5, hour, minute, 0);

        [Fact]
        public void DayWindow()
        {
            var schedule = new DownloadSchedule(true, new TimeOnly(1, 0), new TimeOnly(7, 0));
            Assert.True(schedule.IsAllowed(At(1)));
            Assert.True(schedule.IsAllowed(At(6, 59)));
            Assert.False(schedule.IsAllowed(At(7)));
            Assert.False(schedule.IsAllowed(At(23)));
            Assert.Equal(At(1).AddDays(1), schedule.NextStart(At(8)));
            Assert.Equal(At(1), schedule.NextStart(At(0, 30)));
        }

        [Fact]
        public void WindowOverMidnight()
        {
            var schedule = new DownloadSchedule(true, new TimeOnly(22, 0), new TimeOnly(6, 0));
            Assert.True(schedule.IsAllowed(At(23)));
            Assert.True(schedule.IsAllowed(At(2)));
            Assert.False(schedule.IsAllowed(At(12)));
            Assert.Equal(At(22), schedule.NextStart(At(12)));
        }

        [Fact]
        public void DisabledOrEmptyWindow_AlwaysAllows()
        {
            Assert.True(DownloadSchedule.Always.IsAllowed(At(12)));
            Assert.True(new DownloadSchedule(true, new TimeOnly(3, 0), new TimeOnly(3, 0)).IsAllowed(At(12)));
        }

        [Fact]
        public void ParsesTimes()
        {
            Assert.True(DownloadSchedule.TryParseTime("1:30", out var t1));
            Assert.Equal(new TimeOnly(1, 30), t1);
            Assert.True(DownloadSchedule.TryParseTime(" 22:05 ", out _));
            Assert.False(DownloadSchedule.TryParseTime("25:00", out _));
            Assert.False(DownloadSchedule.TryParseTime("abc", out _));
        }
    }
}
