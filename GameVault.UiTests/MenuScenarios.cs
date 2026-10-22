using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using gamevault.ViewModels;
using IconPath = Avalonia.Controls.Shapes.Path;

namespace GameVault.UiTests
{
    public class MenuScenarios
    {
        /// <summary>The icons of the menu and of the Settings' tabs: in their own box, on the middle line of their title.</summary>
        [AvaloniaFact]
        public async Task TheMenuIcons_AreCenteredOnTheirTitle()
        {
            var session = await TestSession.GetAsync();
            MainWindowViewModel.Instance.SetActiveControl(MainControl.Settings);
            Dispatcher.UIThread.RunJobs();
            try
            {
                var icons = session.Window.GetVisualDescendants().OfType<IconPath>()
                    .Where(p => p.IsEffectivelyVisible && (p.Classes.Contains("tabIcon") || p.FindAncestorOfType<Control>() is { } a && a.GetSelfAndVisualAncestors().OfType<Control>().Any(c => c.Classes.Contains("navRow"))))
                    .Where(p => p.FindAncestorOfType<Button>() is not { } b || !b.Classes.Contains("round"))
                    .ToList();
                Assert.True(icons.Count >= 15, $"{icons.Count} icons");
                foreach (IconPath icon in icons)
                {
                    var row = icon.GetVisualAncestors().OfType<Panel>().FirstOrDefault(p => p.Classes.Contains("navRow")) ?? (Panel)icon.GetVisualParent()!;
                    var title = row.GetVisualChildren().OfType<TextBlock>().First();
                    // Their own 24 x 24 box, not stretched (Uniform puts a wide icon at the top of its box)
                    Assert.Equal(Avalonia.Media.Stretch.None, icon.Stretch);
                    double middle = icon.TranslatePoint(new Point(0, icon.Bounds.Height / 2), row)!.Value.Y;
                    double titleMiddle = title.TranslatePoint(new Point(0, title.Bounds.Height / 2), row)!.Value.Y;
                    Assert.True(Math.Abs(middle - titleMiddle) <= 1, $"{title.Text}: icon middle {middle}, title middle {titleMiddle}");
                }
            }
            finally
            {
                MainWindowViewModel.Instance.SetActiveControl(MainControl.Library);
            }
        }
    }
}
