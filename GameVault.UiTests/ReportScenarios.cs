using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.UserControls;

namespace GameVault.UiTests
{
    public class ReportScenarios
    {
        [AvaloniaFact]
        public async Task APlayer_ReportsAProblem_AndTheAdminsSeeItInTheirConsole()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            Loc.Initialize("fr");
            var game = new RetroGame { Id = 77, Name = "God of War II", FileName = "God of War (USA).iso", Platform = new RetroPlatform { Slug = "ps2", Name = "PlayStation 2" } };
            try
            {
                // A right click on a game of the library offers it
                var cover = new Border { DataContext = game };
                var library = new StackPanel { Children = { cover } };
                new Window { Content = library }.Show();
                ReportProblemPopup.OfferOnRightClick(library);
                var rightClick = new ContextRequestedEventArgs();
                cover.RaiseEvent(rightClick);
                Assert.True(rightClick.Handled);

                var popup = ReportProblemPopup.For(game);
                var status = popup.FindControl<TextBlock>("uiStatus")!;
                await popup.SendAsync();
                Assert.Equal("Choisis le type de problème.", status.Text);
                popup.Categories.Single(c => c.Code == "other").IsChecked = true;
                await popup.SendAsync();
                Assert.Equal("Décris le problème.", status.Text);
                Assert.Empty(session.Server.Reports);

                popup.Categories.Single(c => c.Code == "other").IsChecked = false;
                popup.Categories.Single(c => c.Code == "match").IsChecked = true;
                popup.Details.Text = "C'est le premier";
                await popup.SendAsync();
                var sent = session.Server.Reports.Single();
                Assert.Equal("retro", sent["kind"]!.ToString());
                Assert.Equal("ps2", sent["platform"]!.ToString());
                Assert.Equal("match", sent["category"]!.ToString());
                Assert.StartsWith("PlayStation 2 · God of War (USA).iso · SanctuaryVault", sent["context"]!.ToString());

                // The admin console lists the open ones, until resolved
                var console = new AdminConsoleUserControl();
                await console.LoadReportsAsync();
                Assert.Equal("Signalements (1 en attente)", console.FindControl<TextBlock>("uiReportsTitle")!.Text);
                var item = Assert.Single((IEnumerable<ProblemReportItem>)console.FindControl<ItemsControl>("uiReports")!.ItemsSource!);
                Assert.Equal("Mauvaise jaquette ou mauvais jeu associé", item.CategoryText);
                Assert.Equal("God of War II · ps2", item.Header);
                await ReportsService.ResolveAsync(item.Id);
                await console.LoadReportsAsync();
                Assert.True(console.FindControl<TextBlock>("uiReportsEmpty")!.IsVisible);
            }
            finally
            {
                Loc.Initialize("en");
                lock (session.Server.Reports)
                    session.Server.Reports.Clear();
                SanctuaryService.Current = SanctuaryService.Url;
            }
        }
    }
}
