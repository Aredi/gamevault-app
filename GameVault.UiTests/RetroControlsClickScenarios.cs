using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameVault.Core.Retro;
using gamevault.Helper;
using gamevault.UserControls;
using gamevault.ViewModels;

namespace GameVault.UiTests
{
    /// <summary>Régler mes touches in the app's own popup, used with the mouse and the keyboard only (no event raised by hand).</summary>
    public class RetroControlsClickScenarios
    {
        private static Window Window => gamevault.App.Instance.MainWindow!;

        private static void Settle()
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        /// <summary>A click where the player sees it: the control must be the one under the mouse.</summary>
        private static void Click(Control c, Point? at = null)
        {
            Settle();
            Assert.True(c.IsEffectivelyVisible && c.IsEffectivelyEnabled, $"{c.Name ?? c.GetType().Name} can not be clicked");
            var p = c.TranslatePoint(at ?? new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), Window)!.Value;
            var hit = Window.InputHitTest(p) as Visual;
            Assert.True(hit == c || hit != null && hit.GetVisualAncestors().Contains(c), $"{c.Name ?? c.GetType().Name} covered by {hit}");
            Window.MouseDown(p, MouseButton.Left);
            Window.MouseUp(p, MouseButton.Left);
            Settle();
        }

        private static void Press(Key key, PhysicalKey physical, string? symbol)
        {
            Window.KeyPress(key, RawInputModifiers.None, physical, symbol);
            Window.KeyRelease(key, RawInputModifiers.None, physical, symbol);
            Settle();
        }

        private static T Named<T>(RetroControlsUserControl controls, string name) where T : Control => controls.FindControl<T>(name)!;
        private static IEnumerable<T> In<T>(Control c) where T : Control => c.GetVisualDescendants().OfType<T>();
        private static List<RetroControlsUserControl.ControlRow> Rows(RetroControlsUserControl controls) =>
            Named<ItemsControl>(controls, "uiRows").Items.Cast<RetroControlsUserControl.ControlRow>().ToList();
        private static Button Change(RetroControlsUserControl controls, string bind) =>
            In<Button>(Named<ItemsControl>(controls, "uiRows")).First(b => b.DataContext is RetroControlsUserControl.ControlRow r && r.Bind == bind);
        private static ToggleButton Console(RetroControlsUserControl controls, string slug) =>
            In<ToggleButton>(Named<ItemsControl>(controls, "uiConsoles")).First(b => b.DataContext is RetroControlsUserControl.ConsoleChip c && c.System.Slug == slug);
        private static Button Guided(RetroControlsUserControl controls, int i) => In<Button>(Named<WrapPanel>(controls, "uiGuidedButtons")).ElementAt(i);

        /// <summary>A point of the plan on this button.</summary>
        private static Point On(PadDiagram diagram, string id)
        {
            for (double y = 0; y < diagram.Bounds.Height; y += 3)
                for (double x = 0; x < diagram.Bounds.Width; x += 3)
                    if (diagram.ButtonAt(new Point(x, y)) == id)
                        return new Point(x, y);
            throw new InvalidOperationException($"{id} is not on the plan {diagram.Kind}");
        }

