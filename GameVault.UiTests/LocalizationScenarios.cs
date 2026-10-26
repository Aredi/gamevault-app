using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using gamevault.Localization;
using gamevault.UserControls;

namespace GameVault.UiTests
{
    public class LocalizationScenarios
    {
        [AvaloniaFact]
        public async Task TheInterface_IsShownInFrench()
        {
            await TestSession.GetAsync();
            Loc.Initialize("fr");
            try
            {
                var console = new AdminConsoleUserControl();
                Assert.Contains(console.GetLogicalDescendants().OfType<IconButton>(), b => b.Text == "Publier un jeu");
                Assert.Contains(console.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Informations sur le serveur");
                // Texts with values and statuses go through Loc.F / the app bar
                Assert.Equal("'Celeste' n'est pas installé", Loc.F("'{0}' is not installed", "Celeste"));
                gamevault.ViewModels.MainWindowViewModel.Instance.AppBarText = "You are offline";
                Assert.Equal("Vous êtes hors ligne", gamevault.ViewModels.MainWindowViewModel.Instance.AppBarText);
                // Errors: .NET's English network messages and our own
                Assert.Equal("Impossible de joindre le serveur. Vérifie qu'il est allumé et ta connexion.",
                    gamevault.Helper.WebExceptionHelper.TryGetServerMessage(new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (localhost:8080)")));
                Assert.Equal("RetroArch n'a pas pu démarrer", gamevault.Helper.WebExceptionHelper.TryGetServerMessage(new InvalidOperationException("RetroArch could not be started")));
            }
            finally
            {
                Loc.Initialize("en");
            }
        }
    }
}
