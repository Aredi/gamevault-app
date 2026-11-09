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
    /// PUT  /chat/conversations/{id}/read                the player read it
    /// A conversation between two players is "d-{smaller id}-{larger id}": it exists from its first message.
    /// </summary>
    public static class ChatEndpoints
    {
        private const int PageSize = 50;

        public sealed record GroupRequest(string? Name, List<int>? Members);
        public sealed record GroupChange(string? Name, List<int>? Add);
        public sealed record TextRequest(string? Text);
        public sealed record RetroPlaying(string? Platform, int RomId, string? Name, string? PlatformName);

        private static object Shown(RetroPresenceStore.Playing p) => new { userId = p.UserId, platform = p.Platform, romId = p.RomId, name = p.Name, platformName = p.PlatformName };

        private static object Shown(ChatStore.Message m) => new { id = m.Id, conversationId = m.ConversationId, senderId = m.SenderId, senderName = m.SenderName, text = m.Text, picture = m.Picture, sentAt = m.SentAt };

        private static object Shown(ChatStore.Conversation c, ChatStore.Message? last, int unread) =>
            new { id = c.Id, kind = c.Kind, name = c.Name, members = c.Members, activeAt = c.ActiveAt, unread, last = last == null ? null : Shown(last) };

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
                    now = new RetroPresenceStore.Playing(check.Id, played.Platform!, played.RomId, name[..Math.Min(name.Length, 200)], platformName[..Math.Min(platformName.Length, 80)], DateTimeOffset.UtcNow);
                }
                if (presence.Set(check.Id, now))
                    await hub.SendAllAsync(new { type = "retroPlaying", userId = check.Id, playing = now == null ? null : Shown(now) });
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
                if (text.Length is 0 or > ChatStore.MaxText)
                    return Results.Json(new { error = $"A message has 1 to {ChatStore.MaxText} characters." }, statusCode: StatusCodes.Status400BadRequest);
                if (!ChatStore.IsValidId(id) || chat.Of(id, player.Id) is not ChatStore.Conversation conversation)
                    return NotFound();
                ChatStore.Message message = chat.Add(conversation, player.Id, player.User, text);
                await hub.SendAsync(conversation.Members, new { type = "message", message = Shown(message) });
                return Results.Json(Shown(message));
            });

            app.MapPost("/chat/conversations/{id}/pictures", async (string id, string? text, HttpRequest request, AdminCheck users, ChatStore chat, ChatHub hub, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, chat, ct);
                if (refused != null)
                    return refused;
                string? caption = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
                if (caption is { Length: > ChatStore.MaxText })
                    return Results.Json(new { error = $"A message has 1 to {ChatStore.MaxText} characters." }, statusCode: StatusCodes.Status400BadRequest);
                if (!ChatStore.IsValidId(id) || chat.Of(id, player.Id) is not ChatStore.Conversation conversation)
                    return NotFound();
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
                ChatStore.Message message = chat.Add(conversation, player.Id, player.User, caption, type, data);
                await hub.SendAsync(conversation.Members, new { type = "message", message = Shown(message) });
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

            app.MapPut("/chat/conversations/{id}/read", async (string id, HttpRequest request, AdminCheck users, ChatStore chat, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, chat, ct);
                if (refused != null)
                    return refused;
                if (!ChatStore.IsValidId(id) || chat.Of(id, player.Id) is not ChatStore.Conversation conversation)
                    return NotFound();
                chat.MarkRead(conversation, player.Id);
                return Results.NoContent();
            });
        }
    }
}
