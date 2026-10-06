using GameVault.Core.Diagnostics;

namespace GameVault.Core.Tests
{
    public class RedactorTests
    {
        [Theory]
        [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJlLXZhbHVl", "Bearer [hidden]")]
        [InlineData("Basic YWRtaW46c2VjcmV0cGFzcw==", "Basic [hidden]")]
        [InlineData("{\"username\":\"admin\",\"password\":\"hunter2\"}", "\"password\":\"[hidden]\"")]
        [InlineData("{\"access_token\": \"abc.def\", \"refresh_token\":\"x\"}", "\"access_token\": \"[hidden]\"")]
        [InlineData("Password=hunter2", "Password=[hidden]")]
        [InlineData("ExtractionPassword=secret", "ExtractionPassword=[hidden]")]
        [InlineData("SessionToken=abcdef123", "SessionToken=[hidden]")]
        [InlineData("https://admin:hunter2@gamevault.example.com/api", "https://[hidden]@gamevault.example.com")]
        [InlineData("GET /api/x?token=abc123&page=2", "token=[hidden]&page=2")]
        [InlineData("X-Otp: 98aed746ef2d9265b39940c2e8a4576394940675", "X-Otp: [hidden]")]
        public void Secrets_AreHidden(string text, string expected)
        {
            string redacted = Redactor.Redact(text);
            Assert.Contains(expected, redacted);
            Assert.DoesNotContain("hunter2", redacted);
            Assert.DoesNotContain("YWRtaW46", redacted);
        }

        [Fact]
        public void OrdinaryLogLines_StayReadable()
        {
            string line = "2026-10-06 13:41:46 [INFO] Starting /usr/bin/wine /games/(7)Nile Adventure/setup.exe /S /D=Z:\\games";
            Assert.Equal(line, Redactor.Redact(line));
            Assert.Equal("Download of Hades: 4 of 8 parts", Redactor.Redact("Download of Hades: 4 of 8 parts"));
        }

        [Fact]
        public void Tail_StartsAtAWholeLine()
        {
            string file = Path.GetTempFileName();
            try
            {
                File.WriteAllText(file, string.Join("\n", Enumerable.Range(1, 1000).Select(i => $"line {i}")));
                string tail = Redactor.Tail(file, 100);
                Assert.StartsWith("line ", tail);
                Assert.EndsWith("line 1000", tail);
                Assert.True(tail.Length <= 100);
            }
            finally { File.Delete(file); }
        }
    }
}
