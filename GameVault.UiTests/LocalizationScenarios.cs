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
                // The server's messages, with their values and the form's field names
                foreach (string text in gamevault.Helper.WebExceptionHelper.ServerTexts)
                    Assert.True(Loc.T(text) != text, text);
                Assert.Equal("Le jeu 42 est introuvable sur le serveur.", gamevault.Helper.WebExceptionHelper.Translate("Game with id 42 was not found on the server."));
                Assert.Equal("Le champ « nom d'utilisateur » doit faire entre 2 et 32 caractères",
                    gamevault.Helper.WebExceptionHelper.Translate("username must be longer than or equal to 2 and shorter than or equal to 32 characters"));
                Assert.Equal("Le champ « mot de passe » doit faire au moins 8 caractères", gamevault.Helper.WebExceptionHelper.Translate("password must be longer than or equal to 8 characters"));
                Assert.Equal("Something new from the server", gamevault.Helper.WebExceptionHelper.Translate("Something new from the server"));
            }
            finally
            {
                Loc.Initialize("en");
            }
        }
    }
}
