using Avalonia;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using gamevault.Helper;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.ViewModels;
using GameVault.Core;

namespace GameVault.UiTests
{
    /// <summary>
    /// Screenshots of the library and game pages with real games, rendered by Skia. Only runs when SHOWCASE_DIR
    /// points to a folder with games.json and the images of each game (&lt;appid&gt;/cover.jpg, hero.jpg, shot*.jpg);
    /// the PNG files are written to SHOWCASE_DIR/out.
    /// </summary>
    public class ShowcaseScenarios
    {
        public static string? Folder => Environment.GetEnvironmentVariable("SHOWCASE_DIR");
        public static bool Enabled => !string.IsNullOrEmpty(Folder);

        private sealed class ShowcaseGame
        {
            [JsonPropertyName("appid")] public int AppId { get; set; }
            [JsonPropertyName("title")] public string Title { get; set; } = "";
            [JsonPropertyName("description")] public string Description { get; set; } = "";
            [JsonPropertyName("genres")] public List<string> Genres { get; set; } = new();
            [JsonPropertyName("developers")] public List<string> Developers { get; set; } = new();
            [JsonPropertyName("publishers")] public List<string> Publishers { get; set; } = new();
            [JsonPropertyName("release")] public string Release { get; set; } = "";
            [JsonPropertyName("metacritic")] public int? Metacritic { get; set; }
            [JsonPropertyName("shots")] public List<string> Shots { get; set; } = new();
        }

        [AvaloniaFact]
        public async Task Showcase_LibraryAndGamePage_WithRealGames()
        {
            if (!Enabled)
                return;
            Step("start");
            // Shown in French, like the user's installation
            gamevault.Localization.Loc.Initialize("fr");
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("fr-FR");
            var session = await TestSession.GetAsync();
            Step("session");
            string output = Path.Combine(Folder!, "out");
            Directory.CreateDirectory(output);
            var data = JsonSerializer.Deserialize<List<ShowcaseGame>>(File.ReadAllText(Path.Combine(Folder!, "games.json")))!;

            int mediaId = 70_000;
            var random = new Random(7);
            var games = new List<Game>();
            for (int i = 0; i < data.Count; i++)
            {
                ShowcaseGame source = data[i];
                string folder = Path.Combine(Folder!, source.AppId.ToString(CultureInfo.InvariantCulture));
                Media Image(string file)
                {
                    var media = new Media { ID = mediaId++ };
                    session.Server.Media[media.ID] = File.ReadAllBytes(Path.Combine(folder, file));
                    return media;
                }
                var screenshots = source.Shots.Select(shot => $"{session.Server.Url}/api/media/{Image(shot).ID}").ToArray();
                DateTime? release = DateTime.TryParse(source.Release, new CultureInfo("fr-FR"), DateTimeStyles.AssumeUniversal, out DateTime date) ? date : null;
                var game = new Game
                {
                    ID = 9000 + i,
                    Title = source.Title,
                    SortTitle = source.Title.ToLowerInvariant(),
                    Type = GameType.WINDOWS_PORTABLE,
                    Path = $"/files/{source.Title} (W_P).zip",
                    Size = ((long)(random.Next(2, 90) * 1024L * 1024 * 1024 + random.Next(0, 1000) * 1024L * 1024)).ToString(CultureInfo.InvariantCulture),
                    Version = i % 4 == 0 ? "v1.2" : null,
                    CreatedAt = DateTime.UtcNow.AddDays(-i * 2.5),
                    EntityVersion = 1,
                    DownloadCount = random.Next(0, 40),
                    Progresses = new List<Progress>(),
                    Metadata = new GameMetadata
                    {
                        Title = source.Title,
                        Description = source.Description,
                        ReleaseDate = release,
                        Rating = source.Metacritic,
                        AveragePlaytime = random.Next(300, 6000),
                        Genres = source.Genres.Select(n => new GenreMetadata { Name = n }).ToList(),
                        Developers = source.Developers.Select(n => new DeveloperMetadata { Name = n }).ToList(),
                        Publishers = source.Publishers.Select(n => new PublisherMetadata { Name = n }).ToList(),
                        Cover = Image("cover.jpg"),
                        Background = Image("hero.jpg"),
                        Screenshots = screenshots,
                        // A trailer: the page shows its button (the video is not played here)
                        Trailers = new[] { "https://www.youtube.com/watch?v=gmA6MrX81z4" },
                    },
                };
                games.Add(game);
                session.Server.AddGame(game, Array.Empty<byte>());
            }

            // A few games installed and played on this computer (the last played first)
            int[] installed = { 0, 2, 4, 6, 12, 15 };
            int[] minutes = { 2050, 790, 3400, 120, 45, 0 };
            int[] daysAgo = { 0, 1, 4, 9, 20, -1 };
            string lastPlayed = "";
            for (int n = 0; n < installed.Length; n++)
            {
                Game game = games[installed[n]];
                string dir = Path.Combine(session.LibraryRoot, "GameVault", "Installations", $"({game.ID}){game.Title.Replace(':', ' ')}");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "game.exe"), "");
                if (minutes[n] > 0)
                {
                    var progress = new Progress
                    {
                        Game = game,
                        User = FakeGameVaultServer.Admin,
                        MinutesPlayed = minutes[n],
                        State = State.PLAYING.ToString(),
                        LastPlayedAt = DateTime.UtcNow.AddDays(-daysAgo[n]).AddHours(-2),
                    };
                    session.Server.Progresses.Enqueue(progress);
                    // The game page reads the progresses of its game
                    game.Progresses.Add(new Progress { User = progress.User, MinutesPlayed = progress.MinutesPlayed, State = progress.State, LastPlayedAt = progress.LastPlayedAt });
                    lastPlayed += game.ID + ";";
                }
                // Other players of the server
                game.Progresses.Add(new Progress { User = new User { ID = 2, Username = "Zoé" }, MinutesPlayed = 300 + n * 140, State = State.PLAYING.ToString(), LastPlayedAt = DateTime.UtcNow.AddDays(-n - 2) });
            }
            Preferences.Set(AppConfigKey.LastPlayed, lastPlayed, LoginManager.Instance.GetUserProfile().UserConfigFile);

