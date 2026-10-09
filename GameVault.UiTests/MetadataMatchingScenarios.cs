using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.ViewModels;
using GameVault.Core;

namespace GameVault.UiTests
{
    /// <summary>The remap page of the game settings and the automatic mapping of the admin console, against the fake server.</summary>
    public class MetadataMatchingScenarios
    {
        private static Game Unmapped(int id, string fileName) => new()
        {
            ID = id,
            Title = Path.GetFileNameWithoutExtension(fileName),
            SortTitle = fileName.ToLowerInvariant(),
            Type = GameType.WINDOWS_PORTABLE,
            Path = $"/files/{fileName}",
            Size = "1024",
            EntityVersion = 1,
            Progresses = new List<Progress>(),
            ProviderMetadata = new List<GameMetadata>(),
        };

        private static MinimalGame Candidate(string id, string title, int year) =>
            new() { ProviderSlug = "igdb", ProviderDataId = id, Title = title, ReleaseDate = new DateTime(year, 1, 1) };

        private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        [AvaloniaFact]
        public async Task RemapPage_SearchesTheFileTitle_RanksTheResults_AndScrollsToTheLast()
        {
            var session = await TestSession.GetAsync();
            // Many "Witcher" games: the right one is not the first the provider returns
            foreach (var (id, title, year) in new[]
            {
                ("1101", "The Witcher", 2007), ("1102", "The Witcher 2: Assassins of Kings", 2011), ("1103", "The Witcher 3: Wild Hunt", 2015),
                ("1104", "The Witcher Adventure Game", 2014), ("1105", "The Witcher: Enhanced Edition", 2008), ("1106", "Witcher 3: Hearts of Stone", 2015),
                ("1107", "The Witcher 3: Blood and Wine", 2016), ("1108", "The Witcher: Monster Slayer", 2021), ("1109", "The Witcher Battle Arena", 2014),
            })
                session.Server.ProviderCatalog.Add(Candidate(id, title, year));
            Game game = Unmapped(9701, "The.Witcher.3.Wild.Hunt.v4.04-GOG.zip");
            session.Server.AddGame(game, new byte[1024]);

            var settings = new GameSettingsUserControl(game);
            MainWindowViewModel.Instance.OpenPopup(settings);
            try
            {
                await TestSession.WaitUntil(() => settings.IsLoaded, TimeSpan.FromSeconds(10), "the settings popup");
                settings.FindControl<ListBox>("uiSettingsHeadersRemote")!.SelectedIndex = 1;// Metadata
                var model = (GameSettingsViewModel)settings.DataContext!;
                var search = settings.FindControl<TextBox>("uiRemapSearch")!;
                await TestSession.WaitUntil(() => model.RemapSearchResults?.Length == 9, TimeSpan.FromSeconds(10), "the provider results");
                Assert.Equal("The Witcher 3 Wild Hunt", search.Text);
                Assert.Equal("1103", model.RemapSearchResults![0].ProviderDataId);
                Assert.True(model.RemapSearchResults[0].IsBestMatch);
                Assert.True(model.RemapSearchResults.Zip(model.RemapSearchResults.Skip(1)).All(p => p.First.MatchScore >= p.Second.MatchScore), "ranked by match");

                // Every result can be reached: the list scrolls within the popup, down to the last one
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var remapButtons = settings.GetVisualDescendants().OfType<IconButton>().Where(b => b.DataContext is MinimalGame).ToList();
                Assert.Equal(9, remapButtons.Count);
                var scroll = remapButtons[^1].FindAncestorOfType<ScrollViewer>()!;
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height, $"the results scroll ({scroll.Extent.Height} > {scroll.Viewport.Height})");
                Assert.True(scroll.Viewport.Height >= 200, "the results keep some room");
                scroll.Offset = new Vector(0, scroll.Extent.Height);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Rect last = new Rect(remapButtons[^1].Bounds.Size).TransformToAABB(remapButtons[^1].TransformToVisual(scroll)!.Value);
                Assert.True(last.Top >= 0 && last.Bottom <= scroll.Viewport.Height + 1, $"the last Remap button is visible ({last})");
                Rect popup = new Rect(scroll.Bounds.Size).TransformToAABB(scroll.TransformToVisual(session.Window)!.Value);
                Assert.True(popup.Bottom <= session.Window.Bounds.Height, "the list ends inside the window");

                // Remapping sends the chosen game
                Click(remapButtons[^1]);
                await TestSession.WaitUntil(() => session.Server.Mappings.Any(m => m.GameId == 9701), TimeSpan.FromSeconds(10), "the mapping");
                Assert.Equal(((MinimalGame)remapButtons[^1].DataContext!).ProviderDataId, session.Server.Mappings.Last(m => m.GameId == 9701).ProviderId);
            }
            finally
            {
                MainWindowViewModel.Instance.ClosePopup();
            }
        }

