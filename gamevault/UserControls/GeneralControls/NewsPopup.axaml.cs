using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using gamevault.Helper;
using gamevault.ViewModels;
using GameVault.Core;
using System;

namespace gamevault.UserControls
{
    /// <summary>
    /// The server's news: games added lately, new versions, the administrator's message.
    /// </summary>
    public partial class NewsPopup : UserControl
    {
        public NewsPopup()
        {
            InitializeComponent();
            MarkdownLinks.KeepSafe(uiAnnouncementText);
            Loaded += UserControl_Loaded;
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                    OnClose(this, e);
            };
        }

        private async void UserControl_Loaded(object? sender, RoutedEventArgs e)
        {
            this.Focus();
            try { uiServerName.Text = new Uri(SettingsViewModel.Instance.ServerUrl).Host; }
            catch (Exception ex) { Log.Ignored(ex); }
            try
            {
                ServerNewsData news = await ServerNewsService.LoadAsync();
                if (news.Announcement != null)
                {
                    uiAnnouncementText.Markdown = news.Announcement;
                    uiAnnouncement.IsVisible = true;
                }
                uiGroups.ItemsSource = news.Groups;
                uiEmpty.IsVisible = news.Groups.Count == 0;
                ServerNewsService.MarkSeen();
                MainWindowViewModel.Instance.NewsBadge = "";
            }
            catch (Exception ex) { Log.Ignored(ex); }
            uiLoading.IsActive = false;
            uiLoading.IsVisible = false;
        }

        private void Item_Click(object? sender, RoutedEventArgs e)
        {
            if (((Control)sender!).DataContext is not NewsItem item)
                return;
            MainWindowViewModel.Instance.ClosePopup();
            if (item.Retro is RetroGame retro)
                _ = MainWindowViewModel.Instance.Retro.ShowGameAsync(retro.Platform.Slug, retro.Id);
            else if (item.Game != null)
                MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(item.Game));
        }

        private void OnClose(object? sender, RoutedEventArgs e)
        {
            MainWindowViewModel.Instance.ClosePopup();
        }
    }
}
