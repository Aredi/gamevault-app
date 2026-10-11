using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using gamevault.Models;
using gamevault.UserControls.SettingsComponents;
using gamevault.ViewModels;

namespace GameVault.UiTests
{
    /// <summary>"Publish a Game" offers to publish a file as the new version of a game already on the server.</summary>
    public class PublishVersionScenarios
    {
        [AvaloniaFact]
        public async Task ATitleWrittenDifferently_IsCaught_AndTheExistingGameGetsTheNewVersion()
        {
            var session = await TestSession.GetAsync();
            session.Server.AddGame(9951, "Escape from Duckov", GameType.WINDOWS_PORTABLE, new byte[32], "Escape from Duckov.rar");
            var publish = new PublishGameUserControl();
            MainWindowViewModel.Instance.OpenPopup(publish);
            try
            {
                var model = (PublishGameViewModel)publish.DataContext!;
                string source = Path.Combine(session.LibraryRoot, "Escape from duckov v2.0");
                Directory.CreateDirectory(source);
                model.SourcePath = source;
                model.Title = "Escape from duckov";
                model.Version = "";
                await TestSession.WaitUntil(() => model.HasExistingMatches, TimeSpan.FromSeconds(10), "the game already on the server");

                // As typed, the server would create a second game
                Assert.Null(model.ExistingGame);
                Assert.Contains("Escape from Duckov", model.TitleWarning);

                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                foreach (var window in new[] { session.Window })
                    window.UpdateLayout();
                Button choice = publish.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is ExistingGameChoice { Game.ID: 9951 });
                choice.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Equal("Escape from Duckov", model.Title);
                Assert.Equal(9951, model.ExistingGame?.ID);
                Assert.Null(model.TitleWarning);
                Assert.NotNull(model.ExistingHint);
                Assert.NotNull(model.VersionWarning);// no version given yet

                model.Version = "2.0";
                Assert.Null(model.VersionWarning);
                Assert.StartsWith("Escape from Duckov (v2.0) (W_P)", model.FileName);
            }
            finally
            {
                MainWindowViewModel.Instance.ClosePopup();
            }
        }
    }
}
