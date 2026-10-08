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
    /// The server's news (games added lately, new versions, the administrator's message), and the original project's
    /// news in a second tab.
    /// </summary>
    public partial class NewsPopup : UserControl
    {
        private bool projectNewsLoaded;

        public NewsPopup()
        {
            InitializeComponent();
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

        private void TabServer_Click(object? sender, RoutedEventArgs e) => ShowTab(project: false);

        private async void TabProject_Click(object? sender, RoutedEventArgs e)
        {
            ShowTab(project: true);
            if (projectNewsLoaded)
                return;
            projectNewsLoaded = true;
            try { uiGameVaultNews.Markdown = await WebHelper.GetAsync("https://gamevau.lt/news.md"); }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private void ShowTab(bool project)
        {
            uiTabServer.IsChecked = !project;
            uiTabProject.IsChecked = project;
            uiServerPanel.IsVisible = !project;
            uiGameVaultNews.IsVisible = project;
        }

        private void Item_Click(object? sender, RoutedEventArgs e)
        {
            if (((Control)sender!).DataContext is NewsItem item)
            {
                MainWindowViewModel.Instance.ClosePopup();
                MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(item.Game));
            }
        }

        private void OnClose(object? sender, RoutedEventArgs e)
        {
            MainWindowViewModel.Instance.ClosePopup();
        }
    }
}
