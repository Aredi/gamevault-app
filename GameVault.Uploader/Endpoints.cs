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
    /// POST /remote/devices, GET /remote/devices  a client of the signed-in player says it is open / the player's PCs
    /// GET  /remote/installs[?device=]            the player's "install on my PC" requests (or the ones a PC has to take)
    /// POST /remote/installs                      asks one of the player's PCs to install a game
    /// PUT  /remote/installs/{id}, DELETE ...     the PC says what became of it / the player drops it
    /// GET  /game-requests, POST /game-requests   the signed-in player's game requests / asks for a game (RomM-ComM bot)
    /// GET  /savefiles/{gameId}                   the cloud saves the server keeps for the signed-in player
    /// GET  /savefiles/{gameId}/{name}            one of them (to restore an older save)
    /// POST /auth/login, POST /auth/refresh       the GameVault sign-in for the web page (forwarded to the server)
    /// GET  /me, GET /games?search=, /games/{id}  the signed-in user, the games already on the server (web page)
    /// Uploads need the Authorization header of a GameVault administrator, profiles the one of any GameVault user.
    /// </summary>
    public static class Endpoints
    {
        public static void MapUploadEndpoints(this WebApplication app)
        {
            // The page sent to a new player: download and sign in
            app.MapGet("/invite", (IWebHostEnvironment web) => Results.File(web.WebRootFileProvider.GetFileInfo("invite.html").CreateReadStream(), "text/html; charset=utf-8"));

            app.MapGet("/status", (UploadStore store, ProfileStore profiles, SaveStore saves, RemoteInstallStore remote, GameRequestsProxy requests, RommProxy romm, RetroSaveStore retroSaves) => Results.Json(new { status = "OK", version = UploaderOptions.Version, freeSpace = store.FreeSpace(), profiles = profiles.Enabled, saveHistory = saves.Enabled, remoteInstalls = remote.Enabled, gameRequests = requests.Enabled, retro = romm.Enabled, retroSaves = retroSaves.Enabled }));

            // Game requests, kept by the RomM-ComM bot with the ones made in Discord
            app.MapGet("/game-requests", async (HttpRequest request, AdminCheck users, GameRequestsProxy requests, CancellationToken ct) =>
            {
                var check = await users.CheckUserAsync(request, ct);
                if (!check.Allowed)
                    return Results.Json(new { error = check.Message }, statusCode: check.Status);
                if (!requests.Enabled)
                    return Results.Json(new { error = "Game requests are not available on this server." }, statusCode: StatusCodes.Status503ServiceUnavailable);
                var answer = await requests.ListAsync(check.Id, check.User, ct);
                return Results.Content(answer.Body, "application/json", statusCode: answer.Status);
            });

            app.MapPost("/game-requests", async (GameRequest asked, HttpRequest request, AdminCheck users, GameRequestsProxy requests, CancellationToken ct) =>
            {
                var check = await users.CheckUserAsync(request, ct);
                if (!check.Allowed)
                    return Results.Json(new { error = check.Message }, statusCode: check.Status);
                if (!requests.Enabled)
                    return Results.Json(new { error = "Game requests are not available on this server." }, statusCode: StatusCodes.Status503ServiceUnavailable);
                string game = (asked.Game ?? "").Trim();
                if (game.Length is 0 or > 200)
                    return Results.Json(new { error = "Enter the name of the game." }, statusCode: StatusCodes.Status400BadRequest);
                string? details = asked.Details?.Trim() is { Length: > 0 } d ? d[..Math.Min(d.Length, 1000)] : null;
                var answer = await requests.AskAsync(check.Id, check.User, game, details, ct);
                return Results.Content(answer.Body, "application/json", statusCode: answer.Status);
            });

            // The games of the other platforms, from RomM, for every signed-in player
            app.MapGet("/retro/platforms", (HttpContext context, AdminCheck users, RommProxy romm, CancellationToken ct) =>
                FromRommAsync(context, users, romm, ct, async () => Results.Json(await romm.PlatformsAsync(ct))));
            app.MapGet("/retro/platforms/{platformId:int}/games", (int platformId, HttpContext context, AdminCheck users, RommProxy romm, CancellationToken ct) =>
                FromRommAsync(context, users, romm, ct, async () => Results.Json(await romm.GamesAsync(platformId, ct))));
            app.MapGet("/retro/covers/{**path}", (string path, HttpContext context, AdminCheck users, RommProxy romm, CancellationToken ct) =>
                FromRommAsync(context, users, romm, ct, async () => { await romm.CoverAsync(path, context.Response, ct); return Results.Empty; }));
            app.MapGet("/retro/platforms/{platformId:int}/firmware", (int platformId, HttpContext context, AdminCheck users, RommProxy romm, CancellationToken ct) =>
                FromRommAsync(context, users, romm, ct, async () => Results.Json(await romm.FirmwareAsync(platformId, ct))));
            app.MapGet("/retro/firmware/{id:int}/content", (int id, HttpContext context, AdminCheck users, RommProxy romm, CancellationToken ct) =>
                FromRommAsync(context, users, romm, ct, async () => { await romm.FirmwareContentAsync(id, context.Response, ct); return Results.Empty; }));

            // The saves of the retro games: each player has their own, kept here (RomM only sees the service's account)
            app.MapGet("/retro/saves/{set}/info", (string set, HttpRequest request, AdminCheck users, RetroSaveStore saves, CancellationToken ct) =>
                AsSaverAsync(set, request, users, saves, ct, id =>
                    saves.Get(id, set) is { } found ? Results.Json(new { savedAt = found.SavedAt }) : Results.NotFound()));
            app.MapGet("/retro/saves/{set}", (string set, HttpRequest request, AdminCheck users, RetroSaveStore saves, CancellationToken ct) =>
                AsSaverAsync(set, request, users, saves, ct, id =>
                    saves.Get(id, set) is { } found ? Results.File(found.File, "application/zip", $"{set}.zip") : Results.NotFound()));
            app.MapPut("/retro/saves/{set}", async (string set, HttpContext context, AdminCheck users, RetroSaveStore saves, CancellationToken ct) =>
            {
                // Bigger than the upload chunks Kestrel is limited to
                if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                    limit.MaxRequestBodySize = RetroSaveStore.MaxSize + 1;
                IResult? refused = await RefuseSaverAsync(set, context.Request, users, saves, ct);
                if (refused != null)
                    return refused;
                int id = (await users.CheckUserAsync(context.Request, ct)).Id;
                return await saves.SaveAsync(id, set, context.Request.Body, ct) is long savedAt
                    ? Results.Json(new { savedAt })
                    : Results.Json(new { error = $"Saves can have at most {RetroSaveStore.MaxSize >> 20} MB." }, statusCode: StatusCodes.Status413PayloadTooLarge);
            });
            app.MapGet("/retro/games/{id:int}/content", (int id, HttpContext context, AdminCheck users, RommProxy romm, CancellationToken ct) =>
                FromRommAsync(context, users, romm, ct, async () => { await romm.ContentAsync(id, context.Request, context.Response, ct); return Results.Empty; }));

            // "Install on my PC": the player's clients say they are open, the web page asks one of them for a game,
            // that client takes it and says what became of it (each player sees only their own PCs and requests)
            app.MapPost("/remote/devices", (RemoteDevice device, HttpRequest request, AdminCheck users, RemoteInstallStore remote, CancellationToken ct) =>
                AsPlayerAsync(request, users, remote, ct, id =>
                {
                    string name = (device.Name ?? "").Trim();
                    if (!RemoteInstallStore.IsValidDeviceId(device.Id) || name.Length == 0)
                        return Results.Json(new { error = "A device needs an id and a name." }, statusCode: StatusCodes.Status400BadRequest);
                    remote.Seen(id, device.Id!, name[..Math.Min(name.Length, 64)]);
                    return Results.NoContent();
                }));

            app.MapGet("/remote/devices", (HttpRequest request, AdminCheck users, RemoteInstallStore remote, CancellationToken ct) =>
                AsPlayerAsync(request, users, remote, ct, id =>
                    Results.Json(remote.Devices(id).Select(d => new { id = d.Id, name = d.Name, lastSeen = d.LastSeen, online = d.Online }))));

            // The administrators see when each player last had the client open
            app.MapGet("/users/last-seen", async (HttpRequest request, AdminCheck admins, RemoteInstallStore remote, CancellationToken ct) =>
            {
                var check = await admins.CheckAsync(request, ct);
                if (!check.Allowed)
                    return Results.Json(new { error = check.Message }, statusCode: check.Status);
                return remote.Enabled ? Results.Json(remote.LastSeen()) : Results.Json(new { error = "Remote installs are not available on this server." }, statusCode: StatusCodes.Status503ServiceUnavailable);
            });

            app.MapGet("/remote/installs", (string? device, HttpRequest request, AdminCheck users, RemoteInstallStore remote, CancellationToken ct) =>
                AsPlayerAsync(request, users, remote, ct, id =>
                    Results.Json(remote.List(id, device).Select(e => new
                    {
                        id = e.Id, gameId = e.GameId, title = e.Title, deviceId = e.DeviceId, deviceName = e.DeviceName,
                        status = e.Status, message = e.Message, requestedAt = e.RequestedAt, updatedAt = e.UpdatedAt,
                    }))));

            app.MapPost("/remote/installs", (RemoteInstallRequest asked, HttpRequest request, AdminCheck users, RemoteInstallStore remote, CancellationToken ct) =>
                AsPlayerAsync(request, users, remote, ct, id =>
                {
                    if (asked.GameId <= 0)
                        return Results.Json(new { error = "Choose a game." }, statusCode: StatusCodes.Status400BadRequest);
                    string title = (asked.Title ?? "").Trim();
                    if (title.Length > 200)
                        title = title[..200];
                    return remote.Add(id, asked.GameId, title, asked.DeviceId ?? "") switch
                    {
                        RemoteInstallStore.AddResult.Added => Results.StatusCode(StatusCodes.Status201Created),
                        RemoteInstallStore.AddResult.AlreadyAsked => Results.NoContent(),
                        RemoteInstallStore.AddResult.Full => Results.Json(new { error = $"At most {RemoteInstallStore.MaxPerUser} games can wait." }, statusCode: StatusCodes.Status409Conflict),
                        RemoteInstallStore.AddResult.UnknownDevice => Results.Json(new { error = "Choose one of your PCs." }, statusCode: StatusCodes.Status400BadRequest),
                        _ => Results.Json(new { error = "Remote installs are not available on this server." }, statusCode: StatusCodes.Status503ServiceUnavailable),
                    };
                }));

            // The PC says what became of a request: started, or failed with the reason
            app.MapPut("/remote/installs/{installId}", (string installId, RemoteInstallReport report, HttpRequest request, AdminCheck users, RemoteInstallStore remote, CancellationToken ct) =>
                AsPlayerAsync(request, users, remote, ct, id =>
                {
                    if (report.Status is not (RemoteInstallStore.Started or RemoteInstallStore.Failed))
                        return Results.Json(new { error = "The status is started or failed." }, statusCode: StatusCodes.Status400BadRequest);
                    string? message = report.Message?.Trim() is { Length: > 0 } m ? m[..Math.Min(m.Length, 300)] : null;
                    return remote.Report(id, installId, report.Status!, message) ? Results.NoContent() : Results.NotFound();
                }));

            app.MapDelete("/remote/installs/{installId}", (string installId, HttpRequest request, AdminCheck users, RemoteInstallStore remote, CancellationToken ct) =>
                AsPlayerAsync(request, users, remote, ct, id =>
                {
                    remote.Remove(id, installId);
                    return Results.NoContent();
                }));

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

            app.MapGet("/games/{id:int}", async (int id, HttpRequest request, AdminCheck users, GameVaultProxy gameVault, CancellationToken ct) =>
            {
                var check = await users.CheckUserAsync(request, ct);
                if (!check.Allowed)
                    return Results.Json(new { error = check.Message }, statusCode: check.Status);
                var game = await gameVault.GetGameAsync(request.Headers.Authorization!, id, ct);
                return game == null ? Results.NotFound() : Results.Json(new { id = game.Id, title = game.Title });
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

        /// <summary>Remote installs of the signed-in player: checks the sign-in and that the service keeps them.</summary>
        private static async Task<IResult> AsPlayerAsync(HttpRequest request, AdminCheck users, RemoteInstallStore remote, CancellationToken ct, Func<int, IResult> action)
        {
            var check = await users.CheckUserAsync(request, ct);
            if (!check.Allowed)
                return Results.Json(new { error = check.Message }, statusCode: check.Status);
            if (!remote.Enabled)
                return Results.Json(new { error = "Remote installs are not available on this server." }, statusCode: StatusCodes.Status503ServiceUnavailable);
            return action(check.Id);
        }

        private static async Task<IResult?> RefuseSaverAsync(string set, HttpRequest request, AdminCheck users, RetroSaveStore saves, CancellationToken ct)
        {
            var check = await users.CheckUserAsync(request, ct);
            if (!check.Allowed)
                return Results.Json(new { error = check.Message }, statusCode: check.Status);
            if (!saves.Enabled)
                return Results.Json(new { error = "Retro saves are not kept by this server." }, statusCode: StatusCodes.Status503ServiceUnavailable);
            return RetroSaveStore.IsValidSet(set) ? null : Results.Json(new { error = "Unknown set of saves." }, statusCode: StatusCodes.Status400BadRequest);
        }

        private static async Task<IResult> AsSaverAsync(string set, HttpRequest request, AdminCheck users, RetroSaveStore saves, CancellationToken ct, Func<int, IResult> action) =>
            await RefuseSaverAsync(set, request, users, saves, ct) ?? action((await users.CheckUserAsync(request, ct)).Id);

        private static async Task<IResult> FromRommAsync(HttpContext context, AdminCheck users, RommProxy romm, CancellationToken ct, Func<Task<IResult>> action)
        {
            var check = await users.CheckUserAsync(context.Request, ct);
            if (!check.Allowed)
                return Results.Json(new { error = check.Message }, statusCode: check.Status);
            if (!romm.Enabled)
                return Results.Json(new { error = "The games of the other platforms are not available on this server." }, statusCode: StatusCodes.Status503ServiceUnavailable);
            try
            {
                return await action();
            }
            catch (RommProxy.RommException ex) when (!context.Response.HasStarted)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        }

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
    public sealed record RemoteInstallRequest(int GameId, string? Title, string? DeviceId);
    public sealed record RemoteInstallReport(string? Status, string? Message);
    public sealed record RemoteDevice(string? Id, string? Name);
    public sealed record GameRequest(string? Game, string? Details);
}
