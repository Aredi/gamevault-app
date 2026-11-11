using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameVault.Core;
using GameVault.Core.Couch;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>A game of the living-room mode: one of the library or a retro one.</summary>
    internal sealed class CouchItem : ViewModelBase
    {
        public Game? Pc { get; init; }
        public RetroGame? Retro { get; init; }
        public bool IsPc => Pc != null;
        public bool IsRetro => Retro != null;
        public string Title => Retro?.Name ?? Pc?.Title ?? "";
        /// <summary>The same game in two tabs is the same game.</summary>
        public string Key => Retro != null ? $"retro:{Retro.Platform.Slug}:{Retro.Id}" : $"pc:{Pc!.ID}";

        private bool installed;
        public bool Installed { get => installed; set { if (installed != value) { installed = value; OnPropertyChanged(); } } }

        private bool isSelected;
        public bool IsSelected { get => isSelected; set { if (isSelected != value) { isSelected = value; OnPropertyChanged(); } } }
    }

    internal sealed class CouchTab : ViewModelBase
    {
        public string Name { get; init; } = "";
        public List<CouchItem> Items { get; init; } = new();
        private bool isSelected;
        public bool IsSelected { get => isSelected; set { if (isSelected != value) { isSelected = value; OnPropertyChanged(); } } }
    }

    /// <summary>A line of a game's numbers ("Play time", "12 h"), or a button and what it does.</summary>
    internal sealed record CouchStat(string Label, string Value);

    /// <summary>An action of a game's screen or a line of the menu.</summary>
    internal sealed class CouchLine : ViewModelBase
    {
        public string Text { get; init; } = "";
        public Func<Task> Run { get; init; } = () => Task.CompletedTask;
        private bool isSelected;
        public bool IsSelected { get => isSelected; set { if (isSelected != value) { isSelected = value; OnPropertyChanged(); } } }
    }

    public partial class CouchUserControl : UserControl
    {
        // The place of a cover (larger than it: the selected one grows) and what is around the strip and the grid
        private const double CardWidth = 208, CardHeight = 284, StripGap = 0, GridColumnGap = 2, GridRowGap = 2, Side = 84, GridTop = 8, GridBottom = 44;

        internal CouchNavigator Navigator { get; } = new();
        internal List<CouchTab> Tabs { get; private set; } = new();
        internal List<CouchLine> Actions { get; private set; } = new();
        internal List<CouchLine> MenuLines { get; private set; } = new();
        /// <summary>The selected game of the tab shown.</summary>
        internal CouchItem? Current => Navigator.Index >= 0 && Navigator.Tab < Tabs.Count ? Tabs[Navigator.Tab].Items[Navigator.Index] : null;
        /// <summary>Asked by the menu and by a game's "Open its page": the window shows the desktop again.</summary>
        internal event Action? LeaveRequested;

        private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
        private readonly CouchRepeater repeater = new();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private CouchGamepad? gamepad;
        private bool padListened = true;
        private int ticks;
        private CouchItem? shownBack;
        /// <summary>Every game once, for the search.</summary>
        private List<CouchItem> all = new();
        private List<CouchTab> baseTabs = new();
        internal bool Searching { get; private set; }
        internal string Query { get; private set; } = "";
        internal List<CouchLine> Keys { get; private set; } = new();
        private int key;
        private const int KeyColumns = 10;
        private const string Erase = "⌫", Space = "␣", Done = "OK";
        private int detailGeneration, shot = -1, refreshes;
        private bool shotShown;
        private int backGeneration;
        private bool loading;

        public CouchUserControl()
        {
            InitializeComponent();
            uiGridItems.ItemTemplate = uiStripItems.ItemTemplate;
            timer.Tick += (_, _) => Tick();
            SizeChanged += (_, _) => Fit();
            Tapped += Pointed;
            // The second click of two fast ones is a double tap, not a tap: it opens what the first one selected
            DoubleTapped += Pointed;
        }

        /// <summary>The living-room mode is shown: its games are read again, the gamepads listened to.</summary>
        internal async Task OpenAsync()
        {
            IsVisible = true;
            Focus();
            Fit();
            uiAvatar.Data = MainWindowViewModel.Instance.UserAvatar;
            uiPlayer.Text = LoginManager.Instance.GetCurrentUser()?.Username ?? "";
            ShowClock();
            try { gamepad ??= new CouchGamepad(); }
            catch (Exception ex) { Log.Ignored(ex); }
            repeater.Reset(gamepad?.Read() ?? CouchButtons.None);
            timer.Start();
            await LoadAsync();
        }

        internal void Close()
        {
            timer.Stop();
            gamepad?.Dispose();
            gamepad = null;
            IsVisible = false;
        }

        /// <summary>Everything is laid out 1080 high, then scaled to the screen.</summary>
        private void Fit()
        {
            if (Bounds.Height <= 0 || Bounds.Width <= 0)
                return;
            double scale = Bounds.Height / 1080;
            uiScale.LayoutTransform = new ScaleTransform(scale, scale);
            uiStage.Width = Bounds.Width / scale;
            int columns = Math.Max(1, (int)((uiStage.Width - 2 * Side + GridColumnGap) / (CardWidth + GridColumnGap)));
            var grid = (UniformGridLayout)uiGridItems.Layout!;
            if (columns != Navigator.Columns || grid.MaximumRowsOrColumns != columns)
            {
                Navigator.Columns = columns;
                grid.MaximumRowsOrColumns = columns;
                uiGridItems.Width = columns * CardWidth + (columns - 1) * GridColumnGap;
            }
        }

        /// <summary>The library and the retro games, in tabs: what was played, what is installed, everything, then each console.</summary>
        internal async Task LoadAsync()
        {
            if (loading)
                return;
            loading = true;
            try
            {
                if (Tabs.Count == 0)
                {
                    uiEmpty.Text = Loc.T("Loading your games...");
                    uiEmpty.IsVisible = true;
                }
                var pc = new Dictionary<int, Game>();
                foreach (var install in InstallViewModel.Instance.InstalledGames.Where(g => g.Key != null))
                    pc[install.Key.ID] = install.Key;
                try
                {
                    string json = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games?limit=-1");
                    foreach (Game game in JsonSerializer.Deserialize<PaginatedData<Game>>(json)?.Data ?? Array.Empty<Game>())
                        if (game.DeletedAt == null)
                            pc.TryAdd(game.ID, game);
                }
                catch (Exception ex)
                {
                    // Offline: the installed games are enough
                    Log.Ignored(ex);
                }
                List<RetroGame> retro = new();
                try { retro = await MainWindowViewModel.Instance.Retro.AllGamesAsync(); }
                catch (Exception ex) { Log.Ignored(ex); }
                Show(pc.Values, retro);
            }
            finally { loading = false; }
        }

        private static IEnumerable<T> ByTitle<T>(IEnumerable<T> items, Func<T, string> title) => items.OrderBy(title, StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace));

        internal void Show(IEnumerable<Game> pcGames, IEnumerable<RetroGame> retroGames)
        {
            HashSet<int> installed = InstalledPc();
            var records = InstallViewModel.Instance.PlayRecords;
            List<CouchItem> pc = ByTitle(pcGames, g => g.SortTitle is { Length: > 0 } sort ? sort : g.Title ?? "").Select(g => new CouchItem { Pc = g, Installed = installed.Contains(g.ID) }).ToList();
            List<CouchItem> retro = ByTitle(retroGames, g => g.Name).Select(g => new CouchItem { Retro = g, Installed = g.Installed }).ToList();

            var tabs = new List<CouchTab>();
            // What was played last comes first, as on a console
            var recent = pc.Where(i => records.TryGetValue(i.Pc!.ID, out var record) && record.LastPlayedAt != null)
                .OrderByDescending(i => records[i.Pc!.ID].LastPlayedAt).Take(24).ToList();
            if (recent.Count > 0)
                tabs.Add(new CouchTab { Name = Loc.T("Recent"), Items = recent });
            var here = ByTitle(pc.Concat(retro).Where(i => i.Installed), i => i.Title).ToList();
            if (here.Count > 0)
                tabs.Add(new CouchTab { Name = Loc.T("Installed games"), Items = here });
            tabs.Add(new CouchTab { Name = Loc.T("Library"), Items = pc });
            foreach (var platform in retro.GroupBy(i => i.Retro!.Platform.Name).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
                tabs.Add(new CouchTab { Name = platform.Key, Items = platform.ToList() });

            all = pc.Concat(retro).ToList();
            baseTabs = tabs;
            // The same tab and the same game after the games were read again
            string? tabName = Navigator.Tab < Tabs.Count ? Tabs[Navigator.Tab].Name : null;
            string? selectedKey = Current?.Key;
            SetTabs();
            int tab = tabName == null ? 0 : Math.Max(0, Tabs.FindIndex(t => t.Name == tabName));
            Navigator.Select(tab, selectedKey == null ? 0 : Math.Max(0, Tabs[tab].Items.FindIndex(i => i.Key == selectedKey)));
            ShowClock();
            BuildMenu();
            ShowTab();
            ShowScreen();
        }

        /// <summary>The tabs of the games, and last the games found by the search when something is searched.</summary>
        private void SetTabs()
        {
            var tabs = new List<CouchTab>(baseTabs);
            if (Query.Length > 0)
            {
                var found = all
                    .Select(item => (Item: item, Score: GameVault.Core.Library.QuickMatch.Score(Query, item.Title)))
                    .Where(match => match.Score > 0)
                    .OrderByDescending(match => match.Score).ThenBy(match => match.Item.Title, StringComparer.CurrentCultureIgnoreCase)
                    .Select(match => match.Item).ToList();
                tabs.Add(new CouchTab { Name = Loc.F("Search: {0}", Query), Items = found });
            }
            Tabs = tabs;
            Navigator.SetTabs(tabs.Select(t => (IReadOnlyList<string>)t.Items.Select(i => i.Title).ToList()).ToList());
            uiTabs.ItemsSource = Tabs;
        }

        // --- The search: a keyboard on the screen, the games found under it as it is typed ---

        private void OpenSearch()
        {
            if (Keys.Count == 0)
            {
                Keys = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".Select(c => new CouchLine { Text = c.ToString() })
                    .Concat(new[] { new CouchLine { Text = Space }, new CouchLine { Text = Erase }, new CouchLine { Text = Done } }).ToList();
                uiKeys.ItemsSource = Keys;
            }
            Searching = true;
            ShowKey(0);
            if (Query.Length > 0)
                Navigator.Select(Tabs.Count - 1, 0);
            ShowTab();
            ShowScreen();
        }

        private void CloseSearch()
        {
            Searching = false;
            ShowScreen();
        }

        private void ShowKey(int to)
        {
            key = Math.Clamp(to, 0, Keys.Count - 1);
            for (int i = 0; i < Keys.Count; i++)
                Keys[i].IsSelected = i == key;
        }

        private void InSearch(CouchCommand command)
        {
            switch (command)
            {
                case CouchCommand.Left when key % KeyColumns > 0: ShowKey(key - 1); break;
                case CouchCommand.Right when key % KeyColumns < KeyColumns - 1: ShowKey(key + 1); break;
                case CouchCommand.Up when key >= KeyColumns: ShowKey(key - KeyColumns); break;
                case CouchCommand.Down when key / KeyColumns < (Keys.Count - 1) / KeyColumns: ShowKey(key + KeyColumns); break;
                case CouchCommand.Accept: Type(Keys[key].Text); break;
                case CouchCommand.View: Type(Erase); break;
                case CouchCommand.Search: Type(Space); break;
                case CouchCommand.Back or CouchCommand.Menu: CloseSearch(); break;
            }
        }

        /// <summary>A key of the keyboard on the screen, or of the real one.</summary>
        internal void Type(string text)
        {
            if (text == Done)
            {
                CloseSearch();
                return;
            }
            string query = text == Erase ? (Query.Length > 0 ? Query[..^1] : "") : text == Space ? (Query.Length > 0 && !Query.EndsWith(' ') ? Query + " " : Query) : Query + text;
            if (query == Query || query.Length > 30)
                return;
            // The tab shown before the search is shown again when nothing is searched any more
            bool onSearch = Query.Length > 0 && Navigator.Tab == Tabs.Count - 1;
            Query = query;
            SetTabs();
            if (Query.Length > 0)
                Navigator.Select(Tabs.Count - 1, 0);
            else if (onSearch)
                Navigator.Select(Math.Max(0, Tabs.FindIndex(t => t.Name == Loc.T("Library"))), 0);
            ShowTab();
            ShowScreen();
        }

        private static HashSet<int> InstalledPc() => InstallViewModel.Instance.InstalledGames.Where(g => g.Key != null).Select(g => g.Key.ID).ToHashSet();

        // --- What the gamepad and the keyboard ask ---

        private void Tick()
        {
            if (++ticks % 30 == 0)
                Refresh();
            if (gamepad == null)
                return;
            // A game is played, or another window is in front: its gamepad is not ours
            bool listen = TopLevel.GetTopLevel(this) is Window { IsActive: true } && IsVisible;
            CouchButtons held = gamepad.Read();
            if (!listen || !padListened)
            {
                // What is held when the menu comes back is not a press
                repeater.Reset(held);
                padListened = listen;
                return;
            }
            foreach (CouchCommand command in repeater.Next(held, clock.Elapsed))
                Handle(command);
        }

        /// <summary>The keyboard does what the gamepad does.</summary>
        internal bool HandleKey(Key key)
        {
            if (Searching && MainWindowViewModel.Instance.Popup == null && DialogService.Current == null)
            {
                string? typed = key switch
                {
                    >= Key.A and <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
                    >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
                    >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
                    Key.Space => Space,
                    Key.Back => Erase,
                    Key.Enter => Done,
                    _ => null,
                };
                if (typed != null)
                {
                    Type(typed);
                    return true;
                }
            }
            CouchCommand? command = key switch
            {
                Key.Left => CouchCommand.Left, Key.Right => CouchCommand.Right, Key.Up => CouchCommand.Up, Key.Down => CouchCommand.Down,
                Key.Enter or Key.Space => CouchCommand.Accept,
                Key.Escape or Key.Back => CouchCommand.Back,
                Key.Tab => CouchCommand.View,
                Key.PageUp => CouchCommand.PreviousTab, Key.PageDown => CouchCommand.NextTab,
                Key.Home => CouchCommand.PreviousLetter, Key.End => CouchCommand.NextLetter,
                Key.F10 => CouchCommand.Menu,
                Key.F3 => CouchCommand.Search,
                _ => null,
            };
            if (command == null)
                return false;
            Handle(command.Value);
            return true;
        }

        internal void Handle(CouchCommand command)
        {
            // A question of the client (delete? where to install?): its answers, from left to right
            if (DialogService.Current is { } dialog)
            {
                switch (command)
                {
                    case CouchCommand.Left: dialog.Move(-1); break;
                    case CouchCommand.Right: dialog.Move(1); break;
                    case CouchCommand.Accept: dialog.Accept(); break;
                    case CouchCommand.Back: dialog.Cancel(); break;
                }
                return;
            }
            // A window of the client over the games (a notice, a trailer): B closes it, nothing else goes through
            if (MainWindowViewModel.Instance.Popup != null)
            {
                if (command == CouchCommand.Back)
                    MainWindowViewModel.Instance.ClosePopup();
                return;
            }
            if (Searching)
            {
                InSearch(command);
                return;
            }
            if (command == CouchCommand.Search)
            {
                if (Navigator.Screen == CouchScreen.Home)
                    OpenSearch();
                return;
            }
            int tab = Navigator.Tab;
            CouchItem? before = Current;
            switch (Navigator.Apply(command))
            {
                case CouchEffect.Moved:
                    if (Navigator.Tab != tab)
                        ShowTab();
                    else if (!ReferenceEquals(before, Current))
                        ShowSelection(before);
                    if (Navigator.Screen != CouchScreen.Home)
                        ShowScreen(rebuild: !ReferenceEquals(before, Current));
                    break;
                case CouchEffect.ViewChanged:
                case CouchEffect.ScreenChanged:
                    ShowScreen();
                    break;
                case CouchEffect.Action:
                    List<CouchLine> lines = Navigator.Screen == CouchScreen.Menu ? MenuLines : Actions;
                    int line = Navigator.Screen == CouchScreen.Menu ? Navigator.MenuLine : Navigator.Action;
                    if (line < lines.Count)
                        _ = RunAsync(lines[line]);
                    break;
            }
        }

        private async Task RunAsync(CouchLine line)
        {
            try { await line.Run(); }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex);
            }
            Refresh();
        }

        /// <summary>A mouse or a remote that points: a click selects, a click on what is selected opens it.</summary>
        private void Pointed(object? sender, Avalonia.Input.TappedEventArgs e)
        {
            if (MainWindowViewModel.Instance.Popup != null)
                return;
            object? data = (e.Source as Avalonia.Visual)?.GetSelfAndVisualAncestors().OfType<Control>().Select(c => c.DataContext).FirstOrDefault(d => d is CouchItem or CouchLine or CouchTab);
            if (data is CouchItem item && Navigator.Screen == CouchScreen.Home && Navigator.Tab < Tabs.Count && Tabs[Navigator.Tab].Items.IndexOf(item) is >= 0 and int index)
            {
                if (index == Navigator.Index)
                    Handle(CouchCommand.Accept);
                else
                {
                    CouchItem? before = Current;
                    Navigator.Select(Navigator.Tab, index);
                    ShowSelection(before);
                }
            }
            else if (data is CouchTab tab && Navigator.Screen == CouchScreen.Home && Tabs.IndexOf(tab) is >= 0 and int to && to != Navigator.Tab)
            {
                Navigator.SelectTab(to);
                ShowTab();
            }
            else if (data is CouchLine line && (Navigator.Screen == CouchScreen.Menu ? MenuLines : Actions).IndexOf(line) is >= 0 and int at)
            {
                Navigator.SelectLine(at);
                Handle(CouchCommand.Accept);
            }
        }

        // --- What is shown ---

        /// <summary>The tab's games in the strip and the grid, its selected game shown.</summary>
        private void ShowTab()
        {
            for (int i = 0; i < Tabs.Count; i++)
                Tabs[i].IsSelected = i == Navigator.Tab;
            List<CouchItem> items = Navigator.Tab < Tabs.Count ? Tabs[Navigator.Tab].Items : new();
            foreach (CouchItem item in items)
                item.IsSelected = false;
            uiStripItems.ItemsSource = items;
            uiGridItems.ItemsSource = items;
            uiEmpty.Text = Loc.T("No game here yet.");
            uiEmpty.IsVisible = items.Count == 0;
            // The selected tab in sight among the tabs
            Dispatcher.UIThread.Post(() => (uiTabs.ContainerFromIndex(Navigator.Tab) as Control)?.BringIntoView(), DispatcherPriority.Background);
            ShowSelection(null);
        }

        private void ShowSelection(CouchItem? before)
        {
            if (before != null)
                before.IsSelected = false;
            CouchItem? item = Current;
            if (item != null)
                item.IsSelected = true;
            ShowHero(item);
            ScrollToSelection();
        }

        /// <summary>The selected cover in the middle of the strip, its row in the middle of the grid.</summary>
        private void ScrollToSelection()
        {
            int index = Math.Max(0, Navigator.Index);
            double x = Side + index * (CardWidth + StripGap) + CardWidth / 2 - uiStrip.Viewport.Width / 2;
            double y = GridTop + index / Navigator.Columns * (CardHeight + GridRowGap) + CardHeight / 2 - uiGrid.Viewport.Height / 2;
            // Not past its end: the strip's and the grid's lengths are known from their games
            int count = Navigator.Count, rows = (count + Navigator.Columns - 1) / Navigator.Columns;
            double maxX = 2 * Side + count * CardWidth + Math.Max(0, count - 1) * StripGap - uiStrip.Viewport.Width;
            double maxY = GridTop + GridBottom + rows * CardHeight + Math.Max(0, rows - 1) * GridRowGap - uiGrid.Viewport.Height;
            uiStrip.Offset = new Vector(Math.Clamp(x, 0, Math.Max(0, maxX)), 0);
            uiGrid.Offset = new Vector(0, Math.Clamp(y, 0, Math.Max(0, maxY)));
        }

        internal string HeroTitle => uiTitle.Text ?? "";
        internal IReadOnlyList<CouchStat> Stats { get; private set; } = Array.Empty<CouchStat>();
        internal IReadOnlyList<CouchStat> Hints { get; private set; } = Array.Empty<CouchStat>();

        /// <summary>The selected game's name and numbers; its picture a moment later (a held direction passes many games).</summary>
        private void ShowHero(CouchItem? item)
        {
            uiHero.IsVisible = item != null && !Navigator.IsGrid && !Searching;
            uiTitle.Text = uiGridTitle.Text = item?.Title ?? "";
            uiEyebrow.Text = item == null ? "" : (item.Retro?.Platform.Name ?? Loc.T("Library")).ToUpper(CultureInfo.CurrentCulture);
            uiMeta.Text = item == null ? "" : MetaOf(item);
            Stats = item == null ? Array.Empty<CouchStat>() : StatsOf(item);
            uiStats.ItemsSource = Stats;
            uiStatsCard.IsVisible = Stats.Count > 0;
            int generation = ++backGeneration;
            DispatcherTimer.RunOnce(() =>
            {
                if (generation == backGeneration)
                    ShowBack(item);
            }, TimeSpan.FromMilliseconds(220));
        }

        private void ShowBack(CouchItem? item)
        {
            if (ReferenceEquals(item, shownBack))
                return;
            shownBack = item;
            uiBack.Opacity = 0;
            DispatcherTimer.RunOnce(() =>
            {
                if (!ReferenceEquals(item, shownBack))
                    return;
                uiBackPc.IsVisible = item?.Pc != null;
                uiBackRetro.IsVisible = item?.Retro != null;
                if (item?.Pc != null)
                    uiBackPc.Data = item.Pc;
                if (item?.Retro is RetroGame retro)
                {
                    uiBackRetro.Source = retro.Banner;
                    _ = ShowRetroBackAsync(item, retro);
                }
                uiBack.Opacity = item == null ? 0 : 1;
            }, TimeSpan.FromMilliseconds(140));
        }

        /// <summary>A retro game's first screenshot, larger than its cover, once it is read.</summary>
        private async Task ShowRetroBackAsync(CouchItem item, RetroGame retro)
        {
            try
            {
                await retro.LoadScreenshotsAsync();
                if (ReferenceEquals(item, shownBack))
                    uiBackRetro.Source = retro.Banner;
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private static string Minutes(int minutes) => (string)new Converter.GameTimeConverter().Convert(minutes, typeof(string), null, CultureInfo.CurrentCulture)!;

        private static int MinutesOf(CouchItem item) => item.Retro?.MinutesPlayed
            ?? (InstallViewModel.Instance.PlayRecords.TryGetValue(item.Pc!.ID, out var record) ? record.MinutesPlayed : 0);

        private static string MetaOf(CouchItem item)
        {
            if (item.Retro is RetroGame retro)
                return retro.Info;
            Game game = item.Pc!;
            string? year = (game.ReleaseDate ?? game.Metadata?.ReleaseDate)?.Year.ToString();
            string? genre = game.Metadata?.Genres?.Select(g => g.Name).FirstOrDefault(n => !string.IsNullOrEmpty(n));
            return string.Join(" · ", new[] { year, genre }.Where(p => !string.IsNullOrEmpty(p)));
        }

        private static IReadOnlyList<CouchStat> StatsOf(CouchItem item)
        {
            var stats = new List<CouchStat>();
            int minutes = MinutesOf(item);
            stats.Add(new CouchStat(Loc.T("Play time"), minutes > 0 ? Minutes(minutes) : Loc.T("Not played yet")));
            string? size = item.Retro != null ? (item.Retro.Size > 0 ? item.Retro.SizeText : null)
                : long.TryParse(item.Pc!.Size, out long bytes) && bytes > 0 ? Converter.GameSizeConverter.Text(bytes) : null;
            if (!string.IsNullOrEmpty(size))
                stats.Add(new CouchStat(Loc.T("Size"), size));
            stats.Add(new CouchStat(Loc.T("On this PC"), item.Installed ? Loc.T("Installed") : Loc.T("Not installed")));
            return stats;
        }

        /// <summary>The list of games, a game's screen or the menu, with what the buttons do there.</summary>
        private void ShowScreen(bool rebuild = true)
        {
            CouchScreen screen = Navigator.Screen;
            CouchItem? item = Current;
            bool details = screen == CouchScreen.Details && item != null;
            uiHome.IsVisible = !details;
            uiDetails.IsVisible = details;
            uiMenu.IsVisible = screen == CouchScreen.Menu;
            // The keyboard takes the place of the selected game's name, over the strip of the games found
            bool grid = Navigator.IsGrid && !Searching;
            uiStrip.IsVisible = !grid;
            uiGrid.IsVisible = uiGridTitle.IsVisible = grid;
            uiHero.IsVisible = item != null && !grid && !Searching;
            uiKeyboard.IsVisible = Searching && !details;
            uiQuery.Text = Query.Length > 0 ? Query : Loc.T("Search a game");
            uiQuery.Opacity = Query.Length > 0 ? 1 : 0.5;
            uiDim.IsVisible = details || grid || Searching;
            if (!details && shotShown)
            {
                shotShown = false;
                ShowBack(item);
            }
            if (details && rebuild)
                ShowDetails(item!);
            for (int i = 0; i < Actions.Count; i++)
                Actions[i].IsSelected = i == Navigator.Action;
            for (int i = 0; i < MenuLines.Count; i++)
                MenuLines[i].IsSelected = i == Navigator.MenuLine;
            uiPadState.Text = gamepad is { CanRead: true } ? "" : Loc.T("No gamepad can be read on this PC: the arrows, Enter and Escape do the same.");

            Hints = Searching ? new[] { new CouchStat("A", Loc.T("Type")), new CouchStat("X", Loc.T("Erase")), new CouchStat("Y", Loc.T("Space")), new CouchStat("B", Loc.T("Close")) } : screen switch
            {
                CouchScreen.Menu => new[] { new CouchStat("A", Loc.T("Choose")), new CouchStat("B", Loc.T("Back")) },
                CouchScreen.Details => new[] { new CouchStat("A", Loc.T("Choose")), new CouchStat("B", Loc.T("Back")), new CouchStat("LB RB", Loc.T("Other games")), new CouchStat("START", Loc.T("Menu")) },
                _ => new[]
                {
                    new CouchStat("A", Loc.T("Open")), new CouchStat("X", Navigator.IsGrid ? Loc.T("Strip") : Loc.T("Grid")), new CouchStat("Y", Loc.T("Search")),
                    new CouchStat("LB RB", Loc.T("Tabs")), new CouchStat("LT RT", Loc.T("Letters")), new CouchStat("START", Loc.T("Menu")),
                },
            };
            uiHints.ItemsSource = Hints;
            Dispatcher.UIThread.Post(ScrollToSelection, DispatcherPriority.Background);
        }

        private void ShowDetails(CouchItem item)
        {
            uiDetailCoverPc.IsVisible = item.Pc != null;
            uiDetailCoverRetro.IsVisible = item.Retro != null;
            if (item.Pc != null)
                uiDetailCoverPc.Data = item.Pc;
            uiDetailCoverRetro.Source = item.Retro?.CoverImage;
            uiDetailEyebrow.Text = (item.Retro?.Platform.Name ?? Loc.T("Library")).ToUpper(CultureInfo.CurrentCulture);
            uiDetailTitle.Text = item.Title;
            uiDetailSub.Text = MetaOf(item);
            uiDetailStats.ItemsSource = StatsOf(item);
            string? description = item.Retro?.Summary ?? item.Pc?.Metadata?.Description;
            uiDescription.Text = description;
            uiDescriptionCard.IsVisible = !string.IsNullOrWhiteSpace(description);
            BuildActions(item);
            ShowStatus(item);
            shot = -1;
            refreshes = 0;
            int generation = ++detailGeneration;
            if (item.Retro is RetroGame retro && RommAccount.IsLinked)
                _ = ShowAchievementsAsync(item, retro, generation);
        }

        /// <summary>A retro game's achievements earned, among its numbers, once RomM answered.</summary>
        private async Task ShowAchievementsAsync(CouchItem item, RetroGame retro, int generation)
        {
            try
            {
                JsonElement rom = await RommAccount.RomAsync(retro.Id);
                JsonElement me = default;
                try { me = await RommAccount.MeAsync(); }
                catch (Exception ex) { Log.Ignored(ex); }
                var achievements = GameVault.Core.Retro.RommProgress.AchievementsOf(rom, me);
                if (achievements.Count == 0 || generation != detailGeneration)
                    return;
                uiDetailStats.ItemsSource = StatsOf(item).Append(new CouchStat(Loc.T("Achievements"), Loc.F("{0} of {1}", achievements.Count(a => a.Earned), achievements.Count))).ToList();
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>The game's screenshots behind its screen, one after the other.</summary>
        private async Task NextShotAsync(CouchItem item)
        {
            object game = (object?)item.Retro ?? item.Pc!;
            List<string> sources = CoverSlideshow.Sources(game);
            if (sources.Count == 0)
                return;
            int generation = detailGeneration;
            shot = (shot + 1) % sources.Count;
            Avalonia.Media.Imaging.Bitmap? image = await CoverSlideshow.LoadAsync(game, sources[shot]);
            if (image == null || generation != detailGeneration || Navigator.Screen != CouchScreen.Details)
                return;
            shotShown = true;
            shownBack = null;
            uiBack.Opacity = 0;
            DispatcherTimer.RunOnce(() =>
            {
                if (generation != detailGeneration || !shotShown)
                    return;
                uiBackPc.IsVisible = false;
                uiBackRetro.IsVisible = true;
                uiBackRetro.Source = image;
                uiBack.Opacity = 1;
            }, TimeSpan.FromMilliseconds(300));
        }

        /// <summary>What can be done with the game from the couch; the rest is on its page.</summary>
        private void BuildActions(CouchItem item)
        {
            var lines = new List<CouchLine>();
            if (item.Retro is RetroGame retro)
            {
                if (item.Installed)
                    lines.Add(new CouchLine { Text = Loc.T("Play"), Run = () => RetroPlay.PlayAsync(retro) });
                else
                {
                    lines.Add(new CouchLine { Text = Loc.T("Install and play"), Run = () => RetroPlay.InstallAsync(retro, playWhenDone: true) });
                    lines.Add(new CouchLine { Text = Loc.T("Install"), Run = () => RetroPlay.InstallAsync(retro) });
                }
                if (retro.HasVideo)
                    lines.Add(new CouchLine { Text = Loc.T("Watch the trailer"), Run = () => { MainWindowViewModel.Instance.OpenPopup(new TrailerPopup(new[] { $"https://www.youtube.com/watch?v={Uri.EscapeDataString(retro.Video!)}" }, retro.Name)); return Task.CompletedTask; } });
                lines.Add(new CouchLine { Text = Loc.T("Open its page"), Run = () => { LeaveRequested?.Invoke(); return MainWindowViewModel.Instance.Retro.ShowGameAsync(retro.Platform.Slug, retro.Id); } });
            }
            else
            {
                Game game = item.Pc!;
                if (item.Installed)
                    lines.Add(new CouchLine { Text = Loc.T("Play"), Run = () => InstallUserControl.PlayGame(game.ID) });
                else
                {
                    lines.Add(new CouchLine { Text = Loc.T("Install and play"), Run = () => MainWindowViewModel.Instance.Downloads.InstallAndPlay(game) });
                    lines.Add(new CouchLine { Text = Loc.T("Install"), Run = async () => { await MainWindowViewModel.Instance.Downloads.TryStartDownload(game); } });
                }
                if (game.Metadata is GameMetadata metadata && TrailerPopup.HasVideos(metadata))
                    lines.Add(new CouchLine { Text = Loc.T("Watch the trailer"), Run = () => { MainWindowViewModel.Instance.OpenPopup(new TrailerPopup(metadata, item.Title)); return Task.CompletedTask; } });
                lines.Add(new CouchLine { Text = Loc.T("Open its page"), Run = () => { LeaveRequested?.Invoke(); MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(game)); return Task.CompletedTask; } });
            }
            Actions = lines;
            Navigator.ActionCount = lines.Count;
            uiActions.ItemsSource = lines;
        }

        private void BuildMenu()
        {
            bool atStart = StartsHere;
            MenuLines = new List<CouchLine>
            {
                new() { Text = Loc.T("Back to the games"), Run = () => { Navigator.CloseMenu(); ShowScreen(); return Task.CompletedTask; } },
                new() { Text = Loc.T("Read the games again"), Run = async () => { Navigator.CloseMenu(); ShowScreen(); await LoadAsync(); } },
                new() { Text = atStart ? Loc.T("At start: the living-room mode") : Loc.T("At start: the desktop"), Run = () => { StartsHere = !atStart; BuildMenu(); ShowScreen(false); return Task.CompletedTask; } },
                new() { Text = Loc.T("Back to the desktop"), Run = () => { Navigator.CloseMenu(); ShowScreen(); LeaveRequested?.Invoke(); return Task.CompletedTask; } },
                new() { Text = Loc.T("Quit SanctuaryVault"), Run = () => App.Instance.ExitApp() },
            };
            Navigator.MenuCount = MenuLines.Count;
            uiMenuLines.ItemsSource = MenuLines;
        }

        /// <summary>The client opens in the living-room mode (a PC plugged to the TV).</summary>
        internal static bool StartsHere
        {
            get
            {
                try { return Preferences.Get(AppConfigKey.CouchAtStart, ProfileManager.ProfileConfigFile) == "1"; }
                catch (Exception ex) { Log.Ignored(ex); return false; }
            }
            set
            {
                try { Preferences.Set(AppConfigKey.CouchAtStart, value ? "1" : "0", ProfileManager.ProfileConfigFile); }
                catch (Exception ex) { Log.Ignored(ex); }
            }
        }

        private void ShowStatus(CouchItem item)
        {
            string status = item.Retro?.Status ?? "";
            // A library game's download, as the Downloads page tells it
            if (item.Pc != null && DownloadsViewModel.Instance.DownloadedGames.FirstOrDefault(d => d.GetGameId() == item.Pc.ID) is { } download && !item.Installed)
                status = download.IsDownloading() ? Loc.F("Downloading: {0}%", download.GetDownloadProgress()) : download.StateText;
            uiDetailStatus.Text = status;
            uiDetailStatus.IsVisible = status.Length > 0;
        }

        private void ShowClock() => uiClock.Text = DateTime.Now.ToString("t", CultureInfo.CurrentCulture);

        /// <summary>Twice a second: the time, a game installed or removed meanwhile, a retro game's progress.</summary>
        private void Refresh()
        {
            ShowClock();
            HashSet<int>? installed = null;
            foreach (CouchItem item in Navigator.Tab < Tabs.Count ? Tabs[Navigator.Tab].Items : new())
            {
                bool now = item.Retro?.Installed ?? (installed ??= InstalledPc()).Contains(item.Pc!.ID);
                if (now == item.Installed)
                    continue;
                item.Installed = now;
                if (ReferenceEquals(item, Current))
                {
                    ShowHero(item);
                    if (Navigator.Screen == CouchScreen.Details)
                        ShowScreen();
                }
            }
            if (Navigator.Screen == CouchScreen.Details && Current is CouchItem current)
            {
                ShowStatus(current);
                // A screenshot after 3 seconds, then one every 6
                if (++refreshes % 12 == 6 && MainWindowViewModel.Instance.Popup == null)
                    _ = NextShotAsync(current);
            }
            DialogService.Current?.Mark();
            int unread = MainWindowViewModel.Instance.IsChatAvailable ? ChatService.Instance.TotalUnread : 0;
            uiUnreadPill.IsVisible = unread > 0;
            uiUnread.Text = Loc.Plural(unread, "{0} message", "{0} messages");
        }
    }
}
