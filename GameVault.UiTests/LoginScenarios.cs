using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using gamevault.Localization;
using gamevault.ViewModels;
using gamevault.Windows;

namespace GameVault.UiTests
{
    /// <summary>The sign in page: the server URL is checked so the credentials can be typed.</summary>
    public class LoginScenarios
    {
        private static void Layout(Window window)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        /// <summary>The round button above the "Sign In" caption of the sign in or sign up page.</summary>
        private static Button SignInButton(Window window) => window.GetVisualDescendants().OfType<Button>()
            .First(b => b.IsEffectivelyVisible && b.Parent is StackPanel panel && panel.Children.OfType<TextBlock>().Any(t => t.Text == Loc.T("Sign In")));

        [AvaloniaFact]
        public async Task TheServerUrl_IsCheckedEachTimeTheSignInPageOpens()
        {
            var session = await TestSession.GetAsync();
            var window = new LoginWindow(skipBootTasks: true);
            var model = (LoginWindowViewModel)window.DataContext!;
            model.LoginUser.ServerUrl = session.Server.Url;
            window.Show();
            try
            {
                for (int time = 0; time < 2; time++)
                {
                    model.LoginStepIndex = (int)LoginStep.SignInOrSignUp;
                    Layout(window);
                    SignInButton(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Layout(window);
                    await TestSession.WaitUntil(() => model.LoginServerInfo.IsAvailable, TimeSpan.FromSeconds(10), "the server checked");
                    Assert.True(model.LoginServerInfo.IsBasicAuthEnabled);
                    // Saving a profile forgets what the server offers, the URL stays: the page must check it again
                    model.LoginServerInfo = new gamevault.Models.BindableServerInfo();
                    model.LoginStepIndex = (int)LoginStep.ChooseProfile;
                    Layout(window);
                }
            }
            finally { window.Close(); }
        }
    }
}
