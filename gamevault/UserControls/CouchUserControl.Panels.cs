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

        /// <summary>The download whose actions are shown (a library game's or a retro game's).</summary>
        private object? shownDownload;

        private static string Percent(int progress) => progress > 0 ? $" · {progress} %" : "";

        private List<CouchLine> DownloadsLines()
        {
            var lines = new List<CouchLine>();
            foreach (GameDownloadUserControl download in DownloadsViewModel.Instance.DownloadedGames.ToList())
            {
                string state = Loc.T(download.StateText);
                lines.Add(new CouchLine
                {
                    Text = download.GameTitle,
                    Detail = state + (download.IsDownloading() || download.IsPaused() ? Percent(download.GetDownloadProgress()) : ""),
                    Run = () => { OpenDownload(download); return Task.CompletedTask; },
                });
            }
            foreach (RetroDownload download in DownloadsViewModel.Instance.RetroDownloads.ToList())
            {
                lines.Add(new CouchLine
                {
                    Text = download.Game.Name,
                    Detail = $"{download.Game.Platform.Name} · {download.State}" + (download.IsRunning || download.IsPaused ? Percent(download.Progress) : ""),
                    Run = () => { OpenDownload(download); return Task.CompletedTask; },
                });
            }
            if (lines.Count == 0)
                lines.Add(Line(Loc.T("None in progress"), () => { }));
            lines.Add(Line(Loc.T("Back"), CloseMenu));
            return lines;
        }

        private void OpenDownload(object download)
        {
            shownDownload = download;
            ShowMenu(CouchMenu.Download);
        }

        /// <summary>What can be done with the download chosen: pause or resume it, try it again, cancel it.</summary>
        private List<CouchLine> DownloadLines()
        {
            var lines = new List<CouchLine>();
            void Back() => ShowMenu(CouchMenu.Downloads);
            void Then(Action action)
            {
                try { action(); }
                catch (Exception ex) { Log.Ignored(ex); }
                Back();
            }
            CouchLine Cancel(string title, Action cancel) => new()
            {
                Text = Loc.T("Cancel the download"),
                Run = async () =>
                {
                    if (await DialogService.ConfirmAsync(Loc.F("Cancel the download of {0}?", title)))
                        Then(cancel);
                    else
                        ShowMenu(CouchMenu.Download);
                },
            };
            switch (shownDownload)
            {
                case GameDownloadUserControl pc when DownloadsViewModel.Instance.DownloadedGames.Contains(pc):
                    lines.Add(new CouchLine { Text = pc.GameTitle, Detail = Loc.T(pc.StateText) + Percent(pc.GetDownloadProgress()), Run = () => Task.CompletedTask });
                    if (pc.IsPaused())
                        lines.Add(Line(Loc.T("Resume"), () => Then(pc.TogglePause)));
                    else if (pc.IsDownloading())
                        lines.Add(Line(Loc.T("Pause"), () => Then(pc.TogglePause)));
                    if (pc.IsDownloading() || pc.IsPaused())
                        lines.Add(Cancel(pc.GameTitle, pc.CancelDownload));
                    break;
                case RetroDownload retro when DownloadsViewModel.Instance.RetroDownloads.Contains(retro):
                    lines.Add(new CouchLine { Text = retro.Game.Name, Detail = retro.State + Percent(retro.Progress), Run = () => Task.CompletedTask });
                    if (retro.IsPaused)
                        lines.Add(Line(Loc.T("Resume"), () => Then(retro.Resume)));
                    else if (retro.IsActive)
                        lines.Add(Line(Loc.T("Pause"), () => Then(() => retro.Pause())));
                    if (retro.IsFailed)
                        lines.Add(Line(Loc.T("Retry"), () => Then(retro.Retry)));
                    if (retro.IsRunning || retro.IsPaused || retro.IsQueued)
                        lines.Add(Cancel(retro.Game.Name, retro.Cancel));
                    if (retro.IsDone || retro.IsFailed)
                        lines.Add(Line(Loc.T("Remove from the list"), () => Then(retro.Dismiss)));
                    break;
                default:
                    // Gone meanwhile (finished and removed)
                    lines.Add(Line(Loc.T("None in progress"), () => { }));
                    break;
            }
            lines.Add(Line(Loc.T("Back"), Back));
            return lines;
        }

        /// <summary>Twice a second, the downloads shown move on: their lines are written again, on the same one.</summary>
        private void RefreshDownloads()
        {
            if (Navigator.Screen != CouchScreen.Menu || menu is not (CouchMenu.Downloads or CouchMenu.Download))
                return;
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
        private string locationGame = "";
        private bool askLocationAgain = true;

        /// <summary>The desktop's question as a screen of the mode: the game folders with their free room, chosen with A; B: none.</summary>
        internal void ChooseInstallLocation(InstallLocationUserControl picker, string game)
        {
            locationPicker?.Choose(string.Empty);
            locationPicker = picker;
            locationGame = game;
            askLocationAgain = true;
            ShowMenu(CouchMenu.InstallLocation);
            // The folder used last is under the finger
            int line = MenuLines.FindIndex(l => picker.Rows.Any(r => r.IsLast && r.HasRoom && r.Path == l.Text));
            if (line >= 0)
            {
                Navigator.SelectLine(line);
                ShowScreen(false);
            }
        }

        private List<CouchLine> InstallLocationLines()
        {
            var lines = new List<CouchLine>();
            if (locationPicker == null)
                return new List<CouchLine> { Line(Loc.T("Back"), CloseMenu) };
            if (locationPicker.uiNeeded.IsVisible)
                lines.Add(new CouchLine { Text = locationGame, Detail = locationPicker.uiNeeded.Text, Opacity = 0.7, Run = () => Task.CompletedTask });
            foreach (InstallLocationRow row in locationPicker.Rows)
            {
                string detail = row.IsLast && row.Details.Length > 0 ? $"{Loc.T("Last used")} · {row.Details}" : row.IsLast ? Loc.T("Last used") : row.Details;
                lines.Add(new CouchLine
                {
                    Text = row.Path,
                    Detail = detail,
                    // Not enough room, or a disk unplugged: shown, not chosen
                    Opacity = row.HasRoom ? 1 : 0.45,
                    Run = () =>
                    {
                        if (row.HasRoom)
                            AnswerInstallLocation(row.Path);
                        return Task.CompletedTask;
                    },
                });
            }
            lines.Add(Line(Loc.F("Ask each time: {0}", askLocationAgain ? Loc.T("Yes") : Loc.T("No")), () =>
            {
                askLocationAgain = !askLocationAgain;
                int line = Navigator.MenuLine;
                BuildMenu();
                Navigator.SelectLine(line);
                ShowScreen(false);
            }));
            lines.Add(Line(Loc.T("Cancel"), () => AnswerInstallLocation(string.Empty)));
            return lines;
        }

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
