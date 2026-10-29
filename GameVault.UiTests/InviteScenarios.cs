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
    public class InviteScenarios
    {
        [AvaloniaFact]
        public async Task TheAdmin_MakesANamedCode_ANewAccountIsActivatedWithIt_AndTheConsoleSaysWhichCode()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            Loc.Initialize("fr");
            var window = new LoginWindow(skipBootTasks: true);
            var model = (LoginWindowViewModel)window.DataContext!;
            try
            {
                var console = new AdminConsoleUserControl();
                await console.LoadInvitesAsync();
                Assert.True(console.FindControl<Border>("uiInvitesCard")!.IsVisible);
                InviteItem made = await InvitesService.CreateAsync("Cousin Lucas");
                await console.LoadInvitesAsync();
                var listed = Assert.Single((IEnumerable<InviteItem>)console.FindControl<ItemsControl>("uiInvites")!.ItemsSource!);
                Assert.StartsWith("Pas encore utilisé", listed.StatusText);

                // The account waiting for its activation gives the code on the waiting page
                window.Show();
                model.PendingUser = new LoginUser { Username = "newbie", InviteCode = "WRONG" };
                model.LoginStepIndex = (int)LoginStep.PendingActivation;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var activate = window.FindControl<IconButton>("uiActivate")!;
                activate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await TestSession.WaitUntil(() => activate.IsEnabled && model.AppBarText != null, TimeSpan.FromSeconds(10), "the refusal");
                Assert.Equal("Ce code d'invitation n'est pas valable ou a déjà été utilisé.", model.AppBarText);

                model.AppBarText = null;
                model.PendingUser.InviteCode = made.Code.ToLowerInvariant();
                activate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await TestSession.WaitUntil(() => activate.IsEnabled && model.AppBarText != null, TimeSpan.FromSeconds(10), "the activation");
                Assert.Equal("Ton compte est activé.", model.AppBarText);

                // The console: the code is used, by whom; the account says which code it came with; older accounts nothing
                var users = new[] { new User { ID = 11, Username = "newbie" }, new User { ID = 1, Username = "admin" } };
                await console.LoadInvitesAsync(users);
                Assert.Equal("Code d'invitation Cousin Lucas", users[0].InviteText);
                Assert.Null(users[1].InviteText);
                var used = Assert.Single((IEnumerable<InviteItem>)console.FindControl<ItemsControl>("uiInvites")!.ItemsSource!);
                Assert.False(used.IsUnused);
                Assert.StartsWith("Utilisé par newbie", used.StatusText);
            }
            finally
            {
                window.Close();
                Loc.Initialize("en");
                lock (session.Server.Invites)
                    session.Server.Invites.Clear();
                SanctuaryService.Current = SanctuaryService.Url;
            }
        }
    }
}
