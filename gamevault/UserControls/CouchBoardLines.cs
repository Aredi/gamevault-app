using Avalonia.Media;
using gamevault.Converter;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using GameVault.Core.Downloads;
using System;
using System.Collections.Generic;

namespace gamevault.UserControls
{
    /// <summary>A bold number between two muted words ("7,8 Go" "of 12,6 Go"), as the cards of the downloads tell them.</summary>
    internal sealed record CouchFact(string Before, string Bold, string After);

    /// <summary>A game folder offered for an installation: its disk filled, the game's room striped, what is left after it.</summary>
    internal sealed class CouchDiskLine : CouchLine
    {
        /// <summary>The bar's width on the screen.</summary>
        public const double BarWidth = 600;
        public bool IsLast { get; init; }
        public bool HasRoom { get; init; }
        /// <summary>Why it can not be chosen ("Not enough room"), else null.</summary>
        public string? Warning { get; init; }
        public bool HasWarning => Warning != null;
        public double UsedWidth { get; init; }
        public double GameWidth { get; init; }
        public IBrush UsedBrush => HasRoom ? new SolidColorBrush(Color.Parse("#8CF4F6FA")) : new SolidColorBrush(Color.Parse("#B3FF6B6B"));
        public IReadOnlyList<CouchFact> Legend { get; init; } = Array.Empty<CouchFact>();
        /// <summary>The room left once the game is in, large at the right; a dash when it can not go there.</summary>
        public string After { get; init; } = "—";
        public string AfterLabel { get; init; } = "";

        public static CouchDiskLine Of(InstallLocationRow row, long need, Action choose)
        {
            string Size(long bytes) => GameSizeConverter.Text(bytes);
            long? free = row.Free, total = row.Total;
            double used = 0, game = 0;
            if (free is long f && total is long t && t > 0)
            {
                used = Math.Clamp((t - f) / (double)t, 0, 1);
                game = row.HasRoom ? Math.Clamp(need / (double)t, 0, 1 - used) : 0;
                // The game is seen even on a large disk
                if (row.HasRoom && need > 0)
                    game = Math.Min(Math.Max(game, 0.01), 1 - used);
            }
            var legend = new List<CouchFact>();
            if (!row.Exists)
                legend.Add(new CouchFact("", "", Loc.T("Not found (disk unplugged?)")));
            else if (!row.HasRoom && free is long left)
                legend.Add(new CouchFact(Loc.T("Free"), Size(left), "· " + Loc.F("needs {0}", Size(need))));
            else if (free is long room && total is long whole)
            {
                legend.Add(new CouchFact(Loc.T("Used"), Size(whole - room), ""));
                if (need > 0)
                    legend.Add(new CouchFact(Loc.T("The game"), Size(need), ""));
                legend.Add(new CouchFact(Loc.T("Free"), Size(room), ""));
            }
            else if (row.Details.Length > 0)
                legend.Add(new CouchFact("", "", row.Details));
            return new CouchDiskLine
            {
                Text = row.Path,
                IsLast = row.IsLast,
                HasRoom = row.HasRoom,
                Opacity = row.HasRoom ? 1 : 0.42,
                Warning = !row.Exists ? Loc.T("Not found") : !row.HasRoom ? Loc.T("Not enough room") : null,
                UsedWidth = used * BarWidth,
                GameWidth = game * BarWidth,
                Legend = legend,
                After = row.HasRoom && free is long after ? Size(Math.Max(0, after - need)) : "—",
                AfterLabel = row.HasRoom && free != null ? Loc.T("free afterwards") : "",
                Run = () => { choose(); return System.Threading.Tasks.Task.CompletedTask; },
            };
        }
    }

