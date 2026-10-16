namespace GameVault.Uploader
{
    /// <summary>
    /// GET  /invite                               the page to send to a new player (download, sign in)
    /// GET  /status                               uploader version, free space
    /// GET  /uploads/{name}                       bytes received so far, whether the file exists
    /// PUT  /uploads/{name}?offset=N&amp;total=T  appends a chunk (at most 64 MB) at offset N
    /// POST /uploads/{name}/complete?size=S       makes the file visible to the GameVault server
    /// DELETE /uploads/{name}                     drops an unfinished upload
    /// GET  /profiles/{userId}                    the profile chosen by a player (any signed-in player)
    /// PUT  /profiles/{userId}                    saves it (the player themselves, or an administrator)
    /// GET  /remote-installs                      the games the signed-in player asked to install on their PC
    /// POST /remote-installs, DELETE /remote-installs/{gameId}   asks for one / takes it (the client) or drops it
    /// GET  /savefiles/{gameId}                   the cloud saves the server keeps for the signed-in player
    /// GET  /savefiles/{gameId}/{name}            one of them (to restore an older save)
    /// POST /auth/login, POST /auth/refresh       the GameVault sign-in for the web page (forwarded to the server)
    /// GET  /me, GET /games?search=               the signed-in user, the games already on the server (web page)
    /// Uploads need the Authorization header of a GameVault administrator, profiles the one of any GameVault user.
    /// </summary>
    public static class Endpoints
    {
        public static void MapUploadEndpoints(this WebApplication app)
        {
            // The page sent to a new player: download and sign in
            app.MapGet("/invite", (IWebHostEnvironment web) => Results.File(web.WebRootFileProvider.GetFileInfo("invite.html").CreateReadStream(), "text/html; charset=utf-8"));

            app.MapGet("/status", (UploadStore store, ProfileStore profiles, SaveStore saves, RemoteInstallStore remote) => Results.Json(new { status = "OK", version = UploaderOptions.Version, freeSpace = store.FreeSpace(), profiles = profiles.Enabled, saveHistory = saves.Enabled, remoteInstalls = remote.Enabled }));

            // "Install on my PC": the web page adds, the player's client takes (each player sees only their own list)
            app.MapGet("/remote-installs", async (HttpRequest request, AdminCheck users, RemoteInstallStore remote, CancellationToken ct) =>
            {
                var check = await users.CheckUserAsync(request, ct);
                if (!check.Allowed)
                    return Results.Json(new { error = check.Message }, statusCode: check.Status);
                if (!remote.Enabled)
                    return Results.Json(new { error = "Remote installs are not available on this server." }, statusCode: StatusCodes.Status503ServiceUnavailable);
                return Results.Json(remote.List(check.Id).Select(e => new { gameId = e.GameId, title = e.Title, requestedAt = e.RequestedAt }));
            });

            app.MapPost("/remote-installs", async (RemoteInstallRequest asked, HttpRequest request, AdminCheck users, RemoteInstallStore remote, CancellationToken ct) =>
            {
                var check = await users.CheckUserAsync(request, ct);
                if (!check.Allowed)
                    return Results.Json(new { error = check.Message }, statusCode: check.Status);
                if (asked.GameId <= 0)
                    return Results.Json(new { error = "Choose a game." }, statusCode: StatusCodes.Status400BadRequest);
                string title = (asked.Title ?? "").Trim();
                if (title.Length > 200)
                    title = title[..200];
                return remote.Add(check.Id, asked.GameId, title) switch
                {
                    RemoteInstallStore.AddResult.Added => Results.StatusCode(StatusCodes.Status201Created),
                    RemoteInstallStore.AddResult.AlreadyAsked => Results.NoContent(),
                    RemoteInstallStore.AddResult.Full => Results.Json(new { error = $"At most {RemoteInstallStore.MaxPerUser} games can wait." }, statusCode: StatusCodes.Status409Conflict),
                    _ => Results.Json(new { error = "Remote installs are not available on this server." }, statusCode: StatusCodes.Status503ServiceUnavailable),
                };
            });

            app.MapDelete("/remote-installs/{gameId:int}", async (int gameId, HttpRequest request, AdminCheck users, RemoteInstallStore remote, CancellationToken ct) =>
            {
                var check = await users.CheckUserAsync(request, ct);
                if (!check.Allowed)
                    return Results.Json(new { error = check.Message }, statusCode: check.Status);
                remote.Remove(check.Id, gameId);
                return Results.NoContent();
            });

            // The web page signs in with the GameVault account through here: same origin, nothing to allow on the server
            app.MapPost("/auth/login", async (LoginRequest login, HttpRequest request, GameVaultProxy gameVault, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(login.Username) || string.IsNullOrEmpty(login.Password))
                    return Results.Json(new { message = "Enter the user name and the password." }, statusCode: StatusCodes.Status400BadRequest);
                var answer = await gameVault.LoginAsync(login.Username.Trim(), login.Password, request.Headers.UserAgent.ToString(), ct);
                if (answer.Status != 200)
                    await Task.Delay(800, ct);// a little slower to guess passwords
                return Results.Content(answer.Body, "application/json", statusCode: answer.Status);
            });

            app.MapPost("/auth/refresh", async (HttpRequest request, GameVaultProxy gameVault, CancellationToken ct) =>
            {
                string? authorization = request.Headers.Authorization;
                if (string.IsNullOrEmpty(authorization))
                    return Results.Json(new { message = "Sign in again." }, statusCode: StatusCodes.Status401Unauthorized);
                var answer = await gameVault.RefreshAsync(authorization, ct);
                return Results.Content(answer.Body, "application/json", statusCode: answer.Status);
            });

            app.MapGet("/me", async (HttpRequest request, AdminCheck users, CancellationToken ct) =>
            {
                var check = await users.CheckUserAsync(request, ct);
                return check.Allowed
                    ? Results.Json(new { id = check.Id, username = check.User, admin = check.Admin })
                    : Results.Json(new { error = check.Message }, statusCode: check.Status);
            });

            app.MapGet("/games", async (string? search, HttpRequest request, AdminCheck users, GameVaultProxy gameVault, CancellationToken ct) =>
            {
                var check = await users.CheckUserAsync(request, ct);
                if (!check.Allowed)
                    return Results.Json(new { error = check.Message }, statusCode: check.Status);
                if (string.IsNullOrWhiteSpace(search) || search.Trim().Length < 2)
                    return Results.Json(Array.Empty<object>());
                var games = await gameVault.SearchGamesAsync(request.Headers.Authorization!, search.Trim(), ct);
                return Results.Json(games.Select(g => new { id = g.Id, title = g.Title, version = g.Version, year = g.Year }));
            });

            app.MapGet("/savefiles/{gameId:int}", async (int gameId, HttpRequest request, AdminCheck users, SaveStore saves, CancellationToken ct) =>
            {
                var check = await users.CheckUserAsync(request, ct);
                if (!check.Allowed)
                    return Results.Json(new { error = check.Message }, statusCode: check.Status);
                if (!saves.Enabled)
                    return Results.Json(new { error = "The save history is not available on this server." }, statusCode: StatusCodes.Status503ServiceUnavailable);
                return Results.Json(saves.List(check.Id, gameId).Select(s => new { name = s.Name, savedAt = s.SavedAt, size = s.Size, installationId = s.InstallationId }));
            });

            app.MapGet("/savefiles/{gameId:int}/{name}", async (int gameId, string name, HttpRequest request, AdminCheck users, SaveStore saves, CancellationToken ct) =>
            {
                var check = await users.CheckUserAsync(request, ct);
                if (!check.Allowed)
                    return Results.Json(new { error = check.Message }, statusCode: check.Status);
                // Only the signed-in player's own saves
                string? path = saves.PathOf(check.Id, gameId, name);
                return path == null ? Results.NotFound() : Results.File(path, "application/zip", name);
            });

            app.MapGet("/profiles/{userId:int}", async (int userId, HttpRequest request, AdminCheck users, ProfileStore profiles, CancellationToken ct) =>
            {
                var check = await users.CheckUserAsync(request, ct);
                if (!check.Allowed)
                    return Results.Json(new { error = check.Message }, statusCode: check.Status);
                if (!profiles.Enabled)
                    return Results.Json(new { error = "Profiles are not kept by this server." }, statusCode: StatusCodes.Status503ServiceUnavailable);
                string? profile = profiles.Get(userId);
                return profile == null ? Results.NotFound() : Results.Content(profile, "application/json");
            });

            app.MapPut("/profiles/{userId:int}", async (int userId, HttpRequest request, AdminCheck users, ProfileStore profiles, CancellationToken ct) =>
            {
                var check = await users.CheckUserAsync(request, ct);
                if (!check.Allowed)
                    return Results.Json(new { error = check.Message }, statusCode: check.Status);
                if (check.Id != userId && !check.Admin)
                    return Results.Json(new { error = "Only this player or an administrator can change this profile." }, statusCode: StatusCodes.Status403Forbidden);
                return await profiles.SaveAsync(userId, request.Body, ct) switch
                {
                    ProfileStore.SaveResult.Saved => Results.NoContent(),
                    ProfileStore.SaveResult.TooLarge => Results.Json(new { error = $"A profile can have at most {ProfileStore.MaxSize} bytes." }, statusCode: StatusCodes.Status413PayloadTooLarge),
                    ProfileStore.SaveResult.Disabled => Results.Json(new { error = "Profiles are not kept by this server." }, statusCode: StatusCodes.Status503ServiceUnavailable),
                    _ => Results.Json(new { error = "A profile is a JSON object." }, statusCode: StatusCodes.Status400BadRequest),
                };
            });

            app.MapGet("/uploads/{name}", async (string name, HttpRequest request, AdminCheck admins, UploadStore store, CancellationToken ct) =>
                await Guarded(name, request, admins, ct, () =>
                {
                    var state = store.GetState(name);
                    return Results.Json(new { received = state.Received, exists = state.Exists, freeSpace = state.FreeSpace });
                }));

            app.MapPut("/uploads/{name}", async (string name, long offset, long? total, HttpRequest request, AdminCheck admins, UploadStore store, ILoggerFactory logs, CancellationToken ct) =>
                await GuardedAsync(name, request, admins, ct, async user =>
                {
                    if (offset == 0)
                        logs.CreateLogger("Uploads").LogInformation("{User} uploads {Name} ({Total} bytes)", user, name, total);
                    var (result, received) = await store.AppendAsync(name, offset, total, request.Body, ct);
                    return result switch
                    {
                        UploadStore.AppendResult.Appended => Results.Json(new { received }),
                        UploadStore.AppendResult.WrongOffset => Results.Json(new { received, error = "The upload continues at another position." }, statusCode: StatusCodes.Status409Conflict),
                        UploadStore.AppendResult.NoSpace => Results.Json(new { received, error = "Not enough free space on the server." }, statusCode: StatusCodes.Status507InsufficientStorage),
                        _ => Results.Json(new { received, error = $"A chunk can have at most {UploadStore.MaxChunkSize} bytes." }, statusCode: StatusCodes.Status413PayloadTooLarge),
                    };
                }));

            app.MapPost("/uploads/{name}/complete", async (string name, long size, bool? overwrite, HttpRequest request, AdminCheck admins, UploadStore store, ILoggerFactory logs, CancellationToken ct) =>
                await GuardedAsync(name, request, admins, ct, user =>
                {
                    var result = store.Complete(name, size, overwrite == true);
                    if (result == UploadStore.CompleteResult.Completed)
                        logs.CreateLogger("Uploads").LogInformation("{User} published {Name}", user, name);
                    return Task.FromResult(result switch
                    {
                        UploadStore.CompleteResult.Completed => Results.Json(new { name }, statusCode: StatusCodes.Status201Created),
                        UploadStore.CompleteResult.Exists => Results.Json(new { error = "A file with this name already exists on the server." }, statusCode: StatusCodes.Status409Conflict),
                        UploadStore.CompleteResult.WrongSize => Results.Json(new { error = "The upload is not complete." }, statusCode: StatusCodes.Status409Conflict),
                        _ => Results.Json(new { error = "There is no upload with this name." }, statusCode: StatusCodes.Status404NotFound),
                    });
                }));

            app.MapDelete("/uploads/{name}", async (string name, HttpRequest request, AdminCheck admins, UploadStore store, CancellationToken ct) =>
                await Guarded(name, request, admins, ct, () =>
                {
                    store.Cancel(name);
                    return Results.NoContent();
                }));
        }

        private static Task<IResult> Guarded(string name, HttpRequest request, AdminCheck admins, CancellationToken ct, Func<IResult> action) =>
            GuardedAsync(name, request, admins, ct, _ => Task.FromResult(action()));

        private static async Task<IResult> GuardedAsync(string name, HttpRequest request, AdminCheck admins, CancellationToken ct, Func<string, Task<IResult>> action)
        {
            if (!UploadStore.IsValidName(name, out string error))
                return Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);
            var check = await admins.CheckAsync(request, ct);
            if (!check.Allowed)
                return Results.Json(new { error = check.Message }, statusCode: check.Status);
            return await action(check.User);
        }
    }

    public sealed record LoginRequest(string? Username, string? Password);
    public sealed record RemoteInstallRequest(int GameId, string? Title);
}
