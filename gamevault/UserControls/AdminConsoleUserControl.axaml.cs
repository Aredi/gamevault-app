using gamevault.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using gamevault.Helper.Platform;
using gamevault.Helper;
using gamevault.Models;
using gamevault.UserControls.SettingsComponents;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using gamevault.Models.Mapping;

namespace gamevault.UserControls
{
    /// <summary>
    /// Interaction logic for AdminConsoleUserControl.xaml
    /// </summary>
    public partial class AdminConsoleUserControl : UserControl
    {
        private AdminConsoleViewModel ViewModel { get; set; }

        public AdminConsoleUserControl()
        {
            InitializeComponent();
            ViewModel = new AdminConsoleViewModel();
            this.DataContext = ViewModel;
            Loaded += UserControl_Loaded;
            KeyDown += Reload_Click;
            uiBtnReload.PointerReleased += Reload_Click;
            PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty && IsVisible)
                    this.Focus();
            };
        }
        private async void UserControl_Loaded(object? sender, RoutedEventArgs e)
        {
            if (this.IsVisible)
            {
                await InitUserList();
                ViewModel.ServerVersionInfo = await GetServerVersionInfo();
            }
        }

        public async Task InitUserList()
        {
            try
            {
                string userList = await WebHelper.GetAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/users");
                ViewModel.Users = JsonSerializer.Deserialize<User[]>(userList);
            }
            catch (Exception ex)
            {
                string msg = WebExceptionHelper.TryGetServerMessage(ex);
                MainWindowViewModel.Instance.AppBarText = msg;
            }
        }

        private async void PermissionRole_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                User selectedUser = (User)((Control)sender!).DataContext!;
                if (e.RemovedItems.Count < 1 || e.AddedItems.Count < 1 || e.RemovedItems[0] is not PERMISSION_ROLE || Equals(e.RemovedItems[0], e.AddedItems[0]))
                {
                    return;
                }
                if (LoginManager.Instance.IsLoggedIn() && selectedUser.ID == LoginManager.Instance.GetCurrentUser().ID)
                {
                    MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync(Loc.T("Are you sure you want to change your own role?\nYou may lose privileges."),
                    "", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = Loc.T("Yes"), NegativeButtonText = Loc.T("No") });
                    if (result != MessageDialogResult.Affirmative)
                    {
                        ((ComboBox)sender).SelectionChanged -= PermissionRole_SelectionChanged;
                        ((ComboBox)sender).SelectedItem = e.RemovedItems[0];
                        ((ComboBox)sender).SelectionChanged += PermissionRole_SelectionChanged;
                        return;
                    }
                }
                await WebHelper.PutAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/users/{selectedUser.ID}", JsonSerializer.Serialize(new UpdateUserDto() { Role = selectedUser.Role }));
                MainWindowViewModel.Instance.AppBarText = Loc.F("Successfully updated permission role of user '{0}' to '{1}'", selectedUser.Username, selectedUser.Role);
            }
            catch (Exception ex)
            {
                string msg = WebExceptionHelper.TryGetServerMessage(ex);
                MainWindowViewModel.Instance.AppBarText = msg;
            }
        }

        private async void Activated_Toggled(object sender, RoutedEventArgs e)
        {
            try
            {
                User selectedUser = (User)((Control)sender).DataContext;
                await WebHelper.PutAsync($@"{SettingsViewModel.Instance.ServerUrl}/api/users/{selectedUser.ID}", JsonSerializer.Serialize(new User() { Activated = selectedUser.Activated }));
                string state = selectedUser.Activated == true ? "activated" : "deactivated";
                MainWindowViewModel.Instance.AppBarText = Loc.F("Successfully {0} user '{1}'", state, selectedUser.Username);
            }
            catch (Exception ex)
            {
                string msg = WebExceptionHelper.TryGetServerMessage(ex);
                MainWindowViewModel.Instance.AppBarText = msg;
            }
        }

        private async void DeleteUser_Clicked(object sender, RoutedEventArgs e)
        {

            User selectedUser = (User)((Control)sender).DataContext;
            if (selectedUser == null)
                return;

            if (selectedUser.DeletedAt == null)
            {
                MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync(Loc.F("Are you sure you want to delete User '{0}' ?", selectedUser.Username),
                    "", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = Loc.T("Yes"), NegativeButtonText = Loc.T("No") });
                if (result != MessageDialogResult.Affirmative)
                    return;
            }
            this.IsEnabled = false;

            try
            {
                if (selectedUser.DeletedAt == null)
                {
                    await WebHelper.DeleteAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/users/{selectedUser.ID}");
                    MainWindowViewModel.Instance.AppBarText = Loc.F("Successfully deleted user '{0}'", selectedUser.Username);
                    await InitUserList();
                }
                else
                {
                    await WebHelper.PostAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/users/{selectedUser.ID}/recover", "");
                    MainWindowViewModel.Instance.AppBarText = Loc.F("Successfully recovered deleted user '{0}'", selectedUser.Username);
                    await InitUserList();
                }
            }
            catch (Exception ex)
            {
                string msg = WebExceptionHelper.TryGetServerMessage(ex);
                MainWindowViewModel.Instance.AppBarText = msg;
            }

            this.IsEnabled = true;
        }
        private void EditUser_Clicked(object sender, RoutedEventArgs e)
        {
            User user = JsonSerializer.Deserialize<User>(JsonSerializer.Serialize((User)((Control)sender).DataContext));//Dereference
            MainWindowViewModel.Instance.OpenPopup(new UserSettingsUserControl(user.ID == LoginManager.Instance.GetCurrentUser()?.ID ? LoginManager.Instance.GetCurrentUser() : user) { Width = 1200, Height = 800, Margin = new Thickness(50) });
        }
        private void PublishGame_Click(object sender, RoutedEventArgs e)
        {
            MainWindowViewModel.Instance.OpenPopup(new PublishGameUserControl() { Margin = new Thickness(80, 40) });
        }
        private void AutoMatch_Click(object sender, RoutedEventArgs e)
        {
            MainWindowViewModel.Instance.OpenPopup(new AutoMatchUserControl());
        }
        private void BackupRestore_Click(object sender, RoutedEventArgs e)
        {
            var obj = new BackupRestoreUserControl() { Margin = new Thickness(220) };
            MainWindowViewModel.Instance.OpenPopup(obj);
        }
        protected async void UserSaved(object sender, EventArgs e)
        {
            ((Button)sender).IsEnabled = false;
            this.IsEnabled = false;
            User selectedUser = (User)((Button)sender).DataContext;
            bool error = false;

            try
            {
                await WebHelper.PutAsync($@"{SettingsViewModel.Instance.ServerUrl}/api/users/{selectedUser.ID}", JsonSerializer.Serialize(selectedUser));
                MainWindowViewModel.Instance.AppBarText = Loc.T("Successfully saved user changes");
            }
            catch (Exception ex)
            {
                error = true;
                string msg = WebExceptionHelper.TryGetServerMessage(ex);
                MainWindowViewModel.Instance.AppBarText = msg;
            }

            if (!error)
            {
                await HandleChangesOnCurrentUser(selectedUser);
            }
            ((Button)sender).IsEnabled = true;
            this.IsEnabled = true;
        }
        private async Task HandleChangesOnCurrentUser(User selectedUser)
        {
            if (LoginManager.Instance.GetCurrentUser().ID == selectedUser.ID)
            {
                UserProfile profile = LoginManager.Instance.GetUserProfile();
                bool isLoggedInWithSSO = Preferences.Get(AppConfigKey.IsLoggedInWithSSO, profile.UserConfigFile) == "1";
                if (isLoggedInWithSSO)
                {
                    await LoginManager.Instance.SSOLogin(profile);
                }
                else
                {
                    await LoginManager.Instance.Login(profile, WebHelper.GetCredentials()[0], WebHelper.GetCredentials()[1]);
                }
                MainWindowViewModel.Instance.UserAvatar = LoginManager.Instance.GetCurrentUser();
            }
            await InitUserList();
        }

        private void ShowUser_Click(object sender, RoutedEventArgs e)
        {
            User selectedUser = ((Control)sender).DataContext as User;
            MainWindowViewModel.Instance.Community.ShowUser(selectedUser);
        }

        private async void Reindex_Click(object sender, RoutedEventArgs e)
        {
            ((Control)sender).IsEnabled = false;

            try
            {
                await WebHelper.PutAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/games/reindex", string.Empty);
                MainWindowViewModel.Instance.AppBarText = Loc.T("Successfully reindexed games");
            }
            catch (Exception ex)
            {
                string msg = WebExceptionHelper.TryGetServerMessage(ex);
                MainWindowViewModel.Instance.AppBarText = msg;
            }
            await MainWindowViewModel.Instance.Library.LoadLibrary();
            ((Control)sender).IsEnabled = true;
        }

        private async void Reload_Click(object? sender, EventArgs e)
        {
            if (!uiBtnReload.IsEnabled || (e is KeyEventArgs keyArgs && keyArgs.Key != Key.F5))
                return;

            uiBtnReload.IsEnabled = false;
            await InitUserList();
            uiBtnReload.IsEnabled = true;
        }
        private async Task<KeyValuePair<string, string>> GetServerVersionInfo()
        {
            try
            {
                var gitResponse = await WebHelper.BaseGetAsync("https://api.github.com/repos/Phalcode/gamevault-backend/releases");
                dynamic gitObj = JsonNode.Parse(gitResponse);
                string newestServerVersion = (string)gitObj[0]["tag_name"];
                string serverResponse = await WebHelper.GetAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/status");
                string currentServerVersion = JsonSerializer.Deserialize<ServerInfo>(serverResponse).Version;
                if (GameVault.Core.VersionHelper.IsNewer(newestServerVersion, currentServerVersion))
                {
                    return new KeyValuePair<string, string>(Loc.F("Server Version: {0}", currentServerVersion), (string)gitObj[0]["html_url"]);
                }
                return new KeyValuePair<string, string>(Loc.F("Server Version: {0}", currentServerVersion), "");
            }
            catch
            {
                return new KeyValuePair<string, string>("", "");
            }
        }

        private void ServerUpdate_Navigate(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(ViewModel.ServerVersionInfo.Value))
                PlatformInfo.OpenUrl(ViewModel.ServerVersionInfo.Value);
        }

        private void RegistrateUser_Click(object sender, RoutedEventArgs e)
        {
            MainWindowViewModel.Instance.OpenPopup(new RegistrationUserControl());
        }
    }
}
