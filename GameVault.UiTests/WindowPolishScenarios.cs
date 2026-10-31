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
