using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.ViewModels;

namespace GameVault.UiTests
{
    public class QuickSearchScenarios
    {
        private static QuickSearchUserControl Open(TestSession session)
        {
            var existing = session.Window.FindControl<QuickSearchUserControl>("uiQuickSearch")!;
            if (existing.IsVisible)
                existing.Close();
            session.Window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var search = session.Window.FindControl<QuickSearchUserControl>("uiQuickSearch")!;
            Assert.True(search.IsVisible, "Ctrl+K opens the quick search");
            return search;
        }

        /// <summary>Typed on the keyboard, into the search box that has the focus.</summary>
        private static void Type(TestSession session, string text)
        {
            session.Window.KeyTextInput(text);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaFact]
        public async Task CtrlK_FindsAServerGame_AndEnterOpensIt()
        {
            var session = await TestSession.GetAsync();
            session.Server.AddGame(960, "Quick Search Odyssey", GameType.LINUX_PORTABLE, TestSession.LinuxGameArchive(), "Quick Search Odyssey (L_P).tar.gz");
            var search = Open(session);
            Type(session, "quick odyssey");
            await TestSession.WaitUntil(() => search.Results.Any(r => r.Game?.ID == 960), TimeSpan.FromSeconds(10), "the server's game in the results");
            var list = search.FindControl<ListBox>("uiResults")!;
            list.SelectedIndex = search.Results.ToList().FindIndex(r => r.Game?.ID == 960);
            session.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.False(search.IsVisible);
            Assert.IsType<GameViewUserControl>(MainWindowViewModel.Instance.ActiveControl);
        }

        [AvaloniaFact]
        public async Task Pages_AreFound_WithoutAccentsOrWholeWords()
        {
            var session = await TestSession.GetAsync();
            var search = Open(session);
            Type(session, "dwnl");
            Assert.Equal("Downloads", search.Results.First().Title);
            session.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal((int)MainControl.Downloads, MainWindowViewModel.Instance.ActiveControlIndex);

            search = Open(session);
            session.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.False(search.IsVisible);
            MainWindowViewModel.Instance.SetActiveControl(MainControl.Library);
        }
    }
}
