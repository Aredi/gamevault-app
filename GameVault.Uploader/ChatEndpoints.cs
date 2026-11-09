namespace GameVault.Uploader
{
    /// <summary>
    /// The players' chat (any signed-in player):
    /// GET  /chat/live                                   websocket: what concerns the player, as it happens
    /// GET  /chat/conversations                          the player's conversations, the last active first
    /// POST /chat/groups                                 makes a group {name, members}
    /// PUT  /chat/groups/{id}                            renames it or adds players {name?, add?}
    /// DELETE /chat/groups/{id}/members/me               leaves it
    /// GET  /chat/conversations/{id}/messages?before=    the last 50 messages (before one)
    /// POST /chat/conversations/{id}/messages            sends a text {text}
    /// POST /chat/conversations/{id}/pictures?text=      sends a picture (the body: PNG, JPEG, GIF or WebP, 10 MB at most)
    /// GET  /chat/conversations/{id}/pictures/{name}     a picture of the conversation
    /// PUT  /chat/conversations/{id}/read                the player read it (the others see "Seen")
    /// PUT  /chat/conversations/{id}/messages/{mid}      its sender writes it again {text}
    /// DELETE /chat/conversations/{id}/messages/{mid}    its sender takes it back
    /// PUT  /chat/conversations/{id}/messages/{mid}/reactions   puts or takes away an emoji {emoji}
    /// POST /chat/conversations/{id}/typing              the player is writing (the others see it a few seconds)
    /// POST /chat/news/games                             an administrator's client announces a new game of the library, once
    /// PUT  /chat/presence/me                            away, do not disturb, invisible, since when the game is played
    /// GET  /chat/presence                               the players' statuses
    /// A conversation between two players is "d-{smaller id}-{larger id}": it exists from its first message. "news" is
    /// the administrators' announcements: every player reads it, only an administrator writes in it.
    /// </summary>
    public static class ChatEndpoints
    {
        private const int PageSize = 50;

        public sealed record GroupRequest(string? Name, List<int>? Members);
        public sealed record GroupChange(string? Name, List<int>? Add);
        public sealed record TextRequest(string? Text, string? ReplyTo = null, ChatStore.SharedGame? Game = null);
        public sealed record EditRequest(string? Text);
        public sealed record ReactionRequest(string? Emoji);
        public sealed record PresenceRequest(string? State, int? GameId, DateTimeOffset? GameSince);
        public sealed record RetroPlaying(string? Platform, int RomId, string? Name, string? PlatformName);

        private static object Shown(RetroPresenceStore.Playing p) => new { userId = p.UserId, platform = p.Platform, romId = p.RomId, name = p.Name, platformName = p.PlatformName, since = p.Since };

        private static object Shown(PlayerStatusStore.Status s) => new { userId = s.UserId, state = s.State, gameId = s.GameId, gameSince = s.GameSince };

        private static object Shown(ChatStore.Message m) => new
        {
            id = m.Id, conversationId = m.ConversationId, senderId = m.SenderId, senderName = m.SenderName, text = m.Text, picture = m.Picture, sentAt = m.SentAt,
            replyTo = m.ReplyTo, game = m.Game, editedAt = m.EditedAt, deleted = m.Deleted, reactions = m.Reactions,
        };

        // Who read up to when: "Seen" under the last message (the announcements keep it to themselves)
        private static object Shown(ChatStore.Conversation c, ChatStore.Message? last, int unread) =>
            new { id = c.Id, kind = c.Kind, name = c.Name, members = c.Members, activeAt = c.ActiveAt, unread, last = last == null ? null : Shown(last), readAt = c.IsNews ? null : c.ReadAt };

        /// <summary>To the members' open clients; the announcements to every one.</summary>
        private static Task TellAsync(ChatHub hub, ChatStore.Conversation conversation, object value) =>
            conversation.IsNews ? hub.SendAllAsync(value) : hub.SendAsync(conversation.Members, value);

        private static IResult Bad(string error) => Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);

        private static IResult OnlyAdmins() => Results.Json(new { error = "Only an administrator writes in the announcements." }, statusCode: StatusCodes.Status403Forbidden);

        /// <summary>A game as a message may show it: a library game by its id, a retro game by its platform and RomM id.</summary>
        private static string? Checked(ChatStore.SharedGame game)
        {
            game.Title = (game.Title ?? "").Trim();
            if (game.Title.Length is 0 or > 200 || game.Id <= 0)
                return "A game needs its id and its title (200 characters at most).";
            if (game.Kind == "pc")
            {
                (game.Platform, game.PlatformName, game.Cover, game.CoverVersion) = (null, null, null, null);
                return null;
            }
            if (game.Kind != "retro" || !RetroPlayerStore.IsValidKey($"{game.Platform}:{game.Id}"))
                return "A game is \"pc\", or \"retro\" with its platform.";
            game.PlatformName = game.PlatformName?.Trim() is { Length: > 0 } name ? name[..Math.Min(name.Length, 80)] : null;
            // Its cover goes through /retro/media: a path of RomM's, nothing else
            if (game.Cover is { } cover && (cover.Length > 300 || cover.Contains("..") || cover.Contains("://") || cover.Contains('\\')))
                game.Cover = null;
            if (game.CoverVersion is { Length: > 64 })
                game.CoverVersion = null;
            return null;
        }

        private static async Task<(IResult? Refused, AdminCheck.Result Player)> PlayerAsync(HttpRequest request, AdminCheck users, ChatStore chat, CancellationToken ct)
        {
            var check = await users.CheckUserAsync(request, ct);
            if (!check.Allowed)
                return (Results.Json(new { error = check.Message }, statusCode: check.Status), check);
            if (!chat.Enabled)
                return (Results.Json(new { error = "The chat is not available on this server." }, statusCode: StatusCodes.Status503ServiceUnavailable), check);
            if (check.Id <= 0)
                return (Results.Json(new { error = "GameVault did not say who this player is." }, statusCode: StatusCodes.Status401Unauthorized), check);
            return (null, check);
        }

        private static IResult NotFound() => Results.Json(new { error = "This conversation does not exist, or you are not in it." }, statusCode: StatusCodes.Status404NotFound);

        private static async Task<IResult> ChangedAsync(ChatHub hub, ChatStore.Conversation conversation, ChatStore.Change result, ChatStore.Message? message)
        {
            if (result == ChatStore.Change.NotYours)
                return Results.Json(new { error = "Only its sender changes a message." }, statusCode: StatusCodes.Status403Forbidden);
            if (message == null)
                return Results.Json(new { error = "This message does not exist, or was taken back." }, statusCode: StatusCodes.Status404NotFound);
            await TellAsync(hub, conversation, new { type = "changed", message = Shown(message) });
            return Results.Json(Shown(message));
        }

        public static void MapChatEndpoints(this WebApplication app)
        {
            app.Map("/chat/live", async (HttpContext context, AdminCheck users, ChatStore chat, ChatHub hub) =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
                var (refused, player) = await PlayerAsync(context.Request, users, chat, context.RequestAborted);
                if (refused != null)
                {
                    await refused.ExecuteAsync(context);
                    return;
                }
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                await hub.KeepAsync(player.Id, socket, context.RequestAborted);
            });

            // The retro game a player plays now (GameVault's activity only knows its own games): said each minute while
            // it runs, an empty name when it stops; the others hear it on their live link
            app.MapPut("/activity/retro/me", async (RetroPlaying played, HttpRequest request, AdminCheck users, RetroPresenceStore presence, ChatHub hub, CancellationToken ct) =>
            {
                var check = await users.CheckUserAsync(request, ct);
                if (!check.Allowed)
                    return Results.Json(new { error = check.Message }, statusCode: check.Status);
                string name = (played.Name ?? "").Trim();
                RetroPresenceStore.Playing? now = null;
                if (name.Length > 0)
                {
                    string key = $"{played.Platform}:{played.RomId}";
                    if (!RetroPlayerStore.IsValidKey(key))
                        return Results.Json(new { error = "A retro game needs its platform and its RomM id." }, statusCode: StatusCodes.Status400BadRequest);
                    string platformName = (played.PlatformName ?? "").Trim();
                    now = new RetroPresenceStore.Playing(check.Id, played.Platform!, played.RomId, name[..Math.Min(name.Length, 200)], platformName[..Math.Min(platformName.Length, 80)], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
                }
                if (presence.Set(check.Id, now))
                    await hub.SendAllAsync(new { type = "retroPlaying", userId = check.Id, playing = presence.Now(DateTimeOffset.UtcNow).FirstOrDefault(p => p.UserId == check.Id) is { } kept ? Shown(kept) : null });
                return Results.NoContent();
            });

            app.MapGet("/activity/retro", async (HttpRequest request, AdminCheck users, RetroPresenceStore presence, CancellationToken ct) =>
            {
                var check = await users.CheckUserAsync(request, ct);
                if (!check.Allowed)
                    return Results.Json(new { error = check.Message }, statusCode: check.Status);
                return Results.Json(presence.Now(DateTimeOffset.UtcNow).Select(Shown));
            });

            app.MapGet("/chat/conversations", async (HttpRequest request, AdminCheck users, ChatStore chat, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, chat, ct);
                return refused ?? Results.Json(chat.ListFor(player.Id).Select(s => Shown(s.Conversation, s.Last, s.Unread)));
            });

            app.MapPost("/chat/groups", async (GroupRequest group, HttpRequest request, AdminCheck users, ChatStore chat, ChatHub hub, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, chat, ct);
                if (refused != null)
                    return refused;
                string name = (group.Name ?? "").Trim();
                var members = (group.Members ?? new()).Where(m => m > 0 && m != player.Id).Distinct().ToList();
                if (name.Length is 0 or > ChatStore.MaxGroupName || members.Count == 0 || members.Count >= ChatStore.MaxGroupMembers)
                    return Results.Json(new { error = $"A group needs a name ({ChatStore.MaxGroupName} characters at most) and 1 to {ChatStore.MaxGroupMembers - 1} other players." }, statusCode: StatusCodes.Status400BadRequest);
                ChatStore.Conversation made = chat.CreateGroup(player.Id, name, members);
                await hub.SendAsync(made.Members, new { type = "conversation", conversation = Shown(made, null, 0) });
                return Results.Json(Shown(made, null, 0));
            });

            app.MapPut("/chat/groups/{id}", async (string id, GroupChange change, HttpRequest request, AdminCheck users, ChatStore chat, ChatHub hub, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, chat, ct);
                if (refused != null)
                    return refused;
                string? name = change.Name?.Trim();
                if (name is { Length: 0 or > ChatStore.MaxGroupName })
                    return Results.Json(new { error = $"A group's name has 1 to {ChatStore.MaxGroupName} characters." }, statusCode: StatusCodes.Status400BadRequest);
                if (!ChatStore.IsValidId(id) || chat.ChangeGroup(id, player.Id, name, change.Add) is not ChatStore.Conversation group)
                    return NotFound();
                await hub.SendAsync(group.Members, new { type = "conversation", conversation = Shown(group, null, 0) });
                return Results.Json(Shown(group, null, 0));
            });

            app.MapDelete("/chat/groups/{id}/members/me", async (string id, HttpRequest request, AdminCheck users, ChatStore chat, ChatHub hub, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, chat, ct);
                if (refused != null)
                    return refused;
                if (!ChatStore.IsValidId(id) || chat.Leave(id, player.Id) is not ChatStore.Conversation group)
                    return NotFound();
                // The one who left hears it too: their other PCs take the group away
                await hub.SendAsync(group.Members.Append(player.Id), new { type = "conversation", conversation = Shown(group, null, 0) });
                return Results.NoContent();
            });

            app.MapGet("/chat/conversations/{id}/messages", async (string id, string? before, HttpRequest request, AdminCheck users, ChatStore chat, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, chat, ct);
                if (refused != null)
                    return refused;
                if (!ChatStore.IsValidId(id) || chat.Of(id, player.Id) is null)
                    return NotFound();
                return Results.Json(chat.Messages(id, before, PageSize).Select(Shown));
            });

            app.MapPost("/chat/conversations/{id}/messages", async (string id, TextRequest body, HttpRequest request, AdminCheck users, ChatStore chat, ChatHub hub, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, chat, ct);
                if (refused != null)
                    return refused;
                string text = (body.Text ?? "").Trim();
                // A game alone is a message (shared, or an invitation)
                if (text.Length > ChatStore.MaxText || (text.Length == 0 && body.Game == null))
                    return Bad($"A message has 1 to {ChatStore.MaxText} characters.");
                if (body.Game != null && Checked(body.Game) is string wrong)
                    return Bad(wrong);
                if (!ChatStore.IsValidId(id) || chat.Of(id, player.Id) is not ChatStore.Conversation conversation)
                    return NotFound();
                if (conversation.IsNews && !player.Admin)
                    return OnlyAdmins();
                // An invitation is for the friends, not the announcements
                if (body.Game != null && conversation.IsNews)
                    body.Game.Invite = false;
                ChatStore.Message message = chat.Add(conversation, player.Id, player.User, text.Length == 0 ? null : text, replyTo: body.ReplyTo, game: body.Game);
                await TellAsync(hub, conversation, new { type = "message", message = Shown(message) });
                return Results.Json(Shown(message));
            });

            app.MapPost("/chat/conversations/{id}/pictures", async (string id, string? text, string? replyTo, HttpRequest request, AdminCheck users, ChatStore chat, ChatHub hub, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, chat, ct);
                if (refused != null)
                    return refused;
                string? caption = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
                if (caption is { Length: > ChatStore.MaxText })
                    return Results.Json(new { error = $"A message has 1 to {ChatStore.MaxText} characters." }, statusCode: StatusCodes.Status400BadRequest);
                if (!ChatStore.IsValidId(id) || chat.Of(id, player.Id) is not ChatStore.Conversation conversation)
                    return NotFound();
                if (conversation.IsNews && !player.Admin)
                    return OnlyAdmins();
                // Read whole (10 MB at most) to know what it is before anything is written
                using var picture = new MemoryStream();
                var buffer = new byte[81920];
                int read;
                while ((read = await request.Body.ReadAsync(buffer, ct)) > 0)
                {
                    if (picture.Length + read > ChatStore.MaxPicture)
                        return Results.Json(new { error = "A picture is 10 MB at most." }, statusCode: StatusCodes.Status413PayloadTooLarge);
                    picture.Write(buffer, 0, read);
                }
                byte[] data = picture.ToArray();
                if (ChatStore.PictureType(data) is not string type)
                    return Results.Json(new { error = "Only PNG, JPEG, GIF and WebP pictures can be sent." }, statusCode: StatusCodes.Status415UnsupportedMediaType);
                ChatStore.Message message = chat.Add(conversation, player.Id, player.User, caption, type, data, replyTo);
                await TellAsync(hub, conversation, new { type = "message", message = Shown(message) });
                return Results.Json(Shown(message));
            });

            app.MapGet("/chat/conversations/{id}/pictures/{name}", async (string id, string name, HttpRequest request, AdminCheck users, ChatStore chat, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, chat, ct);
                if (refused != null)
                    return refused;
                if (!ChatStore.IsValidId(id) || chat.Of(id, player.Id) is null || chat.Picture(id, name) is not byte[] picture)
                    return Results.NotFound();
                // A picture never changes: the client keeps it
                request.HttpContext.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
                return Results.File(picture, ChatStore.PictureTypes[Path.GetExtension(name).TrimStart('.')]);
            });

            app.MapPut("/chat/conversations/{id}/read", async (string id, HttpRequest request, AdminCheck users, ChatStore chat, ChatHub hub, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, chat, ct);
                if (refused != null)
                    return refused;
                if (!ChatStore.IsValidId(id) || chat.Of(id, player.Id) is not ChatStore.Conversation conversation)
                    return NotFound();
                // The others see "Seen"; the player's other PCs take the unread count away (the announcements: only them)
                if (chat.MarkRead(conversation, player.Id) is DateTime readAt)
                    await hub.SendAsync(conversation.IsNews ? new[] { player.Id } : conversation.Members, new { type = "read", conversationId = id, userId = player.Id, readAt });
                return Results.NoContent();
            });

            app.MapPut("/chat/conversations/{id}/messages/{messageId}", async (string id, string messageId, EditRequest body, HttpRequest request, AdminCheck users, ChatStore chat, ChatHub hub, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, chat, ct);
                if (refused != null)
                    return refused;
                string text = (body.Text ?? "").Trim();
                if (text.Length is 0 or > ChatStore.MaxText)
                    return Bad($"A message has 1 to {ChatStore.MaxText} characters.");
                if (!ChatStore.IsValidId(id) || chat.Of(id, player.Id) is not ChatStore.Conversation conversation)
                    return NotFound();
                var (result, message) = chat.Edit(id, messageId, player.Id, text);
                return await ChangedAsync(hub, conversation, result, message);
            });

            app.MapDelete("/chat/conversations/{id}/messages/{messageId}", async (string id, string messageId, HttpRequest request, AdminCheck users, ChatStore chat, ChatHub hub, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, chat, ct);
                if (refused != null)
                    return refused;
                if (!ChatStore.IsValidId(id) || chat.Of(id, player.Id) is not ChatStore.Conversation conversation)
                    return NotFound();
                var (result, message) = chat.Remove(id, messageId, player.Id, player.Admin);
                return await ChangedAsync(hub, conversation, result, message);
            });

            app.MapPut("/chat/conversations/{id}/messages/{messageId}/reactions", async (string id, string messageId, ReactionRequest body, HttpRequest request, AdminCheck users, ChatStore chat, ChatHub hub, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, chat, ct);
                if (refused != null)
                    return refused;
                // An emoji (a flag, a family: a few code units), no text
                string emoji = (body.Emoji ?? "").Trim();
                if (emoji.Length is 0 or > 16 || emoji.Any(char.IsWhiteSpace) || emoji.Any(char.IsAsciiLetterOrDigit) && emoji != "GG")
                    return Bad("A reaction is one emoji.");
                if (!ChatStore.IsValidId(id) || chat.Of(id, player.Id) is not ChatStore.Conversation conversation)
                    return NotFound();
                ChatStore.Message? message = chat.React(id, messageId, player.Id, emoji);
                return await ChangedAsync(hub, conversation, message == null ? ChatStore.Change.Missing : ChatStore.Change.Done, message);
            });

            app.MapPost("/chat/conversations/{id}/typing", async (string id, HttpRequest request, AdminCheck users, ChatStore chat, ChatHub hub, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, chat, ct);
                if (refused != null)
                    return refused;
                if (!ChatStore.IsValidId(id) || chat.Of(id, player.Id) is not ChatStore.Conversation conversation)
                    return NotFound();
                if (!conversation.IsNews)
                    await hub.SendAsync(conversation.Members.Where(m => m != player.Id), new { type = "typing", conversationId = id, userId = player.Id, userName = player.User });
                return Results.NoContent();
            });

            app.MapPost("/chat/news/games", async (ChatStore.SharedGame game, HttpRequest request, AdminCheck users, ChatStore chat, ChatHub hub, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, chat, ct);
                if (refused != null)
                    return refused;
                if (!player.Admin)
                    return OnlyAdmins();
                if (Checked(game) is string wrong)
                    return Bad(wrong);
                // Already told (another administrator's client saw it first): nothing more
                if (chat.Announce(player.Id, player.User, game) is not ChatStore.Message message)
                    return Results.NoContent();
                await hub.SendAllAsync(new { type = "message", message = Shown(message) });
                return Results.Json(Shown(message));
            });

            app.MapPut("/chat/presence/me", async (PresenceRequest body, HttpRequest request, AdminCheck users, PlayerStatusStore statuses, ChatHub hub, CancellationToken ct) =>
            {
                var check = await users.CheckUserAsync(request, ct);
                if (!check.Allowed)
                    return Results.Json(new { error = check.Message }, statusCode: check.Status);
                string state = body.State ?? "online";
                if (!PlayerStatusStore.States.Contains(state))
                    return Bad("A status is online, away, busy or invisible.");
                // Since when: never later than now, never days ago
                DateTimeOffset now = DateTimeOffset.UtcNow;
                DateTimeOffset? since = body.GameId is > 0 && body.GameSince is DateTimeOffset s && s <= now.AddMinutes(1) && now - s < TimeSpan.FromDays(2) ? s : null;
                var status = new PlayerStatusStore.Status(check.Id, state, body.GameId is > 0 ? body.GameId : null, since, now);
                if (statuses.Set(status))
                    await hub.SendAllAsync(new { type = "presence", status = Shown(PlayerStatusStore.Shown(status)) });
                return Results.NoContent();
            });

            app.MapGet("/chat/presence", async (HttpRequest request, AdminCheck users, PlayerStatusStore statuses, CancellationToken ct) =>
            {
                var check = await users.CheckUserAsync(request, ct);
                if (!check.Allowed)
                    return Results.Json(new { error = check.Message }, statusCode: check.Status);
                return Results.Json(statuses.Now(DateTimeOffset.UtcNow).Select(Shown));
            });
        }
    }
}
