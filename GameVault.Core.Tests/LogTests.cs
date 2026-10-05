namespace GameVault.Core.Tests
{
    public class LogTests
    {
        [Fact]
        public void Ignored_WritesLocationAndMessage()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"gv-log-{Guid.NewGuid()}");
            Log.Initialize(dir);
            try
            {
                Log.Ignored(new InvalidOperationException("boom"));
                string content = File.ReadAllText(Log.LogFile!);
                Assert.Contains("LogTests.cs", content);
                Assert.Contains("InvalidOperationException: boom", content);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
