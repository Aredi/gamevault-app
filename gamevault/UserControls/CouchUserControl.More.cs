using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GameVault.Core;
using GameVault.Core.Couch;
using gamevault.Helper;
using gamevault.Helper.Platform;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>The menus of the living-room mode.</summary>
    internal enum CouchMenu { Main, Quick, Filter, Settings, Stats, Achievements, Custom }

    /// <summary>
    /// The menu shown over a game (Start + Back, Windows): a small window always in front, moved through with the
    /// gamepad as it is read whatever window has the keyboard.
    /// </summary>
    internal sealed class CouchOverlay : Window
    {
        private readonly List<(Border Frame, Action Run)> lines = new();
        private int selected;

        public CouchOverlay(string title, IEnumerable<(string Text, Action Run)> actions)
        {
            SystemDecorations = SystemDecorations.None;
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.Parse("#0B1018"));
            var panel = new StackPanel { Width = 520, Margin = new Thickness(34, 30), Spacing = 12 };
            panel.Children.Add(new TextBlock
            {
                Text = title, FontSize = 26, FontWeight = FontWeight.ExtraBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 8),
            });
            foreach (var (text, run) in actions)
            {
                var frame = new Border
                {
                    CornerRadius = new CornerRadius(14), Padding = new Thickness(22, 15),
                    Child = new TextBlock { Text = text, FontSize = 21, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center },
                };
                int index = lines.Count;
                frame.PointerPressed += (_, _) => { Select(index); Accept(); };
                lines.Add((frame, run));
                panel.Children.Add(frame);
            }
            Content = new Border { BorderBrush = new SolidColorBrush(Color.Parse("#40FFFFFF")), BorderThickness = new Thickness(1), Child = panel };
            Select(0);
        }

        internal int Selected => selected;

        private void Select(int line)
        {
            selected = Math.Clamp(line, 0, lines.Count - 1);
            for (int i = 0; i < lines.Count; i++)
                lines[i].Frame.Background = new SolidColorBrush(Color.Parse(i == selected ? "#7C6BFF" : "#1FFFFFFF"));
        }

        private void Accept() => lines[selected].Run();

        internal void Handle(CouchCommand command)
        {
            switch (command)
            {
                case CouchCommand.Up: Select(selected - 1); break;
                case CouchCommand.Down: Select(selected + 1); break;
                case CouchCommand.Accept: Accept(); break;
                // The first line gives the game back
                case CouchCommand.Back: lines[0].Run(); break;
            }
        }
    }

    public partial class CouchUserControl
    {
        private readonly Stopwatch idle = Stopwatch.StartNew();
        private List<string> genres = new();
        private CouchSettings? settings;
        private CouchMenu menu = CouchMenu.Main;
        private CouchOverlay? overlay;

        private static string SettingsFile => Path.Combine(Path.GetDirectoryName(ProfileManager.ProfileConfigFile) ?? "", "couch.json");

        /// <summary>How this mode looks and behaves, as the player set it.</summary>
        internal CouchSettings Settings
        {
            get
            {
                if (settings == null)
                {
                    try { settings = CouchSettings.Load(SettingsFile); }
                    catch (Exception ex) { Log.Ignored(ex); settings = new CouchSettings(); }
                }
                return settings;
            }
            set
            {
                settings = value;
                ApplyLook();
            }
        }

        internal void SaveSettings()
        {
            try { Settings.Save(SettingsFile); }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private void InitMore()
        {
            // A library game's color comes when its cover is read
            uiAccentProbe.PropertyChanged += (_, e) =>
            {
                if (e.Property == CacheImage.AccentColorProperty && shownBack?.Pc != null && ReferenceEquals(uiAccentProbe.Data, shownBack.Pc))
                    ShowAccent(shownBack);
            };
            PointerMoved += (_, _) =>
            {
                idle.Restart();
                if (curtain == Curtain.Saver)
                    HideCurtain();
            };
        }

        // --- The order and the filters of the games ---

        private static CouchEntry EntryOf(CouchItem item)
        {
            if (item.Retro is RetroGame retro)
                return new CouchEntry(retro.Name, item.Installed, retro.MinutesPlayed, null, retro.Genres);
            Game game = item.Pc!;
            InstallViewModel.Instance.PlayRecords.TryGetValue(game.ID, out var record);
            return new CouchEntry(game.Title ?? "", item.Installed, record?.MinutesPlayed ?? 0, record?.LastPlayedAt,
                game.Metadata?.Genres?.Select(g => g.Name).Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>());
        }

        /// <summary>The games of a list (given by name) the filters keep, in the order chosen.</summary>
        private List<CouchItem> Filtered(List<CouchItem> items) =>
            Settings.IsFiltered ? CouchFilter.Apply(items.Select(EntryOf).ToList(), Settings).Select(i => items[i]).ToList() : items;

        /// <summary>The games shown again after a filter or a favorite changed.</summary>
        private void Reshow() => Show(all.Where(i => i.Pc != null).Select(i => i.Pc!), all.Where(i => i.Retro != null).Select(i => i.Retro!));

        // --- The home: tiles instead of games ---

        private readonly Dictionary<string, CouchTile> tiles = new();
        private string challengeText = "";

        private CouchItem? homeArt;

        private List<CouchItem> HomeTiles(CouchItem? lastPlayed)
        {
            homeArt = lastPlayed;
            tiles.Clear();
            var home = new List<CouchItem>();
            void Add(string id, string title, string subtitle, Func<Task> run)
            {
                var tile = new CouchTile { Id = id, Title = title, Subtitle = subtitle, Run = run };
                tiles[id] = tile;
                home.Add(new CouchItem { Tile = tile, Installed = true });
            }
            Task Do(Action action)
            {
                action();
                return Task.CompletedTask;
            }
            if (lastPlayed != null)
                Add("resume", Loc.T("Resume"), lastPlayed.Title, () => Do(() => OpenGame(lastPlayed.Key)));
            Add("random", Loc.T("A game at random"), Loc.T("Among the games installed here"), () => Do(() => OpenRandom()));
            Add("challenge", Loc.T("Challenge of the week"), challengeText, () => Do(() => Leave(() => MainWindowViewModel.Instance.SetActiveControl(MainControl.Challenges))));
            Add("friends", Loc.T("Friends"), FriendsText(), () => Do(() => Leave(() => MainWindowViewModel.Instance.IsChatOpen = true)));
            Add("downloads", Loc.T("Downloads"), DownloadsText(), () => Do(() => Leave(() => MainWindowViewModel.Instance.SetActiveControl(MainControl.Downloads))));
            Add("news", Loc.T("News"), Loc.T("What is new on the server"), () => Do(() => MainWindowViewModel.Instance.OpenPopup(new NewsPopup())));
            Add("stats", Loc.T("Statistics"), Loc.T("Your games and your play time"), () => Do(() => ShowMenu(CouchMenu.Stats)));
            foreach (CouchApp app in Settings.Apps)
                Add("app:" + app.Path, app.Name, Loc.T("Application"), () => Do(() => CouchSystem.Open(app)));
            return home;
        }

        /// <summary>The desktop again, on a page of the client.</summary>
        private void Leave(Action then)
        {
            LeaveRequested?.Invoke();
            then();
        }

        /// <summary>A game's screen, from wherever: its tab is shown, the recent ones first.</summary>
        private void OpenGame(string key)
        {
            for (int tab = 0; tab < Tabs.Count; tab++)
            {
                int index = Tabs[tab].Items.FindIndex(i => i.Key == key);
                if (index < 0)
                    continue;
                Navigator.Select(tab, index);
                ShowTab();
                Navigator.Apply(CouchCommand.Accept);
                ShowScreen();
                return;
            }
        }

        private static string FriendsText()
        {
            try
            {
                int me = LoginManager.Instance.GetCurrentUser()?.ID ?? -1;
                var others = ActivityService.Instance.All.Where(a => a.UserId != me).ToList();
                int playing = others.Count(a => a.IsPlaying);
                return playing > 0 ? Loc.Plural(playing, "{0} friend is playing", "{0} friends are playing")
                    : others.Count > 0 ? Loc.Plural(others.Count, "{0} friend online", "{0} friends online")
                    : Loc.T("Nobody is online");
            }
            catch (Exception ex) { Log.Ignored(ex); return ""; }
        }

        private static string DownloadsText()
        {
            try
            {
                int running = DownloadsViewModel.Instance.DownloadedGames.Count(d => d.IsBusy()) + DownloadsViewModel.Instance.RetroDownloads.Count(d => d.IsRunning);
                return running > 0 ? Loc.Plural(running, "{0} in progress", "{0} in progress") : Loc.T("None in progress");
            }
            catch (Exception ex) { Log.Ignored(ex); return ""; }
        }

        /// <summary>The challenge of the week on its tile, asked once the games are shown.</summary>
        internal async Task LoadChallengeAsync()
        {
            try
            {
                ChallengeState? state = await ChallengesService.GetAsync();
                int me = LoginManager.Instance.GetCurrentUser()?.ID ?? -1;
                ChallengeRank? mine = state?.Board.FirstOrDefault(r => r.UserId == me);
                challengeText = state == null ? Loc.T("Not available on this server")
                    : mine?.DoneAt != null ? $"{state.Challenge.Text} · {Loc.T("Completed")}"
                    : $"{state.Challenge.Text} · {Loc.F("{0} of {1}", state.Challenge.Amount(mine?.Value ?? 0), state.Challenge.Amount(state.Challenge.Goal))}";
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                challengeText = "";
            }
            if (tiles.TryGetValue("challenge", out CouchTile? tile))
                tile.Subtitle = challengeText;
        }

        // --- The menus ---

        internal CouchMenu Menu => menu;
        private string MenuTitle => menu switch
        {
            CouchMenu.Quick => Loc.T("Quick access"),
            CouchMenu.Filter => Loc.T("Order and filters"),
            CouchMenu.Settings => Loc.T("Settings"),
            CouchMenu.Stats => Loc.T("Statistics"),
            CouchMenu.Achievements => Loc.T("Achievements"),
            CouchMenu.Custom => PageName,
            _ => Loc.T("Living-room mode"),
        };

        internal void ShowMenu(CouchMenu kind)
        {
            menu = kind;
            BuildMenu();
            Navigator.OpenMenu();
            ShowScreen();
        }

        private void CloseMenu()
        {
            Navigator.CloseMenu();
            ShowScreen();
        }

        /// <summary>The commands of the menus and of the home, before the games' ones; true when it was one of them.</summary>
        private bool HandleMore(CouchCommand command)
        {
            bool inMenu = Navigator.Screen == CouchScreen.Menu;
            if (!inMenu && command is CouchCommand.Menu or CouchCommand.Quick)
            {
                Play(CouchSound.Panel);
                ShowMenu(command == CouchCommand.Menu ? CouchMenu.Main : CouchMenu.Quick);
                return true;
            }
            if (command is CouchCommand.Quick)
                return true;
            if (command is CouchCommand.Filter)
            {
                if (Navigator.Screen == CouchScreen.Home)
                {
                    Play(CouchSound.Panel);
                    ShowMenu(CouchMenu.Filter);
                }
                return true;
            }
            // The settings and the statistics are opened from the menu: back to it
            if (inMenu && command == CouchCommand.Back && menu is CouchMenu.Settings or CouchMenu.Stats or CouchMenu.Custom)
            {
                Play(CouchSound.Back);
                // A page of the settings goes back to their pages, on the one it was
                if (menu == CouchMenu.Custom)
                {
                    ShowMenu(CouchMenu.Settings);
                    Navigator.SelectLine(Array.FindIndex(Pages, p => p.Page == SettingsPage));
                    ShowScreen(false);
                }
                else
                    ShowMenu(CouchMenu.Main);
                return true;
            }
            if (Navigator.Screen == CouchScreen.Home && command == CouchCommand.Accept && Current?.Tile is CouchTile tile)
            {
                Play(CouchSound.Accept);
                _ = RunAsync(new CouchLine { Text = tile.Title, Run = tile.Run });
                return true;
            }
            return false;
        }

        private void BuildMenu()
        {
            MenuLines = menu switch
            {
                CouchMenu.Quick => QuickLines(),
                CouchMenu.Filter => FilterLines(),
                CouchMenu.Settings => SettingsPagesLines(),
                CouchMenu.Custom => CustomLines(),
                CouchMenu.Stats => StatsLines(),
                CouchMenu.Achievements => AchievementLines(),
                _ => MainLines(),
            };
            Navigator.MenuCount = MenuLines.Count;
            uiMenuLines.ItemsSource = MenuLines;
            // A list of achievements needs the width of their descriptions
            PlaceMenu();
            uiMenuScroll.Offset = default;
        }

        private static CouchLine Line(string text, Action run) => new() { Text = text, Run = () => { run(); return Task.CompletedTask; } };

        /// <summary>A line that changes a setting: kept, and the menu shown again with its new text.</summary>
        private CouchLine Setting(string text, Action change, bool games = false) => Line(text, () =>
        {
            change();
            SaveSettings();
            if (games)
                Reshow();
            else
            {
                BuildMenu();
                ShowScreen(false);
                ShowHero(Current);
            }
        });

        private static string YesNo(bool value) => value ? Loc.T("Yes") : Loc.T("No");

        private List<CouchLine> MainLines() => new()
        {
            Line(Loc.T("Back to the games"), CloseMenu),
            Line(Loc.T("A game at random"), () => OpenRandom()),
            new() { Text = Loc.T("Read the games again"), Run = async () => { CloseMenu(); await LoadAsync(); } },
            Line(Loc.T("Settings"), () => ShowMenu(CouchMenu.Settings)),
            Line(Loc.T("Statistics"), () => ShowMenu(CouchMenu.Stats)),
            Line(Loc.T("Back to the desktop"), () => { CloseMenu(); LeaveRequested?.Invoke(); }),
            new() { Text = Loc.T("Quit SanctuaryVault"), Run = () => App.Instance.ExitApp() },
        };

        private List<CouchLine> QuickLines()
        {
            var lines = new List<CouchLine>
            {
                Line(Loc.T("Volume +"), () => CouchSystem.Volume(1)),
                Line(Loc.T("Volume −"), () => CouchSystem.Volume(-1)),
                Line(Loc.T("Sound on or off"), () => CouchSystem.Volume(0)),
            };
            if (OperatingSystem.IsWindows())
                lines.Add(Setting(Loc.F("Mouse with the right stick: {0}", YesNo(Settings.Mouse)), () => Settings.Mouse = !Settings.Mouse));
            lines.Add(Power(Loc.T("Sleep"), Loc.T("Put this PC to sleep?"), CouchPower.Sleep));
            lines.Add(Power(Loc.T("Restart"), Loc.T("Restart this PC?"), CouchPower.Restart));
            lines.Add(Power(Loc.T("Shut down"), Loc.T("Shut this PC down?"), CouchPower.ShutDown));
            lines.Add(Line(Loc.T("Close"), CloseMenu));
            return lines;
        }

        /// <summary>The PC's sleep or power, after its question.</summary>
        private static CouchLine Power(string text, string question, CouchPower power) => new()
        {
            Text = text,
            Run = async () =>
            {
                if (await DialogService.ConfirmAsync(question))
                    CouchSystem.Power(power);
            },
        };

        private List<CouchLine> FilterLines()
        {
            string order = Settings.Sort switch { CouchSettings.ByRecent => Loc.T("played last"), CouchSettings.ByTime => Loc.T("most played"), _ => Loc.T("name") };
            var choices = new List<string?> { null };
            choices.AddRange(genres);
            return new List<CouchLine>
            {
                Setting(Loc.F("Order: {0}", order), () => Settings.Sort = CouchSettings.Next(new[] { CouchSettings.ByName, CouchSettings.ByRecent, CouchSettings.ByTime }, Settings.Sort), games: true),
                Setting(Loc.F("Installed only: {0}", YesNo(Settings.InstalledOnly)), () => Settings.InstalledOnly = !Settings.InstalledOnly, games: true),
                Setting(Loc.F("Never played only: {0}", YesNo(Settings.NeverPlayed)), () => Settings.NeverPlayed = !Settings.NeverPlayed, games: true),
                Setting(Loc.F("Genre: {0}", Settings.Genre ?? Loc.T("all")), () => Settings.Genre = CouchSettings.Next(choices, Settings.Genre), games: true),
                Setting(Loc.T("Show everything"), () => (Settings.Sort, Settings.InstalledOnly, Settings.NeverPlayed, Settings.Genre) = (CouchSettings.ByName, false, false, null), games: true),
                Line(Loc.T("Close"), CloseMenu),
            };
        }

        /// <summary>A program chosen on the disk (with the mouse or the keyboard, once) becomes a tile of the home.</summary>
        private async Task AddAppAsync()
        {
            if (TopLevel.GetTopLevel(this) is not TopLevel top)
                return;
            var chosen = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Loc.T("Add an application to the home..."), AllowMultiple = false });
            if (chosen.FirstOrDefault()?.TryGetLocalPath() is string path)
                AddApp(new CouchApp(Path.GetFileNameWithoutExtension(path), path));
        }

        internal void AddApp(CouchApp app)
        {
            Settings.Apps.RemoveAll(a => a.Path == app.Path);
            Settings.Apps.Add(app);
            SaveSettings();
            Reshow();
        }

        private List<CouchLine> StatsLines()
        {
            var games = all.Where(i => !i.IsTile).ToList();
            int minutes = games.Sum(MinutesOf);
            var lines = new List<string>
            {
                Loc.F("Library: {0} · Consoles and retro: {1}", games.Count(i => i.Pc != null), games.Count(i => i.Retro != null)),
                Loc.F("Installed here: {0}", games.Count(i => i.Installed)),
                Loc.F("Played: {0} · Never played: {1}", games.Count(i => MinutesOf(i) > 0), games.Count(i => MinutesOf(i) == 0)),
                Loc.F("Play time: {0}", minutes > 0 ? Minutes(minutes) : "0 min"),
            };
            lines.AddRange(games.Where(i => MinutesOf(i) > 0).OrderByDescending(MinutesOf).Take(3).Select((i, place) => $"{place + 1}. {i.Title} · {Minutes(MinutesOf(i))}"));
            var shown = lines.Select(text => Line(text, () => { })).ToList();
            shown.Add(Line(Loc.T("Back"), () => ShowMenu(CouchMenu.Main)));
            return shown;
        }

        // --- The achievements of a retro game ---

        /// <summary>The achievements of the game whose screen is shown, once RomM gave them.</summary>
        internal IReadOnlyList<GameVault.Core.Retro.RommProgress.Achievement> ShownAchievements { get; private set; } = Array.Empty<GameVault.Core.Retro.RommProgress.Achievement>();

        /// <summary>The earned ones first (the last earned on top), then the others; their badges a few at a time.</summary>
        private List<CouchLine> AchievementLines()
        {
            var achievements = ShownAchievements;
            int earned = achievements.Count(a => a.Earned);
            var lines = new List<CouchLine>
            {
                Line(Loc.F("Earned: {0} of {1} · {2} of {3} points", earned, achievements.Count, achievements.Where(a => a.Earned).Sum(a => a.Points), achievements.Sum(a => a.Points)), () => { }),
            };
            var shown = achievements.OrderByDescending(a => a.Earned).ThenByDescending(a => a.EarnedAt ?? DateTime.MinValue).ToList();
            var withBadges = new List<(string Url, Action<Avalonia.Media.Imaging.Bitmap> Show)>();
            foreach (var a in shown)
            {
                string detail = a.Description + " · " + Loc.F("{0} pts", a.Points);
                if (a.EarnedAt is DateTime at)
                    detail += " · " + Loc.F("earned on {0}", at.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture));
                var line = new CouchLine { Text = a.Title, Detail = detail, Opacity = a.Earned ? 1 : 0.5 };
                lines.Add(line);
                if ((a.Earned ? a.Badge : a.LockedBadge ?? a.Badge) is string url)
                    withBadges.Add((url, badge => line.Picture = badge));
            }
            lines.Add(Line(Loc.T("Back"), CloseMenu));
            _ = RetroService.LoadBadgesAsync(withBadges);
            return lines;
        }

        // --- The trailer in a game's screen ---

        private MediaSlider? trailer;
        /// <summary>The video of the game whose screen is shown, its address once YouTube gave it.</summary>
        private string? trailerVideo;
        private Tuple<string, string>? trailerUrl;
        private int trailerGeneration = -1;
        private readonly Stopwatch trailerWait = new();

        /// <summary>The trailer is playing in the game's screen.</summary>
        internal bool TrailerShown => uiTrailerBox.IsVisible;

        /// <summary>The first trailer of a game (else its first gameplay video); none when the setting is off.</summary>
        internal string? TrailerOf(CouchItem item)
        {
            if (!Settings.Trailers || Settings.Performance)
                return null;
            if (item.Retro is RetroGame retro)
                return retro.HasVideo ? $"https://www.youtube.com/watch?v={Uri.EscapeDataString(retro.Video!)}" : null;
            GameMetadata? metadata = item.Pc?.Metadata;
            return metadata?.Trailers?.FirstOrDefault() ?? metadata?.Gameplays?.FirstOrDefault();
        }

        /// <summary>A game's screen is shown: its trailer starts if the player stays on it a moment.</summary>
        private void QueueTrailer(CouchItem item)
        {
            StopTrailer();
            trailerVideo = TrailerOf(item);
            trailerUrl = null;
            trailerGeneration = detailGeneration;
            trailerWait.Restart();
        }

        /// <summary>
        /// The web player is drawn over everything of the window: it plays only on a game's screen with nothing over it
        /// (a menu, a question, the keyboard, the screensaver), and stops as soon as something comes.
        /// </summary>
        private bool TrailerMayPlay => IsVisible && trailerVideo != null && trailerGeneration == detailGeneration
            && Navigator.Screen == CouchScreen.Details && MainWindowViewModel.Instance.Popup == null && DialogService.Current == null
            && curtain == Curtain.None && Editing == null && playing == null && TopLevel.GetTopLevel(this) is Window { IsActive: true };

        /// <summary>Twice a second: started after two seconds on the screen, stopped when something comes over it.</summary>
        internal void KeepTrailer()
        {
            if (!TrailerMayPlay)
            {
                if (TrailerShown)
                    StopTrailer();
                return;
            }
            if (!TrailerShown && trailerWait.Elapsed >= TimeSpan.FromSeconds(2))
                _ = StartTrailerAsync(trailerGeneration);
        }

        private bool trailerStarting;

        private async Task StartTrailerAsync(int generation)
        {
            if (trailerStarting)
                return;
            trailerStarting = true;
            try
            {
                // Shown 860 pixels wide: 720p is plenty
                trailerUrl ??= await TrailerPopup.PlayableUrl(trailerVideo!, maxHeight: 720);
                if (trailerUrl == null)
                {
                    // Not playable: not asked again for this game
                    trailerVideo = null;
                    return;
                }
                if (generation != detailGeneration || !TrailerMayPlay)
                    return;
                if (trailer == null)
                {
                    trailer = new MediaSlider { ShowFullscreenButton = false };
                    uiTrailerHost.Child = trailer;
                }
                uiTrailerBox.IsVisible = true;
                await trailer.InitVideoPlayer();
                trailer.SetMediaList(new List<Tuple<string, string>> { trailerUrl });
                await trailer.LoadFirstElement();
            }
            catch (Exception ex) { Log.Ignored(ex); }
            finally { trailerStarting = false; }
        }

        private void StopTrailer()
        {
            if (!uiTrailerBox.IsVisible)
                return;
            try { trailer?.UnloadMediaSlider(); }
            catch (Exception ex) { Log.Ignored(ex); }
            uiTrailerBox.IsVisible = false;
        }

        // --- The logo of the selected game ---

        /// <summary>The selected game's logo is shown instead of its name.</summary>
        internal bool LogoShown => uiLogo.IsVisible;

        internal async Task ShowLogoAsync(CouchItem? item, int generation)
        {
            if (item == null || item.IsTile || !Settings.Logos)
                return;
            Avalonia.Media.Imaging.Bitmap? logo = item.Retro != null ? await GameLogoService.OfAsync(item.Retro) : await GameLogoService.OfAsync(item.Pc!);
            // Another game was selected while it came
            if (logo == null || generation != backGeneration || !Settings.Logos)
                return;
            uiLogo.Source = logo;
            uiLogo.IsVisible = true;
            uiTitle.IsVisible = false;
        }

        // --- The color of the selected game ---

        /// <summary>The accent color shown; null: the theme's.</summary>
        internal Color? AccentShown { get; private set; }

        private void ShowAccent(CouchItem? item)
        {
            Color? color = null;
            if (Settings.DynamicColors && !Settings.Performance && item != null && !item.IsTile)
            {
                if (item.Pc != null)
                {
                    if (!ReferenceEquals(uiAccentProbe.Data, item.Pc))
                        uiAccentProbe.Data = item.Pc;
                    color = uiAccentProbe.AccentColor;
                }
                else
                    color = item.Retro?.Accent;
            }
            if (color == null && Settings.Accent.Length > 0 && Color.TryParse(Settings.Accent, out Color chosen))
                color = chosen;
            if (color is Color dark)
            {
                // Read on a dark screen: a color too dark is made lighter
                double light = (0.299 * dark.R + 0.587 * dark.G + 0.114 * dark.B) / 255;
                if (light < 0.45)
                {
                    double mix = Math.Min(0.6, 0.45 - light + 0.15);
                    color = Color.FromRgb((byte)(dark.R + (255 - dark.R) * mix), (byte)(dark.G + (255 - dark.G) * mix), (byte)(dark.B + (255 - dark.B) * mix));
                }
            }
            if (color == AccentShown)
                return;
            AccentShown = color;
            if (color is Color accent)
                Resources["Brush.Accent"] = new SolidColorBrush(accent);
            else
                Resources.Remove("Brush.Accent");
        }

        // --- Over everything: the welcome, a game that starts, the screensaver ---

        internal enum Curtain { None, Welcome, Splash, Saver }
        private Curtain curtain;
        private int curtainGeneration, saverTicks;
        internal Curtain CurtainShown => curtain;
        internal string CurtainTitle => uiCurtainTitle.Text ?? "";

        private void ShowCurtain(Curtain kind, CouchItem? item, string title, string text)
        {
            curtain = kind;
            curtainGeneration++;
            uiCurtainPc.IsVisible = item?.Pc != null;
            if (item?.Pc != null)
                uiCurtainPc.Data = item.Pc;
            uiCurtainRetro.IsVisible = item?.Retro != null;
            uiCurtainRetro.Source = item?.Retro?.Banner;
            uiCurtainAvatarFrame.IsVisible = kind == Curtain.Welcome;
            if (kind == Curtain.Welcome)
                uiCurtainAvatar.Data = MainWindowViewModel.Instance.UserAvatar;
            uiCurtainTitle.Text = title;
            uiCurtainText.Text = text;
            uiCurtainClock.Text = kind == Curtain.Saver ? DateTime.Now.ToString("t", CultureInfo.CurrentCulture) : "";
            uiCurtain.IsVisible = true;
        }

        private void HideCurtain()
        {
            curtain = Curtain.None;
            uiCurtain.IsVisible = false;
            idle.Restart();
        }

        private void ShowWelcome() => ShowCurtain(Curtain.Welcome, null,
            Loc.F("Hello {0}", LoginManager.Instance.GetCurrentUser()?.Username ?? ""), Loc.T("Press a button to start"));

        /// <summary>The game's picture while it starts: it goes when the game's window comes, or after a moment.</summary>
        internal void ShowSplash(CouchItem item)
        {
            if (!Settings.Splash)
                return;
            ShowCurtain(Curtain.Splash, item, item.Title, Loc.T("Starting..."));
            int generation = curtainGeneration;
            DispatcherTimer.RunOnce(() =>
            {
                if (curtain == Curtain.Splash && generation == curtainGeneration)
                    HideCurtain();
            }, TimeSpan.FromSeconds(8));
        }

        /// <summary>Nothing touched for the time set: the games are shown one after the other.</summary>
        internal void CheckIdle(TimeSpan idleFor, Random? random = null)
        {
            if (curtain == Curtain.Saver)
            {
                // Another game every 8 seconds
                if (++saverTicks % 16 == 0)
                    ShowSaverGame(random);
                else
                    uiCurtainClock.Text = DateTime.Now.ToString("t", CultureInfo.CurrentCulture);
                return;
            }
            if (Settings.ScreenSaverMinutes <= 0 || curtain != Curtain.None || idleFor < TimeSpan.FromMinutes(Settings.ScreenSaverMinutes) || all.Count == 0
                || MainWindowViewModel.Instance.Popup != null || DialogService.Current != null || Editing != null || overlay != null)
                return;
            saverTicks = 0;
            ShowSaverGame(random);
        }

        private void ShowSaverGame(Random? random)
        {
            // The library's games have a large picture; the retro ones when there are no others
            var pool = all.Where(i => i.Pc != null).ToList();
            if (pool.Count == 0)
                pool = all;
            CouchItem item = pool[(random ?? Random.Shared).Next(pool.Count)];
            ShowCurtain(Curtain.Saver, item, item.Title, item.Retro?.Platform.Name ?? MetaOf(item));
        }

        private int refreshesMore;

        /// <summary>Twice a second: the screensaver, and what the home's tiles tell.</summary>
        private void RefreshMore()
        {
            bool front = TopLevel.GetTopLevel(this) is Window { IsActive: true };
            if (front && !ActivityService.Instance.IsPlayingHere)
                CheckIdle(idle.Elapsed);
            else if (curtain != Curtain.Saver)
                idle.Restart();
            if (++refreshesMore % 6 != 0)
                return;
            if (tiles.TryGetValue("friends", out CouchTile? friends))
                friends.Subtitle = FriendsText();
            if (tiles.TryGetValue("downloads", out CouchTile? downloads))
                downloads.Subtitle = DownloadsText();
        }

        // --- The friends who played the game ---

        private async Task ShowFriendsAsync(CouchItem item, int generation)
        {
            try
            {
                Game? game = JsonSerializer.Deserialize<Game>(await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games/{item.Pc!.ID}"));
                int me = LoginManager.Instance.GetCurrentUser()?.ID ?? -1;
                var played = (game?.Progresses ?? new List<Progress>()).Where(p => p.User != null && p.User.ID != me && (p.MinutesPlayed ?? 0) > 0)
                    .OrderByDescending(p => p.MinutesPlayed ?? 0).Take(3).Select(p => $"{p.User!.Username} {Minutes(p.MinutesPlayed ?? 0)}").ToList();
                if (played.Count == 0 || generation != detailGeneration)
                    return;
                var shown = (uiDetailStats.ItemsSource as IEnumerable<CouchStat> ?? StatsOf(item)).ToList();
                shown.Add(new CouchStat(Loc.T("Friends"), string.Join(", ", played)));
                uiDetailStats.ItemsSource = shown;
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        // --- Windows: the mouse with the right stick, and the menu over a game ---

        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

        private (double X, double Y) mouseRest;
        private bool mouseDown, comboHeld;

        /// <summary>How far the mouse goes for where the stick points: slow near its rest, fast at its end; what is under a pixel is kept for the next time.</summary>
        internal static (int X, int Y) MouseStep((double X, double Y) stick, ref (double X, double Y) rest)
        {
            double pushed = Math.Sqrt(stick.X * stick.X + stick.Y * stick.Y);
            const double dead = 0.18;
            if (pushed < dead)
            {
                rest = default;
                return (0, 0);
            }
            double strength = Math.Min(1, (pushed - dead) / (1 - dead));
            double speed = 1.5 + 22 * strength * strength;
            double x = stick.X / pushed * speed + rest.X, y = stick.Y / pushed * speed + rest.Y;
            (int X, int Y) step = ((int)Math.Truncate(x), (int)Math.Truncate(y));
            rest = (x - step.X, y - step.Y);
            return step;
        }

        private void ApplyMouse(CouchButtons held, (double X, double Y) stick)
        {
            if (!OperatingSystem.IsWindows())
                return;
            try
            {
                var (x, y) = MouseStep(stick, ref mouseRest);
                if (x != 0 || y != 0)
                    mouse_event(0x0001, x, y, 0, UIntPtr.Zero);
                bool down = held.HasFlag(CouchButtons.RightStick);
                if (down != mouseDown)
                {
                    mouseDown = down;
                    mouse_event(down ? 0x0002u : 0x0004u, 0, 0, 0, UIntPtr.Zero);
                }
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>At each reading of the gamepads, whatever window is in front.</summary>
        private void TickMore(CouchButtons held, bool listen)
        {
            if (Settings.Mouse || mouseDown)
                ApplyMouse(Settings.Mouse ? held : CouchButtons.None, Settings.Mouse ? gamepad!.RightStick : default);
            bool combo = held.HasFlag(CouchButtons.Start) && held.HasFlag(CouchButtons.Back);
            if (combo && !comboHeld && OperatingSystem.IsWindows() && Settings.Overlay)
            {
                if (overlay != null)
                    CloseOverlay();
                else if (!listen)
                    OpenOverlayOverGame();
            }
            comboHeld = combo;
        }

        /// <summary>The window in front is a game's: the menu over it gives it back, closes it, or shows SanctuaryVault.</summary>
        private void OpenOverlayOverGame()
        {
            try
            {
                IntPtr window = GetForegroundWindow();
                if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out uint id) == 0 || id == 0 || id == Environment.ProcessId)
                    return;
                string name;
                using (Process game = Process.GetProcessById((int)id))
                    name = string.IsNullOrWhiteSpace(game.MainWindowTitle) ? game.ProcessName : game.MainWindowTitle;
                // Never the desktop itself
                if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                    return;
                OpenOverlay(name, () => CouchSystem.GiveFront(window), () =>
                {
                    using Process game = Process.GetProcessById((int)id);
                    game.Kill(entireProcessTree: true);
                });
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        internal CouchOverlay? Overlay => overlay;

        internal void OpenOverlay(string title, Action resume, Action quit)
        {
            CloseOverlay();
            void Then(Action action, bool here)
            {
                CloseOverlay();
                try { action(); }
                catch (Exception ex) { Log.Ignored(ex); }
                if (here && TopLevel.GetTopLevel(this) is Window window)
                    window.Activate();
            }
            overlay = new CouchOverlay(title, new (string, Action)[]
            {
                (Loc.T("Back to the game"), () => Then(resume, false)),
                (Loc.T("Quit the game"), () => Then(quit, true)),
                (Loc.T("Show SanctuaryVault"), () => Then(() => { }, true)),
            });
            overlay.Closed += (_, _) => overlay = null;
            overlay.Show();
            overlay.Activate();
            repeater.Reset(CouchButtons.Start | CouchButtons.Back);
        }

        private void CloseOverlay()
        {
            CouchOverlay? open = overlay;
            overlay = null;
            open?.Close();
        }
    }
}
