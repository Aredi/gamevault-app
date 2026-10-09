using Avalonia.Media;
using GameVault.Core.Library;
using gamevault.Converter;
using gamevault.Localization;
using gamevault.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace gamevault.ViewModels
{
    /// <summary>A section of a profile as the page shows it (and lets its owner change it).</summary>
    internal abstract class ProfileModuleView : ViewModelBase
    {
        protected ProfileModuleView(ProfileModule module) => Module = module;

        public ProfileModule Module { get; }
        public string Type => Module.Type;

        public static string DefaultTitle(string type) => type switch
        {
            ProfileModuleType.Showcase => Loc.T("Game showcase"),
            ProfileModuleType.Favorite => Loc.T("Favorite game"),
            ProfileModuleType.Text => Loc.T("About me"),
            ProfileModuleType.Badges => Loc.T("Badges"),
            ProfileModuleType.Stats => Loc.T("Statistics"),
            ProfileModuleType.Recent => Loc.T("Played recently"),
            ProfileModuleType.MostPlayed => Loc.T("Most played"),
            ProfileModuleType.Completed => Loc.T("Completed games"),
            _ => type,
        };

        public static string Description(string type) => type switch
        {
            ProfileModuleType.Showcase => Loc.T("Up to 8 games of your choice"),
            ProfileModuleType.Favorite => Loc.T("One game, shown large with your time on it"),
            ProfileModuleType.Text => Loc.T("A text of your own"),
            ProfileModuleType.Badges => Loc.T("Earned by playing on the server"),
            ProfileModuleType.Stats => Loc.T("Play time, games played and completed"),
            ProfileModuleType.Recent => Loc.T("The games played last"),
            ProfileModuleType.MostPlayed => Loc.T("The games with the most play time"),
            ProfileModuleType.Completed => Loc.T("The games marked as completed"),
            _ => "",
        };

        public string Title
        {
            get => Module.Title ?? DefaultTitle(Type);
            set { Module.Title = string.IsNullOrWhiteSpace(value) || value == DefaultTitle(Type) ? null : value; OnPropertyChanged(); }
        }

        /// <summary>Showcases and texts take a title of the player's own.</summary>
        public bool CanRename => Type is ProfileModuleType.Showcase or ProfileModuleType.Text;

        private bool isEditing;
        public bool IsEditing
        {
            get => isEditing;
            set { isEditing = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowTitle)); OnPropertyChanged(nameof(ShowRenameBox)); EditingChanged(); }
        }
        protected virtual void EditingChanged() { }

        /// <summary>The statistics strip has no title, except while the profile is changed.</summary>
        public virtual bool ShowTitle => true;
        public bool ShowRenameBox => IsEditing && CanRename;

        private bool canMoveUp, canMoveDown;
        public bool CanMoveUp { get => canMoveUp; set { canMoveUp = value; OnPropertyChanged(); } }
        public bool CanMoveDown { get => canMoveDown; set { canMoveDown = value; OnPropertyChanged(); } }

        /// <summary>Nothing to show (no game played yet...): hidden for visitors, explained to the owner.</summary>
        public virtual bool IsEmpty => false;
        public bool IsVisibleOnPage => !IsEmpty || IsEditing;
        public virtual string EmptyText => "";
        protected void Refresh()
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsVisibleOnPage));
        }
    }

    /// <summary>A game in a section: its cover, what the player did with it.</summary>
    internal sealed class ProfileGameView
    {
        public ProfileGameView(Game game, Progress? progress, string caption)
        {
            Game = game;
            Progress = progress;
            Caption = caption;
        }

        public Game Game { get; }
        public Progress? Progress { get; }
        public string Caption { get; }
        public string Title => Game.Metadata?.Title ?? Game.Title ?? "";
    }

    internal sealed class StatsModuleView : ProfileModuleView
    {
        public StatsModuleView(ProfileModule module, ProfileStats stats) : base(module) => Stats = stats;
        public ProfileStats Stats { get; }
        public override bool ShowTitle => IsEditing;
    }

    internal sealed class GamesModuleView : ProfileModuleView
    {
        public GamesModuleView(ProfileModule module, IEnumerable<ProfileGameView> games) : base(module)
        {
            Games = new ObservableCollection<ProfileGameView>(games);
        }

        public ObservableCollection<ProfileGameView> Games { get; }
        public bool ChoosesGames => ProfileModuleType.ChoosesGames(Type);
        public bool CanAddGame => IsEditing && ChoosesGames && Games.Count < ProfileModuleType.MaxGames(Type);
        public bool CanRemoveGames => IsEditing && ChoosesGames;
        public override bool IsEmpty => Games.Count == 0;
        public override string EmptyText => Type switch
        {
            ProfileModuleType.Showcase => Loc.T("Choose the games to show here."),
            ProfileModuleType.Completed => Loc.T("No game marked as completed yet."),
            _ => Loc.T("No game played yet."),
        };
        protected override void EditingChanged()
        {
            OnPropertyChanged(nameof(CanAddGame));
            OnPropertyChanged(nameof(CanRemoveGames));
            Refresh();
        }
    }

    internal sealed class FavoriteModuleView : ProfileModuleView
    {
        public FavoriteModuleView(ProfileModule module, ProfileGameView? game, string details) : base(module)
        {
            Game = game;
            Details = details;
        }

        public ProfileGameView? Game { get; }
        public bool HasGame => Game != null;
        /// <summary>"34 h · last session yesterday · Playing".</summary>
        public string Details { get; }
        public bool CanChoose => IsEditing;
        public bool ShowChooseTile => IsEditing && Game == null;
        public override bool IsEmpty => Game == null;
        public override string EmptyText => Loc.T("Choose your favorite game.");
        protected override void EditingChanged()
        {
            OnPropertyChanged(nameof(CanChoose));
            OnPropertyChanged(nameof(ShowChooseTile));
            Refresh();
        }
    }

    internal sealed class TextModuleView : ProfileModuleView
    {
        public TextModuleView(ProfileModule module) : base(module) { }

        public string? Text
        {
            get => Module.Text;
            set { Module.Text = value; OnPropertyChanged(); Refresh(); }
        }
        public override bool IsEmpty => string.IsNullOrWhiteSpace(Module.Text);
        public bool ShowText => !IsEditing && !IsEmpty;
        public override string EmptyText => Loc.T("Write something about you.");
        protected override void EditingChanged()
        {
            OnPropertyChanged(nameof(ShowText));
            Refresh();
        }
    }

    internal sealed class BadgeView
    {
        public BadgeView(ProfileBadge badge)
        {
            Badge = badge;
            int steps = ProfileBadges.Steps[badge.Key].Length;
            (Name, Description, Icon) = badge.Key switch
            {
                "hours" => (Loc.T("Dedicated player"), Loc.F("{0} h played", badge.Value), "IconGameViewCalendar"),
                "played" => (Loc.T("Explorer"), Loc.F(badge.Value == 1 ? "{0} game played" : "{0} games played", badge.Value), "IconNavLibrary"),
                "completed" => (Loc.T("Finisher"), Loc.F(badge.Value == 1 ? "{0} game completed" : "{0} games completed", badge.Value), "IconTick"),
                "member" => (Loc.T("Veteran"), Loc.F("Member for {0} days", badge.Value), "IconUser"),
                "week" => (Loc.T("On a roll"), Loc.F(badge.Value == 1 ? "{0} game this week" : "{0} games this week", badge.Value), "IconPlay"),
                "marathon" => (Loc.T("Marathoner"), Loc.F("{0} h on a single game", badge.Value), "IconGameViewRating"),
                _ => (badge.Key, "", "IconInfo"),
            };
            LevelText = Loc.F("Level {0}", badge.Level);
            NextText = badge.NextGoal > 0 ? Loc.F("Next level at {0}", badge.NextGoal) : Loc.T("Highest level");
            Progress = (double)badge.Level / steps;
            IsMaxed = badge.NextGoal == 0;
        }

        public ProfileBadge Badge { get; }
        public string Name { get; }
        public string Description { get; }
        public string Icon { get; }
        public string LevelText { get; }
        public string NextText { get; }
        public double Progress { get; }
        public bool IsMaxed { get; }
        public Geometry? IconGeometry => Avalonia.Application.Current!.TryGetResource(Icon, null, out object? geometry) ? geometry as Geometry : null;
    }

    internal sealed class BadgesModuleView : ProfileModuleView
    {
        public BadgesModuleView(ProfileModule module, IEnumerable<ProfileBadge> badges) : base(module)
        {
            Badges = badges.Select(b => new BadgeView(b)).ToList();
        }

        public List<BadgeView> Badges { get; }
        public override bool IsEmpty => Badges.Count == 0;
        public override string EmptyText => Loc.T("Badges come with play time, games played and completed.");
        protected override void EditingChanged() => Refresh();
    }

    /// <summary>A section the owner can add.</summary>
    internal sealed record ProfileModuleChoice(string Type, string Title, string Description);

    internal static class ProfileModuleFactory
    {
        /// <summary>The sections of a profile, from what the player chose and what they played.</summary>
        public static List<ProfileModuleView> Build(ProfileDocument profile, IReadOnlyList<Progress> progresses, IReadOnlyDictionary<int, Game> games, User? user, DateTime nowUtc)
        {
            ProfileStats stats = ProfileStats.Compute(progresses.Select(p => (p.MinutesPlayed ?? 0, p.LastPlayedAt, p.State)), nowUtc);
            var byGame = progresses.Where(p => p.Game != null).GroupBy(p => p.Game!.ID).ToDictionary(g => g.Key, g => g.First());
            string TimeOf(Progress? progress) => progress?.MinutesPlayed > 0
                ? (string)new GameTimeConverter().Convert(progress.MinutesPlayed, typeof(string), null, CultureInfo.CurrentCulture)!
                : Loc.T("Not played yet");
            ProfileGameView? Chosen(int id)
            {
                Game? game = byGame.TryGetValue(id, out Progress? progress) ? progress.Game : games.GetValueOrDefault(id);
                return game == null ? null : new ProfileGameView(game, progress, TimeOf(progress));
            }

            var views = new List<ProfileModuleView>();
            foreach (ProfileModule module in profile.Modules)
            {
                ProfileModuleView view = module.Type switch
                {
                    ProfileModuleType.Stats => new StatsModuleView(module, stats),
                    ProfileModuleType.Recent => new GamesModuleView(module, progresses.Where(p => p.LastPlayedAt != null && p.Game != null)
                        .OrderByDescending(p => p.LastPlayedAt).Take(10)
                        .Select(p => new ProfileGameView(p.Game!, p, RelativeDate.Format(p.LastPlayedAt!.Value)))),
                    ProfileModuleType.MostPlayed => new GamesModuleView(module, progresses.Where(p => p.MinutesPlayed > 0 && p.Game != null)
                        .OrderByDescending(p => p.MinutesPlayed).Take(10)
                        .Select(p => new ProfileGameView(p.Game!, p, TimeOf(p)))),
                    ProfileModuleType.Completed => new GamesModuleView(module, progresses.Where(p => p.Game != null && string.Equals(p.State, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(p => p.LastPlayedAt)
                        .Select(p => new ProfileGameView(p.Game!, p, TimeOf(p)))),
                    ProfileModuleType.Showcase => new GamesModuleView(module, module.Games.Select(Chosen).OfType<ProfileGameView>()),
                    ProfileModuleType.Favorite => Favorite(module, module.Games.Select(Chosen).OfType<ProfileGameView>().FirstOrDefault()),
                    ProfileModuleType.Text => new TextModuleView(module),
                    ProfileModuleType.Badges => new BadgesModuleView(module, ProfileBadges.Compute(stats, user?.CreatedAt,
                        progresses.Select(p => p.MinutesPlayed ?? 0).DefaultIfEmpty(0).Max(), nowUtc)),
                    _ => new TextModuleView(module),
                };
                views.Add(view);
            }
            for (int i = 0; i < views.Count; i++)
            {
                views[i].CanMoveUp = i > 0;
                views[i].CanMoveDown = i < views.Count - 1;
            }
            return views;
        }

        private static FavoriteModuleView Favorite(ProfileModule module, ProfileGameView? game)
        {
            var parts = new List<string>();
            if (game != null)
            {
                parts.Add(game.Progress?.MinutesPlayed > 0 ? Loc.F("{0} played", game.Caption) : game.Caption);
                if (game.Progress?.LastPlayedAt is DateTime last)
                    parts.Add(Loc.F("last session {0}", RelativeDate.Format(last)));
                if (!string.IsNullOrEmpty(game.Progress?.State) && game.Progress.State != "UNPLAYED")
                    parts.Add((string)new GameStateDescriptionConverter().Convert(game.Progress.State, typeof(string), null, CultureInfo.CurrentCulture)!);
            }
            return new FavoriteModuleView(module, game, string.Join(" · ", parts));
        }

        /// <summary>The sections that can still be added to this profile.</summary>
        public static List<ProfileModuleChoice> Choices(ProfileDocument profile) => ProfileModuleType.All
            .Where(type => profile.Modules.Count(m => m.Type == type) < ProfileModuleType.MaxCount(type))
            .Select(type => new ProfileModuleChoice(type, ProfileModuleView.DefaultTitle(type), ProfileModuleView.Description(type)))
            .ToList();
    }
}
