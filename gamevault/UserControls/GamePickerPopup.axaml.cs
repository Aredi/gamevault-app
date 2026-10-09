using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace gamevault.UserControls
{
    /// <summary>Picks a game of the server: the suggested ones (the player's games) until something is searched.</summary>
    public partial class GamePickerPopup : UserControl
    {
        private readonly IReadOnlyList<Game> suggestions;
        private readonly Action<Game> picked;
        private readonly HashSet<int> excluded;
        private readonly DispatcherTimer searchDelay = new() { Interval = TimeSpan.FromMilliseconds(350) };
        private int searchGeneration;

        public GamePickerPopup() : this("", Array.Empty<Game>(), Array.Empty<int>(), _ => { }) { }

        internal GamePickerPopup(string title, IEnumerable<Game> suggestions, IEnumerable<int> excluded, Action<Game> picked)
        {
            InitializeComponent();
            uiTitle.Text = title;
            this.excluded = excluded.ToHashSet();
            this.suggestions = suggestions.Where(g => !this.excluded.Contains(g.ID)).ToList();
            this.picked = picked;
            Show(this.suggestions);
            searchDelay.Tick += async (_, _) =>
            {
                searchDelay.Stop();
                await SearchAsync(uiSearch.Text ?? "");
            };
            Loaded += (_, _) => uiSearch.Focus();
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Close();
                }
            };
        }

        internal IEnumerable<Game> Shown => uiResults.ItemsSource as IEnumerable<Game> ?? Array.Empty<Game>();

        private void Show(IReadOnlyList<Game> games)
        {
            uiResults.ItemsSource = games;
            uiEmpty.IsVisible = games.Count == 0;
        }

        private void Search_TextChanged(object? sender, TextChangedEventArgs e)
        {
            searchDelay.Stop();
            searchDelay.Start();
        }

        private async System.Threading.Tasks.Task SearchAsync(string text)
        {
            int generation = ++searchGeneration;
            text = text.Trim();
            if (text.Length < 2)
            {
                Show(suggestions);
                return;
            }
            try
            {
                string json = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games?search={Uri.EscapeDataString(text)}&limit=40");
                Game[] games = JsonSerializer.Deserialize<PaginatedData<Game>>(json)?.Data ?? Array.Empty<Game>();
                if (generation == searchGeneration)
                    Show(games.Where(g => !excluded.Contains(g.ID)).ToList());
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                if (generation == searchGeneration)
                    MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex);
            }
        }

        private void Game_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not Game game)
                return;
            Close();
            picked(game);
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => Close();

        private void Close()
        {
            searchDelay.Stop();
            MainWindowViewModel.Instance.ClosePopup();
        }
    }
}
