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
        /// <summary>Not a game: a tile of the home (resume, the challenge, an application).</summary>
        public CouchTile? Tile { get; init; }
        public bool IsPc => Pc != null;
        public bool IsRetro => Retro != null;
        public bool IsTile => Tile != null;
        public string Title => Tile?.Title ?? Retro?.Name ?? Pc?.Title ?? "";
        /// <summary>The same game in two tabs is the same game.</summary>
        public string Key => Tile != null ? $"tile:{Tile.Id}" : Retro != null ? $"retro:{Retro.Platform.Slug}:{Retro.Id}" : $"pc:{Pc!.ID}";

        private bool installed;
        public bool Installed { get => installed; set { if (installed != value) { installed = value; OnPropertyChanged(); } } }

        private bool isSelected;
        public bool IsSelected { get => isSelected; set { if (isSelected != value) { isSelected = value; OnPropertyChanged(); } } }
    }

    /// <summary>A tile of the home: what it is, what it tells now, and what A does on it.</summary>
    internal sealed class CouchTile : ViewModelBase
    {
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        private string subtitle = "";
        public string Subtitle { get => subtitle; set { if (subtitle != value) { subtitle = value; OnPropertyChanged(); } } }
        public Func<Task> Run { get; init; } = () => Task.CompletedTask;
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
        /// <summary>A second line under the text (an achievement's description); none for most lines.</summary>
        public string? Detail { get; init; }
        public bool HasDetail => !string.IsNullOrEmpty(Detail);
        /// <summary>A picture before the text (an achievement's badge), once loaded.</summary>
        private Avalonia.Media.Imaging.Bitmap? picture;
        public Avalonia.Media.Imaging.Bitmap? Picture { get => picture; set { picture = value; OnPropertyChanged(); } }
        public double Opacity { get; init; } = 1;
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
        private CouchAudio? audio;
        /// <summary>The sounds played (the tests count them).</summary>
        internal List<CouchSound> Played { get; } = new();

        /// <summary>A short sound for what the gamepad just did, unless they are turned off in the menu.</summary>
        private readonly Stopwatch edgeSound = Stopwatch.StartNew();

        /// <summary>The kind of the gamepad a button was last pressed on (CouchSettings.Xbox, PlayStation, Switch); none yet.</summary>
        internal string? PadUsed { get; private set; }

        /// <summary>The buttons the hints name: the ones chosen, else those of the gamepad in the player's hands.</summary>
        internal string ButtonStyle => CouchGlyphs.Resolve(Settings.Buttons, PadUsed);

        /// <summary>Another gamepad took over (a PlayStation one after an Xbox one): the hints name its buttons at once.</summary>
        internal void UsePad(string? kind)
        {
            if (kind == null || kind == PadUsed)
                return;
            string before = ButtonStyle;
            PadUsed = kind;
            if (ButtonStyle != before)
            {
                if (Navigator.Screen == CouchScreen.Menu && menu == CouchMenu.Settings)
                    BuildMenu();
                ShowScreen(false);
            }
        }

        /// <summary>A sound by event, as a console's menus: a tick to move, a sweep to change tab, a chime to open...</summary>
        internal static CouchSound SoundOf(CouchCommand command, CouchEffect effect, CouchScreen from, CouchScreen to, bool otherTab) => effect switch
        {
            CouchEffect.Moved => otherTab ? CouchSound.Tab : CouchSound.Move,
            CouchEffect.ViewChanged => CouchSound.Tab,
            CouchEffect.ScreenChanged when to == CouchScreen.Menu => CouchSound.Panel,
            CouchEffect.ScreenChanged when from == CouchScreen.Menu => CouchSound.PanelClose,
            CouchEffect.ScreenChanged when to == CouchScreen.Details => CouchSound.Open,
            CouchEffect.ScreenChanged when from == CouchScreen.Details => CouchSound.Close,
            _ => command == CouchCommand.Back ? CouchSound.Back : CouchSound.Accept,
        };

        private void Play(CouchSound sound)
        {
            if (Played.Count < 1000)
                Played.Add(sound);
            if (SoundsOn)
                audio?.Play(sound);
        }

        internal static bool SoundsOn
        {
            get
            {
                try { return Preferences.Get(AppConfigKey.CouchSounds, ProfileManager.ProfileConfigFile) != "0"; }
                catch (Exception ex) { Log.Ignored(ex); return true; }
            }
            set
            {
                try { Preferences.Set(AppConfigKey.CouchSounds, value ? "1" : "0", ProfileManager.ProfileConfigFile); }
                catch (Exception ex) { Log.Ignored(ex); }
            }
        }
        private bool padListened = true;
        private readonly Stopwatch refreshClock = Stopwatch.StartNew();
        private CouchItem? shownBack;
        /// <summary>Every game once, for the search.</summary>
        private List<CouchItem> all = new();
        private List<CouchTab> baseTabs = new();
        internal bool Searching { get; private set; }
        internal string Query { get; private set; } = "";
        internal List<CouchKey> Keys => uiKeyboard.Keys;
        private const string Erase = CouchKeyboard.Erase, Space = CouchKeyboard.Space, Done = CouchKeyboard.Done;
        private int detailGeneration, shot = -1, refreshes;
        private bool shotShown;
        private Image? topShot;

        /// <summary>The game's picture comes back from under its screenshots.</summary>
        private void HideShots()
        {
            shotShown = false;
            uiShotA.Opacity = uiShotB.Opacity = 0;
            topShot = null;
        }
        private int backGeneration;
        private bool loading;

        public CouchUserControl()
        {
            InitializeComponent();
            uiGridItems.ItemTemplate = uiStripItems.ItemTemplate;
            timer.Tick += (_, _) => Tick();
            SizeChanged += (_, _) => Fit();
            uiKeyboard.Typed += Type;
            Tapped += Pointed;
            InitMore();
            // The second click of two fast ones is a double tap, not a tap: it opens what the first one selected
            DoubleTapped += Pointed;
        }

        /// <summary>The mode is on the screen (read from any thread): the system's notifications stay quiet, the mode tells its own. Its window hidden or minimized, they come back.</summary>
        internal static volatile bool Shown;

        /// <summary>The living-room mode is shown: its games are read again, the gamepads listened to.</summary>
        internal async Task OpenAsync(bool welcome = false)
        {
            IsVisible = true;
            Shown = true;
            idle.Restart();
            if (welcome)
                ShowWelcome();
            Focus();
            Fit();
            uiAvatar.Data = MainWindowViewModel.Instance.UserAvatar;
            uiPlayer.Text = LoginManager.Instance.GetCurrentUser()?.Username ?? "";
            ShowClock();
            try { gamepad ??= new CouchGamepad(); }
            catch (Exception ex) { Log.Ignored(ex); }
            audio ??= new CouchAudio();
            repeater.Reset(gamepad?.Read() ?? CouchButtons.None);
            ListenToGames(true);
            timer.Start();
            await LoadAsync();
        }

        internal void Close()
        {
            timer.Stop();
            ListenToGames(false);
            playing = null;
            Shown = false;
            gamepad?.Dispose();
            gamepad = null;
            audio?.Dispose();
            audio = null;
            StopEditing();
            StopTrailer();
            CloseOverlay();
            HideCurtain();
            ApplyMouse(CouchButtons.None, default);
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
                _ = LoadChallengeAsync();
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
            var favorites = ByTitle(pc.Concat(retro).Where(IsFavorite), i => i.Title).ToList();
            if (favorites.Count > 0)
                tabs.Add(new CouchTab { Name = Loc.T("Favorites"), Items = favorites });
            tabs.Add(new CouchTab { Name = Loc.T("Library"), Items = Filtered(pc) });
            foreach (var platform in retro.GroupBy(i => i.Retro!.Platform.Name).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
                tabs.Add(new CouchTab { Name = platform.Key, Items = Filtered(platform.ToList()) });
            genres = CouchFilter.Genres(pc.Concat(retro).Select(EntryOf));
            tabs.Insert(0, new CouchTab { Name = Loc.T("Home"), Items = HomeTiles(recent.FirstOrDefault()) });

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
            uiKeyboard.Open(text: false);
            Searching = true;
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

        private void InSearch(CouchCommand command)
        {
            if (command is CouchCommand.Back or CouchCommand.Menu)
            {
                Play(CouchSound.Back);
                CloseSearch();
            }
            else
            {
                int selected = uiKeyboard.Selected;
                string query = Query;
                uiKeyboard.Handle(command);
                if (uiKeyboard.Selected != selected)
                    Play(CouchSound.Move);
                else if (Query != query)
                    Play(CouchSound.Accept);
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

        /// <summary>A retro game's star, a library game's bookmark.</summary>
        internal static bool IsFavorite(CouchItem item)
        {
            int me = LoginManager.Instance.GetCurrentUser()?.ID ?? -1;
            return !item.IsTile && (item.Retro?.IsFavorite ?? item.Pc!.BookmarkedUsers?.Any(u => u.ID == me) == true);
        }

        private async Task SetFavoriteAsync(CouchItem item, bool favorite)
        {
            if (item.Retro != null)
                item.Retro.IsFavorite = favorite;
            else
            {
                string address = $"{SettingsViewModel.Instance.ServerUrl}/api/users/me/bookmark/{item.Pc!.ID}";
                if (favorite)
                    await WebHelper.PostAsync(address, "");
                else
                    await WebHelper.DeleteAsync(address);
                item.Pc.BookmarkedUsers = favorite ? new List<User> { LoginManager.Instance.GetCurrentUser()! } : new List<User>();
            }
            // The Favorites tab follows; the game's screen stays on its game
            Show(all.Where(i => i.Pc != null).Select(i => i.Pc!), all.Where(i => i.Retro != null).Select(i => i.Retro!));
        }

        /// <summary>Removed from this PC, after the question the client always asks.</summary>
        private async Task UninstallAsync(CouchItem item)
        {
            if (item.Retro is RetroGame retro)
            {
                if (retro.Busy || RetroService.InstalledFolder(retro) is not string folder)
                    return;
                if (!await DialogService.ConfirmAsync(Loc.F("Delete '{0}' from this PC? Its saves are kept.", retro.Name)))
                    return;
                System.IO.Directory.Delete(folder, true);
                retro.Refresh();
                return;
            }
            // The game's settings ask what they need to (its saves, its shortcuts) and remove it
            var settings = new GameSettingsUserControl(item.Pc!) { Width = 1200, Height = 800, Margin = new Thickness(50) };
            MainWindowViewModel.Instance.OpenPopup(settings);
            try { await settings.UninstallGame(); }
            finally
            {
                if (ReferenceEquals(MainWindowViewModel.Instance.Popup, settings))
                    MainWindowViewModel.Instance.ClosePopup();
            }
        }

        /// <summary>"What to play?": a game drawn among the installed ones (the whole library when none is), its screen opened.</summary>
        internal void OpenRandom(Random? random = null)
        {
            int tab = Tabs.FindIndex(t => t.Name == Loc.T("Installed games"));
            if (tab < 0)
                tab = Tabs.FindIndex(t => t.Name == Loc.T("Library"));
            if (tab < 0 || Tabs[tab].Items.Count == 0)
                return;
            Navigator.BackToList();
            Navigator.Select(tab, (random ?? Random.Shared).Next(Tabs[tab].Items.Count));
            ShowTab();
            Navigator.Apply(CouchCommand.Accept);
            ShowScreen();
        }

        private static HashSet<int> InstalledPc() => InstallViewModel.Instance.InstalledGames.Where(g => g.Key != null).Select(g => g.Key.ID).ToHashSet();

        // --- What the gamepad and the keyboard ask ---

        private void Tick()
        {
            if (refreshClock.Elapsed >= TimeSpan.FromMilliseconds(500))
            {
                refreshClock.Restart();
                Refresh();
            }
            // A game is played, or another window is in front: its gamepad is not ours
            bool listen = TopLevel.GetTopLevel(this) is Window { IsActive: true } && IsVisible;
            // Read 60 times a second for the menus; 10 times behind a game (only Start + Back is looked for there)
            TimeSpan rate = listen || overlay != null ? TimeSpan.FromMilliseconds(16) : TimeSpan.FromMilliseconds(100);
            if (timer.Interval != rate)
                timer.Interval = rate;
            if (gamepad == null)
                return;
            CouchButtons held = gamepad.Read();
            UsePad(gamepad.LastUsed);
            TickMore(held, listen);
            if (overlay != null)
                listen = padListened = true;
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
            idle.Restart();
            // The screensaver, a game's picture while it starts, the welcome: any button takes it away
            if (uiCurtain.IsVisible)
            {
                HideCurtain();
                // Pressed here: the player is back, whatever the game does
                GameClosed(bringBack: false);
                return;
            }
            if (overlay != null)
            {
                overlay.Handle(command);
                return;
            }
            // A question of the client (delete? where to install?): its answers, from left to right
            if (DialogService.Current is { Input: null } dialog)
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
            // A question with a text to type, or a window of the client over the games (a game's settings, a notice,
            // a trailer): the cross goes from control to control, A presses, B closes
            if (DialogService.Current is { } asking)
            {
                InWindow(asking.Scope, command, asking.Cancel);
                return;
            }
            if (MainWindowViewModel.Instance.Popup is Control popup)
            {
                InWindow(popup, command, MainWindowViewModel.Instance.ClosePopup);
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
                {
                    Play(CouchSound.Accept);
                    OpenSearch();
                }
                return;
            }
            if (HandleMore(command))
                return;
            int tab = Navigator.Tab;
            CouchItem? before = Current;
            CouchScreen screenBefore = Navigator.Screen;
            CouchEffect effect = Navigator.Apply(command);
            if (effect != CouchEffect.None)
                Play(SoundOf(command, effect, screenBefore, Navigator.Screen, Navigator.Tab != tab));
            else if (command is CouchCommand.Left or CouchCommand.Right or CouchCommand.Up or CouchCommand.Down && edgeSound.Elapsed > TimeSpan.FromMilliseconds(400))
            {
                // The end of the list: once, not at each repeat of a held direction
                edgeSound.Restart();
                Play(CouchSound.Edge);
            }
            switch (effect)
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
                    // A game that starts, a page opened: the trailer stops, and waits again
                    StopTrailer();
                    trailerWait.Restart();
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

        // --- The client's own windows, with the gamepad ---

        private CouchKeyboard? textKeyboard;
        /// <summary>The keyboard shown over the client's windows to type in their text boxes (the window has it).</summary>
        internal CouchKeyboard? TextKeyboard
        {
            get => textKeyboard;
            set
            {
                if (textKeyboard != null)
                    textKeyboard.Typed -= TypeInBox;
                textKeyboard = value;
                if (textKeyboard != null)
                    textKeyboard.Typed += TypeInBox;
            }
        }
        private ComboBox? openChoices;
        /// <summary>The text box typed in with the keyboard on the screen.</summary>
        internal TextBox? Editing { get; private set; }

        private void InWindow(Control scope, CouchCommand command, Action close)
        {
            if (Editing != null)
            {
                if (command is CouchCommand.Back or CouchCommand.Menu)
                    StopEditing();
                else
                    TextKeyboard?.Handle(command);
                return;
            }
            // A list of choices opened with A: up and down go through it, A or B closes it on the choice shown
            if (openChoices is { IsDropDownOpen: true } choices)
            {
                if (command is CouchCommand.Up or CouchCommand.Down && choices.ItemCount > 0)
                {
                    choices.SelectedIndex = Math.Clamp(choices.SelectedIndex + (command == CouchCommand.Up ? -1 : 1), 0, choices.ItemCount - 1);
                    Play(CouchSound.Move);
                }
                else if (command is CouchCommand.Accept or CouchCommand.Back)
                {
                    choices.IsDropDownOpen = false;
                    choices.Focus(NavigationMethod.Directional);
                    openChoices = null;
                    Play(command == CouchCommand.Accept ? CouchSound.Accept : CouchSound.Back);
                }
                return;
            }
            openChoices = null;
            Control? focused = CouchFocus.Focused(scope);
            switch (command)
            {
                case CouchCommand.Left when CouchFocus.Adjust(focused, -1):
                case CouchCommand.Right when CouchFocus.Adjust(focused, 1):
                    Play(CouchSound.Move);
                    break;
                case CouchCommand.Left or CouchCommand.Right or CouchCommand.Up or CouchCommand.Down:
                    if (CouchFocus.Move(scope, command switch
                    {
                        CouchCommand.Left => NavigationDirection.Left, CouchCommand.Right => NavigationDirection.Right,
                        CouchCommand.Up => NavigationDirection.Up, _ => NavigationDirection.Down,
                    }))
                        Play(CouchSound.Move);
                    break;
                case CouchCommand.Accept:
                    Play(CouchSound.Accept);
                    if (focused is TextBox box && TextKeyboard != null)
                        StartEditing(box);
                    else if (focused is ComboBox list)
                    {
                        openChoices = list;
                        list.IsDropDownOpen = true;
                    }
                    else if (focused != null)
                        CouchFocus.Activate(focused);
                    else
                        CouchFocus.Move(scope, NavigationDirection.Down);
                    break;
                case CouchCommand.Back:
                    Play(CouchSound.Back);
                    close();
                    break;
            }
        }

        private void StartEditing(TextBox box)
        {
            Editing = box;
            TextKeyboard!.Open(text: true);
            TextKeyboard.IsVisible = true;
            ShowEdited();
        }

        private void StopEditing()
        {
            Editing = null;
            if (TextKeyboard != null)
                TextKeyboard.IsVisible = false;
        }

        /// <summary>What the box holds, as it shows it (a password stays hidden).</summary>
        private void ShowEdited()
        {
            string text = Editing?.Text ?? "";
            TextKeyboard?.Show(Editing is { PasswordChar: not '\0' } ? new string('•', text.Length) : text);
        }

        private void TypeInBox(string typed)
        {
            // The window closed under the keyboard
            if (Editing == null || !Editing.IsEffectivelyVisible || typed == CouchKeyboard.Done)
            {
                StopEditing();
                return;
            }
            string text = Editing.Text ?? "";
            Editing.Text = typed == CouchKeyboard.Erase ? (text.Length > 0 ? text[..^1] : "") : text + (typed == CouchKeyboard.Space ? " " : typed);
            Editing.CaretIndex = Editing.Text.Length;
            Play(CouchSound.Move);
            ShowEdited();
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
            uiListItems.ItemsSource = items;
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
            double listY = index * ListRow + ListRow / 2 - uiList.Viewport.Height / 2;
            uiList.Offset = new Vector(0, Math.Clamp(listY, 0, Math.Max(0, count * ListRow - uiList.Viewport.Height)));
        }

        /// <summary>A line of the list and the space under it.</summary>
        private const double ListRow = 78;

        internal string HeroTitle => uiTitle.Text ?? "";
        internal IReadOnlyList<CouchStat> Stats { get; private set; } = Array.Empty<CouchStat>();
        internal IReadOnlyList<CouchStat> Hints { get; private set; } = Array.Empty<CouchStat>();

        /// <summary>The selected game's name and numbers; its picture a moment later (a held direction passes many games).</summary>
        private void ShowHero(CouchItem? item)
        {
            uiHero.IsVisible = item != null && !Navigator.IsGrid && !Searching;
            uiTitle.Text = uiGridTitle.Text = item?.Title ?? "";
            uiEyebrow.Text = item == null ? "" : (item.IsTile ? Loc.T("Home") : item.Retro?.Platform.Name ?? Loc.T("Library")).ToUpper(CultureInfo.CurrentCulture);
            uiMeta.Text = item == null ? "" : MetaOf(item);
            Stats = item == null ? Array.Empty<CouchStat>() : StatsOf(item);
            uiStats.ItemsSource = Stats;
            uiStatsCard.IsVisible = Stats.Count > 0 && Settings.ShowStats;
            uiLogo.IsVisible = false;
            uiTitle.IsVisible = true;
            int generation = ++backGeneration;
            DispatcherTimer.RunOnce(() =>
            {
                if (generation != backGeneration)
                    return;
                ShowBack(item);
                _ = ShowLogoAsync(item, generation);
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
                // The home's tiles are over the picture of the game played last
                CouchItem? art = item?.IsTile == true ? homeArt : item;
                uiBackPc.IsVisible = art?.Pc != null;
                uiBackRetro.IsVisible = art?.Retro != null;
                ShowAccent(item);
                if (art?.Pc != null)
                    uiBackPc.Data = art.Pc;
                if (art?.Retro is RetroGame retro)
                {
                    uiBackRetro.Source = retro.Banner;
                    _ = ShowRetroBackAsync(item!, retro);
                }
                uiBack.Opacity = art == null ? 0 : item!.IsTile ? 0.55 : 1;
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

        private static int MinutesOf(CouchItem item) => item.IsTile ? 0 : item.Retro?.MinutesPlayed
            ?? (InstallViewModel.Instance.PlayRecords.TryGetValue(item.Pc!.ID, out var record) ? record.MinutesPlayed : 0);

        private static string MetaOf(CouchItem item)
        {
            if (item.Tile != null)
                return item.Tile.Subtitle;
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
            if (item.IsTile)
                return stats;
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
            // A menu opened over a game's screen keeps it behind
            bool detailsBehind = details || (screen == CouchScreen.Menu && Navigator.BeforeMenu == CouchScreen.Details && item != null);
            uiHome.IsVisible = !detailsBehind;
            uiDetails.IsVisible = detailsBehind;
            uiMenu.IsVisible = screen == CouchScreen.Menu;
            // The keyboard takes the place of the selected game's name, over the strip of the games found
            bool grid = Navigator.IsGrid && !Searching;
            bool list = Navigator.IsList && !Searching;
            uiStrip.IsVisible = !grid && !list;
            uiGrid.IsVisible = uiGridTitle.IsVisible = grid;
            uiListBox.IsVisible = list;
            // Beside the list, the selected game's name keeps the left of the screen
            uiHero.Margin = list ? new Thickness(96, 0, 790, 54) : new Thickness(96, 0, 72, 34);
            uiHero.IsVisible = item != null && !grid && !Searching;
            uiKeyboard.IsVisible = Searching && !details;
            uiKeyboard.Show(Query.Length > 0 ? Query : Loc.T("Search a game"), placeholder: Query.Length == 0);
            uiDim.IsVisible = !detailsBehind && (grid || Searching);
            uiDetailShade.IsVisible = detailsBehind;
            // Coming in: a game's screen from the left, a menu from below
            if (detailsBehind && shownScreen == CouchScreen.Home && screen == CouchScreen.Details)
                Enter(uiDetailPanel, -48, 0);
            if (screen == CouchScreen.Menu && shownScreen != CouchScreen.Menu)
                Enter(uiMenuBox, 0, 28);
            if (!detailsBehind && shotShown)
                HideShots();
            // Back from a menu to the same game: its screen is as it was
            if (details && rebuild && !(shownScreen == CouchScreen.Menu && ReferenceEquals(item, detailsItem)))
                ShowDetails(item!);
            shownScreen = screen;
            for (int i = 0; i < Actions.Count; i++)
                Actions[i].IsSelected = i == Navigator.Action;
            for (int i = 0; i < MenuLines.Count; i++)
                MenuLines[i].IsSelected = i == Navigator.MenuLine;
            if (screen == CouchScreen.Menu)
                Dispatcher.UIThread.Post(() => (uiMenuLines.ContainerFromIndex(Navigator.MenuLine) as Control)?.BringIntoView(), DispatcherPriority.Background);
            uiPadState.Text = gamepad is { CanRead: true } ? "" : Loc.T("No gamepad can be read on this PC: the arrows, Enter and Escape do the same.");

            Hints = Searching ? new[] { new CouchStat("A", Loc.T("Type")), new CouchStat("X", Loc.T("Erase")), new CouchStat("Y", Loc.T("Space")), new CouchStat("B", Loc.T("Close")) } : screen switch
            {
                CouchScreen.Menu => new[] { new CouchStat("A", Loc.T("Choose")), new CouchStat("B", Loc.T("Back")) },
                CouchScreen.Details => new[] { new CouchStat("A", Loc.T("Choose")), new CouchStat("B", Loc.T("Back")), new CouchStat("LB RB", Loc.T("Other games")), new CouchStat("START", Loc.T("Menu")) },
                _ => new[]
                {
                    new CouchStat("A", Loc.T("Open")), new CouchStat("X", Navigator.View switch { CouchView.Strip => Loc.T("Grid"), CouchView.Grid => Loc.T("List"), _ => Loc.T("Strip") }), new CouchStat("Y", Loc.T("Search")),
                    new CouchStat("LB RB", Loc.T("Tabs")), new CouchStat("LT RT", Loc.T("Letters")), new CouchStat("START", Loc.T("Menu")),
                },
            };
            if (!Searching && screen == CouchScreen.Home)
                Hints = Hints.Concat(new[] { new CouchStat("BACK", Loc.T("Quick access")), new CouchStat("L3", Loc.T("Filters")) }).ToArray();
            string style = ButtonStyle;
            Hints = Hints.Select(h => h with { Label = CouchGlyphs.Label(style, h.Label) }).ToArray();
            uiTabLeftKey.Text = CouchGlyphs.Label(style, "LB");
            uiTabRightKey.Text = CouchGlyphs.Label(style, "RB");
            uiHints.ItemsSource = Hints;
            uiMenuTitle.Text = MenuTitle;
            Dispatcher.UIThread.Post(ScrollToSelection, DispatcherPriority.Background);
        }

        private CouchScreen shownScreen = CouchScreen.Home;
        private CouchItem? detailsItem;

        private void ShowDetails(CouchItem item)
        {
            // Another game: its own picture, not the last one's screenshots
            if (!ReferenceEquals(item, detailsItem) && shotShown)
                HideShots();
            detailsItem = item;
            ShownAchievements = Array.Empty<GameVault.Core.Retro.RommProgress.Achievement>();
            uiDetailLogo.IsVisible = false;
            uiDetailTitle.IsVisible = true;
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
            _ = ShowDetailLogoAsync(item, generation);
            if (item.Retro is RetroGame retro && RommAccount.IsLinked)
                _ = ShowAchievementsAsync(item, retro, generation);
            if (item.Pc != null)
                _ = ShowFriendsAsync(item, generation);
            QueueTrailer(item);
        }

        /// <summary>The game's logo instead of its name, as on the list (asked once, kept).</summary>
        private async Task ShowDetailLogoAsync(CouchItem item, int generation)
        {
            if (item.IsTile || !Settings.Logos)
                return;
            try
            {
                Avalonia.Media.Imaging.Bitmap? logo = item.Retro != null ? await GameLogoService.OfAsync(item.Retro) : await GameLogoService.OfAsync(item.Pc!);
                if (logo == null || generation != detailGeneration)
                    return;
                uiDetailLogo.Source = logo;
                uiDetailLogo.IsVisible = true;
                uiDetailTitle.IsVisible = false;
            }
            catch (Exception ex) { Log.Ignored(ex); }
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
                string earned = Loc.F("{0} of {1}", achievements.Count(a => a.Earned), achievements.Count);
                uiDetailStats.ItemsSource = StatsOf(item).Append(new CouchStat(Loc.T("Achievements"), earned)).ToList();
                // Their list, from the game's screen
                ShownAchievements = achievements;
                Actions = Actions.Append(new CouchLine { Text = Loc.F("Achievements ({0})", earned), Run = () => { ShowMenu(CouchMenu.Achievements); return Task.CompletedTask; } }).ToList();
                Navigator.ActionCount = Actions.Count;
                uiActions.ItemsSource = Actions;
                ShowScreen(false);
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>The game's screenshots behind its screen, one after the other.</summary>
        internal async Task NextShotAsync(CouchItem item)
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
            // The new one over the one shown, faded in; the one under is hidden once covered
            Image over = ReferenceEquals(topShot, uiShotA) ? uiShotB : uiShotA, under = topShot;
            over.ZIndex = 1;
            if (under != null)
                under.ZIndex = 0;
            over.Source = image;
            over.Opacity = 1;
            topShot = over;
            DispatcherTimer.RunOnce(() =>
            {
                if (ReferenceEquals(topShot, over) && under != null)
                    under.Opacity = 0;
            }, TimeSpan.FromMilliseconds(650));
        }

        /// <summary>What can be done with the game from the couch; the rest is on its page.</summary>
        private void BuildActions(CouchItem item)
        {
            var lines = new List<CouchLine>();
            if (item.Retro is RetroGame retro)
            {
                if (item.Installed)
                    lines.Add(new CouchLine { Text = Loc.T("Play"), Run = () => { ShowSplash(item); return RetroPlay.PlayAsync(retro); } });
                else
                {
                    lines.Add(new CouchLine { Text = Loc.T("Install and play"), Run = () => RetroPlay.InstallAsync(retro, playWhenDone: true) });
                    lines.Add(new CouchLine { Text = Loc.T("Install"), Run = () => RetroPlay.InstallAsync(retro) });
                }
                if (retro.HasVideo)
                    lines.Add(new CouchLine { Text = Loc.T("Watch the trailer"), Run = () => { MainWindowViewModel.Instance.OpenPopup(new TrailerPopup(new[] { $"https://www.youtube.com/watch?v={Uri.EscapeDataString(retro.Video!)}" }, retro.Name)); return Task.CompletedTask; } });
                AddCommon(lines, item);
                lines.Add(new CouchLine { Text = Loc.T("Open its page"), Run = () => { LeaveRequested?.Invoke(); return MainWindowViewModel.Instance.Retro.ShowGameAsync(retro.Platform.Slug, retro.Id); } });
            }
            else
            {
                Game game = item.Pc!;
                if (item.Installed)
                    lines.Add(new CouchLine { Text = Loc.T("Play"), Run = () => { GameStarted(item); return InstallUserControl.PlayGame(game.ID); } });
                else
                {
                    lines.Add(new CouchLine { Text = Loc.T("Install and play"), Run = () => MainWindowViewModel.Instance.Downloads.InstallAndPlay(game) });
                    lines.Add(new CouchLine { Text = Loc.T("Install"), Run = async () => { await MainWindowViewModel.Instance.Downloads.TryStartDownload(game); } });
                }
                if (game.Metadata is GameMetadata metadata && TrailerPopup.HasVideos(metadata))
                    lines.Add(new CouchLine { Text = Loc.T("Watch the trailer"), Run = () => { MainWindowViewModel.Instance.OpenPopup(new TrailerPopup(metadata, item.Title)); return Task.CompletedTask; } });
                AddCommon(lines, item);
                lines.Add(new CouchLine { Text = Loc.T("Open its page"), Run = () => { LeaveRequested?.Invoke(); MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(game)); return Task.CompletedTask; } });
            }
            Actions = lines;
            Navigator.ActionCount = lines.Count;
            uiActions.ItemsSource = lines;
        }

        /// <summary>The favorites and, for a game installed here, its removal.</summary>
        private void AddCommon(List<CouchLine> lines, CouchItem item)
        {
            bool favorite = IsFavorite(item);
            lines.Add(new CouchLine { Text = favorite ? Loc.T("Remove from the favorites") : Loc.T("Add to the favorites"), Run = () => SetFavoriteAsync(item, !favorite) });
            if (item.Installed)
                lines.Add(new CouchLine { Text = Loc.T("Uninstall"), Run = () => UninstallAsync(item) });
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
            RefreshMore();
            foreach (CouchItem item in Navigator.Tab < Tabs.Count ? Tabs[Navigator.Tab].Items : new())
            {
                if (item.IsTile)
                    continue;
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
                // A screenshot after 3 seconds, then one every 6; nothing while a game is played over the window
                if (++refreshes % 12 == 6 && MainWindowViewModel.Instance.Popup == null && !Settings.Performance && playing == null
                    && TopLevel.GetTopLevel(this) is Window { IsActive: true })
                    _ = NextShotAsync(current);
            }
            WatchGame();
            KeepTrailer();
            bool active = IsVisible && TopLevel.GetTopLevel(this) is Window { IsActive: true };
            KeepDrift(active);
            // The music: in the menus only, never over a game, a trailer or a game that starts
            audio?.Music(SoundsOn && Settings.Music && active && playing == null && !TrailerShown && curtain != Curtain.Splash);
            DialogService.Current?.Mark();
            if (Editing is { IsEffectivelyVisible: false })
                StopEditing();
            int unread = MainWindowViewModel.Instance.IsChatAvailable ? ChatService.Instance.TotalUnread : 0;
            uiUnreadPill.IsVisible = unread > 0;
            uiUnread.Text = Loc.Plural(unread, "{0} message", "{0} messages");
        }
    }
}
