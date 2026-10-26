using GameVault.Core;
using gamevault.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    internal class WebExceptionHelper
    {
        private static string GetServerMessage(WebException ex)
        {
            string errMessage = string.Empty;
            try
            {
                var resp = new StreamReader(ex.Response?.GetResponseStream()).ReadToEnd();
                JsonObject obj = JsonNode.Parse(resp).AsObject();
                errMessage = obj["message"].ToString().Replace("\n", " ").Replace("\r", "");
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
            return errMessage;
        }
        /// <summary>
        /// Tries to get the response message of the server. Else returns exception message
        /// </summary>
        internal static string TryGetServerMessage(Exception ex)
        {
            if (ex is WebException webEx)
            {
                string msg = GetServerMessage(webEx);
                return string.IsNullOrEmpty(msg) ? Describe(ex) : Loc.F("Server responded: {0}", msg);
            }
            return Describe(ex);
        }
        /// <summary>.NET's own network messages are in English only: the usual ones in the interface language.</summary>
        internal static string Describe(Exception ex) => ex switch
        {
            TaskCanceledException { InnerException: TimeoutException } => Loc.T("The server did not answer in time."),
            HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => Loc.T("The server address could not be found. Check the address and your connection."),
            HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError } => Loc.T("The server could not be reached. Check that it is running and your connection."),
            HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => Loc.T("The secure connection to the server failed (certificate or HTTPS)."),
            // Status errors without a server message (EnsureSuccessStatusCode's own text)
            HttpRequestException { StatusCode: HttpStatusCode code } when ex.Message.StartsWith("Response status code") => code switch
            {
                HttpStatusCode.Unauthorized => Loc.T("The server refused the sign-in: sign in again."),
                HttpStatusCode.Forbidden => Loc.T("The server does not allow this for your account."),
                HttpStatusCode.NotFound => Loc.T("Not found on the server."),
                _ => Loc.F("The server answered with an error ({0}).", (int)code),
            },
            // Our own errors are English texts translated in fr.json
            _ => Loc.T(ex.Message),
        };
        public static async Task EnsureSuccessStatusCode(HttpResponseMessage msg)
        {
            if (!msg.IsSuccessStatusCode)
            {
                string? serverMessage = "";
                try
                {
                    string message = await msg.Content.ReadAsStringAsync();
                    using JsonDocument serverResponseJson = JsonDocument.Parse(message);
                    if (serverResponseJson.RootElement.TryGetProperty("message", out JsonElement messageElement))
                    {
                        serverMessage = messageElement.ValueKind switch
                        {
                            JsonValueKind.String => messageElement.GetString(),
                            JsonValueKind.Array => string.Join("; ", messageElement.EnumerateArray().Select(e => e.GetString())),
                            _ => ""
                        };
                    }
                }
                catch
                {
                    msg.EnsureSuccessStatusCode();
                }
                if (string.IsNullOrWhiteSpace(serverMessage))
                {
                    msg.EnsureSuccessStatusCode();
                }
                else
                {
                    serverMessage = Loc.F("Server responded: {0}", serverMessage);
                }
                throw new HttpRequestException(serverMessage, null, msg.StatusCode);
            }
        }
        internal static string GetServerStatusCode(Exception ex)
        {
            if (ex is WebException webex)
            {

                string errMessage = string.Empty;
                try
                {
                    if (webex.Response == null)
                        return string.Empty;

                    var resp = new StreamReader(webex.Response.GetResponseStream()).ReadToEnd();
                    JsonObject obj = JsonNode.Parse(resp).AsObject();
                    errMessage = obj["statusCode"].ToString().Replace("\n", " ").Replace("\r", "");
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
                return errMessage;
            }
            else if (ex is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
            {
                return ((int)httpEx.StatusCode.Value).ToString();
            }
            return string.Empty;
        }
    }
}
