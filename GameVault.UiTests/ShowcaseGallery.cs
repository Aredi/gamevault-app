using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using gamevault.Helper;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.UserControls.SettingsComponents;
using gamevault.ViewModels;
using gamevault.Windows;

namespace GameVault.UiTests
{
    /// <summary>The windows the showcase does not reach: sign in, retro, rooms, admin, every settings tab, the popups and dialogs.</summary>
    public partial class ShowcaseScenarios
    {
        private static async Task Gallery(TestSession session, List<Game> games, string output)
        {
            // Other conditions: SHOWCASE_THEME=light, SHOWCASE_SIZE=800x600
            if (Environment.GetEnvironmentVariable("SHOWCASE_THEME") == "light")
                ThemeManager.Apply(ThemeManager.BuiltInThemeBase + "ThemeDefaultLight.xaml");
            if (Environment.GetEnvironmentVariable("SHOWCASE_SIZE") is { Length: > 0 } size)
                Resize(session, int.Parse(size.Split('x')[0]), int.Parse(size.Split('x')[1]));
            await Settle(1500);// the covers are in the image cache: the sign in page shows them
            await Shot(() => LoginSteps(session, output));
            await Shot(() => Pages(session, games, output));
            await Shot(() => Retro(session, games, output));
            await Shot(() => Popups(session, games, output));
            await Shot(() => Dialogs(session, output));
            await Shot(async () =>
            {
                var error = new ExceptionWindow();
                error.Show();
                await Settle(500);
                Capture(error, output, "90-error-window");
                error.Close();
            });
        }

        /// <summary>One screen that fails does not stop the others.</summary>
        private static async Task Shot(Func<Task> capture)
        {
            try { await capture(); }
            catch (Exception e) { Step($"FAILED {e.GetType().Name}: {e.Message}"); }
            MainWindowViewModel.Instance.ClosePopup();
        }

        private static async Task LoginSteps(TestSession session, string output)
        {
            var window = new LoginWindow(skipBootTasks: true);
            var model = (LoginWindowViewModel)window.DataContext!;
            model.LoginUser.ServerUrl = session.Server.Url;
            model.SignupUser.ServerUrl = session.Server.Url;
            window.Show();
            await Settle(800);
            foreach (LoginStep step in Enum.GetValues<LoginStep>())
            {
                model.LoginStepIndex = (int)step;
                await Settle(900);
                Capture(window, output, $"30-login-{(int)step}-{step}");
            }
            model.LoginStepIndex = (int)LoginStep.ChooseProfile;
            model.AppBarText = "Impossible de joindre le serveur";
            await Settle(600);
            Capture(window, output, "30-login-toast");
            window.Close();
        }

        private static async Task Pages(TestSession session, List<Game> games, string output)
        {
            var vm = MainWindowViewModel.Instance;
            foreach (MainControl page in new[] { MainControl.Activity, MainControl.Library, MainControl.Rooms, MainControl.Library, MainControl.Settings, MainControl.Library, MainControl.Community, MainControl.Library, MainControl.Settings, MainControl.AdminConsole, MainControl.Library })
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                vm.SetActiveControl(page);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                session.Window.UpdateLayout();
                Step($"TIME page {page}: {watch.Elapsed.TotalMilliseconds:0} ms, {((Control)vm.ActiveControl!).GetVisualDescendants().Count()} visuals");
                await Settle(300);
            }
            vm.SetActiveControl(MainControl.Activity);
            await Settle(2500);
            Capture(session, output, "31-activity");

            var server = session.Server;
            var room = new FakeGameVaultServer.FakeRoom { Id = "c4576f0616000091", Code = "AB2CD3", Name = "Soirée Terraria", OwnerId = 2, OwnerName = "Zoé", GameId = games[13].ID, GameTitle = games[13].Title, Public = true };
            room.Members.Add((2, "Zoé", "bbbbbbbb02", "10.147.1.1"));
            room.Members.Add((3, "Malik", "cccccccc03", "10.147.1.2"));
            server.Rooms[room.Id] = room;
            SanctuaryService.Current = server.Url;
            ZeroTierAgent.ApiUrl = server.Url + "/zt";
            ZeroTierAgent.TestToken = "zt-test";
            vm.SetActiveControl(MainControl.Rooms);
            await Settle(3000);
            Capture(session, output, "32-rooms");
            vm.SetActiveControl(MainControl.Library);
            await Settle(300);
            server.Rooms.Clear();
            vm.SetActiveControl(MainControl.Rooms);
            await Settle(2500);
            Capture(session, output, "32b-rooms-empty");

