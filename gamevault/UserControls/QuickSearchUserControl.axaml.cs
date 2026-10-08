using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using GameVault.Core;
using GameVault.Core.Library;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>A line of the quick search: a game, a page or an action.</summary>
    public sealed class QuickSearchItem
    {
        public string Title { get; init; } = "";
        public string? Subtitle { get; init; }
        public Game? Game { get; init; }
        public Geometry? Icon { get; init; }
        /// <summary>"Game", "Page", "Action", "Installed"</summary>
        public string Kind { get; init; } = "";
        public Action Run { get; init; } = () => { };
        public int Score { get; set; }
    }

    /// <summary>
    /// Ctrl+K from anywhere: type a few letters to open a game (installed ones first, then the server's), go to a
    /// page or run an action, without the mouse.
    /// </summary>
    public partial class QuickSearchUserControl : UserControl
    {
        public ObservableCollection<QuickSearchItem> Results { get; } = new();
        private readonly DispatcherTimer serverSearch = new() { Interval = TimeSpan.FromMilliseconds(220) };
        private int searchGeneration;

        public QuickSearchUserControl()
        {
            InitializeComponent();
            DataContext = this;
            serverSearch.Tick += async (_, _) =>
            {
                serverSearch.Stop();
                await SearchServer(uiQuery.Text ?? "");
            };
            uiQuery.AddHandler(KeyDownEvent, Query_KeyDown, RoutingStrategies.Tunnel);
        }

        public void Open()
        {
            IsVisible = true;
            uiQuery.Text = "";
            Refresh("");
            Dispatcher.UIThread.Post(() => uiQuery.Focus(), DispatcherPriority.Input);
        }

        public void Close()
        {
            IsVisible = false;
            serverSearch.Stop();
            searchGeneration++;
            MainWindowViewModel.Instance.ActiveControl?.Focus();
        }

        private void Query_TextChanged(object? sender, TextChangedEventArgs e)
        {
            Refresh(uiQuery.Text ?? "");
            serverSearch.Stop();
            if (!string.IsNullOrWhiteSpace(uiQuery.Text) && LoginManager.Instance.IsLoggedIn())
                serverSearch.Start();
        }

        /// <summary>Pages, actions and installed games; the server's games are added a moment later.</summary>
        private void Refresh(string query)
        {
            searchGeneration++;
            var items = new List<QuickSearchItem>();
            if (string.IsNullOrWhiteSpace(query))
            {
                // Nothing typed yet: the last played games and the pages
                items.AddRange(InstalledGames().Take(4));
                items.AddRange(Actions());
            }
            else
            {
                foreach (QuickSearchItem item in InstalledGames().Concat(Actions()))
                {
                    item.Score = Math.Max(QuickMatch.Score(query, item.Title), QuickMatch.Score(query, item.Subtitle) / 2);
                    // Installed games that match come before an equally good page
                    if (item.Game != null && item.Score > 0)
                        item.Score += 50;
                    if (item.Score > 0)
                        items.Add(item);
                }
                items = items.OrderByDescending(i => i.Score).Take(12).ToList();
            }
            Show(items);
        }

        private async Task SearchServer(string query)
        {
            int generation = searchGeneration;
            try
            {
                // The server looks for the text as typed: with several words, ask for the longest one and rank here
                string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string serverQuery = words.Length > 1 ? words.OrderByDescending(w => w.Length).First() : query.Trim();
                string json = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games?search={Uri.EscapeDataString(serverQuery)}&limit=20");
                Game[] games = JsonSerializer.Deserialize<PaginatedData<Game>>(json)?.Data ?? Array.Empty<Game>();
                if (words.Length > 1)
                    games = games.Where(g => QuickMatch.Score(query, g.Title) > 0 || QuickMatch.Score(query, g.Metadata?.Title) > 0).ToArray();
                if (generation != searchGeneration || !IsVisible)
                    return;
                var known = Results.Where(r => r.Game != null).Select(r => r.Game!.ID).ToHashSet();
                var added = games.Where(g => g.DeletedAt == null && !known.Contains(g.ID)).Select(g =>
                {
                    var item = GameItem(g, installed: false);
                    // The server found it (also by other words than the title): never below a weak title match
                    item.Score = Math.Max(QuickMatch.Score(query, g.Title), 100);
                    return item;
                });
                Show(Results.Concat(added).OrderByDescending(i => i.Score).Take(14).ToList(), keepSelection: true);
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private void Show(List<QuickSearchItem> items, bool keepSelection = false)
        {
            var selected = uiResults.SelectedItem as QuickSearchItem;
            Results.Clear();
            foreach (QuickSearchItem item in items)
                Results.Add(item);
            int index = keepSelection && selected != null ? items.IndexOf(selected) : -1;
            uiResults.SelectedIndex = index >= 0 ? index : (items.Count > 0 ? 0 : -1);
        }

        private static IEnumerable<QuickSearchItem> InstalledGames() =>
            InstallViewModel.Instance.InstalledGames.Where(g => g.Key != null).Select(g => GameItem(g.Key, installed: true));

        private static QuickSearchItem GameItem(Game game, bool installed)
        {
            string? genre = game.Metadata?.Genres?.FirstOrDefault()?.Name;
            int? year = game.Metadata?.ReleaseDate?.Year;
            return new QuickSearchItem
            {
                Title = game.Metadata?.Title is { Length: > 0 } title && SettingsViewModel.Instance.ShowMappedTitle ? title : game.Title,
                Subtitle = string.Join(" · ", new[] { genre, year?.ToString() }.Where(p => !string.IsNullOrEmpty(p))),
                Game = game,
                Kind = installed ? Loc.T("Installed") : Loc.T("Game"),
                Run = () => MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(game)),
            };
        }

        private static Geometry? Icon(string key) =>
            Avalonia.Application.Current!.TryGetResource(key, null, out object? value) ? value as Geometry : null;

        private static IEnumerable<QuickSearchItem> Actions()
        {
            QuickSearchItem Page(string title, string icon, MainControl page, string? hint = null) => new()
            {
                Title = Loc.T(title),
                Subtitle = hint == null ? null : Loc.T(hint),
                Icon = Icon(icon),
                Kind = Loc.T("Page"),
                Run = () => MainWindowViewModel.Instance.SetActiveControl(page),
            };
            yield return Page("Library", "IconNavLibrary", MainControl.Library);
            yield return Page("Downloads", "IconNavDownloads", MainControl.Downloads);
            yield return Page("Community", "IconNavCommunity", MainControl.Community);
            yield return Page("Settings", "IconNavSettings", MainControl.Settings, "Themes, language, installation folders");
            if (LoginManager.Instance.GetCurrentUser()?.Role == PERMISSION_ROLE.ADMIN)
                yield return Page("Admin Console", "IconNavAdmin", MainControl.AdminConsole, "Users, publish a game, server");
            yield return new QuickSearchItem
            {
                Title = Loc.T("Reload Library (F5)"),
                Icon = Icon("IconReload"),
                Kind = Loc.T("Action"),
                Run = () =>
                {
                    MainWindowViewModel.Instance.SetActiveControl(MainControl.Library);
                    _ = MainWindowViewModel.Instance.Library.LoadLibrary();
                },
            };
            yield return new QuickSearchItem
            {
                Title = Loc.T("Report a problem"),
                Icon = Icon("IconNavBug"),
                Kind = Loc.T("Action"),
                Run = () => MainWindowViewModel.Instance.OpenPopup(new SettingsComponents.ProblemReportUserControl()),
            };
            yield return new QuickSearchItem
            {
                Title = Loc.T("Your profile"),
                Icon = Icon("IconUser"),
                Kind = Loc.T("Page"),
                Run = () => MainWindowViewModel.Instance.Community.ShowUser(LoginManager.Instance.GetCurrentUser()),
            };
        }

        private void Query_KeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    Move(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    Move(-1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    RunSelected();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
            }
        }

        private void Move(int offset)
        {
            if (Results.Count == 0)
                return;
            uiResults.SelectedIndex = (Math.Max(0, uiResults.SelectedIndex) + offset + Results.Count) % Results.Count;
            uiResults.ScrollIntoView(uiResults.SelectedIndex);
        }

        private void RunSelected()
        {
            if (uiResults.SelectedItem is not QuickSearchItem item)
                return;
            Close();
            try { item.Run(); }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = ex.Message; }
        }

        private void Results_Tapped(object? sender, TappedEventArgs e)
        {
            if (e.Source is Control { DataContext: QuickSearchItem })
                RunSelected();
        }

        private void Backdrop_PointerPressed(object? sender, PointerPressedEventArgs e) => Close();

        private void Card_PointerPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;
    }
}
