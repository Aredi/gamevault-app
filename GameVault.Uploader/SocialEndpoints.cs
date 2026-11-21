namespace GameVault.Uploader
{
    /// <summary>
    /// What the players say to each other about their games (any signed-in player):
    /// GET    /social/reactions?items=a,b         the reactions on these lines of the activity
    /// PUT    /social/reactions                   {item, emoji}: puts the player's reaction, or takes it away (emoji null)
    /// POST   /social/recommendations             {to, game, note?}: recommends a game to a friend
    /// GET    /social/recommendations/me          the games recommended to the player
    /// DELETE /social/recommendations/{id}        the player puts one away
    /// GET    /social/reviews/{game}              the reviews of a game ("pc:793", "retro:psx:2161")
    /// PUT    /social/reviews/{game}/me           {stars 1-5, text?}: the player's review
    /// DELETE /social/reviews/{game}/me           takes it away; an administrator takes away anyone's with /{userId}
    /// The owner of a line reacted on, and the friend a game is recommended to, are told at once by the chat's live link.
    /// </summary>
    public static class SocialEndpoints
    {
        public sealed record ReactionRequest(string? Item, string? Emoji);
        public sealed record RecommendationRequest(int To, ChatStore.SharedGame? Game, string? Note);
        public sealed record ReviewRequest(int Stars, string? Text);

        private const int MaxItems = 100;

        private static IResult Bad(string error) => Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);

        private static async Task<(IResult? Refused, AdminCheck.Result Player)> PlayerAsync(HttpRequest request, AdminCheck users, SocialStore social, CancellationToken ct)
        {
            var check = await users.CheckUserAsync(request, ct);
            if (!check.Allowed)
                return (Results.Json(new { error = check.Message }, statusCode: check.Status), check);
            if (!social.Enabled)
                return (Results.Json(new { error = "Reactions, recommendations and reviews are not kept by this server." }, statusCode: StatusCodes.Status503ServiceUnavailable), check);
            if (check.Id <= 0)
                return (Results.Json(new { error = "GameVault did not say who this player is." }, statusCode: StatusCodes.Status401Unauthorized), check);
            return (null, check);
        }

        /// <summary>The player a line of the activity is about: the second part of its key.</summary>
        internal static int OwnerOf(string item) => int.Parse(item.Split(':')[1]);

        private static object Shown(SocialStore.Reaction r) => new { userId = r.UserId, username = r.Username, emoji = r.Emoji };

        public static void MapSocialEndpoints(this WebApplication app)
        {
            app.MapGet("/social/reactions", async (string? items, HttpRequest request, AdminCheck users, SocialStore social, CancellationToken ct) =>
            {
                var (refused, _) = await PlayerAsync(request, users, social, ct);
                if (refused != null)
                    return refused;
                var asked = (items ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Where(SocialStore.IsItem).Distinct().Take(MaxItems);
                return Results.Json(social.ReactionsOn(asked).ToDictionary(p => p.Key, p => p.Value.Select(Shown).ToList()));
            });

            app.MapPut("/social/reactions", async (ReactionRequest body, HttpRequest request, AdminCheck users, SocialStore social, ChatHub hub, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, social, ct);
                if (refused != null)
                    return refused;
                if (!SocialStore.IsItem(body.Item))
                    return Bad("This line of the activity is not known.");
                if (body.Emoji != null && !SocialStore.Emojis.Contains(body.Emoji))
                    return Bad($"A reaction is one of {string.Join(" ", SocialStore.Emojis)}.");
                int owner = OwnerOf(body.Item!);
                if (owner == player.Id)
                    return Bad("Your own activity takes no reaction from you.");
                var (put, changed, tooMany) = social.React(body.Item!, owner, player.Id, player.User, body.Emoji);
                if (tooMany)
                    return Results.Json(new { error = $"{SocialStore.ReactionsPerDay} reactions a day at most." }, statusCode: StatusCodes.Status429TooManyRequests);
                // The same reaction again tells nobody twice
                if (put != null && changed)
                    await hub.SendAsync(new[] { owner }, new { type = "reaction", item = put.Item, emoji = put.Emoji, userId = put.UserId, userName = put.Username });
                var now = social.ReactionsOn(new[] { body.Item! }).GetValueOrDefault(body.Item!) ?? new();
                return Results.Json(now.Select(Shown).ToList());
            });

            app.MapPost("/social/recommendations", async (RecommendationRequest body, HttpRequest request, AdminCheck users, SocialStore social, ChatHub hub, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, social, ct);
                if (refused != null)
                    return refused;
                if (body.To <= 0 || body.To == player.Id)
                    return Bad("A game is recommended to another player.");
                if (body.Game == null || ChatEndpoints.Checked(body.Game) is string wrong)
                    return Bad("A game needs its id and its title (200 characters at most); a retro game its platform.");
                string? note = body.Note?.Trim() is { Length: > 0 } text ? text : null;
                if (note is { Length: > SocialStore.MaxNote })
                    return Bad($"A note has {SocialStore.MaxNote} characters at most.");
                body.Game.Invite = false;
                var (made, refusal) = social.Recommend(player.Id, player.User, body.To, body.Game, note);
                if (made == null)
                    return Results.Json(new { error = $"{SocialStore.RecommendationsPerDay} recommendations a day at most." }, statusCode: StatusCodes.Status429TooManyRequests);
                await hub.SendAsync(new[] { body.To }, new { type = "recommendation", recommendation = made });
                return Results.Json(made, statusCode: StatusCodes.Status201Created);
            });

            app.MapGet("/social/recommendations/me", async (HttpRequest request, AdminCheck users, SocialStore social, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, social, ct);
                return refused ?? Results.Json(social.For(player.Id));
            });

            app.MapDelete("/social/recommendations/{id}", async (string id, HttpRequest request, AdminCheck users, SocialStore social, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, social, ct);
                return refused ?? (social.Dismiss(id, player.Id) ? Results.NoContent() : Results.NotFound());
            });

            app.MapGet("/social/reviews", async (HttpRequest request, AdminCheck users, SocialStore social, CancellationToken ct) =>
            {
                var (refused, _) = await PlayerAsync(request, users, social, ct);
                return refused ?? Results.Json(social.Ratings());
            });

            app.MapGet("/social/reviews/{game}", async (string game, HttpRequest request, AdminCheck users, SocialStore social, CancellationToken ct) =>
            {
                var (refused, _) = await PlayerAsync(request, users, social, ct);
                if (refused != null)
                    return refused;
                return SocialStore.IsGame(game) ? Results.Json(social.ReviewsOf(game)) : Bad("A game is \"pc:<id>\" or \"retro:<platform>:<id>\".");
            });

            app.MapPut("/social/reviews/{game}/me", async (string game, ReviewRequest body, HttpRequest request, AdminCheck users, SocialStore social, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, social, ct);
                if (refused != null)
                    return refused;
                if (!SocialStore.IsGame(game))
                    return Bad("A game is \"pc:<id>\" or \"retro:<platform>:<id>\".");
                if (body.Stars is < 1 or > 5)
                    return Bad("A review has 1 to 5 stars.");
                string? text = body.Text?.Trim() is { Length: > 0 } written ? written : null;
                if (text is { Length: > SocialStore.MaxReview })
                    return Bad($"A review has {SocialStore.MaxReview} characters at most.");
                return Results.Json(social.SetReview(game, player.Id, player.User, body.Stars, text));
            });

            app.MapDelete("/social/reviews/{game}/me", async (string game, HttpRequest request, AdminCheck users, SocialStore social, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, social, ct);
                return refused ?? (social.RemoveReview(game, player.Id) ? Results.NoContent() : Results.NotFound());
            });

            app.MapDelete("/social/reviews/{game}/{userId:int}", async (string game, int userId, HttpRequest request, AdminCheck users, SocialStore social, CancellationToken ct) =>
            {
                var (refused, player) = await PlayerAsync(request, users, social, ct);
                if (refused != null)
                    return refused;
                if (!player.Admin && player.Id != userId)
                    return Results.Json(new { error = "Only an administrator takes away another player's review." }, statusCode: StatusCodes.Status403Forbidden);
                return social.RemoveReview(game, userId) ? Results.NoContent() : Results.NotFound();
            });
        }
    }
}