            vm.SetActiveControl(MainControl.AdminConsole);
            server.Reports.Add(new Dictionary<string, object?> { ["id"] = 1, ["kind"] = "pc", ["gameId"] = games[3].ID, ["title"] = games[3].Title, ["message"] = "Le jeu plante à la sortie du menu", ["username"] = "Zoé", ["createdAt"] = DateTime.UtcNow.AddHours(-3) });
            server.Invites.Add(new Dictionary<string, object?> { ["code"] = "K7Q4-MXAB-2CD3", ["name"] = "Pour Léa", ["createdAt"] = DateTime.UtcNow.AddDays(-2), ["usedAt"] = null, ["userId"] = null, ["username"] = null });
            await Settle(3500);
            Capture(session, output, "33-admin");
            var admin = (Control)vm.ActiveControl!;
            foreach (string tile in new[] { "uiTileReports", "uiTileInvites", "uiTileServer", "uiTilePlayers" })
            {
                admin.FindControl<Avalonia.Controls.Primitives.ToggleButton>(tile)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Settle(700);
                Capture(session, output, $"33-admin-{tile}");
            }

            vm.SetActiveControl(MainControl.Settings);
            await Settle(1500);
            var tabs = ((Control)vm.ActiveControl!).FindControl<TabControl>("uiTabControl")!;
            for (int i = 0; i < tabs.ItemCount; i++)
            {
                if (tabs.ContainerFromIndex(i) is not { IsVisible: true })
                    continue;
                tabs.SelectedIndex = i;
                await Settle(1800);
                Capture(session, output, $"34-settings-{i}");
            }
            tabs.SelectedIndex = 0;
            ((Control)vm.ActiveControl!).FindControl<TextBox>("uiSearch")!.Text = "limite";
            await Settle(800);
            Capture(session, output, "34-settings-search");
            ((Control)vm.ActiveControl!).FindControl<TextBox>("uiSearch")!.Text = "";

            var quickSearch = session.Window.FindControl<QuickSearchUserControl>("uiQuickSearch")!;
            vm.SetActiveControl(MainControl.Library);
            await Settle(800);
            quickSearch.Open();
            await Settle(1200);
            Capture(session, output, "35-quick-search");
            quickSearch.Close();

            var settings = new GameSettingsUserControl(games[0]);
            vm.OpenPopup(settings);
            await Settle(2000);
            var local = settings.FindControl<ListBox>("uiSettingsHeadersLocal")!;
            var remote = settings.FindControl<ListBox>("uiSettingsHeadersRemote")!;
            for (int i = 0; i < local.ItemCount; i++)
            {
                local.SelectedIndex = i;
                await Settle(1500);
                Capture(session, output, $"36-game-settings-local-{i}");
            }
            for (int i = 0; i < remote.ItemCount; i++)
            {
                remote.SelectedIndex = i;
                await Settle(1800);
                Capture(session, output, $"36-game-settings-remote-{i}");
            }
            vm.ClosePopup();
        }

