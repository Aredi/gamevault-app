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
using System.Text.RegularExpressions;
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
                errMessage = Translate(obj["message"].ToString().Replace("\n", " ").Replace("\r", ""));
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
            // Our own errors and the services' are English texts translated in fr.json
            _ => Translate(ex.Message),
        };
        /// <summary>
        /// The GameVault server's and the SanctuaryVault service's messages, English only. {0}, {1}... stand for their
        /// values (a game's id, a field's name...), given back translated when they are known words.
        /// </summary>
        internal static readonly string[] ServerTexts =
        {
            // GameVault server 17
            "Authentication Failed: Incorrect Password",
            "Authentication Failed: User not found. If you are a new user, please register first.",
            "Authentication Failed: User has been deleted. Contact an Administrator to recover the User.",
            "Authentication Failed: Contact an Administrator to check the server logs for more information.",
            "Authentication Failed: No user search criteria were provided.",
            "Authentication Failed: Invalid API-Key. If you are a new user, please register first.",
            "Authentication Failed: Missing access token or profile from the identity provider.",
            "Authentication Failed: token has been revoked",
            "Authentication Failed: Your identity provider did not provide all required fields to complete your registration. Missing fields: {0}.",
            "Authorization Failed: User is not activated. Contact an Administrator to activate the User.",
            "Authorization Failed: Insufficient Role. You need to be '{0}' to do this, but you are a '{1}'.",
            "Self-Registration is disabled on this server. Contact an Administrator to register a new user.",
            "A user with this {0} already exists. Please note, that they are case-insensitive.",
            "You are too young to update your birth date. Contact an Administrator to update your birth date.",
            "Usernames can only contain latin letters, numbers and underscores",
            "No Authorization provided",
            "Invalid or expired refresh token",
            "No refresh token provided",
            "Invalid OTP",
            "Expired OTP",
            "User with id {0} was not found.",
            "User with username {0} was not found on the server.",
            "Game with id {0} was not found on the server.",
            "GameMetadata with id {0} was not found on the server.",
            "Game with id {0} not found on IGDB.",
            "Game with id {0} has no downloadable versions associated.",
            "Game with id {0} has no valid version file path associated.",
            "Version with id \"{0}\" not found for game id {1}.",
            "Game file not found on disk for requested version id \"{0}\".",
            "The game has no downloadable versions available.",
            "The game file \"{0}\" could not be found.",
            "No matching games found.",
            "Progress with id {0} was not found on the server.",
            "New value for \"minutes_played\" cannot be less than previous value: {0} minutes",
            "The server does not have write permissions on the files volume \"{0}\". Game deletion requires write access.",
            "The server does not have write permissions on the files volume \"{0}\". Game upload requires write access.",
            "The uploaded file has an invalid filename.",
            "Unsupported file format \"{0}\". Supported formats: {1}",
            "A file named \"{0}\" already exists in the game library.",
            "Media with id {0} was not found.",
            "Media not found on filesystem.",
            "Failed to download media from '{0}'.",
            "Error uploading media. Please retry or use a different file.",
            "Media type must start with {0}",
            "Savefile for user {0} and game {1} was not found.",
            "Installation ID must be a valid UUID v4.",
            "The database password provided in the X-Database-Password Header is incorrect.",
            "This server can't backup its data as it uses an in-memory database.",
            "This server can't restore backups as it uses an in-memory database.",
            "This API endpoint is disabled.",
            "Forbidden resource",
            "Unauthorized",
            "Internal server error",
            // Its form checks (the longer text first: its start is the shorter one's)
            "{0} must be longer than or equal to {1} and shorter than or equal to {2} characters",
            "{0} must be longer than or equal to {1} characters",
            "{0} must be shorter than or equal to {1} characters",
            "{0} must be an email",
            "{0} should not be empty",
            "{0} must contain only letters (a-zA-Z)",
            "{0} must be a valid ISO 8601 date string before the current time.",
            "{0} must be a valid ISO 8601 date string",
            // SanctuaryVault service
            "Saves can have at most {0} MB.",
            "At most {0} rooms per player: leave one first.",
            "This room is full ({0} players).",
            "At most {0} games can wait.",
            "A profile can have at most {0} bytes.",
            "A chunk can have at most {0} bytes.",
        };
        private static readonly Lazy<(Regex Pattern, string Text)[]> serverPatterns = new(() => ServerTexts
            .Select(text => (new Regex("^" + Regex.Replace(Regex.Escape(text), @"\\\{(\d)}", "(?<v$1>.+?)") + "$"), text)).ToArray());

        /// <summary>A message of the server in the interface language when it is a known one, else as it is.</summary>
        internal static string Translate(string message)
        {
            foreach (var (pattern, text) in serverPatterns.Value)
            {
                Match match = pattern.Match(message);
                if (match.Success)
                    return Loc.F(text, Enumerable.Range(0, 3).Select(i => match.Groups["v" + i]).TakeWhile(g => g.Success).Select(g => (object)Loc.T(g.Value)).ToArray());
            }
            return Loc.T(message);
        }
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
                            JsonValueKind.String => Translate(messageElement.GetString() ?? ""),
                            JsonValueKind.Array => string.Join("; ", messageElement.EnumerateArray().Select(e => Translate(e.GetString() ?? ""))),
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