        [AvaloniaFact]
        public async Task AutoMatch_ProposesFromTheFileNames_AndMapsOnlyTheCheckedGames()
        {
            var session = await TestSession.GetAsync();
            foreach (var game in new[]
            {
                Candidate("2201", "Hollow Knight", 2017), Candidate("2202", "Hollow Knight: Silksong", 2025),
                Candidate("2301", "Prey", 2006), Candidate("2302", "Prey", 2017),
                Candidate("2401", "Metal Gear Solid: Portable Ops", 2006), Candidate("2402", "Metal Gear Solid: Portable Ops Plus", 2007),
            })
                session.Server.ProviderCatalog.Add(game);
            session.Server.AddGame(Unmapped(9801, "Hollow_Knight_v1.5.78 (W_P).zip"), new byte[1024]);
            session.Server.AddGame(Unmapped(9802, "Prey (2017) [FitGirl Repack].zip"), new byte[1024]);
            session.Server.AddGame(Unmapped(9803, "Hades (igdb-113112) (W_P).zip"), new byte[1024]);
            session.Server.AddGame(Unmapped(9804, "MGS Portable Ops Plus Collection.iso"), new byte[1024]);
            session.Server.AddGame(Unmapped(9805, "Some Unknown Homebrew.zip"), new byte[1024]);

            var popup = new AutoMatchUserControl();
            MainWindowViewModel.Instance.OpenPopup(popup);
            try
            {
                var analyze = popup.FindControl<IconButton>("uiAnalyze")!;
                await TestSession.WaitUntil(() => analyze.IsEnabled, TimeSpan.FromSeconds(10), "the providers");
                Click(analyze);
                Assert.False(analyze.IsEnabled, "busy while analyzing");
                await TestSession.WaitUntil(() => analyze.IsEnabled, TimeSpan.FromSeconds(20), "the proposals");
                Assert.True(popup.Items.Count > 0, "status: " + popup.FindControl<TextBlock>("uiStatus")!.Text);

                AutoMatchItem Item(int id) => popup.Items.Single(i => i.Game.ID == id);
                Assert.DoesNotContain(popup.Items, i => i.Game.ID < 9800);// the games with metadata are left alone
                Assert.Equal("2201", Item(9801).Proposal!.ProviderDataId);
                Assert.True(Item(9801).IsConfident && Item(9801).IsChecked);
                Assert.Equal("2302", Item(9802).Proposal!.ProviderDataId);// the year in the name decides
                Assert.Equal("113112", Item(9803).Proposal!.ProviderDataId);// the id in the name
                Assert.False(Item(9805).HasProposal);
                // Not sure: proposed, left unchecked
                if (Item(9804).HasProposal)
                    Assert.False(Item(9804).IsChecked);

                Item(9802).IsChecked = false;
                Click(popup.FindControl<IconButton>("uiApply")!);
                await TestSession.WaitUntil(() => Item(9801).IsDone && Item(9803).IsDone, TimeSpan.FromSeconds(10), "the mappings");
                var sent = session.Server.Mappings.Where(m => m.GameId >= 9800).ToList();
                Assert.Contains((9801, "igdb", "2201"), sent);
                Assert.Contains((9803, "igdb", "113112"), sent);
                Assert.DoesNotContain(sent, m => m.GameId is 9802 or 9804 or 9805);
            }
            finally
            {
                MainWindowViewModel.Instance.ClosePopup();
            }
        }
    }
}
