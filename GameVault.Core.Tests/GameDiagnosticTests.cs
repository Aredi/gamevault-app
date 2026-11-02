using GameVault.Core.Diagnostics;
using GameVault.Core.Retro;

namespace GameVault.Core.Tests
{
    public class GameDiagnosticTests
    {
        [Fact]
        public void Cleaned_HidesSecretsAndTheSession_InEveryText()
        {
            var diagnostic = new GameDiagnostic { Failure = "/home/zoe/x failed", System = "Linux on zoe-pc", Log = "Password=hunter2\nreading /home/zoe/game.nsp" }.Cleaned("zoe-pc");
            Assert.Equal("/home/[session]/x failed", diagnostic.Failure);
            Assert.Equal("Linux on [session]", diagnostic.System);
            Assert.Equal("Password=[hidden]\nreading /home/[session]/game.nsp", diagnostic.Log);
        }

        [Fact]
        public void LogEnd_KeepsTheLastLines()
        {
            string file = Path.GetTempFileName();
            try
            {
                File.WriteAllText(file, string.Join("\r\n", Enumerable.Range(1, 1000).Select(i => $"line {i}")) + "\r\n");
                string[] lines = GameDiagnostic.LogEnd(file).Split('\n');
                Assert.Equal(GameDiagnostic.MaxLogLines, lines.Length);
                Assert.Equal("line 801", lines[0]);
                Assert.Equal("line 1000", lines[^1]);
            }
            finally { File.Delete(file); }
        }

        [Fact]
        public void LogEnd_IsCutToItsSize()
        {
            string file = Path.GetTempFileName();
            try
            {
                File.WriteAllText(file, string.Join("\n", Enumerable.Range(1, 100).Select(i => new string('x', 4096))));
                Assert.True(GameDiagnostic.LogEnd(file).Length <= GameDiagnostic.MaxLogBytes);
            }
            finally { File.Delete(file); }
        }

        [Theory]
        [InlineData(unchecked((int)0xC000001D), EmulatorFailure.OlderProcessor)]
        [InlineData(unchecked((int)0xC0000135), EmulatorFailure.MissingRuntime)]
        [InlineData(unchecked((int)0xC0000005), EmulatorFailure.Other)]
        public void Kind_ComesFromItsCode(int code, EmulatorFailure kind) => Assert.Equal(kind, new GameDiagnostic { ExitCode = code, Seconds = 3 }.Kind);

        [Fact]
        public void Graphics_AreReadFromLspci()
        {
            const string output = "00:1f.3 Audio device: Intel Corporation Device 7a50\n01:00.0 VGA compatible controller: NVIDIA Corporation GP106 [GeForce GTX 1060 6GB] (rev a1)\n02:00.0 3D controller: Advanced Micro Devices, Inc. Navi 23\n";
            Assert.Equal(new[] { "NVIDIA Corporation GP106 [GeForce GTX 1060 6GB]", "Advanced Micro Devices, Inc. Navi 23" }, HardwareText.GraphicsOfLspci(output));
        }

        [Fact]
        public void Graphics_AreReadFromSystemProfiler() =>
            Assert.Equal(new[] { "Apple M2" }, HardwareText.GraphicsOfSystemProfiler("Graphics/Displays:\n\n    Apple M2:\n\n      Chipset Model: Apple M2\n      Type: GPU\n"));

        [Fact]
        public void Processor_IsReadFromCpuInfo() =>
            Assert.Equal("Intel(R) Core(TM) i5-4460 CPU @ 3.20GHz", HardwareText.ProcessorOfCpuInfo("processor\t: 0\nmodel name\t: Intel(R) Core(TM) i5-4460 CPU @ 3.20GHz\nflags\t: fpu\n"));

        [Fact]
        public void ThePciVendors_AreKnown()
        {
            Assert.Equal("NVIDIA", HardwareText.VendorOfPciId("0x10de\n"));
            Assert.Null(HardwareText.VendorOfPciId("0x1234"));
        }
    }
}
