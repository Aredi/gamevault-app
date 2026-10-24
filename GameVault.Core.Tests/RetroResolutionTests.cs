using GameVault.Core.Retro;

namespace GameVault.Core.Tests
{
    public class RetroResolutionTests
    {
        [Fact]
        public void TheCores_TakeTheirOwnValues_InTheirOptionsFile_TheOthersKept()
        {
            Assert.Equal(new[] { ("dolphin_efb_scale", "3") }, RetroResolution.CoreOptions("dolphin", 3));
            Assert.Equal(new[] { ("pcsx2_upscale_multiplier", "1x (Native)") }, RetroResolution.CoreOptions("pcsx2", 1));
            Assert.Equal(new[] { ("pcsx2_upscale_multiplier", "4x") }, RetroResolution.CoreOptions("pcsx2", 4));
            Assert.Equal(new[] { ("citra_resolution_factor", "2") }, RetroResolution.CoreOptions("azahar", 2));
            Assert.Equal(new[] { ("swanstation_GPU_ResolutionScale", "5") }, RetroResolution.CoreOptions("swanstation", 5));
            // The DS: its OpenGL renderer above 1x, its software one at 1x
            Assert.Equal(new[] { ("melonds_render_mode", "opengl"), ("melonds_opengl_resolution", "3") }, RetroResolution.CoreOptions("melondsds", 3));
            Assert.Equal(("melonds_render_mode", "software"), RetroResolution.CoreOptions("melondsds", 1)[0]);
            Assert.Empty(RetroResolution.CoreOptions("mesen", 2));

            // 16:9 and back: written both ways
            Assert.Equal(new[] { ("swanstation_GPU_WidescreenHack", "true"), ("swanstation_Display_AspectRatio", "16:9") }, RetroResolution.WidescreenOptions("swanstation", true));
            Assert.Equal(new[] { ("swanstation_GPU_WidescreenHack", "false"), ("swanstation_Display_AspectRatio", "4:3") }, RetroResolution.WidescreenOptions("swanstation", false));
            Assert.Equal(new[] { ("pcsx2_widescreen_hint", "enabled (16:9)") }, RetroResolution.WidescreenOptions("pcsx2", true));
            Assert.Equal(new[] { ("dolphin_widescreen_hack", "disabled") }, RetroResolution.WidescreenOptions("dolphin", false));
            Assert.Empty(RetroResolution.WidescreenOptions("mesen", true));

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