        /// <summary>
        /// Every control of the window, from a game's page as the player opens it: the consoles, Manette / Clavier, a key
        /// given after Change and after a click on the plan, Escape, "Tout régler" and its buttons, the Wii's controllers,
        /// the defaults, Close.
        /// </summary>
        [AvaloniaFact]
        public async Task EveryControl_AnswersTheMouseAndTheKeyboard()
        {
            await TestSession.GetAsync();
            var (keyboards, pads, consoles, games) = (RetroArchManager.Keyboard, RetroArchManager.Pad, RetroArchManager.Controllers, RetroArchManager.GameControllers);
            try
            {
                RetroArchManager.Keyboard = new();
                RetroArchManager.Pad = new();
                RetroArchManager.Controllers = new();
                RetroArchManager.GameControllers = new();
                var mario = new RetroGame { Id = 9001, Name = "Super Mario World", Platform = new RetroPlatform { Slug = "snes", Name = "SNES" } };
                var view = new RetroGameViewUserControl(mario);
                MainWindowViewModel.Instance.OpenPopup(view);
                Settle();
                Click(view.FindControl<Button>("uiControls")!);
                var controls = Assert.IsType<RetroControlsUserControl>(MainWindowViewModel.Instance.Popup);
                Assert.Equal("snes", controls.CurrentSystem.Slug);

                // Each console, then the keyboard
                foreach (var system in RetroControlsUserControl.Systems)
                {
                    Click(Console(controls, system.Slug));
                    Assert.Equal(system.Slug, controls.CurrentSystem.Slug);
                }
                Click(Console(controls, "snes"));
                Click(Named<ToggleButton>(controls, "uiKeyboardTab"));
                Assert.True(controls.IsKeyboard);

                // Change, then an AZERTY keyboard's A: RetroArch's "q"
                Click(Change(controls, "a"));
                Assert.Equal("a", controls.Listening);
                Press(Key.A, PhysicalKey.Q, "a");
                Assert.Null(controls.Listening);
                Assert.Equal("q", RetroArchManager.Keyboard["snes"]["a"].Name);
                Assert.Equal("A", Rows(controls).Single(r => r.Bind == "a").Input);

                // The plan's B, then Escape keeps the key it had
                var diagram = Named<PadDiagram>(controls, "uiDiagram");
                Click(diagram, On(diagram, "b"));
                Assert.Equal("b", controls.Listening);
                Click(Named<Button>(controls, "uiCancel"));
                Assert.Null(controls.Listening);
                Click(diagram, On(diagram, "b"));
                Press(Key.Escape, PhysicalKey.Escape, null);
                Assert.Null(controls.Listening);

                // Tout régler: a key, Garder, Aucune, Précédent, Arrêter
                Click(Named<IconButton>(controls, "uiGuided"));
                string first = controls.Listening!;
                Press(Key.W, PhysicalKey.Z, "w");
                string second = controls.Listening!;
                Assert.NotEqual(first, second);
                Click(Guided(controls, 1));
                Assert.NotEqual(second, controls.Listening);
                string third = controls.Listening!;
                Click(Guided(controls, 2));
                Assert.Equal("nul", RetroArchManager.Keyboard["snes"][third].Name);
                Click(Guided(controls, 0));
                Assert.Equal(third, controls.Listening);
                Click(Guided(controls, 3));
                Assert.Null(controls.Listening);

                // The defaults, back to the gamepad
                Click(Named<Button>(controls, "uiDefaults"));
                Assert.False(RetroArchManager.Keyboard.ContainsKey("snes"));
                Click(Named<ToggleButton>(controls, "uiPadTab"));
                Assert.False(controls.IsKeyboard);
                Click(Change(controls, "a"));
                Assert.Equal("a", controls.Listening);
                Press(Key.Escape, PhysicalKey.Escape, null);

                // The Wii: its controllers, and its plan's stick waits for its four directions
                Click(Console(controls, "wii"));
                var classic = In<ToggleButton>(Named<ItemsControl>(controls, "uiControllers")).First(b => b.DataContext is RetroControlsUserControl.ControllerChip { Device: 1025 });
                Click(classic);
                Assert.Equal(1025, RetroArchManager.Controllers["wii"]);
                Assert.Equal("wiiClassic", diagram.Kind);
                Click(Named<ToggleButton>(controls, "uiKeyboardTab"));
                Click(diagram, On(diagram, "lstick"));
                Assert.StartsWith("l_", controls.Listening);
                Press(Key.Escape, PhysicalKey.Escape, null);
                Click(Guided(controls, 3));

                // Close: the game's page again
                Click(In<Button>(controls).Last(b => b.Content is string s && s == gamevault.Localization.Loc.T("Close")));
                Assert.Null(MainWindowViewModel.Instance.Popup);
            }
            finally
            {
                MainWindowViewModel.Instance.ClosePopup();
                (RetroArchManager.Keyboard, RetroArchManager.Pad, RetroArchManager.Controllers, RetroArchManager.GameControllers) = (keyboards, pads, consoles, games);
            }
        }

        /// <summary>Each button of each console and controller, waiting for its key, lights a button of its plan (L3, R3: their stick).</summary>
        [AvaloniaFact]
        public void EachButton_LightsItsPlan()
        {
            var diagram = new PadDiagram();
            var missing = new List<string>();
            foreach (var system in RetroControlsUserControl.Systems)
                foreach (int? device in RetroControllers.BySystem.TryGetValue(system.Slug, out var controllers) ? controllers.Select(c => (int?)c.Device) : new int?[] { null })
                {
                    diagram.Kind = PadDiagram.KindOf(system.Slug, device);
                    foreach (var button in RetroKeyboard.Of(system.Slug, device))
                    {
                        diagram.Highlight = button.Bind;
                        if (!diagram.Shows(diagram.Lit))
                            missing.Add($"{system.Slug}/{device}: {button.Bind}");
                    }
                }
            Assert.Empty(missing);
        }

        /// <summary>
        /// "Installer RetroArch" in the window, for real (downloads RetroArch in the test's own folder): installed, then its
        /// SDL2 reads the gamepads. Run with SANCTUARYVAULT_NETWORK_TESTS=1.
        /// </summary>
        [AvaloniaFact]
        public async Task InstallingRetroArch_FromTheWindow_ThenReadsTheGamepads()
        {
            if (Environment.GetEnvironmentVariable("SANCTUARYVAULT_NETWORK_TESTS") != "1")
                return;
            await TestSession.GetAsync();
            Assert.False(RetroArchManager.Layout.IsInstalled);
            var controls = new RetroControlsUserControl();
            MainWindowViewModel.Instance.OpenPopup(controls);
            try
            {
                Click(Named<Button>(controls, "uiInstall"));
                var device = Named<TextBlock>(controls, "uiDevice");
                var texts = new HashSet<string?>();
                await TestSession.WaitUntil(() => { texts.Add(device.Text); return RetroArchManager.Layout.IsInstalled && controls.FindControl<Button>("uiInstall")!.IsVisible == false && !device.Text!.Contains("..."); },
                    TimeSpan.FromMinutes(8), "RetroArch to be installed");
                Settle();
                Assert.DoesNotContain(gamevault.Localization.Loc.T("RetroArch could not be installed: {0}").Split('{')[0], device.Text);
                Assert.DoesNotContain(gamevault.Localization.Loc.T("The gamepad can not be read: {0}").Split('{')[0], device.Text);
                Assert.NotNull(RetroArchManager.Sdl2Library);
                Assert.True(texts.Count > 2, string.Join(" | ", texts));
            }
            finally
            {
                MainWindowViewModel.Instance.ClosePopup();
                // The other tests play without RetroArch
                Assert.StartsWith(IsolatedHome.Root, RetroArchManager.Layout.Directory);
                if (Directory.Exists(RetroArchManager.Layout.Directory))
                    Directory.Delete(RetroArchManager.Layout.Directory, true);
            }
        }
    }
}
