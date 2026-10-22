using GameVault.Core.Retro;

namespace GameVault.Core.Tests
{
    public class RetroControllersTests
    {
        [Fact]
        public void AGame_StartsWithItsOwnController_ElseItsConsoles_ElseTheDefault()
        {
            Assert.Equal(1025, RetroControllers.Pick("wii", 1025, 1));
            Assert.Equal(1, RetroControllers.Pick("wii", null, 1));
            Assert.Equal(769, RetroControllers.Pick("wii", null, null));
            // A controller the console does not have (a choice kept from an older client) is not given
            Assert.Equal(769, RetroControllers.Pick("wii", 261, 99));
            Assert.Equal(261, RetroControllers.Pick("psx", null, null));
            // The GameCube shares Dolphin with the Wii, with one controller
            Assert.Null(RetroControllers.Pick("ngc", 769, null));
        }

        [Fact]
        public void TheChoices_AreKeptAsText()
        {
            Assert.Equal(new Dictionary<string, int> { ["psx"] = 1, ["wii"] = 1025 }, RetroControllers.Parse("wii=769;psx=1;wii=1025;broken;x=y"));
            Assert.Equal("559=1025;psx=1", RetroControllers.Format(new Dictionary<string, int> { ["psx"] = 1, ["559"] = 1025 }));
        }

        [Fact]
        public void TheController_GoesInTheGamesRemap_WithThePlayersControls_UnlessTheyHaveTheirOwnForIt()
        {
            string ours = RetroControllers.Remap(null, null, 769)!;
            Assert.StartsWith("# SanctuaryVault", ours);
            Assert.EndsWith("\ninput_libretro_device_p1 = \"769\"\n", ours);
            // The core's remap (the player's controls for all the Wii games) kept in it: RetroArch reads one file only
            string core = "input_player1_btn_a = \"8\"\ninput_libretro_device_p1 = \"1\"\n";
            Assert.EndsWith("\ninput_player1_btn_a = \"8\"\ninput_libretro_device_p1 = \"1025\"\n", RetroControllers.Remap(ours, core, 1025));
            // Saved by the player in RetroArch's menu for this game: theirs
            Assert.Null(RetroControllers.Remap("input_libretro_device_p1 = \"513\"\n", core, 769));

            var layout = new RetroArchLayout("/data/retroarch", windows: false);
            string remaps = Path.Combine(layout.MenuSettingsDirectory, "remaps", "dolphin-emu");
            Assert.Equal(Path.Combine(remaps, "Zelda (E).rmp"), layout.GameRemapFile("dolphin", "/games/Wii/Zelda/Zelda (E).rvz"));
            Assert.Equal(Path.Combine(remaps, "Zelda.rmp"), layout.FolderRemapFile("dolphin", "/games/Wii/Zelda/Zelda (E).rvz"));
            Assert.Equal(Path.Combine(remaps, "dolphin-emu.rmp"), layout.CoreRemapFile("dolphin"));
        }

        [Fact]
        public void Rpcs3_GetsTheFirstController_UnlessThePlayerSetTheirs()
        {
            var windows = new Rpcs3Layout("C:/rpcs3", windows: true);
            string written = windows.Input(null, null)!;
            Assert.Contains("Player 1 Input:\n  Handler: XInput\n  Device: \"XInput Pad #1\"\n", written);
            Assert.Equal(Path.Combine("C:/rpcs3", "config", "input_configs", "global", "Default.yml"), windows.InputFile);

            var linux = new Rpcs3Layout("/rpcs3", windows: false);
            Assert.Contains("Handler: Evdev\n  Device: \"Xbox Wireless Controller\"\n", linux.Input(null, "Xbox Wireless Controller"));
            Assert.Null(linux.Input(null, null));
            // Ours follows the controller; the player's own (saved by RPCS3's window) stays
            Assert.Contains("Device: \"8BitDo Pro 2\"", linux.Input(linux.Input(null, "Xbox Wireless Controller"), "8BitDo Pro 2"));
            Assert.Null(linux.Input("Player 1 Input:\n  Handler: DualSense\n", "Xbox Wireless Controller"));
            Assert.Equal(Path.Combine("/rpcs3", "config", "rpcs3", "input_configs", "global", "Default.yml"), linux.InputFile);
        }

        [Fact]
        public void TheFirstGamepad_IsTheFirstInputWithASouthButton()
        {
            string root = Path.Combine(Path.GetTempPath(), "gv-input-" + Guid.NewGuid().ToString("N"));
            void Device(string name, string label, string keys)
            {
                string folder = Path.Combine(root, name, "device", "capabilities");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "key"), keys + "\n");
                File.WriteAllText(Path.Combine(root, name, "device", "name"), label + "\n");
            }
            try
            {
                Assert.Null(Rpcs3Layout.FirstGamepad(root));
                // Read on this PC: a KVM's emulated mouse and a LED controller have keys, none of them BTN_SOUTH (0x130)
                Device("event2", "JetKVM USB Emulation Device", "1f0000 0 0 0 0");
                Device("event3", "ASRock LED Controller", "c000 0 0 40000001000000 1200000000 c00000000000000 100000800000000 40000010cc00 10168000000000 0");
                Device("event12", "8BitDo Pro 2", "7fff000000000000 0 0 0 0");
                Device("event10", "Xbox Wireless Controller", "7cdb000000000000 0 0 0 0");
                Device("event1", "AT Translated Set 2 keyboard", "402000000 3803078f800d001 feffffdfffefffff fffffffffffffffe");
                Assert.Equal("Xbox Wireless Controller", Rpcs3Layout.FirstGamepad(root));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void Eden_HasAController_OnceOneIsChosenInItsWindow()
        {
            Assert.False(EdenLayout.HasController("[UI]\ncheck_for_updates=false\n"));
            Assert.False(EdenLayout.HasController("[Controls]\nplayer_0_button_a=\"engine:keyboard,code:67,toggle:0\"\n"));
            Assert.True(EdenLayout.HasController("[Controls]\r\nplayer_0_button_a=\"engine:sdl,port:0,guid:030000005e040000,button:1\"\r\n"));
        }
    }
}