            // Players of the server and what they played (the community page)
            var admin = FakeGameVaultServer.Admin;
            admin.Username = "Alexis";
            admin.Background = new Media { ID = games[6].Metadata!.Background!.ID };
            admin.Progresses = session.Server.Progresses.Select(p => new Progress { Game = p.Game, MinutesPlayed = p.MinutesPlayed, State = p.State, LastPlayedAt = p.LastPlayedAt })
                .Append(new Progress { Game = games[8], MinutesPlayed = 1320, State = State.COMPLETED.ToString(), LastPlayedAt = DateTime.UtcNow.AddDays(-40) })
                .ToArray();
            session.Server.Users[1] = admin;
            session.Server.Users[2] = new User { ID = 2, Username = "Zoé", Role = PERMISSION_ROLE.USER, Activated = true, Progresses = Array.Empty<Progress>() };
            session.Server.Users[3] = new User { ID = 3, Username = "Malik", Role = PERMISSION_ROLE.EDITOR, Activated = true, Progresses = Array.Empty<Progress>() };

            // Two collections of the profile, shown as rows in the showcase
            var collections = LibraryData.Collections;
            foreach (var (name, members) in new[] { ("Soirées entre amis", new[] { 1, 13, 15, 10, 3 }), ("Indés cultes", new[] { 2, 14, 12, 8, 7, 16 }) })
            {
                collections.Create(name);
                foreach (int member in members)
                    collections.SetMembership(name, games[member].ID, true);
            }

            MainWindow(session).WindowState = WindowState.Normal;
            Resize(session, 1600, 1000);
            MainWindowViewModel.Instance.SetActiveControl(MainControl.Library);
            await MainWindowViewModel.Instance.Library.GetGameInstalls().RestoreInstalledGames();
            await MainWindowViewModel.Instance.Library.LoadLibrary();
            Step("library loaded");
            await Settle(4000);
            Capture(session, output, "01-library");
            // Each game of the banner: its cover and its art are shown
            var heroLibrary = MainWindowViewModel.Instance.Library;
            for (int h = 0; h < heroLibrary.Model.HeroItems.Count; h++)
            {
                heroLibrary.Model.HeroIndex = h;
                await Settle(1500);
                foreach (string name in new[] { "uiHeroCover", "uiHeroArt" })
                {
                    var image = heroLibrary.FindControl<CacheImage>(name)!;
                    var shown = image.GetVisualDescendants().OfType<Image>().FirstOrDefault();
                    Step($"hero {h} {heroLibrary.Model.CurrentHero?.Game.Title} {name}: source={image.GetImageSource() != null} opacity={shown?.Opacity} replacement={image.IsShowingReplacement}");
                }
                Capture(session, output, $"01-hero-{h}");
            }
            heroLibrary.Model.HeroIndex = 0;

