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
using GameVault.Core;
using gamevault.Helper;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GameVault.Core.Library;

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
                    MainWindowViewModel.Instance.AppBarText = Loc.T("Can not access community tab while offline");
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
                    _ = InitUserList();
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
                await LoadProfileAsync();
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
                MainWindowViewModel.Instance.AppBarText = Loc.T("Cannot open game");
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
                MainWindowViewModel.Instance.AppBarText = Loc.T("You are not logged in or offline");
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
                ProfileService.Forget(currentUserId);
                await LoadProfileAsync();

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
                MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync(Loc.F("Are you sure you want to delete the progress of '{0}' ?", dataContext.Game.Title),
                    "", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = Loc.T("Yes"), NegativeButtonText = Loc.T("No") });
                if (result == MessageDialogResult.Affirmative)
                {
                    await WebHelper.DeleteAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/progresses/user/{ViewModel?.CurrentShownUser?.ID}/game/{dataContext?.Game.ID}");
                    //ToDo: Dirty but i dont want to use ObservableCollection only for this one action
                    List<Progress> copy = ViewModel.UserProgresses;
                    copy.Remove(dataContext);
                    ViewModel.UserProgresses = null;
                    ViewModel.UserProgresses = copy;

                    MainWindowViewModel.Instance.AppBarText = Loc.T("Successfully deleted progress");
                }
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = Loc.F("Could not delete. {0}", WebExceptionHelper.TryGetServerMessage(ex));
            }
        }

        #region Profile
        private ProfileDocument profile = ProfileDocument.Default();
        private ProfileDocument? draft;
        /// <summary>The games chosen in the profile that the player has not played (fetched from the server).</summary>
        private readonly Dictionary<int, Game> chosenGames = new();
        private int profileGeneration;

        internal ProfileDocument ShownProfile => profile;

        /// <summary>The profile of the player shown, from the SanctuaryVault service (the default one without it).</summary>
        private async Task LoadProfileAsync()
        {
            User? user = ViewModel.CurrentShownUser;
            if (user == null)
                return;
            int generation = ++profileGeneration;
            ViewModel.IsEditing = false;
            draft = null;
            User? me = LoginManager.Instance.GetCurrentUser();
            ViewModel.CanCustomize = me != null && (me.ID == user.ID || me.Role == PERMISSION_ROLE.ADMIN);
            ProfileService.LoadResult result = await ProfileService.LoadAsync(user.ID);
            if (generation != profileGeneration)
                return;
            profile = result.Profile;
            ViewModel.ServiceAvailable = result.Available;
            ViewModel.ServiceProblem = result.Problem;
            await FetchChosenGamesAsync(profile);
            if (generation != profileGeneration)
                return;
            ShowProfile();
        }

        /// <summary>The chosen games the player never played: their progress does not bring them.</summary>
        private async Task FetchChosenGamesAsync(ProfileDocument document)
        {
            var played = ViewModel.UserProgresses.Where(p => p.Game != null).Select(p => p.Game!.ID).ToHashSet();
            var missing = document.Modules.SelectMany(m => m.Games).Where(id => !played.Contains(id) && !chosenGames.ContainsKey(id)).Distinct().ToList();
            if (missing.Count == 0)
                return;
            try
            {
                string json = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games?filter.id=$in:{string.Join(",", missing)}&limit=-1");
                foreach (Game game in JsonSerializer.Deserialize<PaginatedData<Game>>(json)?.Data ?? Array.Empty<Game>())
                    chosenGames[game.ID] = game;
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>Shows the profile, or the draft while it is changed.</summary>
        private void ShowProfile()
        {
            ProfileDocument shown = draft ?? profile;
            List<ProfileModuleView> views = ProfileModuleFactory.Build(shown, ViewModel.UserProgresses, chosenGames, ViewModel.CurrentShownUser, DateTime.UtcNow);
            foreach (ProfileModuleView view in views)
                view.IsEditing = draft != null;
            ViewModel.Modules = new System.Collections.ObjectModel.ObservableCollection<ProfileModuleView>(views);
            ViewModel.Tagline = draft != null ? ViewModel.DraftTagline : profile.Tagline;
            if (draft == null)
                ViewModel.AccentColor = profile.Accent != null ? Color.Parse(profile.Accent) : null;
            ViewModel.AddChoices = draft != null ? ProfileModuleFactory.Choices(draft) : new();
        }

        private void Customize_Click(object? sender, RoutedEventArgs e)
        {
            draft = profile.Clone();
            ViewModel.IsEditing = true;
            ViewModel.DraftTagline = profile.Tagline;
            ViewModel.DraftAccentChoice = ViewModel.AccentChoices.FirstOrDefault(c => c.Hex == profile.Accent) ?? ViewModel.AccentChoices[0];
            ShowProfile();
        }

        private void CancelCustomize_Click(object? sender, RoutedEventArgs e)
        {
            draft = null;
            ViewModel.IsEditing = false;
            ShowProfile();
        }

        private async void SaveProfile_Click(object? sender, RoutedEventArgs e)
        {
            if (draft == null || ViewModel.CurrentShownUser == null)
                return;
            draft.Tagline = ViewModel.DraftTagline;
            draft.Accent = ViewModel.DraftAccentChoice?.Hex;
            uiSaveProfile.IsEnabled = false;
            try
            {
                await ProfileService.SaveAsync(ViewModel.CurrentShownUser.ID, draft);
                profile = draft.Sanitized();
                draft = null;
                ViewModel.IsEditing = false;
                ShowProfile();
                MainWindowViewModel.Instance.AppBarText = Loc.T("Profile saved");
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = Loc.F("The profile could not be saved: {0}", ex.Message);
            }
            uiSaveProfile.IsEnabled = true;
        }

        private static ProfileModuleView? ModuleOf(object? sender) =>
            (sender as Control)?.GetSelfAndVisualAncestors().OfType<Control>().Select(c => c.DataContext).OfType<ProfileModuleView>().FirstOrDefault();

        private void MoveModule(object? sender, int step)
        {
            if (draft == null || ModuleOf(sender) is not ProfileModuleView view)
                return;
            int index = draft.Modules.IndexOf(view.Module);
            int target = index + step;
            if (index < 0 || target < 0 || target >= draft.Modules.Count)
                return;
            draft.Modules.RemoveAt(index);
            draft.Modules.Insert(target, view.Module);
            ShowProfile();
        }

        private void ModuleUp_Click(object? sender, RoutedEventArgs e) => MoveModule(sender, -1);
        private void ModuleDown_Click(object? sender, RoutedEventArgs e) => MoveModule(sender, 1);

        private void ModuleRemove_Click(object? sender, RoutedEventArgs e)
        {
            if (draft == null || ModuleOf(sender) is not ProfileModuleView view)
                return;
            draft.Modules.Remove(view.Module);
            ShowProfile();
        }

        private void AddSection_Click(object? sender, RoutedEventArgs e)
        {
            if (draft == null || (sender as Control)?.DataContext is not ProfileModuleChoice choice)
                return;
            uiAddSection.Flyout?.Hide();
            var module = new ProfileModule { Type = choice.Type };
            draft.Modules.Add(module);
            ShowProfile();
            // Showcases and the favorite game start with choosing a game
            if (ProfileModuleType.ChoosesGames(choice.Type))
                PickGame(module);
        }

        private void AddGame_Click(object? sender, RoutedEventArgs e)
        {
            if (draft != null && ModuleOf(sender) is ProfileModuleView view)
                PickGame(view.Module);
        }

        /// <summary>Adds a game to a showcase, or sets the favorite one.</summary>
        internal void PickGame(ProfileModule module)
        {
            bool favorite = module.Type == ProfileModuleType.Favorite;
            var suggestions = ViewModel.UserProgresses.Where(p => p.Game != null).OrderByDescending(p => p.MinutesPlayed ?? 0).Select(p => p.Game!).ToList();
            var picker = new GamePickerPopup(favorite ? Loc.T("Choose your favorite game") : Loc.T("Add a game to the showcase"), suggestions,
                favorite ? Array.Empty<int>() : module.Games, game =>
                {
                    if (draft == null || !draft.Modules.Contains(module))
                        return;
                    chosenGames[game.ID] = game;
                    if (favorite)
                        module.Games = new List<int> { game.ID };
                    else if (module.Games.Count < ProfileModuleType.MaxGames(module.Type) && !module.Games.Contains(game.ID))
                        module.Games.Add(game.ID);
                    ShowProfile();
                });
            MainWindowViewModel.Instance.OpenPopup(picker);
        }

        private void RemoveGame_Click(object? sender, RoutedEventArgs e)
        {
            if (draft == null || (sender as Control)?.DataContext is not ProfileGameView game || ModuleOf((sender as Control)?.GetVisualParent()?.GetVisualParent()) is not GamesModuleView view)
                return;
            view.Module.Games.Remove(game.Game.ID);
            ShowProfile();
        }

        private void ModuleGame_Click(object? sender, RoutedEventArgs e)
        {
            if (draft != null || (sender as Control)?.DataContext is not ProfileGameView game)
                return;
            MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(game.Game));
        }

        private void FavoriteGame_Click(object? sender, RoutedEventArgs e)
        {
            if (ModuleOf(sender) is FavoriteModuleView { Game: not null } view)
                MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(view.Game.Game));
        }
        #endregion
    }
}
