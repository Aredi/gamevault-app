using Avalonia.Controls;
using Avalonia.Interactivity;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    public partial class HypervisorNoticePopup : UserControl
    {
        private readonly TaskCompletionSource closed = new();

        public HypervisorNoticePopup() : this("", "") { }

        internal HypervisorNoticePopup(string gameTitle, string text)
        {
            InitializeComponent();
            uiTitle.Text = Loc.F("Before installing {0}", gameTitle);
            uiText.Text = text;
            // Closed any other way: the download must not wait forever
            DetachedFromVisualTree += (_, _) => closed.TrySetResult();
        }

        /// <summary>Once per game on this profile, for the games an administrator marked; a service out of reach never holds a download back.</summary>
        internal static async Task ShowOnceAsync(Game game)
        {
            string config = LoginManager.Instance.GetUserProfile().UserConfigFile;
            var seen = (Preferences.Get(AppConfigKey.HypervisorNoticesSeen, config) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (seen.Contains(game.ID.ToString()))
                return;
            HypervisorNotice? notice = null;
            try { notice = await HypervisorService.GetAsync(); }
            catch (Exception ex) { Log.Ignored(ex); }
            if (notice?.Games.Contains(game.ID) != true || string.IsNullOrWhiteSpace(notice.Text))
                return;
            var popup = new HypervisorNoticePopup(game.Title, notice.Text);
            MainWindowViewModel.Instance.OpenPopup(popup);
            await popup.closed.Task;
            if (popup.uiDontShow.IsChecked == true)
            {
                seen.Add(game.ID.ToString());
                Preferences.Set(AppConfigKey.HypervisorNoticesSeen, string.Join(",", seen), config);
            }
        }

        private void Understood_Click(object? sender, RoutedEventArgs e)
        {
            MainWindowViewModel.Instance.ClosePopup();
            closed.TrySetResult();
        }
    }
}
