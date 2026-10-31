using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using gamevault.Helper;
using gamevault.UserControls;
using gamevault.ViewModels;
using gamevault.Windows;

namespace GameVault.UiTests
{
    public class WindowPolishScenarios
    {
        private static void Layout(Window window)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        /// <summary>A secondary button is on the window's background: its text is the theme's, not the white of a filled button.</summary>
        [AvaloniaFact]
        public async Task ASecondaryButton_IsReadable_InTheLightTheme()
        {
            var session = await TestSession.GetAsync();
            ThemeManager.Apply(ThemeManager.BuiltInThemeBase + "ThemeDefaultLight.xaml");
            var window = new LoginWindow(skipBootTasks: true);
            window.Show();
            try
            {
                ((LoginWindowViewModel)window.DataContext!).LoginStepIndex = (int)LoginStep.SignInOrSignUp;
                Layout(window);
                var back = window.GetVisualDescendants().OfType<IconButton>().First(b => b.IsEffectivelyVisible && b.Kind == ButtonKind.Skeleton);
                var expected = (Avalonia.Media.SolidColorBrush)Application.Current!.FindResource("Brush.Foreground")!;
                Assert.Equal(expected.Color, ((Avalonia.Media.ISolidColorBrush)back.Foreground!).Color);
            }
            finally
            {
                window.Close();
                ThemeManager.ApplyDefault();
            }
        }

        /// <summary>The sign up form is taller than the window: it scrolls, its last field is reached, its buttons stay.</summary>
        [AvaloniaFact]
        public async Task TheSignUpForm_Scrolls_ToItsInvitationCode()
        {
            await TestSession.GetAsync();
            var window = new LoginWindow(skipBootTasks: true);
            window.Show();
            try
            {
                ((LoginWindowViewModel)window.DataContext!).LoginStepIndex = (int)LoginStep.SignUp;
                Layout(window);
                var invite = window.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "uiSignupInvite");
                Assert.NotNull(invite.FindAncestorOfType<ScrollViewer>());
            }
            finally
            {
                window.Close();
            }
        }

        /// <summary>A counter of the console opens its part; the others close.</summary>
        [AvaloniaFact]
        public async Task TheAdminCounters_OpenTheirPart()
        {
            await TestSession.GetAsync();
            var console = new AdminConsoleUserControl();
            var players = console.FindControl<Panel>("uiSectionPlayers")!;
            var server = console.FindControl<StackPanel>("uiSectionServer")!;
            Assert.True(players.IsVisible);
            Assert.False(server.IsVisible);
            console.FindControl<Avalonia.Controls.Primitives.ToggleButton>("uiTileServer")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.False(players.IsVisible);
            Assert.True(server.IsVisible);
            Assert.False(console.FindControl<Avalonia.Controls.Primitives.ToggleButton>("uiTilePlayers")!.IsChecked);
        }

        /// <summary>The search of the settings looks in every tab, a chosen result opens its tab.</summary>
        [AvaloniaFact]
        public async Task TheSettingsSearch_FindsASettingOfAnotherTab_AndOpensIt()
        {
            await TestSession.GetAsync();
            var vm = MainWindowViewModel.Instance;
            vm.SetActiveControl(MainControl.Settings);
            Dispatcher.UIThread.RunJobs();
            try
            {
                var settings = vm.Settings;
                var tabs = settings.FindControl<TabControl>("uiTabControl")!;
                tabs.SelectedIndex = 0;
                settings.FindControl<TextBox>("uiSearch")!.Text = gamevault.Localization.Loc.T("Auto-Extract Downloaded Games")[..10];
                Dispatcher.UIThread.RunJobs();
                var hits = (IEnumerable<SettingsUserControl.SearchHit>)settings.FindControl<ItemsControl>("uiSearchHits")!.ItemsSource!;
                var hit = hits.First(h => h.Title == gamevault.Localization.Loc.T("Auto-Extract Downloaded Games"));
                ClickHit(settings, hit);
                Dispatcher.UIThread.RunJobs();
                Assert.Same(hit.Item, tabs.SelectedItem);
                Assert.False(settings.FindControl<Border>("uiSearchResults")!.IsVisible);
            }
            finally
            {
                vm.Settings.FindControl<TextBox>("uiSearch")!.Text = "";
                vm.SetActiveControl(MainControl.Library);
            }

            static void ClickHit(SettingsUserControl settings, SettingsUserControl.SearchHit hit)
            {
                var button = settings.FindControl<ItemsControl>("uiSearchHits")!.GetRealizedContainers().SelectMany(c => c.GetVisualDescendants()).OfType<Button>().First(b => b.DataContext == hit);
                button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            }
        }

        /// <summary>The disk's slices are measured away from the UI thread (a brush made there threw, the bar stayed empty).</summary>
        [AvaloniaFact]
        public async Task TheDiskUsage_MeasuredOnAnotherThread_IsShownAsABar()
        {
            var session = await TestSession.GetAsync();
            var game = session.Server.AddGame(9871, "Disk Bar Game", gamevault.Models.GameType.LINUX_PORTABLE, new byte[16], "Disk Bar Game (L_P).tar.gz");
            var settings = new GameSettingsUserControl(game);
            var slices = await Task.Run(() => new List<DiskSlice>
            {
                new("This game", 30, Avalonia.Media.Colors.Orange, "3 GB"),
                new("Free", 70, Avalonia.Media.Colors.Transparent, "7 GB"),
            });
            settings.ShowDiskUsage(slices);
            Assert.Equal(2, settings.FindControl<Grid>("uiDiskBar")!.Children.Count);
            Assert.Same(slices, settings.FindControl<ItemsControl>("uiDiskLegend")!.ItemsSource);
        }

        /// <summary>A message is a card in the middle of the window, its main action on the right.</summary>
        [AvaloniaFact]
        public async Task ADialog_IsACard_WithItsMainActionOnTheRight()
        {
            var session = await TestSession.GetAsync();
            Task shown = session.Window.ShowMessageAsync("Title", "Message", MessageDialogStyle.AffirmativeAndNegative);
            Dispatcher.UIThread.RunJobs();
            session.Window.UpdateLayout();
            var layer = session.Window.FindControl<Panel>("DialogLayer")!;
            var buttons = layer.GetVisualDescendants().OfType<IconButton>().ToList();
            Assert.Equal(new[] { ButtonKind.Skeleton, ButtonKind.Primary }, buttons.Select(b => b.Kind));
            var card = buttons[0].GetVisualAncestors().OfType<Border>().Last(b => b.GetVisualParent() is Panel panel && panel.GetVisualParent() == layer);
            Assert.True(card.Bounds.Width < session.Window.Bounds.Width / 2 + 200, $"{card.Bounds.Width} wide");
            buttons[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await shown;
        }
    }
}
