using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using gamevault.Helper;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.ViewModels;

namespace GameVault.UiTests
{
    public class NavigationScenarios
    {
        /// <summary>The menu's items, as ListBoxItems in their order (MainControl's).</summary>
        private static ListBoxItem Item(TestSession session, MainControl control) =>
            (ListBoxItem)session.Window.FindControl<ListBox>("uiNavigation")!.ContainerFromIndex((int)control)!;

        [AvaloniaFact]
        public async Task AGamesPage_KeepsTheSectionItCameFrom_LitInTheMenu()
        {
            var session = await TestSession.GetAsync();
            var vm = MainWindowViewModel.Instance;
            try
            {
                // From the retro page: its item stays lit, the library's not
                vm.SetActiveControl(MainControl.Retro);
                Dispatcher.UIThread.RunJobs();
                RetroUserControl.OpenGame(new RetroGame { Id = 40, Name = "Castlevania", Platform = new RetroPlatform { Slug = "nes", Name = "NES" } });
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(-1, vm.ActiveControlIndex);
                Assert.Contains("current", Item(session, MainControl.Retro).Classes);
                Assert.DoesNotContain("current", Item(session, MainControl.Library).Classes);

                // Clicking it goes back to the retro page, which is selected again (no longer only lit)
                Item(session, MainControl.Retro).IsSelected = true;
                Dispatcher.UIThread.RunJobs();
                Assert.Same(vm.Retro, vm.ActiveControl);
                Assert.DoesNotContain("current", Item(session, MainControl.Retro).Classes);

                // From the library: the library's
                vm.SetActiveControl(MainControl.Library);
                Dispatcher.UIThread.RunJobs();
                vm.SetActiveControl(new GameViewUserControl(new Game { ID = 1 }));
                Dispatcher.UIThread.RunJobs();
                Assert.Contains("current", Item(session, MainControl.Library).Classes);
                Assert.DoesNotContain("current", Item(session, MainControl.Retro).Classes);

                // A section's page opened without the menu (the downloads from the tray): its own item, not the library's
                vm.SetActiveControl(vm.Downloads);
                Dispatcher.UIThread.RunJobs();
                Assert.Contains("current", Item(session, MainControl.Downloads).Classes);
                Assert.DoesNotContain("current", Item(session, MainControl.Library).Classes);
            }
            finally
            {
                vm.SetActiveControl(MainControl.Library);
                Dispatcher.UIThread.RunJobs();
            }
        }
    }
}
