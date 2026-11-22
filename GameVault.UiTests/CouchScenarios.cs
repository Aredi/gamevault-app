using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using GameVault.Core.Couch;
using gamevault.Converter;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.UserControls.SettingsComponents;
using gamevault.ViewModels;

namespace GameVault.UiTests
{
    /// <summary>The living-room mode: the games in tabs moved through with a gamepad's commands, a game's screen, the menu.</summary>
    public class CouchScenarios
    {
        private static readonly string[] Titles = { "Astral Drift", "Cinder Vale", "Hollow Tides", "Iron Meridian", "Lumen Trail", "Night Harbor", "Paper Kingdoms", "Zephyr Run" };

        /// <summary>COUCH_ART: a folder of "{n}-cover.jpg" and "{n}-hero.jpg" for the screenshots (COUCH_SHOTS, with SHOWCASE_DIR for real frames).</summary>
        private static List<Game> Games(TestSession session)
        {
            string? art = Environment.GetEnvironmentVariable("COUCH_ART");
            var games = new List<Game>();
            for (int i = 0; i < Titles.Length; i++)
            {
                Media? Image(string name, int id)
                {
                    if (art == null || !File.Exists(Path.Combine(art, $"{i}-{name}.jpg")))
                        return null;
                    session.Server.Media[id] = File.ReadAllBytes(Path.Combine(art, $"{i}-{name}.jpg"));
                    return new Media { ID = id };
                }
                var game = new Game
                {
                    ID = 9700 + i, Title = Titles[i], SortTitle = Titles[i].ToLowerInvariant(), Type = GameType.WINDOWS_PORTABLE, Path = $"/files/{Titles[i]}.zip",
                    Size = ((i + 2) * 3L * 1024 * 1024 * 1024).ToString(), EntityVersion = 1, Progresses = new List<Progress>(), ReleaseDate = new DateTime(2019 + i % 6, 3, 1),
                    Metadata = new GameMetadata
                    {
                        Title = Titles[i], Description = "A long road through a quiet country, where each town has its own secret and every night changes the map. Travel alone or with three friends.",
                        Genres = new List<GenreMetadata> { new() { Name = i % 2 == 0 ? "Adventure" : "Strategy" } },
                        Cover = Image("cover", 97000 + i * 2), Background = Image("hero", 97001 + i * 2),
                    },
                };
                games.Add(game);
                session.Server.AddGame(game, Array.Empty<byte>());
            }
            return games;
        }

        private static List<RetroGame> Retro()
        {
            var nes = new RetroPlatform { Slug = "nes", Name = "NES" };
            return new List<RetroGame>
            {
                new() { Id = 40, Name = "Zelda", FileName = "Zelda (E).nes", Platform = nes, Summary = "A sword" },
                new() { Id = 43, Name = "Metroid", FileName = "Metroid (E).nes", Platform = nes },
            };
        }

        private static void Capture(Window window, string name)
        {
            if (Environment.GetEnvironmentVariable("COUCH_SHOTS") is not { Length: > 0 } folder)
                return;
            // The pictures are read, the fades are over
            for (int i = 0; i < 40; i++)
            {
                Thread.Sleep(40);
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }
            window.CaptureRenderedFrame()!.Save(Path.Combine(folder, name + ".png"));
        }

        [AvaloniaFact]
        public async Task TheGames_AreMovedThroughWithTheGamepad_FromTheStripToAGamesScreenAndTheMenu()
        {
            var session = await TestSession.GetAsync();
            List<Game> games = Games(session);
            var installed = new KeyValuePair<Game, string>(games[2], Path.Combine(session.LibraryRoot, "couch-installed"));
            InstallViewModel.Instance.InstalledGames.Add(installed);
            bool startedHere = CouchUserControl.StartsHere;
            var couch = new CouchUserControl();
            var window = new Window { Width = 1920, Height = 1080, Content = couch };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                couch.Settings = new CouchSettings();
                couch.Show(games, Retro());

                // The tabs: the home, what is installed, the library, then each console; the first game selected
                Assert.Equal(new[] { Loc.T("Home"), Loc.T("Installed games"), Loc.T("Library"), "NES" }, couch.Tabs.Select(t => t.Name));
                Assert.True(couch.Tabs[0].IsSelected && couch.Current!.IsTile);
                couch.Handle(CouchCommand.NextTab);
                Assert.Equal("Hollow Tides", couch.Current!.Title);
                Assert.True(couch.Tabs[1].IsSelected && couch.Current.IsSelected && couch.Current.Installed);
                couch.Handle(CouchCommand.NextTab);
                Assert.Equal("Astral Drift", couch.HeroTitle);
                Assert.Equal(new[] { Loc.T("Not played yet"), "6 " + Loc.T("GB"), Loc.T("Not installed") }, couch.Stats.Select(s => s.Value));
                Capture(window, "1-strip");

                // The strip: right, right, and the letters with the triggers
                CouchItem first = couch.Current;
                couch.Handle(CouchCommand.Right);
                couch.Handle(CouchCommand.Right);
                Assert.Equal("Hollow Tides", couch.Current.Title);
                Assert.True(couch.Current.IsSelected && !first.IsSelected);
                Assert.Equal(Loc.T("Installed"), couch.Stats[^1].Value);
                couch.Handle(CouchCommand.NextLetter);
                Assert.Equal("Iron Meridian", couch.Current.Title);
                // The keyboard does the same
                Assert.True(couch.HandleKey(Key.Left));
                Assert.Equal("Hollow Tides", couch.Current.Title);
                Assert.False(couch.HandleKey(Key.A));

                // The grid, by rows
                couch.Handle(CouchCommand.View);
                Assert.True(couch.Navigator.IsGrid && couch.uiGrid.IsVisible && !couch.uiStrip.IsVisible);
                Assert.Equal(8, couch.Navigator.Columns);
                Assert.Contains(couch.Hints, h => h.Label == "X" && h.Value == Loc.T("List"));
                Capture(window, "2-grid");
                // A narrower screen has fewer games by row
                window.Width = 1280;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(5, couch.Navigator.Columns);
                couch.Handle(CouchCommand.Down);
                Assert.Equal("Zephyr Run", couch.Current.Title);
                window.Width = 1920;
                Dispatcher.UIThread.RunJobs();
                // The list, then the strip again
                couch.Handle(CouchCommand.View);
                couch.Handle(CouchCommand.View);
                couch.Handle(CouchCommand.PreviousLetter);
                couch.Handle(CouchCommand.PreviousLetter);
                couch.Handle(CouchCommand.PreviousLetter);
                Assert.Equal("Lumen Trail", couch.Current.Title);

                // A game's screen: what can be done with it, then the next game without leaving it
                couch.Handle(CouchCommand.Accept);
                Assert.True(couch.uiDetails.IsVisible && !couch.uiHome.IsVisible);
                Assert.Equal(new[] { Loc.T("Install and play"), Loc.T("Install"), Loc.T("Add to the favorites") }, couch.Actions.Select(a => a.Text));
                Assert.True(couch.Actions[0].IsSelected);
                couch.Handle(CouchCommand.Down);
                Assert.True(couch.Actions[1].IsSelected && !couch.Actions[0].IsSelected);
                Assert.Equal("Lumen Trail", couch.uiDetailTitle.Text);
                Capture(window, "3-details");
                couch.Handle(CouchCommand.PreviousTab);
                couch.Handle(CouchCommand.PreviousTab);
                Assert.Equal("Hollow Tides", couch.uiDetailTitle.Text);
                Assert.Equal(new[] { Loc.T("Play"), Loc.T("Add to the favorites"), Loc.T("Uninstall") }, couch.Actions.Select(a => a.Text));
                couch.Handle(CouchCommand.Back);
                Assert.True(couch.uiHome.IsVisible);

                // A retro game
                couch.Handle(CouchCommand.NextTab);
                Assert.Equal(new[] { "Metroid", "Zelda" }, couch.Tabs[3].Items.Select(i => i.Title));
                couch.Handle(CouchCommand.Right);
                couch.Handle(CouchCommand.Accept);
                Assert.Equal("NES", couch.uiDetailEyebrow.Text);
                Assert.Equal("A sword", couch.uiDescription.Text);
                Assert.Equal(Loc.T("Install and play"), couch.Actions[0].Text);

                // A popup of the client over it: B closes it, nothing moves under it
                MainWindowViewModel.Instance.OpenPopup(new TextBlock { Text = "notice" });
                couch.Handle(CouchCommand.Down);
                Assert.True(couch.Actions[0].IsSelected);
                couch.Handle(CouchCommand.Back);
                Assert.Null(MainWindowViewModel.Instance.Popup);
                Assert.True(couch.uiDetails.IsVisible);

                // The menu: the start in this mode is kept, the desktop is asked back
                couch.Handle(CouchCommand.Menu);
                Assert.True(couch.uiMenu.IsVisible);
                Assert.Equal(8, couch.MenuLines.Count);
                Capture(window, "4-menu");
                // Its settings: the start in this mode; B goes back to the menu
                CouchUserControl.StartsHere = false;
                couch.Handle(CouchCommand.Down);
                couch.Handle(CouchCommand.Down);
                couch.Handle(CouchCommand.Down);
                couch.Handle(CouchCommand.Accept);
                // The settings: their pages, as Aniki's categories; the start is the seventh, docked at the left with its help
                Assert.Equal(CouchMenu.Settings, couch.Menu);
                Assert.Equal(Avalonia.Layout.HorizontalAlignment.Left, couch.uiMenuBox.HorizontalAlignment);
                for (int page = 0; page < 6; page++)
                    couch.Handle(CouchCommand.Down);
                Assert.Equal(Loc.T("What the mode shows when it opens, and its screensaver."), couch.uiMenuHelp.Text);
                couch.Handle(CouchCommand.Accept);
                Assert.Equal((CouchMenu.Custom, "start"), (couch.Menu, couch.SettingsPage));
                couch.Handle(CouchCommand.Accept);
                Assert.True(CouchUserControl.StartsHere);
                Assert.Equal(Loc.T("At start: the living-room mode"), couch.MenuLines[0].Text);
                Assert.True(couch.MenuLines[0].IsSelected);
                // B: back to the pages, on the one it was, then to the menu
                couch.Handle(CouchCommand.Back);
                Assert.Equal(CouchMenu.Settings, couch.Menu);
                Assert.True(couch.MenuLines[6].IsSelected);
                couch.Handle(CouchCommand.Back);
                Assert.Equal(CouchMenu.Main, couch.Menu);
                Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, couch.uiMenuBox.HorizontalAlignment);
                Assert.True(couch.uiMenu.IsVisible);
                bool left = false;
                couch.LeaveRequested += () => left = true;
                for (int line = 0; line < 5; line++)
                    couch.Handle(CouchCommand.Down);
                couch.Handle(CouchCommand.Accept);
                Assert.True(left);
                Assert.False(couch.uiMenu.IsVisible);

                // A mouse or a remote that points: a click selects, a second one opens, a click on a tab shows it
                couch.Handle(CouchCommand.Back);
                void Click(Func<Control?> find)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs();
                    Control control = find() ?? throw new InvalidOperationException("Nothing to click");
                    var point = control.TranslatePoint(new Avalonia.Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(point, MouseButton.Left);
                    Dispatcher.UIThread.RunJobs();
                }
                Click(() => couch.uiStripItems.TryGetElement(0));
                Assert.Equal("Metroid", couch.Current!.Title);
                Assert.True(couch.uiHome.IsVisible);
                Click(() => couch.uiStripItems.TryGetElement(0));
                Assert.True(couch.uiDetails.IsVisible);
                couch.Handle(CouchCommand.Menu);
                Click(() => couch.uiMenuLines.ContainerFromIndex(0));
                // Opened over the game's screen, the menu goes back to it
                Assert.False(couch.uiMenu.IsVisible);
                Assert.True(couch.uiDetails.IsVisible);
                couch.Handle(CouchCommand.Back);
                Click(() => couch.uiTabs.ContainerFromIndex(2));
                Assert.Equal(Loc.T("Library"), couch.Tabs[couch.Navigator.Tab].Name);
                Click(() => couch.uiTabs.ContainerFromIndex(3));
                Assert.Equal("Metroid", couch.Current!.Title);
                couch.Handle(CouchCommand.Right);

