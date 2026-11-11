using Avalonia.Controls;
using Avalonia.Media.Imaging;
using GameVault.Core;
using GameVault.Core.Couch;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using gamevault.Windows;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace gamevault.UserControls
{
    /// <summary>A profile of this PC on the login screen: its name, its server, its avatar.</summary>
    internal sealed class LoginProfile : ViewModelBase
    {
        public string RootDir { get; init; } = "";
        public string Name { get; init; } = "";
        public string Server { get; init; } = "";
        public string Initial => Name.Length > 0 ? Name[..1].ToUpper(CultureInfo.CurrentCulture) : "?";
        /// <summary>The profile signed in now: its avatar as the client shows it.</summary>
        public bool IsCurrent { get; init; }
        public User? AvatarData { get; init; }
        /// <summary>Another profile's avatar, as it was kept on this PC.</summary>
        public Bitmap? Avatar { get; init; }
        private string levelText = "";
        /// <summary>The player's level, for the profile signed in.</summary>
        public string LevelText { get => levelText; set { if (levelText != value) { levelText = value; OnPropertyChanged(); } } }
        private bool isSelected;
        public bool IsSelected { get => isSelected; set { if (isSelected != value) { isSelected = value; OnPropertyChanged(); } } }
    }

    /// <summary>
    /// The login screen, as Aniki ReMake's: shown when the mode opens with the client, and from the menu to change of
    /// profile. The time and the date, the profiles of this PC (left and right), "press A" to come in.
    /// </summary>
    public partial class CouchUserControl
    {
        internal List<LoginProfile> LoginProfiles { get; private set; } = new();
        internal int LoginSelected { get; private set; }
        internal bool LoginShown => uiLogin.IsVisible;

        /// <summary>Signs in with another profile of this PC (the tests look at which instead): the client's login window takes it.</summary>
        internal static Action<string> SwitchProfile { get; set; } = rootDir =>
        {
            Preferences.Set(AppConfigKey.LastUserProfile, rootDir, ProfileManager.ProfileConfigFile);
            // The mode opens again with the other profile, without asking it to log in a second time
            skipLoginOnce = true;
            var mainWindow = (MainWindow)App.Instance.MainWindow!;
            var loginWindow = new LoginWindow();
            App.Instance.MainWindow = loginWindow;
            loginWindow.Show();
            mainWindow.Dispose();
        };

        private static bool skipLoginOnce;

        /// <summary>The login screen when the mode opens with the client, unless a profile was just chosen on it.</summary>
        private void ShowLoginAtStart()
        {
            if (skipLoginOnce)
            {
                skipLoginOnce = false;
                return;
            }
            ShowLogin();
        }

        /// <summary>The profiles of this PC, the one signed in first and chosen.</summary>
        private static List<LoginProfile> ProfilesOfThisPc()
        {
            UserProfile? current = null;
            try { current = LoginManager.Instance.GetUserProfile(); }
            catch (Exception ex) { Log.Ignored(ex); }
            var profiles = new List<LoginProfile>
            {
                new()
                {
                    RootDir = current?.RootDir ?? "",
                    Name = LoginManager.Instance.GetCurrentUser()?.Username ?? current?.Name ?? "",
                    Server = HostOf(SettingsViewModel.Instance.ServerUrl),
                    IsCurrent = true,
                    AvatarData = MainWindowViewModel.Instance.UserAvatar,
                },
            };
            try
            {
                foreach (UserProfile other in ProfileManager.GetUserProfiles().Where(p => p.RootDir != current?.RootDir))
                {
                    Bitmap? avatar = null;
                    try
                    {
                        if (!string.IsNullOrEmpty(other.UserCacheAvatar) && File.Exists(other.UserCacheAvatar))
                            avatar = new Bitmap(other.UserCacheAvatar);
                    }
                    catch (Exception ex) { Log.Ignored(ex); }
                    profiles.Add(new LoginProfile { RootDir = other.RootDir, Name = other.Name ?? "", Server = HostOf(other.ServerUrl), Avatar = avatar });
                }
            }
            catch (Exception ex) { Log.Ignored(ex); }
            return profiles;
        }

        private static string HostOf(string? url) => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.Host : url ?? "";

        /// <param name="profiles">The profiles to show (the tests give theirs); none: those of this PC.</param>
        internal void ShowLogin(List<LoginProfile>? profiles = null)
        {
            LoginProfiles = profiles ?? ProfilesOfThisPc();
            LoginSelected = 0;
            uiLoginProfiles.ItemsSource = LoginProfiles;
            if (Level != null)
                foreach (LoginProfile profile in LoginProfiles.Where(p => p.IsCurrent))
                    profile.LevelText = uiLevelText.Text ?? "";
            ShowLoginBackground();
            ShowLoginSelection();
            ShowLoginClock();
            uiLogin.IsVisible = true;
            Play(CouchSound.Login);
        }

        /// <summary>Behind it: the game played last, or a background made here, as the customization chose.</summary>
        private void ShowLoginBackground()
        {
            string background = Settings.LoginBackground;
            CouchItem? art = background == "game" ? homeArt : null;
            uiLoginPc.IsVisible = art?.Pc != null;
            if (art?.Pc != null)
                uiLoginPc.Data = art.Pc;
            uiLoginRetro.IsVisible = art?.Retro != null;
            uiLoginRetro.Source = art?.Retro?.Banner;
            uiLoginPack.Children.Clear();
            // No game played yet: the aurora
            Control? scene = art != null ? null : background switch
            {
                "folder" => FolderPicture() ?? Aurora(),
                "night" => Night(),
                "synthwave" => Synthwave(),
                "accent" => Accent(),
                _ => Aurora(),
            };
            if (scene != null)
                uiLoginPack.Children.Add(scene);
        }

        private void ShowLoginSelection()
        {
            for (int i = 0; i < LoginProfiles.Count; i++)
                LoginProfiles[i].IsSelected = i == LoginSelected;
            LoginProfile chosen = LoginProfiles[LoginSelected];
            string a = CouchGlyphs.Label(ButtonStyle, "A");
            uiLoginPrompt.Text = chosen.IsCurrent ? Loc.F("Press {0} to come in", a) : Loc.F("Press {0} to sign in as {1}", a, chosen.Name);
            uiLoginOthers.Text = LoginProfiles.Count > 1 ? Loc.T("Left and right: another profile of this PC") : "";
        }

        private void ShowLoginClock()
        {
            DateTime now = DateTime.Now;
            uiLoginTime.Text = now.ToString("t", CultureInfo.CurrentCulture);
            // The date without the weekday, as the client writes it
            uiLoginDate.Text = now.ToString("d MMMM yyyy", CultureInfo.CurrentCulture);
        }

        /// <summary>The gamepad on the login screen: left and right choose a profile, A comes in with it.</summary>
        private void HandleLogin(CouchCommand command)
        {
            switch (command)
            {
                case CouchCommand.Left when LoginSelected > 0:
                case CouchCommand.Right when LoginSelected < LoginProfiles.Count - 1:
                    LoginSelected += command == CouchCommand.Left ? -1 : 1;
                    Play(CouchSound.Move);
                    ShowLoginSelection();
                    break;
                case CouchCommand.Left or CouchCommand.Right:
                    Play(CouchSound.Edge);
                    break;
                case CouchCommand.Accept or CouchCommand.Menu:
                    LoginProfile chosen = LoginProfiles[LoginSelected];
                    if (chosen.IsCurrent)
                    {
                        Play(CouchSound.Open);
                        uiLogin.IsVisible = false;
                        idle.Restart();
                    }
                    else
                    {
                        Play(CouchSound.Launch);
                        SwitchProfile(chosen.RootDir);
                    }
                    break;
            }
        }
    }
}