    /// <summary>
    /// A download on its card: the game's cover, its state in color, its bar, what came down of how much, the speed,
    /// what is left and when it ends; written again from the download twice a second, on the same card.
    /// </summary>
    internal sealed class CouchDownloadLine : CouchLine
    {
        public object Download { get; }
        public Game? Pc { get; }
        public RetroGame? Retro { get; }
        public bool IsPc => Pc != null;
        public bool IsRetro => Retro != null;
        public string Platform { get; }

        public CouchDownloadLine(object download, string title)
        {
            Download = download;
            Text = title;
            Pc = (download as GameDownloadUserControl)?.ViewModel.Game;
            Retro = (download as RetroDownload)?.Game;
            Platform = Retro?.Platform.Name ?? "PC";
            Update();
        }

        public enum Kind { None, Downloading, Extracting, Paused, Queued, Failed, Done }

        public Kind State { get; private set; }
        private string stateText = "", percent = "", eta = "";
        private double progress;
        private IReadOnlyList<CouchFact> facts = Array.Empty<CouchFact>();
        private IReadOnlyList<CouchStat> buttons = Array.Empty<CouchStat>();
        public string StateText { get => stateText; private set => Set(ref stateText, value); }
        public double Progress { get => progress; private set => Set(ref progress, value); }
        public string Percent { get => percent; private set => Set(ref percent, value); }
        public string Eta { get => eta; private set => Set(ref eta, value); }
        public IReadOnlyList<CouchFact> Facts { get => facts; private set => Set(ref facts, value); }
        /// <summary>What A and X do on it, shown on the card while it is chosen.</summary>
        public IReadOnlyList<CouchStat> Buttons { get => buttons; set => Set(ref buttons, value); }
        /// <summary>Its speed now, counted in the total; 0 when it does not download.</summary>
        public double PerSecond { get; private set; }
        /// <summary>What is left to come down, counted in the total.</summary>
        public long BytesLeft { get; private set; }

        public IBrush StateBrush => new SolidColorBrush(Color.Parse(State switch
        {
            Kind.Downloading => "#A99BFF",
            Kind.Extracting => "#5EC8FF",
            Kind.Paused => "#F5B44A",
            Kind.Failed => "#FF8A8A",
            Kind.Done => "#3DDC97",
            _ => "#B3F4F6FA",
        }));
        public IBrush StateBack => new SolidColorBrush(Color.Parse(State switch
        {
            Kind.Downloading => "#2E7C6BFF",
            Kind.Extracting => "#265EC8FF",
            Kind.Paused => "#24F5B44A",
            Kind.Failed => "#29FF6B6B",
            Kind.Done => "#243DDC97",
            _ => "#14FFFFFF",
        }));
        public IBrush BarBrush => State switch
        {
            Kind.Extracting => Gradient("#3DA9FC", "#5EC8FF"),
            Kind.Paused => new SolidColorBrush(Color.Parse("#BFF5B44A")),
            Kind.Done => new SolidColorBrush(Color.Parse("#3DDC97")),
            Kind.Failed => new SolidColorBrush(Color.Parse("#B3FF6B6B")),
            _ => Gradient("#7C6BFF", "#A99BFF"),
        };
        public double PercentOpacity => State is Kind.Paused or Kind.Queued ? 0.6 : 1;
        public IBrush PercentBrush => new SolidColorBrush(Color.Parse(State == Kind.Done ? "#3DDC97" : "#F4F6FA"));

