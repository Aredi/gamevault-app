using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using gamevault.UserControls;

namespace GameVault.UiTests
{
    public class RetroControlsClickScenarios
    {
        /// <summary>Régler mes touches, clicked with the mouse: the consoles and the keyboard tab answer.</summary>
        [AvaloniaFact]
        public async Task TheConsoles_AndTheKeyboardTab_AnswerTheMouse()
        {
            await TestSession.GetAsync();
            var controls = new RetroControlsUserControl();
            var window = new Window { Width = 1300, Height = 900, Content = controls };
            window.Show();
            try
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                void Click(Control c)
                {
                    window.UpdateLayout();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;
                    var hit = window.InputHitTest(p) as Visual;
                    Assert.True(hit == c || hit?.FindAncestorOfType<Control>(true) is Control a && (a == c || a.GetVisualAncestors().Contains(c)), $"{c.Name} covered by {hit}");
                    window.MouseDown(p, MouseButton.Left);
                    window.MouseUp(p, MouseButton.Left);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                }
                Click(controls.FindControl<ToggleButton>("uiKeyboardTab")!);
                Assert.True(controls.IsKeyboard);
                var psx = controls.FindControl<ItemsControl>("uiConsoles")!.GetVisualDescendants().OfType<ToggleButton>()
                    .First(b => b.DataContext is RetroControlsUserControl.ConsoleChip chip && chip.System.Slug == "psx");
                Click(psx);
                Assert.Equal("psx", controls.CurrentSystem.Slug);
            }
            finally { window.Close(); }
        }
    }
}
