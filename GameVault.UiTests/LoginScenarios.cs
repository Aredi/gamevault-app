using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.UserControls;
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

        [AvaloniaFact]
        public async Task ASessionThatRanOut_SignsInAgainWithTheSavedPassword()
        {
            var session = await TestSession.GetAsync();
            var client = new SSOHttpClient { ServerUrl = session.Server.Url, UserName = "admin", Password = "admin-password" };
            // A PC woken up after days: the access token is old and the server no longer renews the session
            client.InjectTokens("old", "expired");
            using var response = await client.GetAsync($"{session.Server.Url}/api/users/me");
            Assert.True(response.IsSuccessStatusCode);
            Assert.Equal("refresh", client.GetRefreshToken());
        }

        [AvaloniaFact]
        public async Task AProfileWithoutItsPassword_AsksForIt_InsteadOfOpeningOffline()
        {
            var session = await TestSession.GetAsync();
            UserProfile me = LoginManager.Instance.GetUserProfile();
            // Logging out removes the saved password; the profile was connected before
            UserProfile profile = ProfileManager.CreateUserProfile("loggedout");
            profile.Name = "admin";
            profile.ServerUrl = session.Server.Url;
            Preferences.Set(AppConfigKey.ServerUrl, session.Server.Url, profile.UserConfigFile, true);
            Preferences.Set(AppConfigKey.Username, "admin", profile.UserConfigFile);
            Preferences.Set(AppConfigKey.UserID, "1", profile.UserConfigFile);
            var window = new LoginWindow(skipBootTasks: true);
            var model = (LoginWindowViewModel)window.DataContext!;
            window.Show();
            try
            {
                model.UserProfiles.Add(profile);
                model.LoginStepIndex = (int)LoginStep.ChooseProfile;
                Layout(window);
                window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("profile") && b.DataContext == profile)
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await TestSession.WaitUntil(() => model.LoginStepIndex == (int)LoginStep.EditProfile, TimeSpan.FromSeconds(10), $"the password asked (step {model.LoginStepIndex}, {LoginManager.Instance.GetState()}, {model.AppBarText}, {string.Join(" ", session.Server.Requests.Where(r => !r.Contains("refresh") && !r.Contains("/api/games") && !r.Contains("news")))})");
                Assert.Equal("admin", model.EditUser.Username);
                Assert.Equal(Loc.T("Wrong or missing password: type it, save, then choose your profile again."), model.AppBarText);
            }
            finally
            {
                window.Close();
                ProfileManager.DeleteUserProfile(profile);
                // The failed sign in replaced the credentials of the session
                await LoginManager.Instance.Login(me, "admin", "admin-password");
                LoginManager.Instance.SetUserProfile(me);
            }
        }

        [AvaloniaFact]
        public async Task AnSsoProfile_ChangesToItsPassword_InItsSettings()
        {
            var session = await TestSession.GetAsync();
            UserProfile profile = ProfileManager.CreateUserProfile("ssotopassword");
            profile.Name = "admin";
            profile.ServerUrl = session.Server.Url;
            Preferences.Set(AppConfigKey.ServerUrl, session.Server.Url, profile.UserConfigFile, true);
            Preferences.Set(AppConfigKey.IsLoggedInWithSSO, "1", profile.UserConfigFile);
            var window = new LoginWindow(skipBootTasks: true);
            var model = (LoginWindowViewModel)window.DataContext!;
            window.Show();
            try
            {
                model.UserProfiles.Add(profile);
                window.OpenProfileEdit(profile);
                Layout(window);
                var sso = window.FindControl<CheckBox>("uiEditSso")!;
                Assert.True(sso.IsChecked);
                sso.IsChecked = false;
                model.EditUser.Password = "admin-password";
                Layout(window);
                window.GetVisualDescendants().OfType<IconButton>().First(b => b.IsEffectivelyVisible && b.Text == Loc.T("Save"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal((int)LoginStep.ChooseProfile, model.LoginStepIndex);
                Assert.NotEqual("1", Preferences.Get(AppConfigKey.IsLoggedInWithSSO, profile.UserConfigFile));
                Assert.Equal("admin-password", Preferences.Get(AppConfigKey.Password, profile.UserConfigFile, true));

                // And back to SSO
                window.OpenProfileEdit(profile);
                Layout(window);
                Assert.False(sso.IsChecked);
                sso.IsChecked = true;
                Layout(window);
                window.GetVisualDescendants().OfType<IconButton>().First(b => b.IsEffectivelyVisible && b.Text == Loc.T("Save"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("1", Preferences.Get(AppConfigKey.IsLoggedInWithSSO, profile.UserConfigFile));
            }
            finally
            {
                window.Close();
                ProfileManager.DeleteUserProfile(profile);
            }
        }
    }
}
