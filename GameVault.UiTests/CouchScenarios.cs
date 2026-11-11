using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using GameVault.Core.Couch;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.UserControls;
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
                couch.Show(games, Retro());

                // The tabs: what is installed, the library, then each console; the first game selected
                Assert.Equal(new[] { Loc.T("Installed games"), Loc.T("Library"), "NES" }, couch.Tabs.Select(t => t.Name));
                Assert.Equal("Hollow Tides", couch.Current!.Title);
                Assert.True(couch.Tabs[0].IsSelected && couch.Current.IsSelected && couch.Current.Installed);
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
                Assert.Contains(couch.Hints, h => h.Label == "X" && h.Value == Loc.T("Strip"));
                Capture(window, "2-grid");
                // A narrower screen has fewer games by row
                window.Width = 1280;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(5, couch.Navigator.Columns);
                couch.Handle(CouchCommand.Down);
                Assert.Equal("Zephyr Run", couch.Current.Title);
                window.Width = 1920;
                Dispatcher.UIThread.RunJobs();
                couch.Handle(CouchCommand.View);
                couch.Handle(CouchCommand.PreviousLetter);
                couch.Handle(CouchCommand.PreviousLetter);
                couch.Handle(CouchCommand.PreviousLetter);
                Assert.Equal("Lumen Trail", couch.Current.Title);

                // A game's screen: what can be done with it, then the next game without leaving it
                couch.Handle(CouchCommand.Accept);
                Assert.True(couch.uiDetails.IsVisible && !couch.uiHome.IsVisible);
                Assert.Equal(new[] { Loc.T("Install and play"), Loc.T("Install"), Loc.T("Add to the favorites"), Loc.T("Open its page") }, couch.Actions.Select(a => a.Text));
                Assert.True(couch.Actions[0].IsSelected);
                couch.Handle(CouchCommand.Down);
                Assert.True(couch.Actions[1].IsSelected && !couch.Actions[0].IsSelected);
                Assert.Equal("Lumen Trail", couch.uiDetailTitle.Text);
                Capture(window, "3-details");
                couch.Handle(CouchCommand.PreviousTab);
                couch.Handle(CouchCommand.PreviousTab);
                Assert.Equal("Hollow Tides", couch.uiDetailTitle.Text);
                Assert.Equal(new[] { Loc.T("Play"), Loc.T("Add to the favorites"), Loc.T("Uninstall"), Loc.T("Open its page") }, couch.Actions.Select(a => a.Text));
                couch.Handle(CouchCommand.Back);
                Assert.True(couch.uiHome.IsVisible);

                // A retro game
                couch.Handle(CouchCommand.NextTab);
                Assert.Equal(new[] { "Metroid", "Zelda" }, couch.Tabs[2].Items.Select(i => i.Title));
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
                Assert.Equal(7, couch.MenuLines.Count);
                Capture(window, "4-menu");
                CouchUserControl.StartsHere = false;
                couch.Handle(CouchCommand.Down);
                couch.Handle(CouchCommand.Down);
                couch.Handle(CouchCommand.Down);
                couch.Handle(CouchCommand.Accept);
                Assert.True(CouchUserControl.StartsHere);
                Assert.Equal(Loc.T("At start: the living-room mode"), couch.MenuLines[3].Text);
                Assert.True(couch.MenuLines[3].IsSelected);
                bool left = false;
                couch.LeaveRequested += () => left = true;
                couch.Handle(CouchCommand.Down);
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
                Assert.False(couch.uiMenu.IsVisible);
                Click(() => couch.uiTabs.ContainerFromIndex(1));
                Assert.Equal(Loc.T("Library"), couch.Tabs[couch.Navigator.Tab].Name);
                Click(() => couch.uiTabs.ContainerFromIndex(2));
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
                Assert.Equal(new[] { Loc.T("Install and play"), Loc.T("Install"), Loc.T("Watch the trailer"), Loc.T("Add to the favorites"), Loc.T("Open its page") }, couch.Actions.Select(a => a.Text));

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
                couch.OpenRandom(new Random(3));
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
                couch.Handle(CouchCommand.Menu);
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
            }
            finally
            {
                window.LeaveCouch();
                window.WindowState = before;
            }
        }
    }
}
