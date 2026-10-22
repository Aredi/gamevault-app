using GameVault.Core.Retro;

namespace GameVault.Core.Tests
{
    public class RetroResolutionTests
    {
        [Fact]
        public void TheCores_TakeTheirOwnValues_InTheirOptionsFile_TheOthersKept()
        {
            Assert.Equal(("dolphin_efb_scale", "3"), RetroResolution.CoreOption("dolphin", 3));
            Assert.Equal(("pcsx2_upscale_multiplier", "1x (Native)"), RetroResolution.CoreOption("pcsx2", 1));
            Assert.Equal(("pcsx2_upscale_multiplier", "4x"), RetroResolution.CoreOption("pcsx2", 4));
            Assert.Equal(("citra_resolution_factor", "2"), RetroResolution.CoreOption("azahar", 2));
            Assert.Null(RetroResolution.CoreOption("mesen", 2));

            string existing = "dolphin_renderer = \"Hardware\"\r\ndolphin_efb_scale = \"1\"\r\n";
            Assert.Equal("dolphin_renderer = \"Hardware\"\ndolphin_efb_scale = \"2\"\n", RetroResolution.MergeOptions(existing, "dolphin_efb_scale", "2"));
            Assert.Equal("dolphin_efb_scale = \"2\"\n", RetroResolution.MergeOptions("", "dolphin_efb_scale", "2"));
        }

        [Fact]
        public void TheSetting_KeepsOnlyTheMultiplesEachEngineOffers()
        {
            Assert.Equal(new Dictionary<string, int> { ["dolphin"] = 2, ["eden"] = 3 }, RetroResolution.Parse("dolphin=2;pcsx2=3;eden=3;mesen=2;broken"));
            Assert.Equal("dolphin=2;eden=3", RetroResolution.Format(new Dictionary<string, int> { ["eden"] = 3, ["dolphin"] = 2 }));
        }

        [Fact]
        public void Rpcs3_TakesItsScale_UnderVideo_TheRestOfItsYamlKept()
        {
            var layout = new Rpcs3Layout("/rpcs3", windows: false);
            string config = "Core:\n  PPU Decoder: Recompiler (LLVM)\nVideo:\n  Renderer: Vulkan\n  Resolution Scale: 100\n  Anisotropic Filter Override: 0\nAudio:\n  Renderer: Cubeb\n";
            Assert.Equal(config.Replace("Resolution Scale: 100", "Resolution Scale: 200"), layout.Resolution(config, 2));
            Assert.Equal("Core:\n  PPU Decoder: x\nVideo:\n  Resolution Scale: 300\n  Renderer: Vulkan\n", layout.Resolution("Core:\n  PPU Decoder: x\nVideo:\n  Renderer: Vulkan\n", 3));
            Assert.Equal("Video:\n  Resolution Scale: 100\n", layout.Resolution("", 1));
            Assert.Equal(new[] { Path.Combine("/rpcs3", "config", "rpcs3", "config.yml") }, layout.ResolutionFiles);
        }

        [Fact]
        public void Eden_TakesItsResolutionSetup_InRenderer()
        {
            var layout = new EdenLayout("/eden", windows: false);
            string merged = layout.Resolution("[Renderer]\nbackend=1\n[UI]\ntheme=dark\n", 2);
            Assert.Equal("[Renderer]\nbackend=1\nresolution_setup\\default=false\nresolution_setup=6\n[UI]\ntheme=dark\n", merged);
            Assert.Contains("resolution_setup=3\n", layout.Resolution(merged, 1));
        }
    }
}