                // The games read from the server: the same tab and the same game are kept
                couch.Handle(CouchCommand.Back);
                Assert.Equal("Zelda", couch.Current!.Title);
                couch.Handle(CouchCommand.PreviousTab);
                couch.Handle(CouchCommand.NextLetter);
                string kept = couch.Current!.Title;
                await couch.LoadAsync();
                Assert.Contains(couch.Tabs.Single(t => t.Name == Loc.T("Library")).Items, i => i.Title == "Paper Kingdoms");
                Assert.Equal(Loc.T("Library"), couch.Tabs[couch.Navigator.Tab].Name);
                Assert.Equal(kept, couch.Current!.Title);
            }
            finally
            {
                window.Close();
                MainWindowViewModel.Instance.ClosePopup();
                CouchUserControl.StartsHere = startedHere;
                InstallViewModel.Instance.InstalledGames.Remove(installed);
                foreach (Game game in games)
                    session.Server.RemoveGame(game.ID);
            }
        }

        [AvaloniaFact]
        public async Task TheLetters_FollowTheSortTitle_WhenATitleStartsWithThe()
        {
            await TestSession.GetAsync();
            // "The Witcher" is sorted at W by the server: the triggers find it there, not among the T
            var games = new List<Game>
            {
                new() { ID = 9790, Title = "Alan Wake", SortTitle = "alan wake", Type = GameType.WINDOWS_PORTABLE },
                new() { ID = 9791, Title = "The Talos Principle", SortTitle = "talos principle", Type = GameType.WINDOWS_PORTABLE },
                new() { ID = 9792, Title = "Tunic", SortTitle = "tunic", Type = GameType.WINDOWS_PORTABLE },
                new() { ID = 9793, Title = "The Witcher", SortTitle = "witcher", Type = GameType.WINDOWS_PORTABLE },
                new() { ID = 9794, Title = "Wolfenstein", SortTitle = "wolfenstein", Type = GameType.WINDOWS_PORTABLE },
            };
            var couch = new CouchUserControl();
            var window = new Window { Width = 1920, Height = 1080, Content = couch };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                couch.Settings = new CouchSettings();
                couch.Show(games, new List<RetroGame>());
                couch.Navigator.SelectTab(couch.Tabs.FindIndex(t => t.Name == Loc.T("Library")));
                Assert.Equal("Alan Wake", couch.Current!.Title);
                couch.Handle(CouchCommand.NextLetter);
                Assert.Equal("The Talos Principle", couch.Current!.Title);
                couch.Handle(CouchCommand.NextLetter);
                Assert.Equal("The Witcher", couch.Current!.Title);
                couch.Handle(CouchCommand.PreviousLetter);
                Assert.Equal("The Talos Principle", couch.Current!.Title);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public async Task TheInstalledTab_FollowsTheGamesInstalledAndRemoved_WhileTheModeIsShown()
        {
            var session = await TestSession.GetAsync();
            var games = new List<Game>
            {
                new() { ID = 9795, Title = "Brass Orbit", SortTitle = "brass orbit", Type = GameType.WINDOWS_PORTABLE },
                new() { ID = 9796, Title = "Cobalt Reach", SortTitle = "cobalt reach", Type = GameType.WINDOWS_PORTABLE },
                new() { ID = 9797, Title = "Dune Lantern", SortTitle = "dune lantern", Type = GameType.WINDOWS_PORTABLE },
            };
            var installed = new KeyValuePair<Game, string>(games[1], Path.Combine(session.LibraryRoot, "couch-follows"));
            var couch = new CouchUserControl();
            var window = new Window { Width = 1920, Height = 1080, Content = couch };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                couch.Settings = new CouchSettings();
                couch.Show(games, new List<RetroGame>());
                Assert.DoesNotContain(couch.Tabs, t => t.Name == Loc.T("Installed games"));
                couch.Navigator.Select(couch.Tabs.FindIndex(t => t.Name == Loc.T("Library")), 1);

                // Installed meanwhile (a download that ended): the tab comes, the same game stays selected
                InstallViewModel.Instance.InstalledGames.Add(installed);
                couch.Refresh();
                CouchTab here = couch.Tabs.Single(t => t.Name == Loc.T("Installed games"));
                Assert.Equal(new[] { "Cobalt Reach" }, here.Items.Select(i => i.Title));
                Assert.Equal(Loc.T("Library"), couch.Tabs[couch.Navigator.Tab].Name);
                Assert.Equal("Cobalt Reach", couch.Current!.Title);

                // Removed while its screen is shown from that tab: back to the list, not another game's screen
                couch.Navigator.Select(couch.Tabs.IndexOf(here), 0);
                couch.Handle(CouchCommand.Accept);
                Assert.True(couch.uiDetails.IsVisible);
                InstallViewModel.Instance.InstalledGames.Remove(installed);
                couch.Refresh();
                Assert.DoesNotContain(couch.Tabs, t => t.Name == Loc.T("Installed games"));
                Assert.Equal(CouchScreen.Home, couch.Navigator.Screen);
                Assert.False(couch.uiDetails.IsVisible);
            }
            finally
            {
                InstallViewModel.Instance.InstalledGames.Remove(installed);
                window.Close();
            }
        }

        [AvaloniaFact]
        public async Task EachTab_KeepsItsGame_WhenATabComesBeforeIt()
        {
            var session = await TestSession.GetAsync();
            var games = new List<Game>
            {
                new() { ID = 9798, Title = "Ember Coast", SortTitle = "ember coast", Type = GameType.WINDOWS_PORTABLE },
                new() { ID = 9799, Title = "Frost Gate", SortTitle = "frost gate", Type = GameType.WINDOWS_PORTABLE },
                new() { ID = 9800, Title = "Glass Moor", SortTitle = "glass moor", Type = GameType.WINDOWS_PORTABLE },
            };
            var installed = new KeyValuePair<Game, string>(games[0], Path.Combine(session.LibraryRoot, "couch-keeps"));
            var couch = new CouchUserControl();
            var window = new Window { Width = 1920, Height = 1080, Content = couch };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                couch.Settings = new CouchSettings();
                couch.Show(games, new List<RetroGame>());
                couch.Navigator.Select(couch.Tabs.FindIndex(t => t.Name == Loc.T("Library")), 2);
                couch.Navigator.SelectTab(0);

                // A first game installed: its tab comes before the library, which keeps "Glass Moor"
                InstallViewModel.Instance.InstalledGames.Add(installed);
                couch.Show(games, new List<RetroGame>());
                couch.Navigator.SelectTab(couch.Tabs.FindIndex(t => t.Name == Loc.T("Library")));
                Assert.Equal("Glass Moor", couch.Current!.Title);
            }
            finally
            {
                InstallViewModel.Instance.InstalledGames.Remove(installed);
                window.Close();
            }
        }

        [AvaloniaFact]
        public async Task TheSearch_IsTypedOnTheScreen_AndTheClientsQuestions_AreAnsweredWithTheGamepad()
        {
            var session = await TestSession.GetAsync();
            List<Game> games = Games(session);
            games[4].Metadata!.Trailers = new[] { "https://www.youtube.com/watch?v=gmA6MrX81z4" };
            var couch = new CouchUserControl();
            var window = new Window { Width = 1920, Height = 1080, Content = couch };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                couch.Settings = new CouchSettings();
                couch.Show(games, Retro());
                int tabs = couch.Tabs.Count;
                couch.Handle(CouchCommand.View);

                // Y: the keyboard, over the strip even when the grid was shown
                couch.Handle(CouchCommand.Search);
                Assert.True(couch.Searching && couch.uiKeyboard.IsVisible && couch.uiStrip.IsVisible && !couch.uiGrid.IsVisible);
                Assert.Equal(39, couch.Keys.Count);
                Assert.True(couch.Keys[0].IsSelected);
                Assert.Contains(couch.Hints, h => h.Label == "X" && h.Value == Loc.T("Erase"));
                // Down to K, typed with A: the games found are a tab of their own, shown at once
                couch.Handle(CouchCommand.Left);
                couch.Handle(CouchCommand.Down);
                Assert.True(couch.Keys[10].IsSelected);
                couch.Handle(CouchCommand.Accept);
                Assert.Equal("K", couch.Query);
                Assert.Equal(tabs + 1, couch.Tabs.Count);
                Assert.Equal(Loc.F("Search: {0}", "K"), couch.Tabs[^1].Name);
                Assert.True(couch.Tabs[^1].IsSelected);
                Assert.Contains(couch.Tabs[^1].Items, i => i.Title == "Paper Kingdoms");
                Assert.DoesNotContain(couch.Tabs[^1].Items, i => i.Title == "Zelda");
                // X erases: nothing searched, the library again
                couch.Handle(CouchCommand.View);
                Assert.Equal("", couch.Query);
                Assert.Equal(tabs, couch.Tabs.Count);
                Assert.Equal(Loc.T("Library"), couch.Tabs[couch.Navigator.Tab].Name);
                // The real keyboard types too; Y is the space
                Assert.True(couch.HandleKey(Key.Z));
                couch.Handle(CouchCommand.Search);
                Assert.Equal("Z ", couch.Query);
                Assert.True(couch.HandleKey(Key.Back));
                couch.Type("E");
                couch.Type("P");
                Assert.Equal("ZEP", couch.Query);
                Assert.Equal("Zephyr Run", couch.Current!.Title);
                Capture(window, "5-search");
                // B closes the keyboard, the games found stay: the grid is back, the game opens
                couch.Handle(CouchCommand.Back);
                Assert.True(!couch.Searching && !couch.uiKeyboard.IsVisible && couch.uiGrid.IsVisible);
                Assert.Equal("Zephyr Run", couch.Current!.Title);
                couch.Handle(CouchCommand.Accept);
                Assert.Equal("Zephyr Run", couch.uiDetailTitle.Text);
                // Y does nothing on a game's screen
                couch.Handle(CouchCommand.Search);
                Assert.False(couch.Searching);
                couch.Handle(CouchCommand.Back);

                // A game with a trailer has it among its actions
                couch.Handle(CouchCommand.PreviousTab);
                couch.Handle(CouchCommand.PreviousTab);
                Assert.Equal(Loc.T("Library"), couch.Tabs[couch.Navigator.Tab].Name);
                couch.Navigator.Select(couch.Navigator.Tab, 4);
                couch.Handle(CouchCommand.Accept);
                Assert.Equal("Lumen Trail", couch.uiDetailTitle.Text);
                Assert.Equal(new[] { Loc.T("Install and play"), Loc.T("Install"), Loc.T("Watch the trailer"), Loc.T("Add to the favorites") }, couch.Actions.Select(a => a.Text));

                // Its star: the game is bookmarked on the server and gets the Favorites tab; the screen stays on it
                session.Server.Bookmarks.Clear();
                await couch.Actions.Single(a => a.Text == Loc.T("Add to the favorites")).Run();
                Assert.Equal(new[] { $"POST {games[4].ID}" }, session.Server.Bookmarks);
                Assert.Equal(new[] { "Lumen Trail" }, couch.Tabs.Single(t => t.Name == Loc.T("Favorites")).Items.Select(i => i.Title));
                Assert.Equal("Lumen Trail", couch.uiDetailTitle.Text);
                Assert.Contains(couch.Actions, a => a.Text == Loc.T("Remove from the favorites"));
                await couch.Actions.Single(a => a.Text == Loc.T("Remove from the favorites")).Run();
                Assert.Equal($"DELETE {games[4].ID}", session.Server.Bookmarks.Last());
                Assert.DoesNotContain(couch.Tabs, t => t.Name == Loc.T("Favorites"));

                // "A game at random" of the menu: a game's screen, among the library when nothing is installed
                couch.Handle(CouchCommand.Menu);
                Assert.Contains(couch.MenuLines, l => l.Text == Loc.T("A game at random"));
                couch.OpenRandom(new Random(3), spin: false);
                Assert.True(couch.uiDetails.IsVisible && !couch.uiMenu.IsVisible);
                Assert.Contains(couch.uiDetailTitle.Text, Titles);

                // A question of the client: left and right choose the answer, A gives it, B says no; nothing moves under it
                Task<bool> asked = DialogService.ConfirmAsync("Delete it?");
                Assert.NotNull(DialogService.Current);
                Assert.Equal(new[] { Loc.T("No"), Loc.T("Yes") }, DialogService.Current!.Texts);
                Assert.Equal(Loc.T("Yes"), DialogService.Current.Focused);
                couch.Handle(CouchCommand.Down);
                Assert.True(couch.Actions[0].IsSelected);
                couch.Handle(CouchCommand.Left);
                Assert.Equal(Loc.T("No"), DialogService.Current.Focused);
                couch.Handle(CouchCommand.Left);
                couch.Handle(CouchCommand.Right);
                couch.Handle(CouchCommand.Accept);
                Assert.True(await asked);
                Assert.Null(DialogService.Current);
                asked = DialogService.ConfirmAsync("Delete it?");
                couch.Handle(CouchCommand.Back);
                Assert.False(await asked);
                // A question with one answer: B gives it
                Task told = DialogService.ShowInfoAsync("Done.");
                couch.Handle(CouchCommand.Back);
                await told;
                Assert.Null(DialogService.Current);
                Assert.True(couch.uiDetails.IsVisible);
            }
            finally
            {
                while (DialogService.Current is { } left)
                    left.Cancel();
                window.Close();
                foreach (Game game in games)
                    session.Server.RemoveGame(game.ID);
            }
        }

        [AvaloniaFact]
        public async Task TheClientsWindows_AreMovedThroughWithTheGamepad_AndTheirTextsTypedOnTheScreen()
        {
            var session = await TestSession.GetAsync();
            var window = session.Window;
            WindowState before = window.WindowState;
            bool sounds = CouchUserControl.SoundsOn;
            try
            {
                window.EnterCouch();
                var couch = window.FindControl<CouchUserControl>("uiCouch")!;
                var keyboard = window.FindControl<CouchKeyboard>("uiCouchKeys")!;
                Assert.Same(keyboard, couch.TextKeyboard);

                // A window of the client: a name, a box to check, a choice, a slider and two buttons
                var name = new TextBox { Width = 300 };
                var check = new CheckBox { Content = "Cloud saves" };
                var choice = new ComboBox { ItemsSource = new[] { "Low", "Medium", "High" }, SelectedIndex = 0, Width = 300 };
                var slider = new Slider { Minimum = 0, Maximum = 10, SmallChange = 1, Value = 5, Width = 300 };
                int cancelled = 0, saved = 0;
                var cancel = new Button { Content = "Cancel" };
                var save = new Button { Content = "Save" };
                cancel.Click += (_, _) => cancelled++;
                save.Click += (_, _) => saved++;
                var popup = new StackPanel
                {
                    Width = 420, Spacing = 14, Margin = new Thickness(30),
                    Children = { name, check, choice, slider, new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10, Children = { cancel, save } } },
                };
                MainWindowViewModel.Instance.OpenPopup(popup);
                void Settle()
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs();
                }
                Settle();
                Assert.Equal(new Control[] { name, check, choice, slider, cancel, save }, CouchFocus.Candidates(popup));

                // The cross reaches the first control, A opens the keyboard over the window
                couch.Played.Clear();
                couch.Handle(CouchCommand.Down);
                Assert.Same(name, CouchFocus.Focused(popup));
                couch.Handle(CouchCommand.Accept);
                Assert.Same(name, couch.Editing);
                Assert.True(keyboard.IsVisible);
                Assert.Equal(45, keyboard.Keys.Count);
                // "a", then the capitals with the shift key, a space with Y, erased with X
                couch.Handle(CouchCommand.Accept);
                Assert.Equal("a", name.Text);
                int shift = keyboard.Keys.FindIndex(k => k.Text == CouchKeyboard.Shift);
                for (int row = 0; row < 4; row++)
                    couch.Handle(CouchCommand.Down);
                Assert.Equal(shift, keyboard.Selected);
                couch.Handle(CouchCommand.Accept);
                Assert.Equal("A", keyboard.Keys[0].Text);
                for (int row = 0; row < 4; row++)
                    couch.Handle(CouchCommand.Up);
                couch.Handle(CouchCommand.Right);
                couch.Handle(CouchCommand.Accept);
                couch.Handle(CouchCommand.Search);
                Assert.Equal("aB ", name.Text);
                couch.Handle(CouchCommand.View);
                Assert.Equal("aB", name.Text);
                Assert.Equal("aB", keyboard.Shown);
                // The other page: the accents and the signs on the same keys, in the case chosen
                int page = keyboard.Keys.FindIndex(k => k.Text == CouchKeyboard.Signs2);
                Assert.Equal(1, keyboard.Selected);
                for (int row = 0; row < 4; row++)
                    couch.Handle(CouchCommand.Down);
                Assert.Equal(page, keyboard.Selected);
                couch.Handle(CouchCommand.Accept);
                Assert.Equal(("É", "!", CouchKeyboard.Abc), (keyboard.Keys[0].Text, keyboard.Keys[16].Text, keyboard.Keys[page].Text));
                for (int row = 0; row < 4; row++)
                    couch.Handle(CouchCommand.Up);
                couch.Handle(CouchCommand.Accept);
                Assert.Equal("aBÈ", name.Text);
                couch.Handle(CouchCommand.View);
                // B closes the keyboard, not the window
                couch.Handle(CouchCommand.Back);
                Assert.Null(couch.Editing);
                Assert.False(keyboard.IsVisible);
                Assert.Same(popup, MainWindowViewModel.Instance.Popup);

                // Down from control to control: the box is checked, the choice and the slider change with left and right
                couch.Handle(CouchCommand.Down);
                Assert.Same(check, CouchFocus.Focused(popup));
                couch.Handle(CouchCommand.Accept);
                Assert.True(check.IsChecked);
                couch.Handle(CouchCommand.Down);
                couch.Handle(CouchCommand.Right);
                couch.Handle(CouchCommand.Right);
                couch.Handle(CouchCommand.Right);
                couch.Handle(CouchCommand.Left);
                Assert.Equal(1, choice.SelectedIndex);
                // A opens its list: up and down go through it without leaving it, A closes it on the choice
                couch.Handle(CouchCommand.Accept);
                Assert.True(choice.IsDropDownOpen);
                couch.Handle(CouchCommand.Down);
                couch.Handle(CouchCommand.Down);
                couch.Handle(CouchCommand.Left);
                Assert.Equal(2, choice.SelectedIndex);
                couch.Handle(CouchCommand.Up);
                couch.Handle(CouchCommand.Accept);
                Assert.False(choice.IsDropDownOpen);
                Assert.Equal(1, choice.SelectedIndex);
                Settle();
                Assert.Same(choice, CouchFocus.Focused(popup));
                couch.Handle(CouchCommand.Down);
                couch.Handle(CouchCommand.Right);
                Assert.Equal(6, slider.Value);
                // The buttons side by side
                couch.Handle(CouchCommand.Down);
                Assert.Same(cancel, CouchFocus.Focused(popup));
                couch.Handle(CouchCommand.Right);
                Assert.Same(save, CouchFocus.Focused(popup));
                couch.Handle(CouchCommand.Accept);
                Assert.Equal((0, 1), (cancelled, saved));
                couch.Handle(CouchCommand.Up);
                Assert.Same(slider, CouchFocus.Focused(popup));
                // Each of them had its sound
                Assert.Contains(CouchSound.Move, couch.Played);
                Assert.Contains(CouchSound.Accept, couch.Played);
                couch.Handle(CouchCommand.Back);
                Assert.Null(MainWindowViewModel.Instance.Popup);
                Assert.Equal(CouchSound.Back, couch.Played[^1]);

                // A question with a text to type: the same keyboard, then its answer
                Task<string?> asked = window.ShowInputAsync("Collection", "Its name");
                Settle();
                TextBox input = Assert.IsType<TextBox>(DialogService.Current!.Input);
                couch.Handle(CouchCommand.Up);
                Assert.Same(input, CouchFocus.Focused(DialogService.Current.Scope));
                couch.Handle(CouchCommand.Accept);
                Assert.Same(input, couch.Editing);
                couch.Handle(CouchCommand.Right);
                couch.Handle(CouchCommand.Right);
                couch.Handle(CouchCommand.Accept);
                Assert.Equal("c", input.Text);
                // OK, the keyboard's last key
                keyboard.Handle(CouchCommand.Down);
                keyboard.Handle(CouchCommand.Down);
                keyboard.Handle(CouchCommand.Down);
                keyboard.Handle(CouchCommand.Down);
                keyboard.Handle(CouchCommand.Right);
                keyboard.Handle(CouchCommand.Right);
                Assert.Equal(CouchKeyboard.Done, keyboard.Keys[keyboard.Selected].Text);
                couch.Handle(CouchCommand.Accept);
                Assert.Null(couch.Editing);
                couch.Handle(CouchCommand.Down);
                couch.Handle(CouchCommand.Right);
                couch.Handle(CouchCommand.Right);
                couch.Handle(CouchCommand.Accept);
                Assert.Equal("c", await asked);
                Assert.Null(DialogService.Current);

                // The sounds are turned off in the menu
                CouchUserControl.SoundsOn = true;
                couch.ShowSettingsPage("sounds");
                CouchLine line = couch.MenuLines.Single(l => l.Text == Loc.T("Sounds: on"));
                await line.Run();
                Assert.False(CouchUserControl.SoundsOn);
                Assert.Contains(couch.MenuLines, l => l.Text == Loc.T("Sounds: off"));
                couch.Handle(CouchCommand.Back);
            }
            finally
            {
                while (DialogService.Current is { } left)
                    left.Cancel();
                MainWindowViewModel.Instance.ClosePopup();
                CouchUserControl.SoundsOn = sounds;
                window.LeaveCouch();
                window.WindowState = before;
            }
        }

        [AvaloniaFact]
        public async Task TheHome_TheMenus_AndTheSettings_OfTheLivingRoom()
        {
            var session = await TestSession.GetAsync();
            List<Game> games = Games(session);
            // The library reads the play times when it opens: after it, these ones stay
            DateTime settled = DateTime.UtcNow.AddSeconds(1.5);
            await TestSession.WaitUntil(() => DateTime.UtcNow > settled, TimeSpan.FromSeconds(10), "the library to load");
            var records = InstallViewModel.Instance.PlayRecords.Values.ToList();
            InstallViewModel.Instance.SetPlayRecords(new[]
            {
                new GameVault.Core.Library.PlayRecord(games[1].ID, DateTime.UtcNow.AddDays(-1), 90),
                new GameVault.Core.Library.PlayRecord(games[3].ID, DateTime.UtcNow.AddDays(-5), 300),
            });
            var started = new List<string>();
            var (start, open) = (CouchSystem.Start, CouchSystem.Open);
            CouchSystem.Start = (file, arguments) => started.Add($"{file} {string.Join(" ", arguments)}");
            CouchSystem.Open = app => started.Add("open " + app.Path);
            var couch = new CouchUserControl();
            var window = new Window { Width = 1920, Height = 1080, Content = couch };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                couch.Settings = new CouchSettings();
                couch.Show(games, Retro());

                // The home: tiles instead of games; "Resume" opens the screen of the game played last
                Assert.Equal(new[] { "tile:resume", "tile:random", "tile:challenge", "tile:friends", "tile:downloads", "tile:news", "tile:stats" }, couch.Tabs[0].Items.Select(i => i.Key));
                Assert.Equal((Loc.T("Resume"), "Cinder Vale"), (couch.HeroTitle, couch.Current!.Tile!.Subtitle));
                Assert.Empty(couch.Stats);
                Capture(window, "6-home");
                couch.Handle(CouchCommand.Accept);
                Assert.True(couch.uiDetails.IsVisible);
                Assert.Equal("Cinder Vale", couch.uiDetailTitle.Text);
                Assert.Equal(Loc.T("Recent"), couch.Tabs[couch.Navigator.Tab].Name);
                couch.Handle(CouchCommand.Back);
                couch.Handle(CouchCommand.PreviousTab);
                // An application added to the home is a tile that opens it
                couch.AddApp(new CouchApp("Kodi", "/opt/kodi/kodi"));
                int kodi = couch.Tabs[0].Items.FindIndex(i => i.Title == "Kodi");
                couch.Navigator.Select(0, kodi);
                couch.Handle(CouchCommand.Accept);
                Assert.Equal(new[] { "open /opt/kodi/kodi" }, started);
                Assert.True(couch.uiHome.IsVisible);

                // The left stick's click: the order and the filters of the lists of games
                int library = couch.Tabs.FindIndex(t => t.Name == Loc.T("Library"));
                couch.Handle(CouchCommand.Filter);
                Assert.Equal(CouchMenu.Filter, couch.Menu);
                Assert.Equal(Loc.F("Order: {0}", Loc.T("name")), couch.MenuLines[0].Text);
                await couch.MenuLines[0].Run();
                await couch.MenuLines[0].Run();
                Assert.Equal(CouchSettings.ByTime, couch.Settings.Sort);
                Assert.True(couch.uiMenu.IsVisible);
                Assert.Equal(new[] { "Iron Meridian", "Cinder Vale", "Astral Drift" }, couch.Tabs[library].Items.Take(3).Select(i => i.Title));
                await couch.MenuLines.Single(l => l.Text == Loc.F("Genre: {0}", Loc.T("all"))).Run();
                Assert.Equal("Adventure", couch.Settings.Genre);
                Assert.Equal(new[] { "Astral Drift", "Hollow Tides", "Lumen Trail", "Paper Kingdoms" }, couch.Tabs[library].Items.Select(i => i.Title));
                await couch.MenuLines.Single(l => l.Text == Loc.T("Show everything")).Run();
                Assert.False(couch.Settings.IsFiltered);
                Assert.Equal(Titles, couch.Tabs[library].Items.Select(i => i.Title));
                couch.Handle(CouchCommand.Back);
                Assert.False(couch.uiMenu.IsVisible);

                // Back (Select): the quick access; the PC's power asks first
                started.Clear();
                couch.Handle(CouchCommand.Quick);
                Assert.Equal(CouchMenu.Quick, couch.Menu);
                Assert.Equal(Loc.T("Quick access"), couch.uiMenuTitle.Text);
                await couch.MenuLines[0].Run();
                if (OperatingSystem.IsLinux())
                    Assert.Equal(new[] { "pactl set-sink-volume @DEFAULT_SINK@ +5%" }, started);
                started.Clear();
                Task off = couch.MenuLines.Single(l => l.Text == Loc.T("Shut down")).Run();
                Assert.NotNull(DialogService.Current);
                couch.Handle(CouchCommand.Back);
                await off;
                Assert.Empty(started);
                off = couch.MenuLines.Single(l => l.Text == Loc.T("Shut down")).Run();
                couch.Handle(CouchCommand.Accept);
                await off;
                var (file, arguments) = CouchSystem.PowerCommand(CouchPower.ShutDown, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());
                Assert.Equal(new[] { $"{file} {string.Join(" ", arguments)}" }, started);
                Assert.Equal(("shutdown.exe", "/s /t 0"), (CouchSystem.PowerCommand(CouchPower.ShutDown, true, false).File, string.Join(" ", CouchSystem.PowerCommand(CouchPower.ShutDown, true, false).Arguments)));
                Assert.Equal("systemctl suspend", "systemctl " + CouchSystem.PowerCommand(CouchPower.Sleep, false, false).Arguments[0]);
                Assert.Equal("pmset", CouchSystem.PowerCommand(CouchPower.Sleep, false, true).File);
                Assert.Null(CouchSystem.VolumeCommand(1, true, false));
                couch.Handle(CouchCommand.Back);

                // The settings: the buttons named as the player's gamepad, the color, the numbers
                couch.ShowSettingsPage("interface");
                // By default, the gamepad in the player's hands: a PlayStation one names its buttons at once
                Assert.Contains(couch.MenuLines, l => l.Text == Loc.F("Buttons shown: {0}", Loc.F("as the gamepad used ({0})", "Xbox")));
                couch.UsePad(CouchSettings.PlayStation);
                Assert.Contains(couch.Hints, h => h.Label == "✕");
                Assert.Equal("L1", couch.uiTabLeftKey.Text);
                Assert.Contains(couch.MenuLines, l => l.Text == Loc.F("Buttons shown: {0}", Loc.F("as the gamepad used ({0})", "PlayStation")));
                couch.UsePad(CouchSettings.Xbox);
                Assert.Contains(couch.Hints, h => h.Label == "A");
                Assert.Equal("LB", couch.uiTabLeftKey.Text);
                // Chosen: they stay, whatever the gamepad
                await couch.MenuLines.Single(l => l.Text.StartsWith(Loc.F("Buttons shown: {0}", ""))).Run();
                Assert.Equal(CouchSettings.Xbox, couch.Settings.Buttons);
                await couch.MenuLines.Single(l => l.Text == Loc.F("Buttons shown: {0}", "Xbox")).Run();
                Assert.Equal(CouchSettings.PlayStation, couch.Settings.Buttons);
                couch.UsePad(CouchSettings.Switch);
                Assert.Contains(couch.Hints, h => h.Label == "✕");
                Assert.Contains(couch.MenuLines, l => l.Text == Loc.F("Buttons shown: {0}", "PlayStation"));
                couch.Settings.DynamicColors = false;
                couch.ShowSettingsPage("look");
                await couch.MenuLines.Single(l => l.Text == Loc.F("Color: {0}", Loc.T("the theme's"))).Run();
                Assert.Equal(Avalonia.Media.Color.Parse("#7C6BFF"), couch.AccentShown);
                couch.Handle(CouchCommand.Back);
                couch.Handle(CouchCommand.Back);
                couch.Handle(CouchCommand.Back);
                couch.Navigator.Select(library, 0);
                couch.Handle(CouchCommand.NextTab);
                couch.Handle(CouchCommand.PreviousTab);
                Assert.Contains(couch.Hints, h => h.Label == "L1 R1");
                Assert.Contains(couch.Hints, h => h.Label == "L3" && h.Value == Loc.T("Filters"));
                Assert.True(couch.uiStatsCard.IsVisible);
                couch.ShowSettingsPage("library");
                await couch.MenuLines.Single(l => l.Text == Loc.F("{0}: {1}", Loc.T("Numbers of the selected game"), Loc.T("Yes"))).Run();
                Assert.False(couch.uiStatsCard.IsVisible);
                // The statistics, from the menu
                couch.ShowMenu(CouchMenu.Stats);
                Assert.Contains(couch.MenuLines, l => l.Text.StartsWith("1. Iron Meridian"));
                Assert.Contains(couch.MenuLines, l => l.Text == Loc.F("Library: {0} · Consoles and retro: {1}", 8, 2));
                couch.Handle(CouchCommand.Back);
                Assert.Equal(CouchMenu.Main, couch.Menu);
                couch.Handle(CouchCommand.Back);

                // Nothing touched for a while: the games one after the other, until a button
                string selected = couch.Current!.Title;
                couch.CheckIdle(TimeSpan.FromMinutes(4));
                Assert.Equal(CouchUserControl.Curtain.None, couch.CurtainShown);
                couch.CheckIdle(TimeSpan.FromMinutes(6), new Random(1));
                Assert.Equal(CouchUserControl.Curtain.Saver, couch.CurtainShown);
                Assert.True(couch.uiCurtain.IsVisible);
                Assert.Contains(couch.CurtainTitle, Titles);
                Capture(window, "7-saver");
                couch.Handle(CouchCommand.Right);
                Assert.Equal(CouchUserControl.Curtain.None, couch.CurtainShown);
                Assert.Equal(selected, couch.Current!.Title);
                couch.Settings.ScreenSaverMinutes = 0;
                couch.CheckIdle(TimeSpan.FromHours(2));
                Assert.Equal(CouchUserControl.Curtain.None, couch.CurtainShown);
                // A game that starts shows its picture
                couch.ShowSplash(couch.Current);
                Assert.Equal((CouchUserControl.Curtain.Splash, selected), (couch.CurtainShown, couch.CurtainTitle));
                couch.Handle(CouchCommand.Accept);
                Assert.False(couch.uiCurtain.IsVisible);
                Assert.True(couch.uiHome.IsVisible);

                // The menu over a game: its lines with the gamepad, whatever is behind
                int resumed = 0, quit = 0;
                couch.OpenOverlay("Hades", () => resumed++, () => quit++);
                Assert.NotNull(couch.Overlay);
                couch.Handle(CouchCommand.Right);
                Assert.Equal(selected, couch.Current!.Title);
                couch.Handle(CouchCommand.Down);
                Assert.Equal(1, couch.Overlay!.Selected);
                couch.Handle(CouchCommand.Accept);
                Assert.Equal((0, 1), (resumed, quit));
                Assert.Null(couch.Overlay);
                couch.OpenOverlay("Hades", () => resumed++, () => quit++);
                couch.Handle(CouchCommand.Back);
                Assert.Equal((1, 1), (resumed, quit));
                Assert.Null(couch.Overlay);

                // The mouse with the right stick: nothing near its rest, fast at its end, what is under a pixel kept
                (double X, double Y) rest = default;
                Assert.Equal((0, 0), CouchUserControl.MouseStep((0.1, 0.05), ref rest));
                Assert.Equal((23, 0), CouchUserControl.MouseStep((1, 0), ref rest));
                rest = default;
                var slow = Enumerable.Range(0, 4).Select(_ => CouchUserControl.MouseStep((0, 0.3), ref rest).Y).ToList();
                Assert.InRange(slow.Sum(), 6, 9);
                Assert.All(slow, step => Assert.InRange(step, 1, 3));
            }
            finally
            {
                while (DialogService.Current is { } left)
                    left.Cancel();
                (CouchSystem.Start, CouchSystem.Open) = (start, open);
                couch.Settings = new CouchSettings();
                couch.SaveSettings();
                window.Close();
                InstallViewModel.Instance.SetPlayRecords(records);
                foreach (Game game in games)
                    session.Server.RemoveGame(game.ID);
            }
        }

        [AvaloniaFact]
        public async Task TheSelectedGame_ShowsItsLogo_InsteadOfItsName_WhenTheServerHasOne()
        {
            var session = await TestSession.GetAsync();
            List<Game> games = Games(session);
            // COUCH_LOGO: a real logo for the screenshot; else a small white picture
            byte[] logo = Environment.GetEnvironmentVariable("COUCH_LOGO") is { Length: > 0 } file && File.Exists(file) ? File.ReadAllBytes(file) : Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAgAAAADCAYAAACuyE5IAAAAFUlEQVR4nGP8////fwY8gAmfJFEKAIIZBALOunCsAAAAAElFTkSuQmCC");
            SanctuaryService.Current = session.Server.Url;
            GameLogoService.Forget(files: true);
            session.Server.Logos[$"/logos/pc/{games[2].ID}"] = logo;
            session.Server.Logos["/retro/games/40/logo"] = logo;
            session.Server.Logos["/retro/games/40/hero"] = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAJCAIAAAC0SDtlAAAAFUlEQVR4nGP438BAEiJN9agGWmkAACwH13FB5UAoAAAAAElFTkSuQmCC");
            var couch = new CouchUserControl();
            var window = new Window { Width = 1920, Height = 1080, Content = couch };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                couch.Settings = new CouchSettings();
                couch.Show(games, Retro());
                int library = couch.Tabs.FindIndex(t => t.Name == Loc.T("Library"));
                couch.Navigator.Select(library, 1);
                couch.Handle(CouchCommand.PreviousTab);
                couch.Handle(CouchCommand.NextTab);
                // No logo for this game: its name
                Assert.Equal("Cinder Vale", couch.HeroTitle);
                Assert.True(couch.uiTitle.IsVisible && !couch.LogoShown);
                // The next one has a logo: it takes the place of the name, a moment after the game is selected
                couch.Handle(CouchCommand.Right);
                Assert.True(couch.uiTitle.IsVisible && !couch.LogoShown);
                await TestSession.WaitUntil(() => couch.LogoShown, TimeSpan.FromSeconds(10), "the game's logo");
                Assert.Equal("Hollow Tides", couch.HeroTitle);
                Assert.False(couch.uiTitle.IsVisible);
                Assert.Contains($"GET /logos/pc/{games[2].ID}", session.Server.Requests);
                Capture(window, "8-logo");
                // Back on the game without one: its name again, at once
                couch.Handle(CouchCommand.Left);
                Assert.True(couch.uiTitle.IsVisible && !couch.LogoShown);
                DateTime waited = DateTime.UtcNow.AddMilliseconds(600);
                await TestSession.WaitUntil(() => DateTime.UtcNow > waited, TimeSpan.FromSeconds(5), "a moment");
                Assert.True(couch.uiTitle.IsVisible && !couch.LogoShown);
                // Asked once by game: the second time the logo is there without the server
                int asked = session.Server.Requests.Count(r => r.Contains("/logos/pc/"));
                couch.Handle(CouchCommand.Right);
                await TestSession.WaitUntil(() => couch.LogoShown, TimeSpan.FromSeconds(10), "the game's logo again");
                Assert.Equal(asked, session.Server.Requests.Count(r => r.Contains("/logos/pc/")));

                // A retro game's logo is RomM's
                couch.Handle(CouchCommand.NextTab);
                couch.Handle(CouchCommand.Right);
                Assert.Equal("Zelda", couch.HeroTitle);
                await TestSession.WaitUntil(() => couch.LogoShown, TimeSpan.FromSeconds(10), "the retro game's logo");
                Assert.Contains("GET /retro/games/40/logo", session.Server.Requests);
                // Behind it, its large picture (fanart, hero) rather than a screenshot, as GameVault's backgrounds
                // (pictures are all 1 x 1 without a screen: the hero is told by being the one its loader gives)
                var hero = await GameLogoService.HeroOfAsync(couch.Current!.Retro!);
                Assert.NotNull(hero);
                await TestSession.WaitUntil(() => ReferenceEquals(couch.uiBackRetro.Source, hero), TimeSpan.FromSeconds(10), "the retro game's hero");
                Assert.Contains("GET /retro/games/40/hero", session.Server.Requests);

                // Turned off in the settings: the names
                couch.Settings.Logos = false;
                couch.Handle(CouchCommand.Left);
                couch.Handle(CouchCommand.Right);
                waited = DateTime.UtcNow.AddMilliseconds(600);
                await TestSession.WaitUntil(() => DateTime.UtcNow > waited, TimeSpan.FromSeconds(5), "a moment");
                Assert.True(couch.uiTitle.IsVisible && !couch.LogoShown);
            }
            finally
            {
                window.Close();
                SanctuaryService.Current = SanctuaryService.Url;
                GameLogoService.Forget();
                session.Server.Logos.Clear();
                foreach (Game game in games)
                    session.Server.RemoveGame(game.ID);
            }
        }

        [AvaloniaFact]
        public async Task TheWindow_GoesToTheLivingRoomModeAndBack_WithItsKey()
        {
            var session = await TestSession.GetAsync();
            var window = session.Window;
            WindowState before = window.WindowState;
            try
            {
                window.EnterCouch();
                Assert.True(window.IsCouchShown);
                Assert.Equal(WindowState.FullScreen, window.WindowState);
                Assert.False(window.FindControl<Grid>("uiDesktop")!.IsVisible);
                // The client's windows are larger there, as much as the screen is taller than them; at their size on the desktop
                var zoom = window.FindControl<LayoutTransformControl>("uiPopupZoom")!;
                double expected = Math.Clamp(window.Bounds.Height / 820, 1, 1.7);
                Assert.Equal(expected > 1.01, zoom.LayoutTransform != null);
                window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
                Assert.False(window.IsCouchShown);
                Assert.Null(zoom.LayoutTransform);
                Assert.True(window.FindControl<Grid>("uiDesktop")!.IsVisible);
                Assert.Equal(before, window.WindowState);
                window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
                Assert.True(window.IsCouchShown);

                // Opened at the client's start: the login screen, with the profile signed in, until A comes in
                window.LeaveCouch();
                window.EnterCouch(welcome: true);
                var couch = window.FindControl<CouchUserControl>("uiCouch")!;
                Assert.True(couch.LoginShown);
                Assert.Equal("admin", couch.LoginProfiles[0].Name);
                Assert.True(couch.LoginProfiles[0].IsCurrent && couch.LoginProfiles[0].IsSelected);
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.False(couch.LoginShown);
                Assert.True(window.IsCouchShown);
            }
            finally
            {
                window.LeaveCouch();
                window.WindowState = before;
            }
        }

        [AvaloniaFact]
        public async Task TheGames_AreAlsoAList_AndARetroGamesAchievements_AreListedFromItsScreen()
        {
            var session = await TestSession.GetAsync();
            List<Game> games = Games(session);
            SanctuaryService.Current = session.Server.Url;
            session.Server.RommLinkable = true;
            RommAccount.ForgetUrl();
            var couch = new CouchUserControl();
            var window = new Window { Width = 1920, Height = 1080, Content = couch };
            try
            {
                await new RetroSettingsUserControl().LinkRommAsync(open: false);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                couch.Settings = new CouchSettings();
                couch.Show(games, Retro());

                // The library as a list of names: after the strip and the grid, up and down
                while (couch.Tabs[couch.Navigator.Tab].Name != Loc.T("Library"))
                    couch.Handle(CouchCommand.NextTab);
                couch.Handle(CouchCommand.View);
                couch.Handle(CouchCommand.View);
                Assert.True(couch.Navigator.IsList && couch.uiListBox.IsVisible && !couch.uiStrip.IsVisible && !couch.uiGrid.IsVisible);
                Assert.True(couch.uiHero.IsVisible);
                Assert.Contains(couch.Hints, h => h.Label == "X" && h.Value == Loc.T("Strip"));
                couch.Handle(CouchCommand.Down);
                couch.Handle(CouchCommand.Down);
                Assert.Equal("Hollow Tides", couch.Current!.Title);
                Assert.Equal("Hollow Tides", couch.HeroTitle);
                couch.Handle(CouchCommand.Right);
                Assert.Equal("Hollow Tides", couch.Current.Title);
                Capture(window, "list");

                // A retro game's screen: its achievements, once RomM gave them, one more action
                couch.Handle(CouchCommand.NextTab);
                couch.Handle(CouchCommand.Down);
                Assert.Equal("Zelda", couch.Current.Title);
                couch.Handle(CouchCommand.Accept);
                string action = Loc.F("Achievements ({0})", Loc.F("{0} of {1}", 1, 2));
                await TestSession.WaitUntil(() => couch.Actions.Any(a => a.Text == action), TimeSpan.FromSeconds(10), "the achievements' action");
                couch.Navigator.SelectLine(couch.Actions.FindIndex(a => a.Text == action));
                couch.Handle(CouchCommand.Accept);
                await TestSession.WaitUntil(() => couch.Menu == CouchMenu.Achievements, TimeSpan.FromSeconds(5), "the achievements");
                // What was earned first, then the rest, greyed; the game's screen stays behind
                Assert.Equal(4, couch.MenuLines.Count);
                Assert.Equal(1, couch.MenuLines[1].Opacity);
                Assert.Equal(0.5, couch.MenuLines[2].Opacity);
                Assert.True(couch.MenuLines[1].HasDetail && !couch.MenuLines[0].HasDetail);
                Assert.True(couch.uiDetails.IsVisible && couch.uiMenu.IsVisible);
                Capture(window, "achievements");

                // B: back to the same game's screen, as it was
                couch.Handle(CouchCommand.Back);
                Assert.Equal(CouchScreen.Details, couch.Navigator.Screen);
                Assert.Equal("Zelda", couch.uiDetailTitle.Text);
                Assert.Contains(couch.Actions, a => a.Text == action);
            }
            finally
            {
                window.Close();
                RommAccount.Unlink();
                RommAccount.ForgetUrl();
                session.Server.RommLinkable = false;
                SanctuaryService.Current = SanctuaryService.Url;
                foreach (Game game in games)
                    session.Server.RemoveGame(game.ID);
            }
        }

        [AvaloniaFact]
        public async Task AGamesTrailer_PlaysInItsScreenAfterAMoment_AndStopsUnderTheMenu()
        {
            var session = await TestSession.GetAsync();
            List<Game> games = Games(session);
            games[0].Metadata!.Trailers = new[] { "https://videos.invalid/astral.mp4" };
            var couch = new CouchUserControl();
            var window = new Window { Width = 1920, Height = 1080, Content = couch };
            try
            {
                window.Show();
                window.Activate();
                Dispatcher.UIThread.RunJobs();
                couch.Settings = new CouchSettings();
                couch.Show(games, Retro());
                while (couch.Tabs[couch.Navigator.Tab].Name != Loc.T("Library"))
                    couch.Handle(CouchCommand.NextTab);
                Assert.Equal("Astral Drift", couch.Current!.Title);
                Assert.Equal("https://videos.invalid/astral.mp4", couch.TrailerOf(couch.Current));
                Assert.Null(couch.TrailerOf(couch.Tabs[couch.Navigator.Tab].Items[1]));

                // Not at once: a player passing through the games starts nothing
                couch.Handle(CouchCommand.Accept);
                couch.KeepTrailer();
                Assert.False(couch.TrailerShown);
                await Task.Delay(2100);
                couch.KeepTrailer();
                await TestSession.WaitUntil(() => couch.TrailerShown, TimeSpan.FromSeconds(5), "the trailer");

                // The menu over it: stopped; back to the game: played again
                couch.Handle(CouchCommand.Menu);
                couch.KeepTrailer();
                Assert.False(couch.TrailerShown);
                couch.Handle(CouchCommand.Back);
                await Task.Delay(100);
                couch.KeepTrailer();
                await TestSession.WaitUntil(() => couch.TrailerShown, TimeSpan.FromSeconds(5), "the trailer again");

                // The next game has none; the setting turns them off
                couch.Handle(CouchCommand.NextTab);
                Assert.False(couch.TrailerShown);
                couch.Settings.Trailers = false;
                Assert.Null(couch.TrailerOf(couch.Tabs[couch.Navigator.Tab].Items[0]));
                // Back to the list: the web player itself goes (its processes keep their memory while it exists)
                Assert.True(couch.HasTrailerPlayer);
                couch.Handle(CouchCommand.Back);
                Assert.False(couch.HasTrailerPlayer);
            }
            finally
            {
                window.Close();
                foreach (Game game in games)
                    session.Server.RemoveGame(game.ID);
            }
        }

        [AvaloniaFact]
        public async Task TheDownloads_TheChallenge_AndTheFriends_AreReadInTheMode_NotOnTheDesktop()
        {
            var session = await TestSession.GetAsync();
            List<Game> games = Games(session);
            SanctuaryService.Current = session.Server.Url;
            session.Server.ChallengeState = new
            {
                week = "2026-W46", startsAt = DateTime.UtcNow.AddDays(-2), endsAt = DateTime.UtcNow.AddDays(5),
                challenge = new { kind = "hours", goal = 300 },
                board = new object[]
                {
                    new { userId = 2, username = "Léa", value = 320, doneAt = (DateTime?)DateTime.UtcNow.AddHours(-3) },
                    new { userId = 1, username = "admin", value = 90, doneAt = (DateTime?)null },
                },
                allTime = Array.Empty<object>(), past = Array.Empty<object>(),
            };
            var download = new RetroDownload(Retro()[1], Path.Combine(Path.GetTempPath(), "gv-couch-download-" + Guid.NewGuid().ToString("N")));
            DownloadsViewModel.Instance.RetroDownloads.Add(download);
            var couch = new CouchUserControl();
            var window = new Window { Width = 1920, Height = 1080, Content = couch };
            bool left = false;
            couch.LeaveRequested += () => left = true;
            async Task OpenTile(string key, CouchMenu menu)
            {
                couch.Navigator.Select(0, couch.Tabs[0].Items.FindIndex(i => i.Key == key));
                couch.Handle(CouchCommand.Accept);
                await TestSession.WaitUntil(() => couch.Menu == menu && couch.uiMenu.IsVisible, TimeSpan.FromSeconds(10), menu.ToString());
            }
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                couch.Settings = new CouchSettings();
                couch.Show(games, Retro());

                // The downloads: a card each, on their own screen; what A and X do is the chosen card's; B goes to the home
                download.Paused();
                await OpenTile("tile:downloads", CouchMenu.Downloads);
                Assert.True(couch.uiDownloadsBoard.IsVisible);
                Assert.False(couch.uiMenuBox.IsVisible);
                var metroid = Assert.IsType<CouchDownloadLine>(couch.MenuLines.Single(l => l.Text == "Metroid"));
                Assert.Equal("NES", metroid.Platform);
                Assert.Equal(CouchDownloadLine.Kind.Paused, metroid.State);
                couch.Navigator.SelectLine(couch.MenuLines.IndexOf(metroid));
                // Away and back: the retro downloads come last, after those other tests left in the list
                couch.Handle(CouchCommand.Up);
                couch.Handle(CouchCommand.Down);
                Assert.Same(metroid, couch.MenuLines[couch.Navigator.MenuLine]);
                Capture(window, "40-downloads");
                Assert.Contains(couch.Hints, h => h.Value == Loc.T("Resume"));
                Assert.Contains(couch.Hints, h => h.Value == Loc.T("Cancel"));
                Assert.Equal(Loc.T("Nothing left to download"), couch.uiBoardTimeLabel.Text);
                Assert.Equal("2 min 05", CouchDownloadLine.Duration(TimeSpan.FromSeconds(125)));
                Assert.Equal("1 h 05", CouchDownloadLine.Duration(TimeSpan.FromMinutes(65)));
                Assert.Equal("55 s", CouchDownloadLine.Duration(TimeSpan.FromSeconds(55)));
                couch.Handle(CouchCommand.Back);
                Assert.False(couch.uiMenu.IsVisible);

                // The challenge of the week: what it asks, where I am, the ranking
                await OpenTile("tile:challenge", CouchMenu.Challenge);
                Assert.Equal(Loc.F("Play {0} this week", "5 h"), couch.MenuLines[0].Text);
                Assert.Equal(Loc.F("You: {0}", Loc.F("{0} of {1}", "1 h 30", "5 h")), couch.MenuLines[1].Text);
                Assert.Contains(couch.MenuLines, l => l.Text == "1. Léa" && l.Detail!.Contains(Loc.T("Completed")));
                couch.Handle(CouchCommand.Back);

                // The friends: the other players, not me
                await OpenTile("tile:friends", CouchMenu.Friends);
                Assert.DoesNotContain(couch.MenuLines, l => l.Text == "admin");
                Assert.Equal(Loc.T("Back"), couch.MenuLines[^1].Text);
                couch.Handle(CouchCommand.Back);

                // Never the desktop's pages
                Assert.False(left);
            }
            finally
            {
                window.Close();
                DownloadsViewModel.Instance.RetroDownloads.Remove(download);
                session.Server.ChallengeState = null;
                SanctuaryService.Current = SanctuaryService.Url;
                foreach (Game game in games)
                    session.Server.RemoveGame(game.ID);
            }
        }

        [AvaloniaFact]
        public async Task TheNews_TheTrailer_AndTheClientsMessages_StayInTheMode()
        {
            var session = await TestSession.GetAsync();
            List<Game> games = Games(session);
            games[0].Metadata!.Trailers = new[] { "https://videos.invalid/astral.mp4" };
            var couch = new CouchUserControl();
            var window = new Window { Width = 1920, Height = 1080, Content = couch };
            bool left = false;
            couch.LeaveRequested += () => left = true;
            try
            {
                window.Show();
                window.Activate();
                Dispatcher.UIThread.RunJobs();
                couch.Settings = new CouchSettings();
                couch.Show(games, Retro());

                // The news: a screen of the mode, not the desktop's window
                couch.Navigator.Select(0, couch.Tabs[0].Items.FindIndex(i => i.Key == "tile:news"));
                couch.Handle(CouchCommand.Accept);
                await TestSession.WaitUntil(() => couch.Menu == CouchMenu.News && couch.uiMenu.IsVisible, TimeSpan.FromSeconds(10), "the news");
                Assert.Null(MainWindowViewModel.Instance.Popup);
                Assert.Equal(Loc.T("Back"), couch.MenuLines[^1].Text);
                couch.Handle(CouchCommand.Back);
                Assert.Equal("Une nouvelle carte et un lien", CouchUserControl.PlainText("# Une **nouvelle** carte\n- et [un lien](https://x.invalid)"));

                // "Watch the trailer": the mode's player on the whole screen, any button gives the game's screen back
                while (couch.Tabs[couch.Navigator.Tab].Name != Loc.T("Library"))
                    couch.Handle(CouchCommand.NextTab);
                couch.Handle(CouchCommand.Accept);
                CouchLine watch = couch.Actions.Single(a => a.Text == Loc.T("Watch the trailer"));
                await watch.Run();
                Assert.True(couch.FullTrailerShown);
                Assert.Null(MainWindowViewModel.Instance.Popup);
                couch.Handle(CouchCommand.Accept);
                Assert.False(couch.FullTrailerShown);
                Assert.True(couch.uiDetails.IsVisible);

                // Where a game is installed: a screen of the mode, the folders with their room; B installs nothing
                var picker = new InstallLocationUserControl(1);
                Assert.NotEmpty(picker.Rows);
                Task<string> asked = picker.SelectInstallLocation();
                Game astral = games.First();
                couch.ChooseInstallLocation(picker, astral);
                Assert.Equal(CouchMenu.InstallLocation, couch.Menu);
                Assert.True(couch.uiPlaceBoard.IsVisible);
                Assert.Equal(astral.Title, couch.uiPlaceTitle.Text);
                var disk = Assert.IsType<CouchDiskLine>(couch.MenuLines.First(l => l.Text == picker.Rows[0].Path));
                Assert.True(disk.HasRoom);
                Assert.True(disk.UsedWidth + disk.GameWidth <= CouchDiskLine.BarWidth);
                // Half full, a tenth for the game: the bar says it; too full: not chosen, nothing striped
                var half = CouchDiskLine.Of(new InstallLocationRow("D:", "", false, true, "") { Free = 50_000, Total = 100_000 }, 10_000, () => { });
                Assert.Equal(CouchDiskLine.BarWidth / 2, half.UsedWidth);
                Assert.Equal(CouchDiskLine.BarWidth / 10, half.GameWidth, 3);
                Assert.Equal(GameSizeConverter.Text(40_000), half.After);
                var full = CouchDiskLine.Of(new InstallLocationRow("E:", "", false, false, "") { Free = 9_000, Total = 100_000 }, 12_000, () => { });
                Assert.Equal(Loc.T("Not enough room"), full.Warning);
                Assert.Equal(0, full.GameWidth);
                Assert.Equal("—", full.After);
                Assert.Contains(couch.Hints, h => h.Value == Loc.T("Ask each time"));
                Capture(window, "41-install-location");
                // Y: asked each time, or not any more
                Assert.True(couch.AsksLocationAgain);
                couch.Handle(CouchCommand.Search);
                Assert.False(couch.AsksLocationAgain);
                couch.Handle(CouchCommand.Search);
                Assert.Null(MainWindowViewModel.Instance.Popup);
                couch.Handle(CouchCommand.Back);
                Assert.Equal(string.Empty, await asked);
                Assert.False(couch.uiMenu.IsVisible);
                // A folder chosen: its answer
                picker = new InstallLocationUserControl(1);
                asked = picker.SelectInstallLocation();
                couch.ChooseInstallLocation(picker, astral);
                await couch.MenuLines.First(l => l.Text == picker.Rows[0].Path).Run();
                Assert.Equal(picker.Rows[0].Path, await asked);

                // The client's messages (its bar is under the mode): at the bottom, or on the start screen of a game
                MainWindowViewModel.Instance.AppBarText = "No valid Executable found";
                await TestSession.WaitUntil(() => couch.Notice == Loc.T("No valid Executable found"), TimeSpan.FromSeconds(5), "the message");
                Assert.False(left);
            }
            finally
            {
                window.Close();
                foreach (Game game in games)
                    session.Server.RemoveGame(game.ID);
            }
        }

        [AvaloniaFact]
        public async Task AGameLeftRunning_ByShowSanctuaryVault_IsOfferedBackFirstInTheMenu()
        {
            var session = await TestSession.GetAsync();
            List<Game> games = Games(session);
            var exists = CouchSystem.Exists;
            bool open = true;
            CouchSystem.Exists = window => open && window == (IntPtr)42;
            var couch = new CouchUserControl();
            var window = new Window { Width = 1920, Height = 1080, Content = couch };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                couch.Settings = new CouchSettings();
                couch.Show(games, Retro());
                Assert.Null(couch.GameBehind);
                couch.Handle(CouchCommand.Menu);
                Assert.Equal(Loc.T("Back to the games"), couch.MenuLines[0].Text);
                couch.Handle(CouchCommand.Back);

                // Start and Back: together, or a little out of step (Back at the next reading)
                Assert.True(couch.IsCombo(CouchButtons.Start | CouchButtons.Back));
                Assert.False(couch.IsCombo(CouchButtons.None));
                await Task.Delay(CouchUserControl.ComboWindow + TimeSpan.FromMilliseconds(50));
                Assert.False(couch.IsCombo(CouchButtons.Start));
                Assert.True(couch.IsCombo(CouchButtons.Back));
                Assert.False(couch.IsCombo(CouchButtons.None));
                // One after the other (Start to pause the game, then Select in its menu): not the menu over the game
                await Task.Delay(CouchUserControl.ComboWindow + TimeSpan.FromMilliseconds(50));
                Assert.False(couch.IsCombo(CouchButtons.Start));
                await Task.Delay(TimeSpan.FromMilliseconds(250));
                Assert.False(couch.IsCombo(CouchButtons.Back));

                // "Show SanctuaryVault" over the game: going back to it comes first in the menu
                couch.KeepGameBehind((IntPtr)42, "Hollow Knight");
                Assert.Equal("Hollow Knight", couch.GameBehind);
                couch.Handle(CouchCommand.Menu);
                Assert.Equal(Loc.F("Back to {0}", "Hollow Knight"), couch.MenuLines[0].Text);
                couch.Handle(CouchCommand.Back);

                // The game closed meanwhile: offered no more
                open = false;
                Assert.Null(couch.GameBehind);
                couch.Handle(CouchCommand.Menu);
                Assert.Equal(Loc.T("Back to the games"), couch.MenuLines[0].Text);
            }
            finally
            {
                CouchSystem.Exists = exists;
                window.Close();
                foreach (Game game in games)
                    session.Server.RemoveGame(game.ID);
            }
        }

        [AvaloniaFact]
        public async Task TheThemes_AreSearchedForInTheBackground_AndKeptOnThePC()
        {
            await TestSession.GetAsync();
            var searched = new List<string>();
            GameThemes.SearchInstead = search =>
            {
                lock (searched)
                    searched.Add(search);
                return Task.FromResult<string?>(search.StartsWith("Zelda") ? null : "video-" + search.Length);
            };
            GameThemes.PreloadPause = TimeSpan.Zero;
            File.Delete(GameThemes.File);
            GameThemes.Forget();
            try
            {
                // Each game once, in the order given
                await GameThemes.PreloadAsync(new[] { "Astral main theme", "Zelda main theme", "Astral main theme" });
                Assert.Equal(new[] { "Astral main theme", "Zelda main theme" }, searched);

                // Another start: what was found (or not) is read from the PC, only the new game is searched for
                GameThemes.Forget();
                await GameThemes.PreloadAsync(new[] { "Astral main theme", "Zelda main theme", "Metroid main theme" });
                Assert.Equal(new[] { "Astral main theme", "Zelda main theme", "Metroid main theme" }, searched);
                Assert.Equal("video-17", await GameThemes.VideoOfAsync("Astral main theme"));
                Assert.Null(await GameThemes.VideoOfAsync("Zelda main theme"));
                Assert.Equal(3, searched.Count);
            }
            finally
            {
                GameThemes.SearchInstead = null;
                GameThemes.PreloadPause = TimeSpan.FromSeconds(2);
                File.Delete(GameThemes.File);
                GameThemes.Forget();
            }
        }

        [AvaloniaFact]
        public async Task ASelectedGamesTheme_PlaysAfterAMoment_AndStopsForTheNextGame()
        {
            var session = await TestSession.GetAsync();
            List<Game> games = Games(session);
            var searched = new List<string>();
            GameThemes.Instead = search =>
            {
                searched.Add(search);
                return Task.FromResult<string?>(search.StartsWith("Astral Drift") ? "https://sounds.invalid/astral.m4a?a=1&b=2" : null);
            };
            var couch = new CouchUserControl();
            var window = new Window { Width = 1920, Height = 1080, Content = couch };
            try
            {
                window.Show();
                window.Activate();
                Dispatcher.UIThread.RunJobs();
                couch.Settings = new CouchSettings();
                couch.Show(games, Retro());
                while (couch.Tabs[couch.Navigator.Tab].Name != Loc.T("Library"))
                    couch.Handle(CouchCommand.NextTab);
                Assert.Equal("Astral Drift", couch.Current!.Title);

                // Not at once: passing through the games plays nothing
                couch.KeepTheme();
                Assert.Null(couch.ThemePlaying);
                await Task.Delay(1300);
                couch.KeepTheme();
                await TestSession.WaitUntil(() => couch.ThemePlaying == couch.Current!.Key, TimeSpan.FromSeconds(5), "the theme");
                Assert.Equal("Astral Drift main theme", searched[^1]);

                // Its screen opened: the same theme goes on
                couch.Handle(CouchCommand.Accept);
                couch.KeepTheme();
                Assert.Equal(couch.Current!.Key, couch.ThemePlaying);
                couch.Handle(CouchCommand.Back);

                // The next game: stopped; it has none, nothing plays and it is not searched again
                couch.Handle(CouchCommand.Right);
                Assert.Null(couch.ThemePlaying);
                await Task.Delay(1300);
                couch.KeepTheme();
                await Task.Delay(200);
                int searches = searched.Count;
                couch.KeepTheme();
                await Task.Delay(200);
                Assert.Null(couch.ThemePlaying);
                Assert.Equal(searches, searched.Count);

                // The setting off, or the light mode: none
                couch.Handle(CouchCommand.Left);
                couch.Settings.Themes = false;
                await Task.Delay(1300);
                couch.KeepTheme();
                await Task.Delay(200);
                Assert.Null(couch.ThemePlaying);
                couch.Settings.Themes = true;
                couch.Settings.Performance = true;
                couch.KeepTheme();
                await Task.Delay(200);
                Assert.Null(couch.ThemePlaying);

                // A retro game's search names its console; the player's page plays the sound alone, looped
                Assert.Equal("Metroid NES main theme", GameThemes.SearchOf(" Metroid ", "NES"));
                string page = CouchPlayer.Page(null, "https://sounds.invalid/astral.m4a?a=1&b=2", 0.45, loop: true);
                Assert.Contains("<audio id='a' src='https://sounds.invalid/astral.m4a?a=1&amp;b=2' preload='auto' loop>", page);
                Assert.DoesNotContain("<video", page);
                Assert.Contains("level=0.45", page);
            }
            finally
            {
                GameThemes.Instead = null;
                window.Close();
                foreach (Game game in games)
                    session.Server.RemoveGame(game.ID);
            }
        }

        [AvaloniaFact]
        public async Task AGameStartedFromTheMode_KeepsItsPictureStill_AndGivesTheModeBackWhenClosed()
        {
            var session = await TestSession.GetAsync();
            List<Game> games = Games(session);
            List<RetroGame> retro = Retro();
            var couch = new CouchUserControl();
            var window = new Window { Width = 1920, Height = 1080, Content = couch };
            try
            {
                window.Show();
                window.Activate();
                Dispatcher.UIThread.RunJobs();
                couch.Settings = new CouchSettings();
                couch.Show(games, retro);
                couch.ListenToGames(true);
                while (couch.Tabs[couch.Navigator.Tab].Name != "NES")
                    couch.Handle(CouchCommand.NextTab);
                couch.Handle(CouchCommand.Right);
                couch.Handle(CouchCommand.Accept);
                Assert.Equal("Zelda", couch.Current!.Title);

                // "Install and play": the emulator starts once the game is there; its picture stays, nothing plays under it
                retro[0].Status = "Playing: Select+Start to quit";
                RetroPlay.Raise(true, retro[0]);
                Dispatcher.UIThread.RunJobs();
                Assert.Same(couch.Current, couch.Playing);
                Assert.Equal((CouchUserControl.Curtain.Splash, "Zelda"), (couch.CurtainShown, couch.CurtainTitle));
                Assert.False(couch.TrailerShown);
                // Another game closed: nothing changes
                RetroPlay.Raise(false, retro[1]);
                Dispatcher.UIThread.RunJobs();
                Assert.NotNull(couch.Playing);
                // The menu over it names the game, not the emulator's window
                Assert.Equal(couch.Playing!.Title, couch.OverlayTitle("RetroArch mGBA 0.10.3"));

                // Closed: the mode is back on the game's screen
                RetroPlay.Raise(false, retro[0]);
                Dispatcher.UIThread.RunJobs();
                Assert.Null(couch.Playing);
                Assert.Equal(CouchUserControl.Curtain.None, couch.CurtainShown);
                Assert.True(couch.uiDetails.IsVisible);
                Assert.Equal("RetroArch mGBA 0.10.3", couch.OverlayTitle("RetroArch mGBA 0.10.3"));

                // A button pressed on the picture: the player is back, the game is no longer waited for
                couch.GameStarted(couch.Current!);
                couch.Handle(CouchCommand.Accept);
                Assert.Null(couch.Playing);
                Assert.Equal(CouchUserControl.Curtain.None, couch.CurtainShown);

                // Not listened to anymore once the mode is left
                couch.ListenToGames(false);
                RetroPlay.Raise(true, retro[0]);
                Dispatcher.UIThread.RunJobs();
                Assert.Null(couch.Playing);
            }
            finally
            {
                couch.ListenToGames(false);
                window.Close();
                foreach (Game game in games)
                    session.Server.RemoveGame(game.ID);
            }
        }

        [AvaloniaFact]
        public async Task AGamesScreenshots_FadeOneIntoTheOther_NeverThroughBlack()
        {
            var session = await TestSession.GetAsync();
            List<Game> games = Games(session);
            static byte[] Png(SkiaSharp.SKColor color)
            {
                using var bitmap = new SkiaSharp.SKBitmap(64, 36);
                bitmap.Erase(color);
                using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
                return image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90).ToArray();
            }
            session.Server.Media[9801] = Png(SkiaSharp.SKColors.Red);
            session.Server.Media[9802] = Png(SkiaSharp.SKColors.Blue);
            games[0].Metadata!.Screenshots = new[] { $"{session.Server.Url}/api/media/9801", $"{session.Server.Url}/api/media/9802" };
            var couch = new CouchUserControl();
            var window = new Window { Width = 1920, Height = 1080, Content = couch };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                couch.Settings = new CouchSettings();
                couch.Show(games, Retro());
                while (couch.Tabs[couch.Navigator.Tab].Name != Loc.T("Library"))
                    couch.Handle(CouchCommand.NextTab);
                couch.Handle(CouchCommand.Accept);
                // The fades are animations: their end is read once they ran
                async Task Settle()
                {
                    for (int i = 0; i < 50; i++)
                    {
                        await Task.Delay(40);
                        Dispatcher.UIThread.RunJobs();
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    }
                }

                // The game's picture shown, then the first screenshot fades in over it, the picture staying as it is under it
                await Settle();
                double back = couch.uiBack.Opacity;
                await couch.NextShotAsync(couch.Current!);
                await Settle();
                Assert.Equal((1d, 0d), (couch.uiShotA.Opacity, couch.uiShotB.Opacity));
                Assert.Equal(back, couch.uiBack.Opacity);
                // The next one over it; the one under goes once covered
                await couch.NextShotAsync(couch.Current!);
                Assert.True(couch.uiShotB.ZIndex > couch.uiShotA.ZIndex);
                Assert.Equal(1, couch.uiShotA.Opacity);
                await Settle();
                Assert.Equal((0d, 1d), (couch.uiShotA.Opacity, couch.uiShotB.Opacity));
                Assert.Equal(back, couch.uiBack.Opacity);

                // Back to the list: the game's picture again
                couch.Handle(CouchCommand.Back);
                await Settle();
                Assert.Equal((0d, 0d), (couch.uiShotA.Opacity, couch.uiShotB.Opacity));
            }
            finally
            {
                window.Close();
                foreach (Game game in games)
                    session.Server.RemoveGame(game.ID);
            }
        }

        [AvaloniaFact]
        public async Task ALibraryGameStartedFromTheMode_GivesItBack_OnlyWhenSeenRunningThenClosed()
        {
            var session = await TestSession.GetAsync();
            List<Game> games = Games(session);
            var installed = new KeyValuePair<Game, string>(games[2], Path.Combine(session.LibraryRoot, "couch-watched"));
            InstallViewModel.Instance.InstalledGames.Add(installed);
            var isRunning = CouchUserControl.IsRunning;
            TimeSpan giveUp = CouchUserControl.GiveUpAfter;
            bool running = false;
            CouchUserControl.IsRunning = (_, _) => running;
            var couch = new CouchUserControl();
            var window = new Window { Width = 1920, Height = 1080, Content = couch };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                couch.Settings = new CouchSettings();
                couch.Show(games, Retro());
                CouchItem item = couch.Tabs.SelectMany(t => t.Items).First(i => i.Pc?.ID == games[2].ID);
                void Watch()
                {
                    // Looked at every tenth time (every 5 seconds)
                    for (int i = 0; i < 10; i++)
                        couch.WatchGame();
                }

                // Seen running, then closed: the mode comes back in front
                couch.GameStarted(item);
                running = true;
                Watch();
                await Task.Delay(200);
                Dispatcher.UIThread.RunJobs();
                Assert.Same(item, couch.Playing);
                running = false;
                Watch();
                await TestSession.WaitUntil(() => couch.Playing == null, TimeSpan.FromSeconds(5), "the game's end");
                Assert.Equal(1, couch.BroughtBack);

                // Never seen (it runs from elsewhere): let go after a while, the window is not put over it
                CouchUserControl.GiveUpAfter = TimeSpan.Zero;
                couch.GameStarted(item);
                Watch();
                await TestSession.WaitUntil(() => couch.Playing == null, TimeSpan.FromSeconds(5), "letting the game go");
                Assert.Equal(1, couch.BroughtBack);
            }
            finally
            {
                CouchUserControl.IsRunning = isRunning;
                CouchUserControl.GiveUpAfter = giveUp;
                InstallViewModel.Instance.InstalledGames.Remove(installed);
                window.Close();
                foreach (Game game in games)
                    session.Server.RemoveGame(game.ID);
            }
        }

        [Fact]
        public void EmptyGamepadSlots_AreLookedAtEveryTwoSeconds_NotAtEachFrame()
        {
            long last = long.MinValue;
            Assert.True(CouchGamepad.LookAtEmptySlots(10_000, ref last));
            Assert.False(CouchGamepad.LookAtEmptySlots(10_016, ref last));
            Assert.False(CouchGamepad.LookAtEmptySlots(11_999, ref last));
            Assert.True(CouchGamepad.LookAtEmptySlots(12_000, ref last));
        }

        [Fact]
        public void EachMove_HasItsOwnSound_AsAConsolesMenus()
        {
            CouchSound Of(CouchCommand command, CouchEffect effect, CouchScreen from, CouchScreen to, bool otherTab = false) =>
                CouchUserControl.SoundOf(command, effect, from, to, otherTab);
            Assert.Equal(CouchSound.Move, Of(CouchCommand.Right, CouchEffect.Moved, CouchScreen.Home, CouchScreen.Home));
            Assert.Equal(CouchSound.Tab, Of(CouchCommand.NextTab, CouchEffect.Moved, CouchScreen.Home, CouchScreen.Home, otherTab: true));
            Assert.Equal(CouchSound.Tab, Of(CouchCommand.View, CouchEffect.ViewChanged, CouchScreen.Home, CouchScreen.Home));
            Assert.Equal(CouchSound.Open, Of(CouchCommand.Accept, CouchEffect.ScreenChanged, CouchScreen.Home, CouchScreen.Details));
            Assert.Equal(CouchSound.Close, Of(CouchCommand.Back, CouchEffect.ScreenChanged, CouchScreen.Details, CouchScreen.Home));
            Assert.Equal(CouchSound.Panel, Of(CouchCommand.Menu, CouchEffect.ScreenChanged, CouchScreen.Details, CouchScreen.Menu));
            Assert.Equal(CouchSound.PanelClose, Of(CouchCommand.Back, CouchEffect.ScreenChanged, CouchScreen.Menu, CouchScreen.Details));
            Assert.Equal(CouchSound.Accept, Of(CouchCommand.Accept, CouchEffect.Action, CouchScreen.Details, CouchScreen.Details));
        }

        [AvaloniaFact]
        public async Task TheCustomization_ChangesTheScreenAtOnce_BehindItsMenu()
        {
            var session = await TestSession.GetAsync();
            List<Game> games = Games(session);
            var couch = new CouchUserControl();
            var window = new Window { Width = 1920, Height = 1080, Content = couch };
            string file = Path.Combine(Path.GetTempPath(), "gv-couch-look-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                couch.Settings = new CouchSettings();
                couch.Show(games, Retro());
                while (couch.Tabs[couch.Navigator.Tab].Name != Loc.T("Library"))
                    couch.Handle(CouchCommand.NextTab);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(8, couch.Navigator.Columns);

                // The library: larger covers, fewer by row in the grid; no glow; the numbers under the name
                couch.ShowSettingsPage("library");
                Assert.All(couch.MenuLines.Take(couch.MenuLines.Count - 1), l => Assert.False(string.IsNullOrEmpty(l.Help)));
                CouchLine Line(string label) => couch.MenuLines.Single(l => l.Text.StartsWith(Loc.F("{0}: {1}", Loc.T(label), "")));
                await Line("Cover size").Run();
                await Line("Cover size").Run();
                Assert.Equal("large", couch.Settings.CoverSize);
                Assert.Equal(212d, couch.Resources["Couch.Card.Width"]);
                Dispatcher.UIThread.RunJobs();
                Assert.True(couch.Navigator.Columns < 8);
                await Line("Glow of the chosen cover").Run();
                Assert.Contains("noGlow", couch.Classes);
                await Line("Where the numbers are").Run();
                Assert.True(couch.uiStatsLine.IsVisible && !couch.uiStatsCard.IsVisible);
                Capture(window, "custom-menu");

                // The backgrounds: a synthwave horizon behind the lists, the game's own picture in its screen
                couch.ShowSettingsPage("backgrounds");
                await Line("Behind the lists").Run();
                await Line("Behind the lists").Run();
                await Line("Behind the lists").Run();
                Assert.Equal("synthwave", couch.Settings.Background);
                couch.Handle(CouchCommand.Back);
                couch.Handle(CouchCommand.Back);
                couch.Handle(CouchCommand.Back);
                Assert.True(couch.uiPack.IsVisible && !couch.uiBack.IsVisible);
                Capture(window, "custom-synthwave");

                // A game's screen centered at the bottom, the system's font, no help of the buttons
                couch.ShowSettingsPage("look");
                await Line("Game screen").Run();
                await Line("Game screen").Run();
                await Line("Font").Run();
                await Line("Font").Run();
                Assert.Equal(("showcase", "system"), (couch.Settings.Layout, couch.Settings.Font));
                Assert.Equal(Avalonia.Media.FontFamily.Parse("$Default"), couch.Resources["Couch.Font"]);
                couch.ShowSettingsPage("interface");
                await Line("Help of the buttons").Run();
                Assert.False(couch.uiHints.IsVisible);
                couch.Handle(CouchCommand.Back);
                couch.Handle(CouchCommand.Back);
                couch.Handle(CouchCommand.Back);
                couch.Handle(CouchCommand.Accept);
                Assert.True(couch.uiBack.IsVisible && !couch.uiPack.IsVisible);
                Assert.Equal((Avalonia.Layout.HorizontalAlignment.Center, Avalonia.Layout.VerticalAlignment.Bottom), (couch.uiDetailCard.HorizontalAlignment, couch.uiDetailCard.VerticalAlignment));
                Capture(window, "custom-showcase");

                // In a panel: the card is glass
                couch.Settings.Layout = "panel";
                couch.ApplyLook();
                Assert.Contains("couchGlass", couch.uiDetailCard.Classes);
                Capture(window, "custom-panel");

                // Kept as chosen
                couch.Settings.Save(file);
                CouchSettings read = CouchSettings.Load(file);
                Assert.Equal(("large", "synthwave", "panel", false, "compact"), (read.CoverSize, read.Background, read.Layout, read.Glow, read.InfoStyle));
            }
            finally
            {
                File.Delete(file);
                window.Close();
                foreach (Game game in games)
                    session.Server.RemoveGame(game.ID);
            }
        }

        [AvaloniaFact]
        public async Task TheLoginScreen_ShowsTheTimeAndTheProfiles_AndSignsInWithAnother()
        {
            var session = await TestSession.GetAsync();
            List<Game> games = Games(session);
            var switched = new List<string>();
            var switchProfile = CouchUserControl.SwitchProfile;
            CouchUserControl.SwitchProfile = (dir, _) => switched.Add(dir);
            var couch = new CouchUserControl();
            var window = new Window { Width = 1920, Height = 1080, Content = couch };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                couch.Settings = new CouchSettings { LoginBackground = "synthwave" };
                couch.Show(games, Retro());
                couch.ShowLogin(new List<LoginProfile>
                {
                    new() { RootDir = "/me", Name = "admin", Server = "gamevault.example", IsCurrent = true },
                    new() { RootDir = "/kid", Name = "Lou", Server = "gamevault.example" },
                });
                Assert.True(couch.LoginShown);
                Assert.Equal(CouchSound.Login, couch.Played[^1]);
                Assert.Equal(DateTime.Now.ToString("t", System.Globalization.CultureInfo.CurrentCulture), couch.uiLoginTime.Text);
                Assert.Equal(Loc.F("Press {0} to come in", "A"), couch.uiLoginPrompt.Text);
                Assert.Single(couch.uiLoginPack.Children);
                Capture(window, "login");

                // Left and right choose a profile; nothing else moves behind it
                string? selected = couch.Current?.Title;
                couch.Handle(CouchCommand.Right);
                Assert.Equal(1, couch.LoginSelected);
                Assert.True(couch.LoginProfiles[1].IsSelected && !couch.LoginProfiles[0].IsSelected);
                Assert.Equal(Loc.F("Press {0} to sign in as {1}", "A", "Lou"), couch.uiLoginPrompt.Text);
                Assert.Equal(selected, couch.Current?.Title);
                couch.Handle(CouchCommand.Right);
                Assert.Equal(CouchSound.Edge, couch.Played[^1]);
                // A PlayStation gamepad: its cross
                couch.UsePad(CouchSettings.PlayStation);
                Assert.Equal(Loc.F("Press {0} to sign in as {1}", "✕", "Lou"), couch.uiLoginPrompt.Text);
                // A on another profile: the client signs in with it
                couch.Handle(CouchCommand.Accept);
                Assert.Equal(new[] { "/kid" }, switched);

                // A on the profile signed in: the mode
                couch.Handle(CouchCommand.Left);
                couch.Handle(CouchCommand.Accept);
                Assert.False(couch.LoginShown);
                Assert.Equal(CouchSound.Open, couch.Played[^1]);

                // From the menu: change of profile; no screensaver over it
                couch.ShowMenu(CouchMenu.Main);
                await couch.MenuLines.Single(l => l.Text == Loc.T("Change of profile")).Run();
                Assert.True(couch.LoginShown && !couch.uiMenu.IsVisible);
                couch.Settings.ScreenSaverMinutes = 5;
                couch.CheckIdle(TimeSpan.FromHours(1));
                Assert.Equal(CouchUserControl.Curtain.None, couch.CurtainShown);
            }
            finally
            {
                CouchUserControl.SwitchProfile = switchProfile;
                window.Close();
                foreach (Game game in games)
                    session.Server.RemoveGame(game.ID);
            }
        }

        [AvaloniaFact]
        public async Task AnikisExtras_FactsRouletteSoundsLevelTrophiesBarFolderAndReset()
        {
            var session = await TestSession.GetAsync();
            List<Game> games = Games(session);
            GameMetadata metadata = games[0].Metadata!;
            metadata.Rating = 84;
            metadata.AgeRating = 16;
            metadata.AveragePlaytime = 1500;
            metadata.Developers = new List<DeveloperMetadata> { new() { Name = "Lantern Works" } };
            metadata.Tags = new List<TagMetadata> { new() { Name = "Open World" }, new() { Name = "Co-op" } };
            var couch = new CouchUserControl();
            var window = new Window { Width = 1920, Height = 1080, Content = couch };
            string folder = Path.Combine(Path.GetTempPath(), "gv-couch-pictures-" + Guid.NewGuid().ToString("N"));
            async Task Settle(int times = 25)
            {
                for (int i = 0; i < times; i++)
                {
                    await Task.Delay(40);
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                }
            }
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                couch.Settings = new CouchSettings();
                couch.Show(games, Retro());
                while (couch.Tabs[couch.Navigator.Tab].Name != Loc.T("Library"))
                    couch.Handle(CouchCommand.NextTab);

                // A game's facts: its rating, its age, its length, who made it, its genres and tags
                couch.Handle(CouchCommand.Accept);
                Assert.Equal("Astral Drift", couch.uiDetailTitle.Text);
                var facts = (List<string>)couch.uiDetailFacts.ItemsSource!;
                Assert.Equal(new[] { "★★★★☆  84", "PEGI 16", Loc.F("About {0} h to finish", 25) }, facts);
                Assert.StartsWith("Lantern Works · 2019", couch.uiDetailSub.Text);
                Assert.Equal("Adventure  •  Open World  •  Co-op", couch.uiDetailGenres.Text);
                Capture(window, "extras-facts");
                couch.Handle(CouchCommand.Back);

                // The roulette: the strip runs through the games and stops on one, a fanfare, then its screen
                couch.Played.Clear();
                couch.OpenRandom(new Random(5));
                couch.Handle(CouchCommand.Right);
                await TestSession.WaitUntil(() => couch.uiDetails.IsVisible, TimeSpan.FromSeconds(15), "the roulette's game");
                Assert.True(couch.Played.Count(p => p == CouchSound.Move) >= 24);
                Assert.Contains(CouchSound.Lucky, couch.Played);
                couch.Handle(CouchCommand.Back);

                // A game installed meanwhile: its sound
                var installed = new KeyValuePair<Game, string>(games[0], Path.Combine(session.LibraryRoot, "couch-extras"));
                InstallViewModel.Instance.InstalledGames.Add(installed);
                try
                {
                    couch.Refresh();
                    Assert.Contains(CouchSound.Installed, couch.Played);
                }
                finally { InstallViewModel.Instance.InstalledGames.Remove(installed); }
                couch.Refresh();
                Assert.Equal(CouchSound.Uninstalled, couch.Played.Last(p => p is CouchSound.Installed or CouchSound.Uninstalled));

                // The player's level, beside their name
                couch.ShowLevel(CouchLevel.Of(300, 0, 0));
                Assert.True(couch.uiLevel.IsVisible);
                Assert.Equal(Loc.F("Level {0} · {1}", 11, Loc.T("Silver") + " I"), couch.uiLevelText.Text);
                await couch.LoadLevelAsync();
                Assert.NotNull(couch.Level);

                // An achievement earned: it slides in with the sound of its rarity; the next one after it
                couch.ShowTrophy(new GameVault.Core.Retro.RommProgress.Achievement("1", "Sky Master", "Win every race", 30, null, null, true));
                couch.ShowTrophy(new GameVault.Core.Retro.RommProgress.Achievement("2", "First Lap", "Finish a race", 3, null, null, true));
                Assert.True(couch.TrophyShown);
                Assert.Equal("Sky Master", couch.uiTrophyTitle.Text);
                Assert.Equal(CouchSound.TrophyUltra, couch.Played[^1]);
                Assert.Equal(CouchSound.Trophy, CouchUserControl.TrophySound(3));
                Assert.Equal(CouchSound.TrophyRare, CouchUserControl.TrophySound(10));
                Capture(window, "extras-trophy");
                await TestSession.WaitUntil(() => couch.uiTrophyTitle.Text == "First Lap", TimeSpan.FromSeconds(8), "the next achievement");

                // The bar of the tabs, shown a moment when the tab changes
                couch.Settings.TopBar = "auto";
                couch.ApplyLook();
                couch.Handle(CouchCommand.NextTab);
                Assert.Equal(1, couch.uiTopBar.Opacity);
                await Settle(85);
                Assert.Equal(0, couch.uiTopBar.Opacity);
                couch.Settings.TopBar = "always";
                couch.ApplyLook();
                await Settle(12);
                Assert.Equal(1, couch.uiTopBar.Opacity);

                // Your own pictures behind the lists
                Directory.CreateDirectory(folder);
                using (var bitmap = new SkiaSharp.SKBitmap(64, 36))
                {
                    bitmap.Erase(SkiaSharp.SKColors.Teal);
                    using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
                    File.WriteAllBytes(Path.Combine(folder, "mine.png"), image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90).ToArray());
                }
                File.WriteAllText(Path.Combine(folder, "notes.txt"), "not a picture");
                Assert.Equal(Path.Combine(folder, "mine.png"), CouchUserControl.PictureOf(folder));
                Assert.Null(CouchUserControl.PictureOf(Path.Combine(folder, "nowhere")));
                couch.UseFolder(folder);
                Assert.Equal("folder", couch.Settings.Background);
                Assert.IsType<Image>(Assert.Single(couch.uiPack.Children));

                // Back to the usual settings, and back to the ones before
                couch.Settings.Layout = "panel";
                couch.ResetSettings();
                Assert.Equal(("signature", "game"), (couch.Settings.Layout, couch.Settings.Background));
                Assert.True(couch.HasBackup);
                couch.RestoreSettings();
                Assert.Equal(("panel", "folder"), (couch.Settings.Layout, couch.Settings.Background));
                Assert.False(couch.HasBackup);
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch { }
                window.Close();
                foreach (Game game in games)
                    session.Server.RemoveGame(game.ID);
            }
        }

        [AvaloniaFact]
        public async Task GoingThroughTheGames_KeepsTheMemoryAndTheScreenQuiet()
        {
            var session = await TestSession.GetAsync();
            var nes = new RetroPlatform { Slug = "nes", Name = "NES" };
            // Retro games with screenshots: only the first one of the game shown is read, nothing is kept on the games
            var retro = Enumerable.Range(0, 8).Select(i => new RetroGame
            {
                Id = 7000 + i, Name = $"Game {i}", FileName = $"Game {i}.nes", Platform = nes,
                Screenshots = new[] { $"roms/{7000 + i}/shot1.png", $"roms/{7000 + i}/shot2.png", $"roms/{7000 + i}/shot3.png" },
            }).ToList();
            var couch = new CouchUserControl();
            var window = new Window { Width = 1920, Height = 1080, Content = couch };
            var driftFor = CouchUserControl.DriftFor;
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                couch.Settings = new CouchSettings();
                couch.Show(new List<Game>(), retro);
                while (couch.Tabs[couch.Navigator.Tab].Name != "NES")
                    couch.Handle(CouchCommand.NextTab);
                for (int i = 0; i < retro.Count; i++)
                {
                    await Task.Delay(420);
                    Dispatcher.UIThread.RunJobs();
                    couch.Handle(CouchCommand.Right);
                }
                await Task.Delay(500);
                Assert.All(retro, game => Assert.Null(game.ScreenshotImages));

                // The picture drifts a while after the last button, then stays still: the screen is not drawn again for nothing
                couch.KeepDrift(active: true);
                Assert.Contains("drift", couch.uiBack.Classes);
                CouchUserControl.DriftFor = TimeSpan.Zero;
                couch.KeepDrift(active: true);
                Assert.DoesNotContain("drift", couch.uiBack.Classes);
                CouchUserControl.DriftFor = driftFor;
                couch.Handle(CouchCommand.Left);
                couch.KeepDrift(active: true);
                Assert.Contains("drift", couch.uiBack.Classes);

                // The light mode: no blurred shadow under the titles
                Assert.NotNull(couch.uiTitle.Effect);
                couch.Settings.Performance = true;
                couch.ApplyLook();
                Assert.Null(couch.uiTitle.Effect);
                Assert.Null(couch.uiDetailTitle.Effect);
                couch.KeepDrift(active: true);
                Assert.DoesNotContain("drift", couch.uiBack.Classes);
                couch.Settings.Performance = false;
                couch.ApplyLook();
                Assert.NotNull(couch.uiTitle.Effect);
            }
            finally
            {
                CouchUserControl.DriftFor = driftFor;
                window.Close();
            }
        }
    }
}
