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
using GameVault.Core;
using gamevault.Helper;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    public partial class CommunityUserControl : UserControl
    {
        private CommunityViewModel ViewModel { get; set; }
        private int forceShowId = -1;

        private bool initialized;
        public CommunityUserControl()
        {
            InitializeComponent();
            ViewModel = new CommunityViewModel();
            this.DataContext = ViewModel;
            Loaded += UserControl_Loaded;
            KeyDown += ReloadUser_Clicked;
            uiBtnReloadUser.Click += ReloadUser_Clicked;
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
                try
                {
                    if (!initialized)
                    {
                        initialized = true;
                        ViewModel.LoadingUser = true;
                    }
                    await InitUserList();
                }
                catch
                {
                    MainWindowViewModel.Instance.AppBarText = "Can not access community tab while offline";
                }
            }
        }
        public async Task InitUserList()
        {
            string result = await WebHelper.GetAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/users");
            var users = JsonSerializer.Deserialize<User[]>(result);
            users = BringCurrentUserToTop(users?.Where(u => u.DeletedAt == null)?.ToArray());
            ViewModel.Users = users;
            if (uiSelectUser.SelectedIndex == -1 && ViewModel.CurrentShownUser == null)
            {
                if (forceShowId != -1)
                {
                    int index = ViewModel.Users.ToList().FindIndex(u => u.ID == forceShowId);
                    if (index != -1)
                    {
                        uiSelectUser.SelectedIndex = index;
                    }
                }
                else
                {
                    uiSelectUser.SelectedIndex = 0;
                }
            }
        }
        internal void Reset()
        {
            ViewModel = new CommunityViewModel();
            this.DataContext = ViewModel;
        }
        internal void ShowUser(User userToShow)
        {
            if (userToShow != null)
            {
                forceShowId = userToShow.ID;
                if (MainWindowViewModel.Instance.ActiveControl == MainWindowViewModel.Instance.Community)
                {
                    InitUserList();
                }
                else
                {
                    MainWindowViewModel.Instance.SetActiveControl(MainControl.Community);
                }
            }
        }
        private async void Users_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (uiSelectUser.SelectedIndex == -1 && ViewModel.CurrentShownUser != null)
                {
                    int index = -1;
                    if (forceShowId == -1)
                    {
                        index = ViewModel.Users.ToList().FindIndex(u => u.ID == ViewModel.CurrentShownUser.ID);
                    }
                    else
                    {
                        index = ViewModel.Users.ToList().FindIndex(u => u.ID == forceShowId);
                        forceShowId = -1;
                    }
                    if (index != -1)
                    {
                        uiSelectUser.SelectedIndex = index;
                    }
                    return;
                }
                uiProgressScrollView.ScrollToHome();
                int selectedUserId = -1;
                if (e.AddedItems.Count == 0)
                {
                    return;
                }
                else
                {
                    selectedUserId = ((User)e.AddedItems[0]!).ID;
                }
                ViewModel.LoadingUser = true;
                string userUrl = @$"{SettingsViewModel.Instance.ServerUrl}/api/users/{selectedUserId}";
                if (selectedUserId == LoginManager.Instance.GetCurrentUser()?.ID)
                {
                    userUrl = @$"{SettingsViewModel.Instance.ServerUrl}/api/users/me";
                }
                string currentShownUser = await WebHelper.GetAsync(userUrl);
                ViewModel.CurrentShownUser = JsonSerializer.Deserialize<User>(currentShownUser);
                ViewModel.LoadingUser = false;
                string lastSort = TryGetLastProgressSort();
                if (uiSortBy.SelectedItem as string == lastSort)
                {
                    ApplySort(lastSort);
                }
                else
                {
                    uiSortBy.SelectedItem = lastSort;
                }

            }
            catch (Exception ex)
            {
                ViewModel.LoadingUser = false;
                MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex);
            }
        }
        private string TryGetLastProgressSort()
        {
            string result = Preferences.Get(AppConfigKey.LastCommunitySortBy, LoginManager.Instance.GetUserProfile().UserConfigFile);
            try
            {
                if (!string.IsNullOrWhiteSpace(result) && ViewModel.SortBy.Contains(result))
                {
                    return result;
                }
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
            return "Last played"; //default is 'Last played'
        }
        private User[] BringCurrentUserToTop(User[] users)
        {
            int currentUserId = LoginManager.Instance.GetCurrentUser().ID;
            for (int i = 0; i < users.Length; i++)
            {
                if (users[i].ID == currentUserId && i != 0)
                {
                    User temp = users[i];
                    users[i] = users[0];
                    users[0] = temp;
                }
            }
            return users;
        }

        private void SortBy_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (uiSortBy.SelectedItem is not string sort)
                return;
            ApplySort(sort);
            Preferences.Set(AppConfigKey.LastCommunitySortBy, sort, LoginManager.Instance.GetUserProfile().UserConfigFile);
        }
        private void ApplySort(string? sort)
        {
            User currentUser = ViewModel.CurrentShownUser;
            if (currentUser != null && sort != null)
            {
                switch (sort)
                {
                    case "Time played":
                        {
                            ViewModel.UserProgresses = ViewModel.UserProgresses.OrderByDescending(o => o.MinutesPlayed).ToList();
                        }
                        break;
                    case "Last played":
                        {
                            ViewModel.UserProgresses = ViewModel.UserProgresses.OrderByDescending(o => o.LastPlayedAt).ToList();
                        }
                        break;
                    case "State":
                        {
                            ViewModel.UserProgresses = ViewModel.UserProgresses.OrderByDescending(o => o.State).ToList();
                        }
                        break;
                }
            }
        }

        private void GameImage_Click(object sender, RoutedEventArgs e)
        {
            if (((Progress)((Control)sender).DataContext).Game == null)
            {
                MainWindowViewModel.Instance.AppBarText = "Cannot open game";
                return;
            }
            MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(((Progress)((Control)sender).DataContext).Game));
        }
        private async void ReloadUser_Clicked(object? sender, EventArgs e)
        {
            if (e is KeyEventArgs { Key: not Key.F5 })
                return;
            if (!LoginManager.Instance.IsLoggedIn())
            {
                MainWindowViewModel.Instance.AppBarText = "You are not logged in or offline";
                return;
            }
            if (uiBtnReloadUser.IsEnabled == false || (e is KeyEventArgs keyArgs && keyArgs.Key != Key.F5))
                return;

            uiBtnReloadUser.IsEnabled = false;
            try
            {
                int currentUserId = ViewModel.CurrentShownUser.ID;
                string userUrl = @$"{SettingsViewModel.Instance.ServerUrl}/api/users/{currentUserId}";
                if (currentUserId == LoginManager.Instance.GetCurrentUser()?.ID)
                {
                    userUrl = @$"{SettingsViewModel.Instance.ServerUrl}/api/users/me";
                }
                string currentShownUser = await WebHelper.GetAsync(userUrl);
                ViewModel.CurrentShownUser = JsonSerializer.Deserialize<User>(currentShownUser);
                ApplySort(uiSortBy.SelectedItem as string);

            }
            catch (Exception ex) { Log.Ignored(ex); }
            uiBtnReloadUser.IsEnabled = true;
        }
        private void UserEdit_Clicked(object sender, RoutedEventArgs e)
        {
            if (ViewModel.CurrentShownUser != null)
            {
                User user = JsonSerializer.Deserialize<User>(JsonSerializer.Serialize(ViewModel.CurrentShownUser)); //Dereference
                MainWindowViewModel.Instance.OpenPopup(new UserSettingsUserControl(user) { Width = 1200, Height = 800, Margin = new Thickness(50) });
            }
        }
        private async void DeleteProgress_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Progress dataContext = (Progress)((Control)sender).DataContext;
                MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync($"Are you sure you want to delete the progress of '{dataContext.Game.Title}' ?",
                    "", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = "Yes", NegativeButtonText = "No" });
                if (result == MessageDialogResult.Affirmative)
                {
                    await WebHelper.DeleteAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/progresses/user/{ViewModel?.CurrentShownUser?.ID}/game/{dataContext?.Game.ID}");
                    //ToDo: Dirty but i dont want to use ObservableCollection only for this one action
                    List<Progress> copy = ViewModel.UserProgresses;
                    copy.Remove(dataContext);
                    ViewModel.UserProgresses = null;
                    ViewModel.UserProgresses = copy;

                    MainWindowViewModel.Instance.AppBarText = $"Successfully deleted progress";
                }
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = $"Could not delete. {WebExceptionHelper.TryGetServerMessage(ex)}";
            }
        }
    }
}
