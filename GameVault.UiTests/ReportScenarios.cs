using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.UserControls;
using GameVault.Core.Diagnostics;

namespace GameVault.UiTests
{
    public class ReportScenarios
    {
        [AvaloniaFact]
        public async Task AFailedStart_GoesToTheAdminsWithItsDiagnostic_ShownToThePlayerFirst()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            Loc.Initialize("fr");
            var game = new RetroGame { Id = 78, Name = "Hades", FileName = "Hades.nsp", Platform = new RetroPlatform { Slug = "switch", Name = "Nintendo Switch" } };
            game.Failure = new GameFailure("Eden s'est arrêté dès son lancement (code 0xC000001D).", unchecked((int)0xC000001D), 3, DateTime.UtcNow);
            try
            {
                Assert.True(game.HasFailure);
                Assert.Contains("3 s", game.FailureAdvice);

                // Reported as a game that does not start, its diagnostic shown before anything leaves
                var popup = ReportProblemPopup.For(game);
                new Window { Content = popup }.Show();
                Assert.True(popup.Categories.Single(c => c.Code == "launch").IsChecked);
                await WaitUntilAsync(() => !string.IsNullOrEmpty(popup.FindControl<SelectableTextBlock>("uiDiagnosticText")!.Text));
                Assert.True(popup.FindControl<Border>("uiDiagnostic")!.IsVisible);
                Assert.Contains("0xC000001D", popup.FindControl<SelectableTextBlock>("uiDiagnosticText")!.Text);
                Assert.DoesNotContain(Environment.UserName, popup.FindControl<SelectableTextBlock>("uiDiagnosticText")!.Text);
                await popup.SendAsync();
                var sent = session.Server.Reports.Single();
                Assert.Equal(true, sent["hasDiagnostic"]);

                // The administrators read it, with what its code tells
                var console = new AdminConsoleUserControl();
                await console.LoadReportsAsync();
                var item = Assert.Single((IEnumerable<ProblemReportItem>)console.FindControl<ItemsControl>("uiReports")!.ItemsSource!);
                Assert.True(item.HasDiagnostic);
                var read = new ReportDiagnosticPopup(item, await ReportsService.DiagnosticAsync(item.Id));
                Assert.Equal("Processeur trop ancien pour Eden", read.FindControl<TextBlock>("uiHintTitle")!.Text);

                // Without the switch nothing is joined
                var second = ReportProblemPopup.For(game);
                new Window { Content = second }.Show();
                second.FindControl<ToggleSwitch>("uiAttach")!.IsChecked = false;
                await second.SendAsync();
                Assert.Equal(false, session.Server.Reports[^1]["hasDiagnostic"]);
            }
            finally
            {
                Loc.Initialize("en");
                lock (session.Server.Reports)
                    session.Server.Reports.Clear();
                SanctuaryService.Current = SanctuaryService.Url;
            }
        }

        private static async Task WaitUntilAsync(Func<bool> done)
        {
            for (int i = 0; i < 200 && !done(); i++)
                await Task.Delay(25);
            Assert.True(done());
        }

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
