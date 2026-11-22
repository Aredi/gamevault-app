using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GameVault.Core;
using GameVault.Core.Couch;
using GameVault.Core.Retro;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>
    /// What Aniki ReMake has besides its look: a game's facts on its screen, the roulette of a game at random, the
    /// sounds of what happens, the player's level, the achievements that ring, the bar of the tabs that hides, the
    /// player's own backgrounds, and the settings back to the usual ones.
    /// </summary>
    public partial class CouchUserControl
    {
        // --- A game's facts ---

        /// <summary>Its rating, its age, how long it lasts, its players, early access: as pills on its screen.</summary>
        internal static List<string> FactsOf(CouchItem item)
        {
            var facts = new List<string>();
            double? rating = item.Retro?.Rating ?? (double?)item.Pc?.Metadata?.Rating;
            if (rating is double score && score > 0)
            {
                // Out of 100 (IGDB, RomM); a provider out of 10 is brought to it
                double hundred = score <= 10 ? score * 10 : score;
                int stars = (int)Math.Round(hundred / 20);
                facts.Add($"{new string('★', stars)}{new string('☆', 5 - stars)}  {hundred:0}");
            }
            string? age = item.Retro?.AgeRatings.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a))
                ?? (item.Pc?.Metadata?.AgeRating is int years && years > 0 ? $"PEGI {years}" : null);
            if (age != null)
                facts.Add(age);
            long? minutes = item.Retro?.MainStory is long seconds ? seconds / 60 : item.Pc?.Metadata?.AveragePlaytime;
            if (minutes is long length && length > 0)
                facts.Add(Loc.F("About {0} h to finish", Math.Max(1, Math.Round(length / 60.0))));
            if (!string.IsNullOrWhiteSpace(item.Retro?.Players))
                facts.Add(Loc.F("{0} players", item.Retro!.Players!));
            if (item.Pc?.Metadata?.EarlyAccess == true)
                facts.Add(Loc.T("Early access"));
            return facts;
        }

        /// <summary>Who made it: "Nintendo · 1986 · Action".</summary>
        internal static string CreditsOf(CouchItem item, string meta)
        {
            string? maker = item.Retro?.Companies.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))
                ?? item.Pc?.Metadata?.Developers?.Select(d => d.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                ?? item.Pc?.Metadata?.Publishers?.Select(p => p.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            return string.Join(" · ", new[] { maker, meta }.Where(p => !string.IsNullOrEmpty(p)));
        }

        /// <summary>Its genres then its tags, four at most: "Action • Adventure • RPG".</summary>
        internal static string GenresOf(CouchItem item)
        {
            IEnumerable<string?> names = item.Retro != null
                ? item.Retro.Genres
                : (item.Pc?.Metadata?.Genres?.Select(g => g.Name) ?? Enumerable.Empty<string?>()).Concat(item.Pc?.Metadata?.Tags?.Select(t => t.Name) ?? Enumerable.Empty<string?>());
            return string.Join("  •  ", names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).Take(4));
        }

        private void ShowFacts(CouchItem item)
        {
            List<string> facts = FactsOf(item);
            uiDetailFacts.ItemsSource = facts;
            uiDetailFacts.IsVisible = facts.Count > 0;
            uiDetailSub.Text = CreditsOf(item, MetaOf(item));
            uiDetailGenres.Text = GenresOf(item);
            uiDetailGenres.IsVisible = Settings.Layout != "showcase";
        }

        // --- The roulette of a game at random ---

        private bool spinning;

        /// <summary>
        /// "What to play?": the strip runs through the games, slower and slower, and stops on one (a fanfare), then its
        /// screen. Nothing else answers meanwhile; B stops it where it is.
        /// </summary>
        private async Task SpinAsync(int tab, int target)
        {
            spinning = true;
            try
            {
                int count = Tabs[tab].Items.Count;
                // At least two turns' worth of steps (or 24), ending on the game drawn
                int steps = Math.Max(24, count) + 1;
                int start = (target - steps + count * 100) % count;
                for (int i = 1; i <= steps && spinning; i++)
                {
                    CouchItem? before = Current;
                    Navigator.Select(tab, (start + i) % count);
                    ShowSelection(before);
                    Play(CouchSound.Move);
                    // Fast, then slowing down at the end
                    double t = (double)i / steps;
                    await Task.Delay(TimeSpan.FromMilliseconds(35 + 320 * t * t * t));
                }
                if (!spinning)
                    return;
                Play(CouchSound.Lucky);
                await Task.Delay(450);
                Navigator.Apply(CouchCommand.Accept);
                ShowScreen();
            }
            finally { spinning = false; }
        }

        // --- The sounds of what happens ---

        /// <summary>A game of the list is now installed here, or removed: its sound.</summary>
        private void InstalledChanged(bool installed) => Play(installed ? CouchSound.Installed : CouchSound.Uninstalled);

        // --- The player's level ---

        internal CouchLevel? Level { get; private set; }

        /// <summary>The level, from the player's retro achievements and time (the service) and their time on the library's games.</summary>
        internal async Task LoadLevelAsync()
        {
            try
            {
                int achievements = 0, mastered = 0, minutes = all.Where(i => i.Pc != null).Sum(MinutesOf);
                if (LoginManager.Instance.GetCurrentUser() is { } user)
                {
                    RetroLibrary.Player player = await RetroLibrary.PlayerOfAsync(user.ID);
                    achievements = player.Achievements.Values.Sum(g => g.Earned);
                    mastered = player.Achievements.Values.Count(g => g.Total > 0 && g.Earned == g.Total);
                    minutes += player.Plays.Sum(p => p.Minutes);
                }
                ShowLevel(CouchLevel.Of(achievements, mastered, minutes));
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        internal static string TierName(CouchLevel level) => Loc.T(level.Tier switch
        {
            CouchLevel.Silver => "Silver",
            CouchLevel.Gold => "Gold",
            CouchLevel.Platinum => "Platinum",
            _ => "Bronze",
        }) + " " + new string('I', level.Step);

        internal void ShowLevel(CouchLevel level)
        {
            Level = level;
            uiLevel.IsVisible = true;
            uiLevelText.Text = Loc.F("Level {0} · {1}", level.Level, TierName(level));
            uiLevelMedal.Fill = new SolidColorBrush(Color.Parse(level.Tier switch
            {
                CouchLevel.Silver => "#C9D3DD",
                CouchLevel.Gold => "#F2C14E",
                CouchLevel.Platinum => "#9FE7F5",
                _ => "#C98A55",
            }));
            uiLevelBar.Width = Math.Round(70 * Math.Clamp(level.Progress, 0, 1));
            foreach (LoginProfile profile in LoginProfiles.Where(p => p.IsCurrent))
                profile.LevelText = uiLevelText.Text;
        }

        // --- The achievements that ring ---

        private readonly Queue<RommProgress.Achievement> trophies = new();
        private HashSet<string>? earnedBefore;
        private bool showingTrophy;

        /// <summary>A retro game starts: what it had earned, to tell what it earns.</summary>
        private async Task RememberAchievementsAsync(RetroGame game)
        {
            earnedBefore = null;
            if (!RommAccount.IsLinked)
                return;
            try { earnedBefore = (await AchievementsNowAsync(game)).Where(a => a.Earned).Select(a => a.Id).ToHashSet(); }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private static async Task<IReadOnlyList<RommProgress.Achievement>> AchievementsNowAsync(RetroGame game)
        {
            JsonElement rom = await RommAccount.RomAsync(game.Id);
            JsonElement me = default;
            try { me = await RommAccount.MeAsync(); }
            catch (Exception ex) { Log.Ignored(ex); }
            return RommProgress.AchievementsOf(rom, me);
        }

        /// <summary>The game's achievements came back from RetroAchievements: the new ones ring, one after the other.</summary>
        private async Task TellNewAchievementsAsync(RetroGame game)
        {
            if (earnedBefore is not HashSet<string> before)
                return;
            earnedBefore = null;
            try
            {
                foreach (var earned in (await AchievementsNowAsync(game)).Where(a => a.Earned && !before.Contains(a.Id)).OrderBy(a => a.EarnedAt ?? DateTime.MaxValue))
                    ShowTrophy(earned);
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>How rare an achievement is, by its points: 25 and more ultra-rare, 10 and more rare.</summary>
        internal static CouchSound TrophySound(int points) => points >= 25 ? CouchSound.TrophyUltra : points >= 10 ? CouchSound.TrophyRare : CouchSound.Trophy;

        /// <summary>An achievement just earned, at the top right for five seconds (the next ones after it).</summary>
        internal void ShowTrophy(RommProgress.Achievement achievement)
        {
            trophies.Enqueue(achievement);
            if (!showingTrophy)
                NextTrophy();
        }

        internal bool TrophyShown => uiTrophy.IsVisible;

        private void NextTrophy()
        {
            if (!trophies.TryDequeue(out var achievement))
            {
                showingTrophy = false;
                uiTrophy.IsVisible = false;
                return;
            }
            showingTrophy = true;
            CouchSound sound = TrophySound(achievement.Points);
            uiTrophyKind.Text = (sound switch
            {
                CouchSound.TrophyUltra => Loc.T("Ultra-rare achievement"),
                CouchSound.TrophyRare => Loc.T("Rare achievement"),
                _ => Loc.T("Achievement earned"),
            }).ToUpper(CultureInfo.CurrentCulture);
            uiTrophyKind.Foreground = new SolidColorBrush(Color.Parse(sound switch { CouchSound.TrophyUltra => "#9FE7F5", CouchSound.TrophyRare => "#F2C14E", _ => "#C9D3DD" }));
            uiTrophyTitle.Text = achievement.Title;
            uiTrophyText.Text = $"{achievement.Description} · {Loc.F("{0} pts", achievement.Points)}";
            uiTrophyBadge.Source = null;
            if (achievement.Badge is string url)
                _ = RetroService.LoadBadgesAsync(new[] { (url, (Action<Bitmap>)(badge => uiTrophyBadge.Source = badge)) });
            uiTrophy.IsVisible = true;
            Enter(uiTrophy, 60, 0);
            Play(sound);
            DispatcherTimer.RunOnce(NextTrophy, TimeSpan.FromSeconds(5));
        }

        // --- The bar of the tabs that hides ---

        private int topBarGeneration;

        /// <summary>Hidden, the bar shows a moment when the tab changes (or always, as the customization chose).</summary>
        private void RevealTopBar()
        {
            if (Settings.TopBar != "auto")
            {
                uiTopBar.Opacity = 1;
                return;
            }
            uiTopBar.Opacity = 1;
            int generation = ++topBarGeneration;
            DispatcherTimer.RunOnce(() =>
            {
                if (generation == topBarGeneration && Settings.TopBar == "auto")
                    uiTopBar.Opacity = 0;
            }, TimeSpan.FromSeconds(2.5));
        }

        // --- The player's own backgrounds ---

        private static readonly string[] Pictures = { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };

        /// <summary>A picture drawn among the folder's (another each time the mode opens); none: nothing.</summary>
        internal static string? PictureOf(string folder, Random? random = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    return null;
                var files = Directory.EnumerateFiles(folder).Where(f => Pictures.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f, StringComparer.Ordinal).ToList();
                return files.Count == 0 ? null : files[(random ?? Random.Shared).Next(files.Count)];
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return null;
            }
        }

        private Control? FolderPicture()
        {
            if (PictureOf(Settings.BackgroundFolder) is not string file)
                return null;
            try
            {
                using FileStream stream = File.OpenRead(file);
                return new Image { Source = Bitmap.DecodeToWidth(stream, 1920), Stretch = Stretch.UniformToFill };
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return null;
            }
        }

        private async Task ChooseFolderAsync()
        {
            if (TopLevel.GetTopLevel(this) is not TopLevel top)
                return;
            var chosen = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Loc.T("A folder of your pictures"), AllowMultiple = false });
            if (chosen.FirstOrDefault()?.TryGetLocalPath() is string path)
                UseFolder(path);
        }

        internal void UseFolder(string path)
        {
            Settings.BackgroundFolder = path;
            Settings.Background = "folder";
            shownPack = "";
            SaveSettings();
            ApplyLook();
            BuildMenu();
            ShowScreen(false);
        }

        // --- Back to the usual settings ---

        private static string BackupFile => Path.Combine(Path.GetDirectoryName(ProfileManager.ProfileConfigFile) ?? "", "couch.before-reset.json");

        internal bool HasBackup => File.Exists(BackupFile);

        /// <summary>Every setting back to the usual one (the applications kept); the ones before kept aside to come back to.</summary>
        internal void ResetSettings()
        {
            Settings.Save(BackupFile);
            Settings = Settings.Reset();
            shownPack = "";
            SaveSettings();
            ApplyLook();
            Reshow();
        }

        internal void RestoreSettings()
        {
            if (!HasBackup)
                return;
            Settings = CouchSettings.Load(BackupFile);
            File.Delete(BackupFile);
            shownPack = "";
            SaveSettings();
            ApplyLook();
            Reshow();
        }
    }
}
