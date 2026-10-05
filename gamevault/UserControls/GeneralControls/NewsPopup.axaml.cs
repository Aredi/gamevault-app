using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GameVault.Core;
using gamevault.Helper;
using gamevault.ViewModels;
using System;

namespace gamevault.UserControls
{
    public partial class NewsPopup : UserControl
    {
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
            try
            {
                string gameVaultNews = await WebHelper.GetAsync("https://gamevau.lt/news.md");
                uiGameVaultNews.Markdown = gameVaultNews;
                string serverNews = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/config/news");
                uiServerNews.Markdown = serverNews;
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }

        private void OnClose(object? sender, RoutedEventArgs e)
        {
            MainWindowViewModel.Instance.ClosePopup();
        }
    }
}