        private static async Task Retro(TestSession session, List<Game> games, string output)
        {
            var vm = MainWindowViewModel.Instance;
            var page = vm.Retro;
            await page.LoadPlatformsAsync();
            var snes = new RetroPlatform { Id = 3, Slug = "nes", Name = "NES" };
            var names = new[] { "The Legend of Zelda", "Metroid", "Mega Man 2", "Super Mario Bros. 3", "Castlevania", "Contra", "Kirby's Adventure", "Punch-Out!!", "Final Fantasy", "Ninja Gaiden", "Duck Tales", "Tetris" };
            await page.SelectPlatformAsync(page.ViewModel.Platforms.Single(p => p.Slug == "nes"));
            page.ViewModel.AllGames = names.Select((name, i) => new RetroGame
            {
                Id = 7100 + i,
                Name = name,
                FileName = name + ".nes",
                Size = 262144 + i * 4096,
                Cover = $"{session.Server.Url}/api/media/{games[i].Metadata!.Cover!.ID}",
                Summary = games[i].Metadata!.Description,
                Genres = new[] { i % 2 == 0 ? "Aventure" : "Plateforme" },
                Companies = new[] { "Nintendo" },
                Released = new DateTimeOffset(1986 + i % 8, 3, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
                Rating = 70 + i * 2,
                Players = i % 3 == 0 ? "1-2" : "1",
                Regions = new[] { "Europe", "USA" },
                Platform = snes,
            }).ToList();
            vm.SetActiveControl(MainControl.Retro);
            await Settle(3500);
            Capture(session, output, "40-retro");

            RetroGame zelda = page.ViewModel.AllGames[0];
            vm.SetActiveControl(new RetroGameViewUserControl(zelda));
            await Settle(3000);
            Capture(session, output, "41-retro-game");
            vm.OpenPopup(new RetroControlsUserControl(zelda));
            await Settle(1800);
            Capture(session, output, "42-retro-controls");
            vm.ClosePopup();
            vm.OpenPopup(new RetroMatchPopup(zelda));
            await Settle(1500);
            Capture(session, output, "43-retro-match");
            vm.ClosePopup();
            vm.OpenPopup(new RetroSaveHistoryUserControl(zelda, null));
            await Settle(1500);
            Capture(session, output, "44-retro-saves");
            vm.ClosePopup();
            vm.SetActiveControl(MainControl.Library);
        }

        private static async Task Popups(TestSession session, List<Game> games, string output)
        {
            var hades = games[0];
            Directory.CreateDirectory(session.InstallFolder(hades));
            (string Name, Func<Control> Make)[] popups =
            {
                ("50-report-problem", () => new ReportProblemPopup()),
                ("51-hypervisor", () => new HypervisorNoticePopup(hades.Title, "Ce jeu nécessite un hyperviseur. Lisez la notice avant de le télécharger.")),
                ("52-room-join", () => new RoomJoinPopup(games[13])),
                ("53-zerotier", () => new ZeroTierSetupPopup()),
                ("55-game-requests", () => new GameRequestsUserControl("Metal Gear Solid")),
                ("56-user-settings", () => new UserSettingsUserControl(session.Server.Users[1])),
                ("57-choose-executable", () => new ChooseExecutableUserControl(hades, session.InstallFolder(hades))),
                ("58-install-location", () => new InstallLocationUserControl(12L * 1024 * 1024 * 1024)),
                ("59-backup-restore", () => new BackupRestoreUserControl()),
                ("60-registration", () => new RegistrationUserControl()),
                ("61-problem-report", () => new ProblemReportUserControl()),
                ("62-publish", () => new PublishGameUserControl()),
            };
            MainWindowViewModel.Instance.SetActiveControl(MainControl.Library);
            foreach (var (name, make) in popups)
                await Shot(async () =>
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    Control popup = make();
                    double built = watch.Elapsed.TotalMilliseconds;
                    MainWindowViewModel.Instance.OpenPopup(popup);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    session.Window.UpdateLayout();
                    Step($"TIME {name}: built {built:0} ms, shown {watch.Elapsed.TotalMilliseconds:0} ms");
                    await Settle(1800);
                    Capture(session, output, name);
                });
        }

        private static async Task Dialogs(TestSession session, string output)
        {
            async Task Show(string name, Task shown)
            {
                await Settle(700);
                Capture(session, output, name);
                var layer = session.Window.FindControl<Panel>("DialogLayer")!;
                layer.GetVisualDescendants().OfType<IconButton>().Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Settle(200);
            }
            await Show("70-dialog-info", session.Window.ShowMessageAsync("Téléchargement terminé", "Hades est prêt à être installé."));
            await Show("71-dialog-confirm", session.Window.ShowMessageAsync("Supprimer ce jeu ?", "Hades et ses fichiers locaux seront supprimés. Vos sauvegardes dans le cloud sont conservées.", MessageDialogStyle.AffirmativeAndNegative));
            await Show("72-dialog-input", session.Window.ShowInputAsync("Nouvelle collection", "Donnez un nom à la collection"));
        }
    }
}
