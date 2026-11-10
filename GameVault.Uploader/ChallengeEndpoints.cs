namespace GameVault.Uploader
{
    /// <summary>
    /// The challenge of the week (any signed-in player):
    /// GET  /challenges            this week's challenge and ranking, the challenges completed by each player, the last weeks
    /// PUT  /challenges/week       an administrator chooses this week's challenge {kind, goal, game?}
    /// POST /challenges/me/plays   a minute played on these games of the library {gameIds}
    /// The retro games count through /retro/players/me/plays and the achievements through /retro/players/me/achievements.
    /// A request that completes the challenge is answered {"challengeDone": true} instead of 204.
    /// </summary>
    public static class ChallengeEndpoints
    {
        public sealed record PlayedMinute(List<int>? GameIds);
        public sealed record Chosen(string? Kind, int Goal, ChatStore.SharedGame? Game);

        private static IResult Off() => Results.Json(new { error = "Challenges are not kept by this server." }, statusCode: StatusCodes.Status503ServiceUnavailable);

        /// <summary>What a request that counted for the challenge answers: it says when the challenge was just completed.</summary>
        internal static IResult Told(bool done) => done ? Results.Json(new { challengeDone = true }) : Results.NoContent();

        /// <summary>
        /// The first time a player counts for the challenges, the games they played before are asked for (the library's
        /// to the GameVault server, as them): none of these will be called a discovery.
        /// </summary>
        internal static async Task SeedAsync(int userId, HttpRequest request, ChallengeStore challenges, GameVaultProxy gameVault, RetroPlayerStore players, CancellationToken ct)
        {
            if (!challenges.Enabled || challenges.IsSeeded(userId))
                return;
            List<int>? played = await gameVault.PlayedGamesAsync(request.Headers.Authorization.ToString(), userId, ct);
            if (played != null)
                challenges.Seed(userId, played.Select(ChallengeStore.LibraryKey).Concat(players.Get(userId).Plays.Select(play => play.Key)));
        }

        private static object? Shown(ChallengeStore.Rank? rank) => rank == null ? null : new { userId = rank.UserId, username = rank.Username, value = rank.Value, doneAt = rank.DoneAt };

        /// <summary>The challenge as an administrator may choose it, or why not.</summary>
        internal static (ChallengeStore.Challenge? Challenge, string? Error) Checked(Chosen asked, string admin)
        {
            ChatStore.SharedGame? game = asked.Game;
            (int min, int max) = asked.Kind switch
            {
                ChallengeStore.Hours or ChallengeStore.Discovery or ChallengeStore.Retro or ChallengeStore.Game => (15, 6000),
                ChallengeStore.Games => (1, 20),
                ChallengeStore.Achievements => (1, 200),
                _ => (0, 0),
            };
            if (max == 0)
                return (null, "A challenge is \"hours\", \"games\", \"discovery\", \"retro\", \"achievements\" or \"game\".");
            if (asked.Goal < min || asked.Goal > max)
                return (null, $"The goal of this challenge goes from {min} to {max}.");
            if (asked.Kind is not (ChallengeStore.Game or ChallengeStore.Achievements))
                game = null;
            if (asked.Kind == ChallengeStore.Game && game == null)
                return (null, "This challenge needs its game.");
            if (game != null)
            {
                game.Title = (game.Title ?? "").Trim();
                bool retro = game.Kind == "retro" && RetroPlayerStore.IsValidKey($"{game.Platform}:{game.Id}");
                if (game.Title.Length is 0 or > 200 || game.Id <= 0 || !(retro || game.Kind == "pc") || (asked.Kind == ChallengeStore.Achievements && !retro))
                    return (null, "The game of a challenge is \"pc\" or \"retro\" with its platform, and has a title; achievements are earned on retro games.");
                game.Invite = false;
                game.CoverVersion = null;
                if (retro)
                {
                    game.PlatformName = game.PlatformName?.Trim() is { Length: > 0 } name ? name[..Math.Min(name.Length, 80)] : null;
                    game.Cover = RommProxy.IsMedia(game.Cover) ? game.Cover : null;
                }
                else
                    (game.Platform, game.PlatformName, game.Cover) = (null, null, null);
            }
            return (new ChallengeStore.Challenge { Kind = asked.Kind!, Goal = asked.Goal, Game = game, SetBy = admin }, null);
        }

        public static void MapChallengeEndpoints(this WebApplication app)
        {
            app.MapGet("/challenges", async (HttpRequest request, AdminCheck users, ChallengeStore challenges, CancellationToken ct) =>
            {
                var check = await users.CheckUserAsync(request, ct);
                if (!check.Allowed)
                    return Results.Json(new { error = check.Message }, statusCode: check.Status);
                if (!challenges.Enabled)
                    return Off();
                ChallengeStore.State state = challenges.Get();
                return Results.Json(new
                {
                    week = state.Week, startsAt = state.StartsAt, endsAt = state.EndsAt, challenge = state.Challenge,
                    board = state.Board.Select(Shown),
                    allTime = state.AllTime.Select(honor => new { userId = honor.UserId, username = honor.Username, completed = honor.Completed, first = honor.First }),
                    past = state.Past.Select(week => new { week = week.Id, startsAt = week.StartsAt, challenge = week.Challenge, winner = Shown(week.Winner), completed = week.Completed }),
                });
            });

            app.MapPut("/challenges/week", async (Chosen asked, HttpRequest request, AdminCheck admins, ChallengeStore challenges, CancellationToken ct) =>
            {
                var check = await admins.CheckAsync(request, ct);
                if (!check.Allowed)
                    return Results.Json(new { error = check.Message }, statusCode: check.Status);
                if (!challenges.Enabled)
                    return Off();
                var (challenge, error) = Checked(asked, check.User);
                if (challenge == null)
                    return Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);
                challenges.Set(challenge);
                return Results.NoContent();
            });

            app.MapPost("/challenges/me/plays", async (PlayedMinute played, HttpRequest request, AdminCheck users, ChallengeStore challenges, GameVaultProxy gameVault, RetroPlayerStore players, CancellationToken ct) =>
            {
                var check = await users.CheckUserAsync(request, ct);
                if (!check.Allowed)
                    return Results.Json(new { error = check.Message }, statusCode: check.Status);
                if (!challenges.Enabled)
                    return Off();
                if (played.GameIds is not { Count: >= 1 and <= 8 } games || games.Any(id => id <= 0))
                    return Results.Json(new { error = "A minute is played on 1 to 8 games." }, statusCode: StatusCodes.Status400BadRequest);
                await SeedAsync(check.Id, request, challenges, gameVault, players, ct);
                return Told(challenges.AddLibraryMinute(check.Id, check.User, games));
            });
        }
    }
}
