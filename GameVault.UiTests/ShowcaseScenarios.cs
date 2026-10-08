using Avalonia;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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

            var library = MainWindowViewModel.Instance.Library;
            var scroll = library.FindControl<ScrollViewer>("uiMainScrollBar")!;
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
        }

        private static Window MainWindow(TestSession session) => session.Window;

        /// <summary>
        /// What is on screen, for checking the layout without looking at the picture: each visible text with its
        /// position (and "CUT" when it does not fit), images not loaded, texts overlapping each other.
        /// </summary>
        private static string LayoutReport(Window window)
        {
            var sb = new System.Text.StringBuilder();
            var screen = new Avalonia.Rect(window.Bounds.Size);
            var texts = new List<(string Text, Avalonia.Rect Rect)>();
            foreach (var visual in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window))
            {
                if (visual is not Control control || !control.IsEffectivelyVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
                    continue;
                var origin = control.TranslatePoint(new Avalonia.Point(0, 0), window);
                if (origin == null)
                    continue;
                var rect = new Avalonia.Rect(origin.Value, control.Bounds.Size);
                if (!rect.Intersects(screen))
                    continue;
                // Parts scrolled out of a clipping parent are not on screen either
                if (control is TextBlock text && !string.IsNullOrWhiteSpace(text.Text) && text.Opacity > 0)
                {
                    bool cut = text.TextLayout != null && (text.TextLayout.TextLines.Any(l => l.HasCollapsed) || text.TextLayout.Width > control.Bounds.Width + 1);
                    sb.AppendLine($"T {rect.X,5:0} {rect.Y,5:0} {rect.Width,4:0}x{rect.Height,-3:0} {(cut ? "CUT " : "")}{text.FontSize:0}px \"{text.Text!.Replace('\n', ' ')}\"");
                    texts.Add((text.Text!, rect));
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

        private static void Capture(TestSession session, string folder, string name)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var frame = session.Window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame!.Save(Path.Combine(folder, name + ".png"));
            File.WriteAllText(Path.Combine(folder, name + ".txt"), LayoutReport(session.Window));
            Step(name);
        }
    }
}
