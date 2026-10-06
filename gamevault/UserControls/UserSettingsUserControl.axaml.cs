using gamevault.Localization;
using Avalonia.Platform.Storage;
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
using gamevault.Models.Mapping;
using gamevault.ViewModels;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace gamevault.UserControls
{
    /// <summary>
    /// Interaction logic for UserSettingsUserControl.xaml
    /// </summary>
    public partial class UserSettingsUserControl : UserControl
    {
        private UserSettingsViewModel ViewModel { get; set; }
        private bool loaded = false;
        internal UserSettingsUserControl(User user)
        {
            ViewModel = new UserSettingsViewModel();
            ViewModel.OriginUser = user;
            ConvertToUpdateUser();
            InitializeComponent();
            this.DataContext = ViewModel;
            uiBirthDate.DisplayDateEnd = DateTime.Now;
            Loaded += UserControl_Loaded;
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                    MainWindowViewModel.Instance.ClosePopup();
            };
            foreach (var zone in new[] { uiBackgroundDropZone, uiAvatarDropZone })
            {
                zone.AddHandler(DragDrop.DropEvent, Image_Drop);
                zone.KeyDown += Image_Paste;
                zone.PointerEntered += (s, _) => ((Control)s!).Focus();
            }
        }
        private void ConvertToUpdateUser()
        {
            UpdateUserDto newUpdateUser = new UpdateUserDto();
            newUpdateUser.Username = ViewModel.OriginUser.Username;
            newUpdateUser.FirstName = ViewModel.OriginUser.FirstName;
            newUpdateUser.LastName = ViewModel.OriginUser.LastName;
            newUpdateUser.EMail = ViewModel.OriginUser.EMail;
            newUpdateUser.BirthDate = ViewModel.OriginUser.BirthDate;
            ViewModel.UpdateUser = newUpdateUser;
        }
        private void UserControl_Loaded(object? sender, RoutedEventArgs e)
        {
            this.Focus();
            loaded = true;
        }

        private void Close_Click(object? sender, PointerReleasedEventArgs e)
        {
            MainWindowViewModel.Instance.ClosePopup();
        }

        #region Edit Image
        private async void Image_Drop(object? sender, DragEventArgs e)
        {
            string? tag = ((Control)sender!).Tag as string;
            try
            {
                var files = e.Data.GetFiles();
                string? file = files?.Select(f => f.TryGetLocalPath()).FirstOrDefault(f => f != null);
                if (file != null)
                {
                    await SetImageFromPath(tag, file);
                    return;
                }
                // Images dragged out of a browser come as HTML or as a plain URL
                string? html = e.Data.Get("text/html") as string ?? (e.Data.Get("HTML Format") as string);
                string imagePath = html != null ? ExtractImageUrlFromHtml(html) : string.Empty;
                if (string.IsNullOrEmpty(imagePath))
                    imagePath = e.Data.GetText()?.Trim() ?? "";
                if (Uri.IsWellFormedUriString(imagePath, UriKind.Absolute))
                {
                    await SetImageFromPath(tag, imagePath);
                }
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = Loc.F("Failed to load image: {0}", ex.Message);
            }
        }
        /// <summary>
        /// The avatar keeps its source path (so animated GIFs survive the upload), the background is decoded.
        /// </summary>
        private async Task SetImageFromPath(string? tag, string path)
        {
            if (tag == "avatar")
            {
                ViewModel.AvatarImageUrl = path;
            }
            else
            {
                IImage? image = File.Exists(path) ? BitmapHelper.GetBitmapImage(path) : await BitmapHelper.GetBitmapImageAsync(path);
                if (image != null)
                    ViewModel.BackgroundImageSource = image;
            }
        }
        private string ExtractImageUrlFromHtml(string html)
        {
            Regex regex = new Regex("<img[^>]+?src\\s*=\\s*['\"]([^'\"]+)['\"][^>]*>");
            Match match = regex.Match(html);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return string.Empty;
        }
        private static readonly Dictionary<string, string[]> ImageFileTypes = new Dictionary<string, string[]>
        {
            { "Images", new[] { "*.jpg", "*.jpeg", "*.png", "*.gif", "*.tif", "*.tiff", "*.ico", "*.bmp", "*.webp" } },
        };
        private async void ChooseImage(object? sender, PointerReleasedEventArgs e)
        {
            try
            {
                string? tag = ((Control)sender!).Tag as string;
                string? file = await StorageHelper.PickFileAsync("Select an image", ImageFileTypes);
                if (!string.IsNullOrEmpty(file) && File.Exists(file))
                {
                    await SetImageFromPath(tag, file);
                }
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = ex.Message;
            }
        }

        private async void Image_Paste(object? sender, KeyEventArgs e)
        {
            if (e.KeyModifiers != KeyModifiers.Control || e.Key != Key.V)
                return;
            try
            {
                string? tag = ((Control)sender!).Tag as string;
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard == null)
                    return;
                string[] formats = await clipboard.GetFormatsAsync();
                foreach (string format in new[] { "image/png", "PNG", "image/jpeg", "image/bmp" })
                {
                    if (formats.Contains(format) && await clipboard.GetDataAsync(format) is byte[] data)
                    {
                        if (tag == "avatar")
                        {
                            // The avatar preview works on paths, so the pasted image is stored in a temporary file
                            string tempFile = Path.Combine(Path.GetTempPath(), $"gamevault-avatar-{Guid.NewGuid():N}.png");
                            await File.WriteAllBytesAsync(tempFile, data);
                            ViewModel.AvatarImageUrl = tempFile;
                        }
                        else
                        {
                            ViewModel.BackgroundImageSource = new Bitmap(new MemoryStream(data));
                        }
                        return;
                    }
                }
                string? text = (await clipboard.GetTextAsync())?.Trim();
                if (!string.IsNullOrEmpty(text) && (File.Exists(text) || Uri.IsWellFormedUriString(text, UriKind.Absolute)))
                {
                    await SetImageFromPath(tag, text);
                }
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = ex.Message;
            }
        }

        private async void LoadImageUrl(string url, string tag)
        {
            try
            {
                if (tag == "avatar")
                {
                    ViewModel.AvatarImageUrl = url;
                }
                else
                {
                    ViewModel.BackgroundImageSource = await BitmapHelper.GetBitmapImageAsync(url);
                }
            }
            catch (Exception ex)
            {
                if (url != string.Empty)
                    MainWindowViewModel.Instance.AppBarText = ex.Message;
            }
        }
        #region Generic Events
        private InputTimer backgroundImageUrldebounceTimer { get; set; }
        private InputTimer avatarImageUrldebounceTimer { get; set; }
        private void InitImageUrlTimer()
        {
            if (backgroundImageUrldebounceTimer == null)
            {
                backgroundImageUrldebounceTimer = new InputTimer() { Data = string.Empty };
                backgroundImageUrldebounceTimer.Interval = TimeSpan.FromMilliseconds(400);
                backgroundImageUrldebounceTimer.Tick += BackgroundImageDebounceTimerElapsed;
            }
            if (avatarImageUrldebounceTimer == null)
            {
                avatarImageUrldebounceTimer = new InputTimer() { Data = string.Empty };
                avatarImageUrldebounceTimer.Interval = TimeSpan.FromMilliseconds(400);
                avatarImageUrldebounceTimer.Tick += AvatarImageDebounceTimerElapsed;
            }
        }
        private void BackgoundImageUrl_TextChanged(object? sender, TextChangedEventArgs e)
        {
            InitImageUrlTimer();
            backgroundImageUrldebounceTimer.Stop();
            backgroundImageUrldebounceTimer.Data = ((TextBox)sender!).Text ?? string.Empty;
            backgroundImageUrldebounceTimer.Start();
        }
        private void AvatarImageUrl_TextChanged(object? sender, TextChangedEventArgs e)
        {
            InitImageUrlTimer();
            avatarImageUrldebounceTimer.Stop();
            avatarImageUrldebounceTimer.Data = ((TextBox)sender!).Text ?? string.Empty;
            avatarImageUrldebounceTimer.Start();
        }
        private void BackgroundImageDebounceTimerElapsed(object? sender, EventArgs e)
        {
            backgroundImageUrldebounceTimer.Stop();
            LoadImageUrl(backgroundImageUrldebounceTimer.Data, "");
        }
        private void AvatarImageDebounceTimerElapsed(object? sender, EventArgs e)
        {
            avatarImageUrldebounceTimer.Stop();
            LoadImageUrl(avatarImageUrldebounceTimer.Data, "avatar");
        }
        #endregion
        private async void BackgroundImage_Save(object sender, RoutedEventArgs e)
        {
            await SaveImage("");
        }
        private async void AvatarImage_Save(object sender, RoutedEventArgs e)
        {
            await SaveImage("avatar");
        }
        private async Task SaveImage(string tag)
        {
            bool success = false;
            try
            {
                MemoryStream ms = null;
                string filename = "x.jpg";
                if (tag == "avatar")
                {
                    ViewModel.AvatarImageChanged = false;
                    string avatarImageUrl = ViewModel.AvatarImageUrl.Replace("&amp;", "&");
                    if (System.Uri.IsWellFormedUriString(avatarImageUrl, UriKind.Absolute))
                    {
                        ms = await BitmapHelper.UrlToMemoryStream(avatarImageUrl);
                    }
                    else
                    {
                        ms = BitmapHelper.UriToMemoryStream(avatarImageUrl);
                    }
                    if (GifHelper.IsGif(ms))
                    {
                        filename = "x.gif";
                    }
                }
                else
                {
                    ViewModel.BackgroundImageChanged = false;
                    ms = BitmapHelper.BitmapSourceToMemoryStream(ViewModel.BackgroundImageSource);
                }
                ms.Position = 0;
                string resp = await WebHelper.UploadFileAsync($"{SettingsViewModel.Instance.ServerUrl}/api/media", ms, filename, null);
                ms.Dispose();
                var newImageId = JsonSerializer.Deserialize<Media>(resp).ID;
                try
                {
                    UpdateUserDto updateObject = new UpdateUserDto();
                    if (tag == "avatar")
                    {
                        updateObject.AvatarId = newImageId;
                    }
                    else
                    {
                        updateObject.BackgroundId = newImageId;
                    }
                    string url = $"{SettingsViewModel.Instance.ServerUrl}/api/users/{ViewModel.OriginUser.ID}";
                    if (LoginManager.Instance.GetCurrentUser().ID == ViewModel.OriginUser.ID)
                    {
                        url = @$"{SettingsViewModel.Instance.ServerUrl}/api/users/me";
                    }
                    string updatedUser = await WebHelper.PutAsync(url, JsonSerializer.Serialize(updateObject));
                    ViewModel.OriginUser = JsonSerializer.Deserialize<User>(updatedUser);
                    success = true;
                    MainWindowViewModel.Instance.AppBarText = Loc.T("Successfully updated image");
                }
                catch (Exception ex)
                {
                    string msg = WebExceptionHelper.TryGetServerMessage(ex);
                    MainWindowViewModel.Instance.AppBarText = msg;
                }
                //Update Data Context for Community Page. So that the images are also refreshed there directly
                if (success)
                {
                    await MainWindowViewModel.Instance.AdminConsole.InitUserList();
                    await MainWindowViewModel.Instance.Community.InitUserList();
                    if (LoginManager.Instance.GetCurrentUser().ID == ViewModel.OriginUser.ID)
                    {
                        MainWindowViewModel.Instance.UserAvatar = ViewModel.OriginUser;
                    }
                }
            }
            catch (Exception ex)
            {
                string msg = WebExceptionHelper.TryGetServerMessage(ex);
                MainWindowViewModel.Instance.AppBarText = msg;
            }
        }
        #endregion

        private void UserDetails_DateChanged(object? sender, SelectionChangedEventArgs e) => UserDetails_TextChanged(sender, e);
        private void UserDetails_TextChanged(object? sender, RoutedEventArgs e)
        {
            if (loaded)
            {
                ViewModel.UserDetailsChanged = true;
            }
        }

        private async void SaveUserDetails_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.UserDetailsChanged = false;
            this.IsEnabled = false;

            UpdateUserDto selectedUser = ViewModel.UpdateUser;
            string newPassword = uiUserPassword.Text ?? string.Empty;

            if (newPassword != "")
                selectedUser.Password = newPassword;

            if (selectedUser.BirthDate == ViewModel.OriginUser.BirthDate)//Set birthday to null, so a underage user can edit the rest of its data
            {
                selectedUser.BirthDate = null;
            }

            bool error = false;
            try
            {
                string url = $"{SettingsViewModel.Instance.ServerUrl}/api/users/{ViewModel.OriginUser.ID}";
                if (LoginManager.Instance.GetCurrentUser().ID == ViewModel.OriginUser.ID)
                {
                    url = @$"{SettingsViewModel.Instance.ServerUrl}/api/users/me";
                }
                string result = await WebHelper.PutAsync(url, JsonSerializer.Serialize(selectedUser));
                ViewModel.OriginUser = JsonSerializer.Deserialize<User>(result);
                if (LoginManager.Instance.GetCurrentUser().ID == ViewModel.OriginUser.ID)
                {
                    WebHelper.OverrideCredentials(selectedUser.Username, selectedUser.Password);
                }
                MainWindowViewModel.Instance.AppBarText = Loc.T("Successfully saved user changes");
            }
            catch (Exception ex)
            {
                ConvertToUpdateUser();//Reset to Origin User
                error = true;
                string msg = WebExceptionHelper.TryGetServerMessage(ex);
                MainWindowViewModel.Instance.AppBarText = msg;
            }
            if (!error)
            {
                try
                {
                    ViewModel.OriginUser.Password = newPassword;
                    await HandleChangesOnCurrentUser(ViewModel.OriginUser);
                }
                catch (Exception ex)
                {
                    string msg = WebExceptionHelper.TryGetServerMessage(ex);
                    MainWindowViewModel.Instance.AppBarText = msg;
                }
            }
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

            await MainWindowViewModel.Instance.AdminConsole.InitUserList();
            await MainWindowViewModel.Instance.Community.InitUserList();

        }
        private void CopyUserApiKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ClipboardHelper.SetText(ViewModel.OriginUser.ApiKey);
                MainWindowViewModel.Instance.AppBarText = Loc.T("Copied API Key to Clipboard");
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private void Help_Click(object? sender, PointerReleasedEventArgs e)
        {
            try
            {
                string url = "";
                switch (uiSettingsContent.SelectedIndex)
                {
                    case 0:
                        {
                            url = "https://gamevau.lt/docs/client-docs/gui/#edit-user-images";
                            break;
                        }
                    case 1:
                        {
                            url = "https://gamevau.lt/docs/client-docs/gui#edit-details";
                            break;
                        }
                }
                PlatformInfo.OpenUrl(url);
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = ex.Message;
            }
        }        
    }
}

