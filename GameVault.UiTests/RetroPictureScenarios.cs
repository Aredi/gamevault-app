using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameVault.Core.Retro;
using gamevault.Helper;
using gamevault.UserControls.SettingsComponents;

namespace GameVault.UiTests
{
    public class RetroPictureScenarios
    {
        /// <summary>
        /// Settings > Retro > Picture: the PlayStation's definition and the PS2 in 16:9, written in their cores' options
        /// files (the PS2's under its core's own name, "ParaLLEl PS2") before RetroArch starts; 16:9 off again, written too.
        /// </summary>
        [AvaloniaFact]
        public async Task TheDefinition_AndWidescreen_AreWrittenForTheirCores()
        {
            await TestSession.GetAsync();
            var (resolutions, widescreen) = (RetroArchManager.Resolutions, RetroArchManager.WidescreenCores);
            string ps2 = RetroArchManager.Layout.CoreOptionsFile("pcsx2"), ps1 = RetroArchManager.Layout.CoreOptionsFile("swanstation");
            var window = new Window { Width = 1200, Height = 900 };
            try
            {
                RetroArchManager.Resolutions = new();
                RetroArchManager.WidescreenCores = new();
                Assert.EndsWith(Path.Combine("ParaLLEl PS2", "ParaLLEl PS2.opt"), ps2);
                var settings = new RetroSettingsUserControl();
                window.Content = settings;
                window.Show();
                await settings.RefreshAsync();
                Dispatcher.UIThread.RunJobs();

                // The PlayStation at 3x, the PS2 in 16:9
                var definition = settings.FindControl<ItemsControl>("uiResolution")!.GetVisualDescendants().OfType<ComboBox>()
                    .First(c => c.DataContext is RetroSettingsUserControl.ResolutionRow { Core: "swanstation" });
                definition.SelectedIndex = 2;
                Assert.Equal(3, RetroArchManager.Resolutions["swanstation"]);
                CheckBox Wide(string core) => settings.FindControl<ItemsControl>("uiWidescreen")!.GetVisualDescendants().OfType<CheckBox>()
                    .First(c => c.DataContext is RetroSettingsUserControl.CrtRow row && row.Slug == core);
                Assert.Equal("PlayStation 2", Wide("pcsx2").Content);
                Wide("pcsx2").IsChecked = true;
                Assert.Contains("pcsx2", RetroArchManager.WidescreenCores);

                Assert.ThrowsAny<Exception>(() => RetroArchManager.Launch(RetroSystems.All["psx"], "/nowhere/ff7.chd", "English"));
                Assert.Contains("swanstation_GPU_ResolutionScale = \"3\"", File.ReadAllText(ps1));
                Assert.Contains("swanstation_GPU_WidescreenHack = \"false\"", File.ReadAllText(ps1));
                Assert.ThrowsAny<Exception>(() => RetroArchManager.Launch(RetroSystems.All["ps2"], "/nowhere/gt4.iso", "English"));
                Assert.Contains("pcsx2_widescreen_hint = \"enabled (16:9)\"", File.ReadAllText(ps2));

                // 16:9 off: written back
                Wide("pcsx2").IsChecked = false;
                Assert.ThrowsAny<Exception>(() => RetroArchManager.Launch(RetroSystems.All["ps2"], "/nowhere/gt4.iso", "English"));
                Assert.Contains("pcsx2_widescreen_hint = \"disabled\"", File.ReadAllText(ps2));
            }
            finally
            {
                window.Close();
                (RetroArchManager.Resolutions, RetroArchManager.WidescreenCores) = (resolutions, widescreen);
                foreach (string file in new[] { ps1, ps2 }.Where(File.Exists))
                    File.Delete(file);
            }
        }
    }
}
