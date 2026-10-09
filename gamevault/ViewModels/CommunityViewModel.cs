using gamevault.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gamevault.ViewModels
{
    internal class CommunityViewModel : ViewModelBase
    {
        #region PrivateMembers

        private User[]? m_Users { get; set; }
        private User? m_CurrentShownUser { get; set; }
        private List<Progress> m_UserProgresses { get; set; }
        private bool loadingUser { get; set; }

        #endregion
        public User[]? Users
        {
            get { return m_Users; }
            set { m_Users = value; OnPropertyChanged(); }
        }
        public User? CurrentShownUser
        {
            get { return m_CurrentShownUser; }
            set
            {
                m_CurrentShownUser = value;
                UserProgresses = new List<Progress>(m_CurrentShownUser.Progresses ?? Array.Empty<Progress>());
                m_CurrentShownUser.Progresses = null;
                OnPropertyChanged();
            }
        }

        public List<Progress> UserProgresses
        {
            get
            {
                if (m_UserProgresses == null)
                {
                    m_UserProgresses = new List<Progress>();
                }
                return m_UserProgresses;
            }
            set
            {
                m_UserProgresses = value; OnPropertyChanged();
                Stats = GameVault.Core.Library.ProfileStats.Compute(m_UserProgresses.Select(p => (p.MinutesPlayed ?? 0, p.LastPlayedAt, p.State)), DateTime.UtcNow);
                RecentProgresses = m_UserProgresses.Where(p => p.LastPlayedAt != null && p.Game != null).OrderByDescending(p => p.LastPlayedAt).Take(10).ToList();
            }
        }

        private GameVault.Core.Library.ProfileStats stats = new(0, 0, 0, 0);
        /// <summary>Play time, games played, completed, played this week.</summary>
        public GameVault.Core.Library.ProfileStats Stats
        {
            get => stats;
            private set { stats = value; OnPropertyChanged(); }
        }
        private List<Progress> recentProgresses = new();
        /// <summary>The games played last, for the row of covers.</summary>
        public List<Progress> RecentProgresses
        {
            get => recentProgresses;
            private set { recentProgresses = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRecent)); }
        }
        public bool HasRecent => RecentProgresses.Count > 0;
        #region Profile
        private ObservableCollection<ProfileModuleView> modules = new();
        /// <summary>The sections of the profile shown, in their order.</summary>
        public ObservableCollection<ProfileModuleView> Modules
        {
            get => modules;
            set { modules = value; OnPropertyChanged(); }
        }

        private string? tagline;
        public string? Tagline
        {
            get => tagline;
            set { tagline = value; OnPropertyChanged(); }
        }

        private Avalonia.Media.Color? accentColor;
        /// <summary>The color the player chose (null: the theme's).</summary>
        public Avalonia.Media.Color? AccentColor
        {
            get => accentColor;
            set { accentColor = value; OnPropertyChanged(); }
        }

        private bool canCustomize, isEditing, serviceAvailable = true;
        /// <summary>This player (or an administrator) may change this profile.</summary>
        public bool CanCustomize
        {
            get => canCustomize;
            set { canCustomize = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowCustomize)); OnPropertyChanged(nameof(ShowServiceSetup)); }
        }
        public bool IsEditing
        {
            get => isEditing;
            set { isEditing = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowCustomize)); }
        }
        public bool ServiceAvailable
        {
            get => serviceAvailable;
            set { serviceAvailable = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowCustomize)); OnPropertyChanged(nameof(ShowServiceSetup)); }
        }
        private string? serviceProblem;
        public string? ServiceProblem
        {
            get => serviceProblem;
            set { serviceProblem = value; OnPropertyChanged(); }
        }
        public bool ShowCustomize => CanCustomize && ServiceAvailable && !IsEditing;
        /// <summary>Own profile without the SanctuaryVault service: where to enter its address.</summary>
        public bool ShowServiceSetup => CanCustomize && !ServiceAvailable;

        private string? draftTagline;
        public string? DraftTagline
        {
            get => draftTagline;
            set { draftTagline = value; OnPropertyChanged(); }
        }

        public List<AccentChoice> AccentChoices { get; } = AccentChoice.All();
        private AccentChoice? draftAccentChoice;
        public AccentChoice? DraftAccentChoice
        {
            get => draftAccentChoice;
            set
            {
                draftAccentChoice = value;
                OnPropertyChanged();
                if (IsEditing && value != null)
                    AccentColor = value.Color;
            }
        }

        private List<ProfileModuleChoice> addChoices = new();
        public List<ProfileModuleChoice> AddChoices
        {
            get => addChoices;
            set { addChoices = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanAddSection)); }
        }
        public bool CanAddSection => AddChoices.Count > 0;
        #endregion

        public bool LoadingUser
        {
            get { return loadingUser; }
            set { loadingUser = value; OnPropertyChanged(); }
        }
        public string[] SortBy
        {
            get
            {
                return new string[] { "State", "Time played", "Last played" };
            }
        }

    }

    /// <summary>A color a player can give their profile.</summary>
    internal sealed record AccentChoice(string? Hex, string Name)
    {
        public Avalonia.Media.Color? Color => Hex == null ? null : Avalonia.Media.Color.Parse(Hex);
        public Avalonia.Media.IBrush Brush => new Avalonia.Media.SolidColorBrush(Color ?? gamevault.Helper.CoverColors.ThemeAccent);

        private static string L(string english) => gamevault.Localization.Loc.T(english);

        public static List<AccentChoice> All() => new()
        {
            new(null, gamevault.Localization.Loc.T("Theme color")),
            new("#7C5CFF", L("Violet")), new("#3B82F6", L("Blue")), new("#06B6D4", L("Cyan")), new("#10B981", L("Green")), new("#84CC16", L("Lime")),
            new("#F59E0B", L("Amber")), new("#F97316", L("Orange")), new("#EF4444", L("Red")), new("#EC4899", L("Pink")), new("#94A3B8", L("Slate")),
        };
    }
}
