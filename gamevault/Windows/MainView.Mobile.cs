using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using gamevault.Helper;
using gamevault.Helper.Platform;
using gamevault.UserControls;
using gamevault.UserControls.Mobile;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace gamevault.Windows
{
    /// <summary>The phone's tabs, at the bottom of its screen.</summary>
    public enum MobileTab { Home, Games, Social, Challenges, Profile }

    /// <summary>
    /// Android: the computer's menu on the left gives way to a phone's tabs (a rail on the left when unfolded), and pages
    /// of their own for the home, the games and the profile. The other pages (a game's, the challenges, the settings...)
    /// are the computer's, laid out for a narrow screen; Back goes from them to the tab they were opened from.
    /// </summary>
    public partial class MainView
    {
        private MobileHomeUserControl? mobileHome;
        private MobileLibraryUserControl? mobileGames;
        private MobileProfileUserControl? mobileProfile;
        private MobileTab mobileTab;
        private bool rail;

        /// <summary>The phone's layout is on (Android).</summary>
        internal bool IsMobile { get; private set; }

        private void InitMobile()
        {
            if (!PlatformInfo.IsAndroid)
                return;
            IsMobile = true;
            uiSidebar.IsVisible = false;
            uiMobileNav.IsVisible = true;
            SizeChanged += (_, e) => PlaceMobileNav(e.NewSize.Width);
            MainWindowViewModel.Instance.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(MainWindowViewModel.IsChatOpen) or nameof(MainWindowViewModel.ActiveControl))
                    MarkMobileTab();
            };
        }

        /// <summary>At the bottom of a phone; a rail on the left of a wide screen (a foldable open, a tablet).</summary>
        private void PlaceMobileNav(double width)
        {
            if (!IsMobile || width <= 0)
                return;
            bool wide = width >= 760;
            if (wide == rail && uiMobileNav.Tag != null)
                return;
            rail = wide;
            uiMobileNav.Tag = true;
            Grid.SetRow(uiMobileNav, wide ? 0 : 1);
            Grid.SetRowSpan(uiMobileNav, wide ? 2 : 1);
            Grid.SetColumn(uiMobileNav, 0);
            Grid.SetColumnSpan(uiMobileNav, wide ? 1 : 3);
            uiMobileNav.Width = wide ? 88 : double.NaN;
            uiMobileNav.BorderThickness = wide ? new Thickness(0, 0, 1, 0) : new Thickness(0, 1, 0, 0);
            uiMobileTabs.Rows = wide ? 0 : 1;
            uiMobileTabs.Columns = wide ? 1 : 0;
            uiMobileTabs.VerticalAlignment = wide ? Avalonia.Layout.VerticalAlignment.Top : Avalonia.Layout.VerticalAlignment.Stretch;
            uiMobileTabs.Height = wide ? 5 * 76 : double.NaN;
            uiMobileTabs.Margin = wide ? new Thickness(0, 16, 0, 0) : new Thickness(4, 6, 4, 8);
            // The messages above the bar
            uiToast.Margin = new Thickness(0, 0, 24, wide ? 24 : 96);
        }

        private void MobileTab_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.Tag is string name && Enum.TryParse(name, out MobileTab tab))
                ShowMobileTab(tab);
        }

        /// <summary>A tab: its page, and the pages opened from it come back to it.</summary>
        internal void ShowMobileTab(MobileTab tab)
        {
            var vm = MainWindowViewModel.Instance;
            MainWindowViewModel.Instance.ClosePopup();
            if (tab == MobileTab.Social)
            {
                // The chat on the whole page, over the tab that was there
                vm.IsChatOpen = true;
                _ = ChatService.Instance.RefreshAsync();
                mobileTab = tab;
                MarkMobileTab();
                return;
            }
            vm.IsChatOpen = false;
            mobileTab = tab;
            UserControl page = tab switch
            {
                MobileTab.Home => mobileHome ??= new MobileHomeUserControl(),
                MobileTab.Games => mobileGames ??= new MobileLibraryUserControl(),
                MobileTab.Challenges => vm.Challenges,
                _ => mobileProfile ??= new MobileProfileUserControl(),
            };
            vm.MobileReturn = page;
            if (tab == MobileTab.Challenges)
                vm.SetActiveControl(MainControl.Challenges);
            else
                vm.SetActiveControl(page);
            MarkMobileTab();
        }

        /// <summary>The tab of the page shown lit: its own, or the one it was opened from.</summary>
        private void MarkMobileTab()
        {
            if (!IsMobile)
                return;
            MobileTab lit = MainWindowViewModel.Instance.IsChatOpen ? MobileTab.Social : mobileTab == MobileTab.Social ? TabOf(MainWindowViewModel.Instance.MobileReturn) : mobileTab;
            foreach (var (button, tab) in new[] { (uiTabHome, MobileTab.Home), (uiTabGames, MobileTab.Games), (uiTabSocial, MobileTab.Social), (uiTabChallenges, MobileTab.Challenges), (uiTabProfile, MobileTab.Profile) })
                button.Classes.Set("on", tab == lit);
        }

        private MobileTab TabOf(UserControl? page) =>
            page == mobileGames ? MobileTab.Games : page == MainWindowViewModel.Instance.Challenges ? MobileTab.Challenges : page == mobileProfile ? MobileTab.Profile : MobileTab.Home;

        /// <summary>Android's back on a tab's own page: another tab goes home; home leaves it to Android (the app to the background).</summary>
        private bool MobileBack()
        {
            if (mobileTab == MobileTab.Home)
                return false;
            ShowMobileTab(MobileTab.Home);
            return true;
        }

        /// <summary>The friends of the chat, who plays and who is online first.</summary>
        internal IReadOnlyList<ChatEntry> ChatFriends => uiChat.Friends;

        /// <summary>The chat on its page; a friend's conversation open when one is given.</summary>
        internal void ShowSocial(ChatEntry? entry)
        {
            ShowMobileTab(MobileTab.Social);
            if (entry != null)
                _ = uiChat.OpenAsync(entry);
        }

        internal void OpenQuickSearch() => uiQuickSearch.Open();
        internal void OpenNews() => News_Click(null, new RoutedEventArgs());
        internal void OpenProblemReport() => ProblemReport_Click(null, new RoutedEventArgs());
    }
}
