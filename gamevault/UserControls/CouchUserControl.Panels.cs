using gamevault.Converter;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using GameVault.Core;
using GameVault.Core.Couch;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>
    /// What the desktop's pages show, in the living-room mode itself, read with the gamepad: the downloads (paused,
    /// resumed, cancelled from here), the challenge of the week and its ranking, the friends and what they play.
    /// </summary>
    public partial class CouchUserControl
    {
        // --- The downloads ---

        /// <summary>The speeds of the last minute, a sample at each refresh: the curve beside the total speed.</summary>
        private readonly List<double> speeds = new();
        private const int SpeedSamples = 60;

        private IEnumerable<(object Download, string Title)> Downloads() =>
            DownloadsViewModel.Instance.DownloadedGames.ToList().Select(d => ((object)d, d.GameTitle))
                .Concat(DownloadsViewModel.Instance.RetroDownloads.ToList().Select(d => ((object)d, d.Game.Name)));

        /// <summary>A card a download: A does what it needs most (pause, resume, start, retry, open the game), X cancels or removes it.</summary>
        private List<CouchLine> DownloadsLines()
        {
            var lines = new List<CouchLine>();
            foreach (var (download, title) in Downloads())
            {
                var line = new CouchDownloadLine(download, title) { Run = () => DownloadAction(download, cancel: false) };
                lines.Add(line);
            }
            if (lines.Count == 0)
                lines.Add(Line(Loc.T("None in progress"), () => { }));
            foreach (CouchLine line in lines)
                if (line is CouchDownloadLine card)
                    card.Buttons = DownloadButtons(card);
            ShowDownloadsSummary(lines);
            return lines;
        }

        /// <summary>What A and X do on a download, as it is now; none for what can not be done.</summary>
        private static (string? Main, string? Second) DownloadActions(object download) => download switch
        {
            GameDownloadUserControl pc when pc.ViewModel.DownloadFailedVisibility => (Loc.T("Retry"), null),
            GameDownloadUserControl pc when pc.IsPaused() => (Loc.T("Resume"), Loc.T("Cancel")),
            GameDownloadUserControl pc when DownloadQueue.IsWaiting(pc) => (Loc.T("Start now"), Loc.T("Cancel")),
            GameDownloadUserControl pc when pc.IsDownloading() => (Loc.T("Pause"), Loc.T("Cancel")),
            GameDownloadUserControl pc when pc.ViewModel.ExtractionUIVisibility => (null, null),
            GameDownloadUserControl => (Loc.T("Open"), null),
            RetroDownload retro when retro.IsFailed => (Loc.T("Retry"), Loc.T("Remove from the list")),
            RetroDownload retro when retro.IsDone => (Loc.T("Open"), Loc.T("Remove from the list")),
            RetroDownload retro when retro.IsPaused => (Loc.T("Resume"), Loc.T("Cancel")),
            RetroDownload retro when retro.IsQueued => (Loc.T("Start now"), Loc.T("Cancel")),
            RetroDownload retro when retro.IsActive => (Loc.T("Pause"), Loc.T("Cancel")),
            RetroDownload retro when retro.IsPreparing => (null, Loc.T("Cancel")),
            _ => (null, null),
        };

        private IReadOnlyList<CouchStat> DownloadButtons(CouchDownloadLine card)
        {
            var (main, second) = DownloadActions(card.Download);
            var buttons = new List<CouchStat>();
            if (main != null)
                buttons.Add(new CouchStat(CouchGlyphs.Label(ButtonStyle, "A"), main));
            if (second != null)
                buttons.Add(new CouchStat(CouchGlyphs.Label(ButtonStyle, "X"), second));
            return buttons;
        }

        /// <summary>A or X on a download: what its card says it does.</summary>
        private async Task DownloadAction(object download, bool cancel)
        {
            var (main, second) = DownloadActions(download);
            string? action = cancel ? second : main;
            if (action == null)
                return;
            string title = download is RetroDownload r ? r.Game.Name : ((GameDownloadUserControl)download).GameTitle;
            try
            {
                if (action == Loc.T("Cancel"))
                {
                    if (!await DialogService.ConfirmAsync(Loc.F("Cancel the download of {0}?", title)))
                        return;
                    if (download is RetroDownload retro)
                        retro.Cancel();
                    else
                        ((GameDownloadUserControl)download).CancelDownload();
                }
                else if (action == Loc.T("Open"))
                {
                    string key = download is RetroDownload done ? $"retro:{done.Game.Platform.Slug}:{done.Game.Id}" : $"pc:{((GameDownloadUserControl)download).GetGameId()}";
                    if (all.Any(i => i.Key == key))
                    {
                        CloseMenu();
                        OpenGame(key);
                        return;
                    }
                }
                else
                {
                    switch (download)
                    {
                        case GameDownloadUserControl pc when action == Loc.T("Retry"): pc.Retry(); break;
                        case GameDownloadUserControl pc when action == Loc.T("Start now"): pc.StartNow(); break;
                        case GameDownloadUserControl pc: pc.TogglePause(); break;
                        case RetroDownload retro when action == Loc.T("Retry"): retro.Retry(); break;
                        case RetroDownload retro when action == Loc.T("Remove from the list"): retro.Dismiss(); break;
                        case RetroDownload retro when action == Loc.T("Start now"): retro.StartNow(); break;
                        case RetroDownload retro when retro.IsPaused: retro.Resume(); break;
                        case RetroDownload retro: retro.Pause(); break;
                    }
                }
            }
            catch (Exception ex) { Log.Ignored(ex); }
            if (menu == CouchMenu.Downloads && Navigator.Screen == CouchScreen.Menu)
                RefreshDownloads(rebuild: true);
        }

        /// <summary>X in the downloads: the second action of the chosen card (cancel, remove).</summary>
        private bool HandleDownloadsButtons(CouchCommand command)
        {
            if (Navigator.Screen != CouchScreen.Menu || menu != CouchMenu.Downloads || command != CouchCommand.View)
                return false;
            if (Navigator.MenuLine < MenuLines.Count && MenuLines[Navigator.MenuLine] is CouchDownloadLine card && DownloadActions(card.Download).Second != null)
            {
                Play(CouchSound.Accept);
                _ = DownloadAction(card.Download, cancel: true);
            }
            return true;
        }

        /// <summary>The total speed and its curve, what is left of all of them, and when all are done.</summary>
        private void ShowDownloadsSummary(IEnumerable<CouchLine> lines)
        {
            var cards = lines.OfType<CouchDownloadLine>().ToList();
            double speed = cards.Sum(c => c.PerSecond);
            long left = cards.Sum(c => c.BytesLeft);
            // Nothing: "0 Mo/s", "0 Go", not bytes
            (uiBoardSpeed.Text, uiBoardSpeedUnit.Text) = speed >= 1 ? Split(GameSizeConverter.Text(speed, 1000), "/s") : ("0", Loc.T("MB") + "/s");
            (uiBoardLeft.Text, uiBoardLeftUnit.Text) = left > 0 ? Split(GameSizeConverter.Text(left), "") : ("0", Loc.T("GB"));
            if (GameVault.Core.Downloads.TimeLeft.Of(left, speed) is TimeSpan time && left > 0)
            {
                uiBoardTime.Text = CouchDownloadLine.Duration(time);
                uiBoardTimeLabel.Text = Loc.F("All done around {0}", DateTime.Now.Add(time).ToString("HH:mm", CultureInfo.CurrentCulture));
            }
            else
            {
                uiBoardTime.Text = "—";
                uiBoardTimeLabel.Text = left > 0 ? Loc.T("Nothing downloads now") : Loc.T("Nothing left to download");
            }
            DrawSpeeds();
        }

        /// <summary>"48,2 Mo/s" as "48,2" large and "Mo/s" small.</summary>
        private static (string Number, string Unit) Split(string size, string after)
        {
            int space = size.LastIndexOf(' ');
            return space < 0 ? (size, after) : (size[..space], size[(space + 1)..] + after);
        }

        /// <summary>The curve of the last speeds, the highest at the top.</summary>
        private void DrawSpeeds()
        {
            const double width = 170, height = 56;
            double top = Math.Max(1, speeds.DefaultIfEmpty(0).Max());
            var points = new List<Avalonia.Point>();
            for (int i = 0; i < speeds.Count; i++)
            {
                double x = speeds.Count == 1 ? width : i * width / (speeds.Count - 1);
                points.Add(new Avalonia.Point(x, height - 4 - speeds[i] / top * (height - 10)));
            }
            uiBoardCurve.Points = points;
            uiBoardCurveFill.Points = points.Count == 0 ? new List<Avalonia.Point>()
                : points.Prepend(new Avalonia.Point(points[0].X, height)).Append(new Avalonia.Point(points[^1].X, height)).ToList();
        }

        /// <summary>
        /// Twice a second the downloads shown move on: their cards are written again, on the same ones; a download
        /// added or gone gives the list again, on the same line.
        /// </summary>
        private void RefreshDownloads()
        {
            // Sampled always: the curve has the last half minute as soon as the screen opens
            double speed = DownloadsViewModel.Instance.DownloadedGames.ToList().Where(d => d.IsDownloading() && !d.IsPaused()).Sum(d => d.Bytes.PerSecond)
                + DownloadsViewModel.Instance.RetroDownloads.ToList().Where(d => d.IsActive).Sum(d => d.Bytes.PerSecond);
            speeds.Add(speed);
            if (speeds.Count > SpeedSamples)
                speeds.RemoveRange(0, speeds.Count - SpeedSamples);
            RefreshDownloads(rebuild: false);
        }

        private void RefreshDownloads(bool rebuild)
        {
            if (Navigator.Screen != CouchScreen.Menu || menu != CouchMenu.Downloads)
                return;
            var now = Downloads().Select(d => d.Download).ToList();
            var shown = MenuLines.OfType<CouchDownloadLine>().Select(l => l.Download).ToList();
            if (!rebuild && now.SequenceEqual(shown) && (now.Count > 0 || MenuLines.Count == 1))
            {
                foreach (CouchDownloadLine card in MenuLines.OfType<CouchDownloadLine>())
                {
                    card.Update();
                    card.Buttons = DownloadButtons(card);
                }
                ShowDownloadsSummary(MenuLines);
                ShowScreen(false);
                return;
            }
            int line = Navigator.MenuLine;
            BuildMenu();
            Navigator.SelectLine(Math.Min(line, MenuLines.Count - 1));
            ShowScreen(false);
        }

        // --- The challenge of the week ---

        private ChallengeState? challenge;

        private async Task OpenChallengeAsync()
        {
            try { challenge = await ChallengesService.GetAsync(); }
            catch (Exception ex) { Log.Ignored(ex); challenge = null; }
            ShowMenu(CouchMenu.Challenge);
        }

        private List<CouchLine> ChallengeLines()
        {
            var lines = new List<CouchLine>();
            if (challenge is not ChallengeState state)
                lines.Add(Line(Loc.T("Not available on this server"), () => { }));
            else
            {
                ChallengeInfo info = state.Challenge;
                int me = LoginManager.Instance.GetCurrentUser()?.ID ?? -1;
                ChallengeRank? mine = state.Board.FirstOrDefault(r => r.UserId == me);
                lines.Add(new CouchLine { Text = info.Text, Detail = info.Hint, Run = () => Task.CompletedTask });
                string progress = mine?.DoneAt != null ? Loc.T("Completed") : Loc.F("{0} of {1}", info.Amount(mine?.Value ?? 0), info.Amount(info.Goal));
                lines.Add(new CouchLine
                {
                    Text = Loc.F("You: {0}", progress),
                    Detail = Loc.F("Ends on {0}", state.EndsAt.ToLocalTime().ToString("d MMMM yyyy, HH:mm", CultureInfo.CurrentCulture)),
                    Run = () => Task.CompletedTask,
                });
                // A game to play for it: its screen
                if (info.TheGame is { } game && all.FirstOrDefault(i => i.Pc?.ID == game.Id) is CouchItem item)
                    lines.Add(Line(Loc.F("Open {0}", item.Title), () => { CloseMenu(); OpenGame(item.Key); }));
                int place = 0;
                foreach (ChallengeRank rank in state.Board)
                {
                    place++;
                    string done = rank.DoneAt != null ? " · " + Loc.T("Completed") : "";
                    lines.Add(new CouchLine { Text = $"{place}. {rank.Username}", Detail = info.Amount(rank.Value) + done, Opacity = rank.UserId == me ? 1 : 0.8, Run = () => Task.CompletedTask });
                }
                if (state.Board.Count == 0)
                    lines.Add(Line(Loc.T("Nobody took part yet"), () => { }));
            }
            lines.Add(Line(Loc.T("Back"), CloseMenu));
            return lines;
        }

        // --- The friends ---

        private static User[]? users;

        private async Task OpenFriendsAsync()
        {
            try
            {
                users ??= JsonSerializer.Deserialize<User[]>(await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/users"));
                await PresenceConverter.LoadTitlesAsync();
            }
            catch (Exception ex) { Log.Ignored(ex); }
            ShowMenu(CouchMenu.Friends);
        }

        /// <summary>The players of the server, the ones playing first, then online, away, offline; A on one playing opens the game.</summary>
        private List<CouchLine> FriendsLines()
        {
            var lines = new List<CouchLine>();
            int me = LoginManager.Instance.GetCurrentUser()?.ID ?? -1;
            var friends = (users ?? Array.Empty<User>()).Where(u => u.ID != me && u.DeletedAt == null)
                .Select(u => (User: u, Seen: PresenceConverter.Describe(u.ID)))
                .OrderBy(f => f.Seen.Rank).ThenBy(f => f.User.Username, StringComparer.CurrentCultureIgnoreCase);
            foreach (var (user, seen) in friends)
            {
                string? game = GameKeyOf(user.ID);
                lines.Add(new CouchLine
                {
                    Text = user.Username ?? "",
                    Detail = seen.Text,
                    Opacity = seen.Dot == null ? 0.55 : 1,
                    Run = () =>
                    {
                        if (game != null && all.Any(i => i.Key == game))
                        {
                            CloseMenu();
                            OpenGame(game);
                        }
                        return Task.CompletedTask;
                    },
                });
            }
            if (lines.Count == 0)
                lines.Add(Line(Loc.T("Nobody is online"), () => { }));
            lines.Add(Line(Loc.T("Back"), CloseMenu));
            return lines;
        }

        /// <summary>The game a player is playing, as this mode names it; null when none or not in its lists.</summary>
        private static string? GameKeyOf(int userId) =>
            ActivityService.Instance.Of(userId) is { IsPlaying: true } activity ? $"pc:{activity.GameId}"
            : ActivityService.Instance.RetroOf(userId) is RetroActivity retro ? $"retro:{retro.Platform}:{retro.RomId}"
            : null;

        // --- The news ---

        private ServerNewsData? news;

        private async Task OpenNewsAsync()
        {
            try
            {
                news = await ServerNewsService.LoadAsync();
                ServerNewsService.MarkSeen();
                MainWindowViewModel.Instance.NewsBadge = "";
            }
            catch (Exception ex) { Log.Ignored(ex); news = null; }
            ShowMenu(CouchMenu.News);
        }

        /// <summary>The administrator's message, then the games added and updated lately, by group; A on one opens its screen here.</summary>
        private List<CouchLine> NewsLines()
        {
            var lines = new List<CouchLine>();
            if (news?.Announcement is { Length: > 0 } announcement)
                lines.Add(new CouchLine { Text = Loc.T("Message from the administrator"), Detail = PlainText(announcement), Run = () => Task.CompletedTask });
            foreach (NewsGroup group in news?.Groups ?? new List<NewsGroup>())
            {
                lines.Add(new CouchLine { Text = group.Title, Opacity = 0.6, Run = () => Task.CompletedTask });
                foreach (NewsItem item in group.Items)
                {
                    string? key = item.Retro is RetroGame retro ? $"retro:{retro.Platform.Slug}:{retro.Id}" : item.Game != null ? $"pc:{item.Game.ID}" : null;
                    lines.Add(new CouchLine
                    {
                        Text = item.Title,
                        Detail = string.Join(" · ", new[] { item.KindText, item.Info, item.DateText }.Where(t => !string.IsNullOrEmpty(t))),
                        Run = () =>
                        {
                            if (key != null && all.Any(i => i.Key == key))
                            {
                                CloseMenu();
                                OpenGame(key);
                            }
                            return Task.CompletedTask;
                        },
                    });
                }
            }
            if (lines.Count == 0)
                lines.Add(Line(Loc.T("Nothing new for now"), () => { }));
            lines.Add(Line(Loc.T("Back"), CloseMenu));
            return lines;
        }

        /// <summary>The message's Markdown read as words: links as their text, no marks.</summary>
        internal static string PlainText(string markdown)
        {
            string text = System.Text.RegularExpressions.Regex.Replace(markdown, @"!?\[([^\]]*)\]\([^)]*\)", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(^|\n)\s*(#+|[-*]|>)\s+", "$1");
            text = text.Replace("**", "").Replace("__", "").Replace("`", "");
            return string.Join(" ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0));
        }

        // --- What the client says ---

        private int noticeGeneration;

        /// <summary>The text shown at the bottom, or on the start screen of a game; empty when none.</summary>
        internal string Notice => uiNotice.IsVisible ? uiNoticeText.Text ?? "" : "";

        private void ClientSaid(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MainWindowViewModel.AppBarText) || !IsVisible || MainWindowViewModel.Instance.AppBarText is not { Length: > 0 } text)
                return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowNotice(text));
        }

        /// <summary>
        /// A game being started: on its start screen, under its name ("Syncing cloud save...", "No valid Executable
        /// found"); else at the bottom of the screen for some seconds, as the client's bar does.
        /// </summary>
        internal void ShowNotice(string text)
        {
            if (curtain == Curtain.Splash)
            {
                uiCurtainText.Text = text;
                return;
            }
            uiNoticeText.Text = text;
            uiNotice.IsVisible = true;
            int generation = ++noticeGeneration;
            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                if (generation == noticeGeneration)
                    uiNotice.IsVisible = false;
            }, TimeSpan.FromSeconds(Math.Clamp(4 + text.Length / 40.0, 4, 10)));
        }

        // --- Where a game is installed ---

        private InstallLocationUserControl? locationPicker;
        private bool askLocationAgain = true;

        /// <summary>
        /// The desktop's question as a screen of the mode: the game at the left, the game folders at the right as
        /// their disks, filled, the game's room striped; A installs, Y asks again or not, B installs nothing.
        /// </summary>
        internal void ChooseInstallLocation(InstallLocationUserControl picker, Game game)
        {
            locationPicker?.Choose(string.Empty);
            locationPicker = picker;
            askLocationAgain = true;
            uiPlaceCover.Data = game;
            uiPlaceTitle.Text = game.Title ?? "";
            uiPlaceSize.Text = picker.RequiredBytes > 0 ? GameSizeConverter.Text(picker.RequiredBytes) : "";
            uiPlaceSizeChip.IsVisible = picker.RequiredBytes > 0;
            ShowAskAgain();
            ShowMenu(CouchMenu.InstallLocation);
            // The folder used last is under the finger, else the first one with room
            int line = MenuLines.FindIndex(l => l is CouchDiskLine { IsLast: true, HasRoom: true });
            if (line < 0)
                line = MenuLines.FindIndex(l => l is CouchDiskLine { HasRoom: true });
            if (line >= 0)
            {
                Navigator.SelectLine(line);
                ShowScreen(false);
            }
        }

        private List<CouchLine> InstallLocationLines()
        {
            if (locationPicker == null)
                return new List<CouchLine> { Line(Loc.T("Back"), CloseMenu) };
            var lines = new List<CouchLine>();
            foreach (InstallLocationRow row in locationPicker.Rows)
            {
                // Not enough room, or a disk unplugged: shown, not chosen
                lines.Add(CouchDiskLine.Of(row, locationPicker.RequiredBytes, () =>
                {
                    if (row.HasRoom)
                        AnswerInstallLocation(row.Path);
                }));
            }
            if (lines.Count == 0)
                lines.Add(Line(Loc.T("Cancel"), () => AnswerInstallLocation(string.Empty)));
            return lines;
        }

        /// <summary>Y on the question: asked each time, or not any more.</summary>
        private bool HandleInstallLocationButtons(CouchCommand command)
        {
            if (Navigator.Screen != CouchScreen.Menu || menu != CouchMenu.InstallLocation || command != CouchCommand.Search)
                return false;
            Play(CouchSound.Accept);
            askLocationAgain = !askLocationAgain;
            ShowAskAgain();
            return true;
        }

        private void ShowAskAgain()
        {
            uiPlaceAsk.HorizontalAlignment = askLocationAgain ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left;
            Avalonia.Media.IBrush on = Avalonia.Controls.ResourceNodeExtensions.TryFindResource(this, "Brush.Accent", out object? accent) && accent is Avalonia.Media.IBrush brush
                ? brush : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#7C6BFF"));
            uiPlaceAskSwitch.Background = askLocationAgain ? on : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#33FFFFFF"));
        }

        /// <summary>Asked again at the next installation (the tests read it).</summary>
        internal bool AsksLocationAgain => askLocationAgain;

        private void AnswerInstallLocation(string directory)
        {
            InstallLocationUserControl? picker = locationPicker;
            locationPicker = null;
            CloseMenu();
            if (picker == null)
                return;
            picker.uiDontAsk.IsChecked = !askLocationAgain;
            picker.Choose(directory);
        }
    }
}
