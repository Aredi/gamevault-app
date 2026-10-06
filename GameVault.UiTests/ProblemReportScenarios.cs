using System.IO.Compression;
using Avalonia.Headless.XUnit;
using gamevault.Helper;
using GameVault.Core;

namespace GameVault.UiTests
{
    public class ProblemReportScenarios
    {
        [AvaloniaFact]
        public async Task TheReport_DescribesTheClient_AndContainsNoSecret()
        {
            var session = await TestSession.GetAsync();
            // Things that must not leave the computer, also when they end up in the log
            Log.Info("Request with Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJlLXZhbHVl");
            Log.Info("Login {\"username\":\"admin\",\"password\":\"admin-password\"}");
            string folder = Path.Combine(IsolatedHome.Root, "reports");

            string file = await ProblemReport.CreateAsync(folder);

            using var zip = ZipFile.OpenRead(file);
            string Read(string name) => new StreamReader(zip.GetEntry(name)!.Open()).ReadToEnd();
            string report = Read("report.txt");
            Assert.Contains(session.Server.Url, report);
            Assert.Contains("Signed in", report);
            Assert.Contains("Root directory", report);
            Assert.Contains("Connections per download", report);
            string log = Read("logs/gamevault.log");
            Assert.Contains("Bearer [hidden]", log);
            foreach (var entry in zip.Entries)
            {
                string content = new StreamReader(entry.Open()).ReadToEnd();
                Assert.DoesNotContain("admin-password", content);
                Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", content);
                Assert.DoesNotContain("access_token\":\"access", content);
            }
        }
    }
}