        private static IBrush Gradient(string from, string to) => new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(1, 0, Avalonia.RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse(from), 0), new GradientStop(Color.Parse(to), 1) },
        };

        private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;
            field = value;
            OnPropertyChanged(name);
        }

        private static string Size(double bytes) => GameSizeConverter.Text(bytes);

        /// <summary>"2 min 30", "55 s", "1 h 05".</summary>
        internal static string Duration(TimeSpan time) => DownloadText.Duration(time);

        /// <summary>Read again from the download.</summary>
        public void Update()
        {
            Kind kind;
            string state;
            int percentDone;
            DownloadBytes bytes;
            int? extraction = null;
            switch (Download)
            {
                case GameDownloadUserControl pc:
                    var vm = pc.ViewModel;
                    kind = vm.DownloadFailedVisibility ? Kind.Failed
                        : vm.ExtractionUIVisibility ? Kind.Extracting
                        : pc.IsPaused() ? Kind.Paused
                        : DownloadQueue.IsWaiting(pc) || vm.IsQueued ? Kind.Queued
                        : pc.IsDownloading() ? Kind.Downloading
                        : vm.DownloadUIVisibility ? Kind.None
                        : Kind.Done;
                    state = Loc.T(pc.StateText);
                    percentDone = pc.GetDownloadProgress();
                    bytes = pc.Bytes;
                    if (kind == Kind.Extracting)
                        extraction = vm.GameExtractionProgress;
                    if (kind == Kind.Done && bytes.Total <= 0 && long.TryParse(vm.Game?.Size, out long size))
                        bytes = new DownloadBytes(size, size, 0);
                    break;
                case RetroDownload retro:
                    kind = retro.IsFailed ? Kind.Failed
                        : retro.IsDone ? Kind.Done
                        : retro.IsPaused ? Kind.Paused
                        : retro.IsQueued ? Kind.Queued
                        : retro.IsActive ? Kind.Downloading
                        : retro.IsPreparing ? Kind.Extracting
                        : Kind.None;
                    state = retro.State;
                    percentDone = retro.Progress;
                    bytes = retro.Bytes;
                    if (kind == Kind.Done && bytes.Total <= 0 && retro.Game.Size > 0)
                        bytes = new DownloadBytes(retro.Game.Size, retro.Game.Size, 0);
                    break;
                default:
                    return;
            }
            bool changed = kind != State;
            State = kind;
            StateText = state;
            long left = bytes.Total > 0 ? Math.Max(0, bytes.Total - bytes.Done) : 0;
            PerSecond = kind == Kind.Downloading ? bytes.PerSecond : 0;
            BytesLeft = kind is Kind.Downloading or Kind.Paused or Kind.Queued ? left : 0;
            var facts = new List<CouchFact>();
            switch (kind)
            {
                case Kind.Downloading or Kind.Paused or Kind.Queued when bytes.Total > 0:
                    facts.Add(new CouchFact("", Size(bytes.Done), Loc.F("of {0}", Size(bytes.Total))));
                    if (PerSecond > 0)
                        facts.Add(new CouchFact(Loc.T("Speed"), $"{GameSizeConverter.Text(PerSecond, 1000)}/s", ""));
                    facts.Add(new CouchFact(Loc.T("Still to come"), Size(left), ""));
                    break;
                case Kind.Extracting:
                    if (bytes.Total > 0)
                        facts.Add(new CouchFact(Loc.T("Downloaded"), Size(bytes.Total), ""));
                    if (extraction is int ex)
                        facts.Add(new CouchFact(Loc.T("Extraction"), $"{ex} %", ""));
                    break;
                case Kind.Done when bytes.Total > 0:
                    facts.Add(new CouchFact(Loc.T("Size"), Size(bytes.Total), ""));
                    break;
            }
            Facts = facts;
            int shown = extraction ?? percentDone;
            Progress = kind == Kind.Done ? 100 : shown;
            Percent = kind == Kind.Done ? "✓" : kind is Kind.Queued or Kind.None && shown == 0 ? "" : $"{shown} %";
            Eta = kind == Kind.Downloading && TimeLeft.Of(left, PerSecond) is TimeSpan time ? Loc.F("{0} left", Duration(time)) : "";
            if (changed)
            {
                OnPropertyChanged(nameof(StateBrush));
                OnPropertyChanged(nameof(StateBack));
                OnPropertyChanged(nameof(BarBrush));
                OnPropertyChanged(nameof(PercentOpacity));
                OnPropertyChanged(nameof(PercentBrush));
            }
        }
    }
}
