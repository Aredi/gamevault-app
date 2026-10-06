namespace GameVault.Uploader
{
    /// <summary>
    /// GET  /status                               uploader version, free space
    /// GET  /uploads/{name}                       bytes received so far, whether the file exists
    /// PUT  /uploads/{name}?offset=N&amp;total=T  appends a chunk (at most 64 MB) at offset N
    /// POST /uploads/{name}/complete?size=S       makes the file visible to the GameVault server
    /// DELETE /uploads/{name}                     drops an unfinished upload
    /// Everything except /status needs the Authorization header of a GameVault administrator.
    /// </summary>
    public static class Endpoints
    {
        public static void MapUploadEndpoints(this WebApplication app)
        {
            app.MapGet("/status", (UploadStore store) => Results.Json(new { status = "OK", version = UploaderOptions.Version, freeSpace = store.FreeSpace() }));

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
}