            var library = MainWindowViewModel.Instance.Library;
            var scroll = library.FindControl<ScrollViewer>("uiMainScrollBar")!;
            await MeasureScrolling(session, scroll, "library, first pass");
            await MeasureScrolling(session, scroll, "library, second pass");
            scroll.Offset = new Avalonia.Vector(0, 800);
            await Settle(2500);
            Capture(session, output, "02a-library-collections");
            scroll.Offset = new Avalonia.Vector(0, 1150 + 2 * 330);
            await Settle(2500);
            Capture(session, output, "02-library-all-games");
            scroll.Offset = new Avalonia.Vector(0, 0);

            library.FindControl<Avalonia.Controls.Primitives.ToggleButton>("uiViewShelf")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await Settle(2500);
            scroll.Offset = new Avalonia.Vector(0, 1150);
            await Settle(2500);
            Capture(session, output, "03-library-shelf");
            library.FindControl<Avalonia.Controls.Primitives.ToggleButton>("uiViewGallery")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            scroll.Offset = new Avalonia.Vector(0, 0);

            // Game page of an installed game
            MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(games[0]));
            await Settle(4000);
            Capture(session, output, "04-game-page");
            var page = (Control)MainWindowViewModel.Instance.ActiveControl!;
            var pageScroll = page.FindControl<ScrollViewer>("uiPageScroll");
            if (pageScroll != null)
            {
                pageScroll.Offset = new Avalonia.Vector(0, 520);
                await Settle(2000);
                Capture(session, output, "05-game-page-details");
            }
            // Red Dead Redemption 2: a long page with many screenshots, measured while scrolling
            MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(games[10]));
            await Settle(4000);
            Capture(session, output, "04b-game-page-rdr2");
            var rdrScroll = ((Control)MainWindowViewModel.Instance.ActiveControl!).FindControl<ScrollViewer>("uiPageScroll")!;
            await MeasureScrolling(session, rdrScroll, "game page, Red Dead Redemption 2");
            rdrScroll.Offset = new Avalonia.Vector(0, 560);
            await Settle(2500);
            Capture(session, output, "04c-game-page-rdr2-gallery");
            MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(games[7]));
            await Settle(4000);
            Capture(session, output, "06-game-page-not-installed");

            // Game settings in the popup layer, and a message
            MainWindowViewModel.Instance.OpenPopup(new GameSettingsUserControl(games[0]) { Width = 1200, Height = 800, Margin = new Avalonia.Thickness(50) });
            MainWindowViewModel.Instance.AppBarText = "Hades a été ajouté à la file de téléchargement";
            await Settle(2500);
            Capture(session, output, "10-game-settings-popup");
            MainWindowViewModel.Instance.ClosePopup();

            // Smaller window and light theme
            MainWindowViewModel.Instance.SetActiveControl(MainControl.Library);
            Resize(session, 1060, 760);
            await Settle(2500);
            Capture(session, output, "07-library-small-window");
            ThemeManager.Apply(ThemeManager.BuiltInThemeBase + "ThemeDefaultLight.xaml");
            Resize(session, 1600, 1000);
            await Settle(2500);
            Capture(session, output, "08-library-light");
            MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(games[2]));
            await Settle(4000);
            Capture(session, output, "09-game-page-light");
            ThemeManager.ApplyDefault();

            // News: games added lately, and a new version of an installed game
            await ServerNewsService.LoadAsync();// first look: the files of the games are recorded
            games[2].Path = "/files/Hollow Knight (v1.5.78) (W_P).zip";
            games[2].Size = "9875611648";
            games[2].Version = "v1.5.78";
            games[2].UpdatedAt = DateTime.UtcNow;
            MainWindowViewModel.Instance.SetActiveControl(MainControl.Library);
            Resize(session, 1600, 1000);
            MainWindowViewModel.Instance.OpenPopup(new NewsPopup());
            await Settle(4000);
            Capture(session, output, "16-news", (Control)MainWindowViewModel.Instance.Popup!);
            MainWindowViewModel.Instance.ClosePopup();

            // Community: the profile of the signed in player, customized like a Steam profile
            session.Server.Profiles[1] = new GameVault.Core.Library.ProfileDocument
            {
                Tagline = "Toujours une dernière partie avant de dormir",
                Accent = "#F59E0B",
                Modules =
                {
                    new() { Type = GameVault.Core.Library.ProfileModuleType.Favorite, Games = { games[0].ID } },
                    new() { Type = GameVault.Core.Library.ProfileModuleType.Showcase, Title = "Mes indispensables", Games = { games[2].ID, games[14].ID, games[8].ID, games[11].ID, games[15].ID, games[7].ID } },
                    new() { Type = GameVault.Core.Library.ProfileModuleType.Stats },
                    new() { Type = GameVault.Core.Library.ProfileModuleType.Badges },
                    new() { Type = GameVault.Core.Library.ProfileModuleType.Text, Text = "Fan de roguelikes et de metroidvanias. Je termine (presque) tout ce que je commence ; proposez-moi vos pépites indés !" },
                    new() { Type = GameVault.Core.Library.ProfileModuleType.Recent },
                },
            }.ToJson();
            admin.CreatedAt = DateTime.UtcNow.AddDays(-420);
            ProfileService.ConfiguredUrl = session.Server.Url;
            MainWindowViewModel.Instance.SetActiveControl(MainControl.Community);
            await Settle(4000);
            Capture(session, output, "12-community");
            var communityPage = MainWindowViewModel.Instance.Community;
            var communityScroll = communityPage.FindControl<ScrollViewer>("uiProgressScrollView")!;
            communityScroll.Offset = new Avalonia.Vector(0, 900);
            await Settle(2000);
            Capture(session, output, "12b-community-sections");
            communityScroll.Offset = default;
            communityPage.FindControl<Button>("uiCustomize")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Settle(2500);
            Capture(session, output, "12c-community-customize");
            var showcaseModule = ((CommunityViewModel)communityPage.DataContext!).Modules.OfType<GamesModuleView>().First(m => m.Type == GameVault.Core.Library.ProfileModuleType.Showcase);
            communityPage.PickGame(showcaseModule.Module);
            await Settle(2500);
            Capture(session, output, "12d-game-picker");
            MainWindowViewModel.Instance.ClosePopup();

            // Living room mode at the sizes of common screens (1080p at 150 % is 1280 x 720)
            var living = session.Window.FindControl<LivingRoomUserControl>("uiLivingRoom")!;
            foreach (var (width, height) in new[] { (1920, 1080), (1280, 720), (1536, 864), (2560, 1440), (2560, 1080), (1920, 1200) })
            {
                session.Window.WindowState = WindowState.Normal;
                Resize(session, width, height);
                await ((gamevault.Windows.MainWindow)session.Window).OpenLivingRoom();
                session.Window.WindowState = WindowState.Normal;
                Resize(session, width, height);
                living.Handle(GameVault.Core.Input.PadAction.Right);
                await Settle(2500);
                Capture(session, output, $"14-living-room-{width}x{height}", living);
                ((gamevault.Windows.MainWindow)session.Window).CloseLivingRoom();
            }
            session.Window.WindowState = WindowState.Normal;
            Resize(session, 1600, 1000);

            MainWindowViewModel.Instance.SetActiveControl(MainControl.Settings);
            await Settle(2000);
            Capture(session, output, "13-settings");

            // Downloads: two running (slowly), one waiting in the queue
            Resize(session, 1600, 1000);
            SettingsViewModel.Instance.MaxConcurrentDownloadsIndex = 2;
            session.Server.ChunkDelay = TimeSpan.FromMilliseconds(120);
            foreach (int index in new[] { 1, 5, 9 })
            {
                Game game = games[index];
                byte[] file = new byte[12 * 1024 * 1024];
                game.Size = file.Length.ToString(CultureInfo.InvariantCulture);
                session.Server.AddGame(game, file);
                await MainWindowViewModel.Instance.Downloads.TryStartDownload(game);
            }
            MainWindowViewModel.Instance.SetActiveControl(MainControl.Downloads);
            await Settle(5000);
            Capture(session, output, "11-downloads");
            MainWindowViewModel.Instance.SetActiveControl(MainControl.Library);

            // Metadata: the provider knows the showcase games (with their covers) and a few namesakes
            string Cover(Game game) => $"{session.Server.Url}/api/media/{game.Metadata!.Cover!.ID}";
            MinimalGame Known(string id, string title, int year, string? cover = null, string? description = null) =>
                new() { ProviderSlug = "igdb", ProviderDataId = id, Title = title, ReleaseDate = new DateTime(year, 1, 1), CoverUrl = cover, Description = description };
            for (int i = 0; i < games.Count; i++)
                session.Server.ProviderCatalog.Add(Known((1000 + i).ToString(CultureInfo.InvariantCulture), games[i].Title, games[i].Metadata!.ReleaseDate?.Year ?? 2020, Cover(games[i]), games[i].Metadata!.Description));
            foreach (var known in new[]
            {
                Known("2001", "Hades II", 2024, description: "Battle beyond the Underworld using dark sorcery to take on the Titan of Time."),
                Known("2002", "Hades' Star", 2016, description: "A real-time space strategy game."),
                Known("2003", "Hades Challenge", 1998, description: "An adventure in the myths of ancient Greece."),
                Known("2004", "Hades: Original Soundtrack", 2020),
                Known("2005", "Hades Canyon", 2019),
                Known("2006", "Dead Cells: Return to Castlevania", 2023),
                Known("2007", "Cuphead: The Delicious Last Course", 2022),
            })
                session.Server.ProviderCatalog.Add(known);
            games[0].ProviderMetadata ??= new List<GameMetadata>();
            var remap = new GameSettingsUserControl(games[0]);
            MainWindowViewModel.Instance.OpenPopup(remap);
            await Settle(1500);
            remap.FindControl<ListBox>("uiSettingsHeadersRemote")!.SelectedIndex = 1;
            await Settle(4000);
            Capture(session, output, "17-remap");
            var remapButtons = remap.GetVisualDescendants().OfType<IconButton>().Where(b => b.DataContext is MinimalGame).ToList();
            if (remapButtons.Count > 0)
            {
                var resultsScroll = remapButtons[^1].FindAncestorOfType<ScrollViewer>()!;
                resultsScroll.Offset = new Avalonia.Vector(0, resultsScroll.Extent.Height);
                await Settle(1500);
                Capture(session, output, "17b-remap-scrolled");
            }
            MainWindowViewModel.Instance.ClosePopup();

            // Automatic mapping: games added with scene or repack names, nothing mapped yet
            int unmappedId = 9950;
            foreach (string file in new[]
            {
                "Outer_Wilds_v1.1.15 (W_P).zip", "Dead.Cells.v3.4.Rise.of.the.Giant-GOG.zip", "Cuphead (2017) [GOG] (W_P).zip",
                "Portal 2 (igdb-1009) (W_P).zip", "Balatro v1.0.1n.zip", "Terraria v1.4.4.9 [FitGirl Repack].zip",
                "Sea.of.Stars.v1.0.47-GOG.zip", "Ori and the Will of the Wisps.iso", "My Homebrew Demo.zip",
            })
                session.Server.AddGame(new Game
                {
                    ID = unmappedId++, Title = Path.GetFileNameWithoutExtension(file), SortTitle = file.ToLowerInvariant(), Type = GameType.WINDOWS_PORTABLE,
                    Path = $"/files/{file}", Size = "1073741824", EntityVersion = 1, Progresses = new List<Progress>(), ProviderMetadata = new List<GameMetadata>(),
                }, Array.Empty<byte>());
            var autoMatch = new AutoMatchUserControl();
            MainWindowViewModel.Instance.OpenPopup(autoMatch);
            await Settle(1500);
            autoMatch.FindControl<IconButton>("uiAnalyze")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Settle(5000);
            Capture(session, output, "18-auto-match");
            MainWindowViewModel.Instance.ClosePopup();
        }

        private static Window MainWindow(TestSession session) => session.Window;

        /// <summary>Scrolls down and up in small steps and logs how long each frame takes to lay out and render.</summary>
        private static async Task MeasureScrolling(TestSession session, ScrollViewer scroll, string what)
        {
            var times = new List<double>();
            var layoutTimes = new List<double>();
            double max = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            var watch = new System.Diagnostics.Stopwatch();
            var layoutWatch = new System.Diagnostics.Stopwatch();
            for (int pass = 0; pass < 2; pass++)
            {
                for (double y = 0; y <= max; y += 60)
                {
                    scroll.Offset = new Avalonia.Vector(0, pass == 0 ? y : max - y);
                    watch.Restart();
                    layoutWatch.Restart();
                    Dispatcher.UIThread.RunJobs();
                    session.Window.UpdateLayout();
                    layoutWatch.Stop();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    session.Window.CaptureRenderedFrame();
                    watch.Stop();
                    times.Add(watch.Elapsed.TotalMilliseconds);
                    layoutTimes.Add(layoutWatch.Elapsed.TotalMilliseconds);
                    await Task.Delay(5);
                }
            }
            times.Sort();
            layoutTimes.Sort();
            Step($"  layout only: median {layoutTimes[layoutTimes.Count / 2]:0.0} ms, 90th {layoutTimes[(int)(layoutTimes.Count * 0.9)]:0.0} ms, max {layoutTimes[^1]:0.0} ms");
            Step($"scrolling {what}: {times.Count} frames, median {times[times.Count / 2]:0.0} ms, 90th {times[(int)(times.Count * 0.9)]:0.0} ms, max {times[^1]:0.0} ms, memory {GC.GetTotalMemory(false) / 1048576} MB");
            scroll.Offset = new Avalonia.Vector(0, 0);
        }

        /// <summary>
        /// What is on screen, for checking the layout without looking at the picture: each visible text with its
        /// position (and "CUT" when it does not fit), images not loaded, texts overlapping each other.
        /// </summary>
        private static string LayoutReport(Window window, Control? within = null)
        {
            var sb = new System.Text.StringBuilder();
            var screen = new Avalonia.Rect(window.Bounds.Size);
            var texts = new List<(string Text, Avalonia.Rect Rect)>();
            foreach (var visual in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants((Avalonia.Visual?)within ?? window))
            {
                if (visual is not Control control || !control.IsEffectivelyVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
                    continue;
                // Position and size on screen (scaled controls included), cut by the parents that clip (scrolling)
                var toWindow = control.TransformToVisual(window);
                if (toWindow == null)
                    continue;
                var rect = new Avalonia.Rect(control.Bounds.Size).TransformToAABB(toWindow.Value);
                foreach (var ancestor in Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(control).OfType<Control>())
                {
                    if (!ancestor.ClipToBounds || ancestor == window)
                        continue;
                    var clip = ancestor.TransformToVisual(window);
                    if (clip != null)
                        rect = rect.Intersect(new Avalonia.Rect(ancestor.Bounds.Size).TransformToAABB(clip.Value));
                }
                if (rect.Width < 1 || rect.Height < 1 || !rect.Intersects(screen))
                    continue;
                // Parts scrolled out of a clipping parent are not on screen either
                if (control is TextBlock text && !string.IsNullOrWhiteSpace(text.Text) && text.Opacity > 0)
                {
                    bool cut = text.TextLayout != null && (text.TextLayout.TextLines.Any(l => l.HasCollapsed) || text.TextLayout.Width > control.Bounds.Width + 1);
                    sb.AppendLine($"T {rect.X,5:0} {rect.Y,5:0} {rect.Width,4:0}x{rect.Height,-3:0} {(cut ? "CUT " : "")}{text.FontSize:0}px \"{text.Text!.Replace('\n', ' ')}\"");
                    texts.Add((text.Text!, rect));
                }
                else if (control is gamevault.UserControls.CacheImage cacheImage && cacheImage.IsShowingReplacement)
                {
                    sb.AppendLine($"I {rect.X,5:0} {rect.Y,5:0} {rect.Width,4:0}x{rect.Height,-3:0} REPLACEMENT image ({cacheImage.ImageCacheType})");
                }
                else if (control is Image image)
                {
                    if (image.Source == null && image.Opacity > 0)
                        sb.AppendLine($"I {rect.X,5:0} {rect.Y,5:0} {rect.Width,4:0}x{rect.Height,-3:0} EMPTY image in {image.Parent?.Parent?.GetType().Name}");
                }
            }
            for (int i = 0; i < texts.Count; i++)
                for (int j = i + 1; j < texts.Count; j++)
                {
                    var overlap = texts[i].Rect.Intersect(texts[j].Rect);
                    if (overlap.Width > 4 && overlap.Height > 4)
                        sb.AppendLine($"OVERLAP \"{texts[i].Text}\" / \"{texts[j].Text}\"");
                }
            return sb.ToString();
        }

        private static void Step(string what) => File.AppendAllText(Path.Combine(Folder!, "progress.log"), $"{DateTime.Now:HH:mm:ss} {what}\n");

        private static void Resize(TestSession session, double width, double height)
        {
            session.Window.Width = width;
            session.Window.Height = height;
        }

        /// <summary>Lets images download, decode and fade in.</summary>
        private static async Task Settle(int milliseconds)
        {
            var end = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < end)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.Delay(50);
            }
        }

        private static void Capture(TestSession session, string folder, string name, Control? within = null)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var frame = session.Window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame!.Save(Path.Combine(folder, name + ".png"));
            File.WriteAllText(Path.Combine(folder, name + ".txt"), LayoutReport(session.Window, within));
            Step(name);
        }
    }
}
