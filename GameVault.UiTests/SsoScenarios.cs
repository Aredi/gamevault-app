using gamevault.Helper;

namespace GameVault.UiTests
{
    public class SsoScenarios
    {
        [Fact]
        public void TheServersRefusal_EndsTheSignIn_TheTokensStillSignIn()
        {
            var refused = SsoAuthenticator.ParseServerError("{\"message\":\"Unauthorized\",\"statusCode\":401}");
            Assert.Equal((401, "Unauthorized"), (refused!.StatusCode, refused.Message));
            // The token page, the pages on the way (Authentik's) and a broken page are waited on
            string tokens = "{\"access_token\":\"a\",\"refresh_token\":\"r\"}";
            Assert.Null(SsoAuthenticator.ParseServerError(tokens));
            Assert.NotNull(SsoAuthenticator.ParseTokenJson(tokens));
            Assert.Null(SsoAuthenticator.ParseServerError("Sign in with Plex"));
            Assert.Null(SsoAuthenticator.ParseServerError("{\"statusCode\":"));
        }
    }
}
