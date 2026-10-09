using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using gamevault.Helper;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.ViewModels;
using GameVault.Core;
using GameVault.Core.Library;

namespace GameVault.UiTests
{
    /// <summary>Customized profiles: read from the SanctuaryVault service, changed on the community page, saved back.</summary>
    public class ProfileScenarios
    {
        private static void Click(Button button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout();
        }

        /// <summary>Lays the window out, so rebuilt sections have their controls.</summary>
        private static void Layout()
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            foreach (var window in Avalonia.Application.Current!.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop ? desktop.Windows : Array.Empty<Window>())
                window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        private static Game NewGame(int id, string title) => new()
        {
            ID = id, Title = title, SortTitle = title.ToLowerInvariant(), Type = GameType.WINDOWS_PORTABLE, Path = $"/files/{title}.zip", Size = "1024", EntityVersion = 1,
            Metadata = new GameMetadata { Title = title }, Progresses = new List<Progress>(),
        };

        [AvaloniaFact]
        public async Task AProfile_IsShownAsChosen_ThenChangedAndSaved()
        {
            var session = await TestSession.GetAsync();
            Game played = NewGame(9601, "Profile Quest"), other = NewGame(9602, "Showcase Saga"), never = NewGame(9603, "Never Played Odyssey");
            foreach (Game game in new[] { played, other, never })
                session.Server.AddGame(game, new byte[16]);
            var me = FakeGameVaultServer.Admin;
            me.CreatedAt = DateTime.UtcNow.AddDays(-400);
            me.Progresses = new[]
            {
                new Progress { Game = played, MinutesPlayed = 130 * 60, State = "COMPLETED", LastPlayedAt = DateTime.UtcNow.AddDays(-1) },
                new Progress { Game = other, MinutesPlayed = 90, State = "PLAYING", LastPlayedAt = DateTime.UtcNow.AddDays(-3) },
            };
            session.Server.Users[1] = me;
            session.Server.Profiles[1] = new ProfileDocument
            {
                Tagline = "Speedrunner du dimanche",
                Accent = "#EF4444",
                Modules =
                {
                    new ProfileModule { Type = ProfileModuleType.Favorite, Games = { 9601 } },
                    new ProfileModule { Type = ProfileModuleType.Showcase, Games = { 9602, 9603 } },
                    new ProfileModule { Type = ProfileModuleType.Badges },
                    new ProfileModule { Type = ProfileModuleType.Text, Text = "Je joue surtout le soir." },
                },
            }.ToJson();
            ProfileService.ConfiguredUrl = session.Server.Url;
            try
            {
                MainWindowViewModel.Instance.SetActiveControl(MainControl.Community);
                var community = MainWindowViewModel.Instance.Community;
                var model = (CommunityViewModel)community.DataContext!;
                await TestSession.WaitUntil(() => model.Modules.Count == 4 && model.CurrentShownUser?.ID == 1, TimeSpan.FromSeconds(15), "the customized profile");

                Assert.Equal("Speedrunner du dimanche", model.Tagline);
                Assert.Equal(Avalonia.Media.Color.Parse("#EF4444"), model.AccentColor);
                var favorite = Assert.IsType<FavoriteModuleView>(model.Modules[0]);
                Assert.Equal(9601, favorite.Game!.Game.ID);
                Assert.Contains(Loc("Completed"), favorite.Details);
                // A game never played is fetched from the server for the showcase
                var showcase = Assert.IsType<GamesModuleView>(model.Modules[1]);
                Assert.Equal(new[] { 9602, 9603 }, showcase.Games.Select(g => g.Game.ID));
                var badges = Assert.IsType<BadgesModuleView>(model.Modules[2]);
                Assert.Contains(badges.Badges, b => b.Badge.Key == "hours" && b.Badge.Level == 3);
                Assert.True(model.ShowCustomize);

                // Customize: move the text up, drop the badges, choose another game, new tagline and color
                Click(community.FindControl<Button>("uiCustomize")!);
                Assert.True(model.IsEditing);
                Assert.All(model.Modules, m => Assert.True(m.IsEditing));
                Button ButtonOf(ProfileModuleView view, string handler) => community.GetVisualDescendants().OfType<Button>()
                    .First(b => b.DataContext == view && b.GetType() == typeof(Button) && (ToolTip.GetTip(b) as string) == handler);
                Click(ButtonOf(model.Modules[3], gamevault.Localization.Loc.T("Move up")));
                Assert.Equal(ProfileModuleType.Text, model.Modules[2].Type);
                Click(ButtonOf(model.Modules[3], gamevault.Localization.Loc.T("Remove this section")));
                Assert.Equal(3, model.Modules.Count);

                community.PickGame(((GamesModuleView)model.Modules[1]).Module);
                var picker = Assert.IsType<GamePickerPopup>(MainWindowViewModel.Instance.Popup);
                Assert.DoesNotContain(picker.Shown, g => g.ID is 9602 or 9603);// already in the showcase
                Layout();
                Button card = picker.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is Game { ID: 9601 });
                Click(card);
                Assert.Null(MainWindowViewModel.Instance.Popup);
                Assert.Equal(new[] { 9602, 9603, 9601 }, ((GamesModuleView)model.Modules[1]).Games.Select(g => g.Game.ID));

                model.DraftTagline = "Toujours une partie de plus";
                model.DraftAccentChoice = model.AccentChoices.First(c => c.Hex == "#10B981");
                Click(community.FindControl<Button>("uiSaveProfile")!);
                await TestSession.WaitUntil(() => !model.IsEditing, TimeSpan.FromSeconds(10), "the saved profile");

                ProfileDocument saved = ProfileDocument.Parse(session.Server.Profiles[1]);
                Assert.Equal("Toujours une partie de plus", saved.Tagline);
                Assert.Equal("#10B981", saved.Accent);
                Assert.Equal(new[] { "favorite", "showcase", "text" }, saved.Modules.Select(m => m.Type));
                Assert.Equal(new[] { 9602, 9603, 9601 }, saved.Modules[1].Games);
                Assert.Equal("Toujours une partie de plus", model.Tagline);
                Assert.False(model.Modules.Any(m => m.IsEditing));
            }
            finally
            {
                ProfileService.ConfiguredUrl = "";
                session.Server.Profiles.Clear();
                MainWindowViewModel.Instance.SetActiveControl(MainControl.Library);
            }
        }

        private static string Loc(string english) => gamevault.Localization.Loc.T(english);
    }
}
