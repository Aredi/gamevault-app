using Avalonia.Controls;
using System.Threading.Tasks;

namespace gamevault.Windows
{
    /// <summary>The PC's window around <see cref="LoginView"/>, closed once signed in.</summary>
    public partial class LoginWindow : Window
    {
        public LoginView View { get; }

        /// <summary>Completes with true once the main window has been created, false if the window was closed.</summary>
        public Task<bool> Completion => View.Completion;

        /// <summary>A step that waits for the player (a profile to choose, a password): not just loading.</summary>
        internal event System.Action? NeedsThePlayer
        {
            add => View.NeedsThePlayer += value;
            remove => View.NeedsThePlayer -= value;
        }

        public LoginWindow(bool skipBootTasks = false)
        {
            InitializeComponent();
            View = new LoginView(skipBootTasks);
            Content = View;
            // The view model, as before the view was split from the window
            DataContext = View.DataContext;
            Closed += (_, _) => View.HostClosed();
            _ = CloseOnceSignedIn();
        }

        private async Task CloseOnceSignedIn()
        {
            if (await View.Completion)
                Close();
        }
    }
}
