using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using gamevault.Models;

namespace GameVault.UiTests
{
    /// <summary>
    /// The parts of the GameVault server API the client needs: login, users/me, games, downloads (with Range),
    /// progresses. Games and their files are added by the tests; every request is recorded.
    /// </summary>
    public sealed class FakeGameVaultServer : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly CancellationTokenSource stop = new();
        private readonly ConcurrentDictionary<int, (Game Game, byte[] File)> games = new();

        public string Url { get; }
        /// <summary>Images served by /api/media/{id} (covers, backgrounds, screenshots).</summary>
        public ConcurrentDictionary<int, byte[]> Media { get; } = new();
        /// <summary>Players returned by /api/users (the admin is user 1); empty: only the admin.</summary>
        public ConcurrentDictionary<int, User> Users { get; } = new();
        /// <summary>Play data returned by /api/progresses.</summary>
        public ConcurrentQueue<Progress> Progresses { get; } = new();
        /// <summary>The newest save of each game: GameVault names it "&lt;upload ms&gt;_&lt;installation id&gt;.zip".</summary>
        public ConcurrentDictionary<int, (string FileName, byte[] Data)> Saves { get; } = new();
        public ConcurrentQueue<string> Requests { get; } = new();
        public ConcurrentQueue<string> RangeHeaders { get; } = new();

        /// <summary>The next download of this game ends after this many bytes (the connection closes early).</summary>
        public ConcurrentDictionary<int, long> CutNextDownloadAfter { get; } = new();
        /// <summary>The next download of this game sends damaged bytes (same size), like a disk or network fault.</summary>
        public ConcurrentDictionary<int, bool> DamageNextDownload { get; } = new();
        /// <summary>
        /// Ranges answered like standard HTTP servers (206 + Content-Range). By default they are answered like the
        /// real GameVault server: 200 with the part only, its size in X-Download-Size and no Content-Range.
        /// </summary>
        public bool StandardRanges { get; set; }
        /// <summary>How long images take to arrive (a slow server or connection).</summary>
        public TimeSpan MediaDelay { get; set; } = TimeSpan.Zero;
        /// <summary>The games the "igdb" provider knows: its search returns those sharing a word with the query.</summary>
        public ConcurrentBag<MinimalGame> ProviderCatalog { get; } = new();
        /// <summary>The SanctuaryVault service part: the players' profiles (JSON) it keeps.</summary>
        public ConcurrentDictionary<int, string> Profiles { get; } = new();
        /// <summary>The SanctuaryVault service part: the signed-in player's cloud saves per game (file name → content), any order.</summary>
        public ConcurrentDictionary<int, ConcurrentDictionary<string, byte[]>> SaveVersions { get; } = new();
        /// <summary>"Install on my PC": the PCs that said they are open (id, name) and the requests (by request id).</summary>
        public ConcurrentDictionary<string, string> RemoteDevices { get; } = new();
        public ConcurrentDictionary<string, RemoteInstall> RemoteInstalls { get; } = new();
        public sealed record RemoteInstall(int GameId, string Title, string DeviceId, string Status = "pending", string? Message = null);
        /// <summary>Game requests sent through the service (to the RomM-ComM bot): name of the game.</summary>
        public ConcurrentQueue<string> GameRequests { get; } = new();
        public static readonly byte[] RetroRom = Encoding.ASCII.GetBytes("NES\x1A-test-rom");
        /// <summary>A PS1 game in two files, as RomM zips them.</summary>
        public static readonly byte[] RetroCdGame = Zip(("Castlevania.cue", "FILE \"Castlevania.bin\" BINARY"), ("Castlevania.bin", "track"));
        /// <summary>The player's retro saves: (zip, when saved).</summary>
        public (byte[] Zip, long SavedAt)? RetroSaves { get; set; }

        public static byte[] Zip(params (string Name, string Text)[] files)
        {
            var stream = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
                foreach (var (name, text) in files)
                    using (var writer = new StreamWriter(zip.CreateEntry(name).Open()))
                        writer.Write(text);
            return stream.ToArray();
        }
        /// <summary>Status the bot gives a request (pending when not set): game, status.</summary>
        public ConcurrentDictionary<string, string> GameRequestStatuses { get; } = new();
        /// <summary>The mappings received (PUT /api/games/{id}): game, provider, provider id.</summary>
        public ConcurrentQueue<(int GameId, string Slug, string ProviderId)> Mappings { get; } = new();
        /// <summary>Downloads ignore the Range header and always send the whole file with 200.</summary>
        public bool IgnoreRange { get; set; }
        /// <summary>Delay between two chunks of a download, to keep downloads running for a while.</summary>
        public TimeSpan ChunkDelay { get; set; } = TimeSpan.Zero;

        public FakeGameVaultServer()
        {
            int port = FreePort();
            Url = $"http://127.0.0.1:{port}";
            listener.Prefixes.Add(Url + "/");
            listener.Start();
            _ = Task.Run(Loop);
        }

        public Game AddGame(int id, string title, GameType type, byte[] file, string fileName, string? version = null)
        {
            var game = new Game
            {
                ID = id,
                Title = title,
                SortTitle = title.ToLowerInvariant(),
                Type = type,
                Path = $"/files/{fileName}",
                Size = file.Length.ToString(),
                Version = version,
                EntityVersion = 1,
                Metadata = new gamevault.Models.GameMetadata { Title = title },
                Progresses = new List<Progress>(),
            };
            games[id] = (game, file);
            return game;
        }

        public static User Admin => new() { ID = 1, Username = "admin", Role = PERMISSION_ROLE.ADMIN, Activated = true };

        private static int FreePort()
        {
            var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            int port = ((IPEndPoint)socket.LocalEndpoint).Port;
            socket.Stop();
            return port;
        }

        private async Task Loop()
        {
            while (!stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch { return; }
                _ = Task.Run(() => Handle(context));
            }
        }

        private static bool HasPassword(HttpListenerRequest request)
        {
            string? header = request.Headers["Authorization"];
            if (header == null || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                return false;
            string[] credentials = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..].Trim())).Split(':', 2);
            return credentials.Length == 2 && credentials[1] != "";
        }

        private async Task Handle(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            string path = request.Url!.AbsolutePath;
            Requests.Enqueue($"{request.HttpMethod} {request.Url.PathAndQuery}");
            try
            {
                Match match;
                if (path == "/api/auth/basic/login" && !HasPassword(request))
                    response.StatusCode = 401;// as the real server, which refuses an empty password at once
                else if (path == "/api/auth/basic/login")
                    await Json(response, new { id = "1", access_token = "access", refresh_token = "refresh" });
                else if (path == "/api/auth/refresh" && request.Headers["Authorization"] == "Bearer expired")
                    response.StatusCode = 401;
                else if (path == "/api/auth/refresh")
                    await Json(response, new { id = "1", access_token = "access", refresh_token = "refresh" });
                else if (path == "/api/users/me")
                    await Json(response, Users.TryGetValue(1, out User? me) ? me : Admin);
                else if (path == "/api/users")
                    await Json(response, Users.IsEmpty ? new[] { Admin } : Users.Values.OrderBy(u => u.ID).ToArray());
                else if ((match = Regex.Match(path, @"^/api/users/(\d+)$")).Success && Users.TryGetValue(int.Parse(match.Groups[1].Value), out User? user))
                    await Json(response, user);
                // RomM through the SanctuaryVault service: a NES game, and a platform the client has no emulator for
                else if (path == "/retro/platforms")
                    await Json(response, new object[] { new { id = 3, slug = "nes", name = "NES", count = 1 }, new { id = 19, slug = "psx", name = "PlayStation", count = 1 }, new { id = 9, slug = "switch", name = "Switch", count = 4 } });
                else if (path == "/retro/platforms/19/games")
                    await Json(response, new[] { new { id = 50, name = "Castlevania", fileName = "Castlevania", size = RetroCdGame.LongLength, cover = (string?)null, summary = (string?)null, multi = true } });
                else if (path == "/retro/games/50/content")
                {
                    response.ContentType = "application/zip";
                    response.AddHeader("Content-Disposition", "attachment; filename*=UTF-8''Castlevania.zip");
                    response.ContentLength64 = RetroCdGame.LongLength;
                    await response.OutputStream.WriteAsync(RetroCdGame);
                }
                else if (path == "/retro/platforms/19/firmware")
                    await Json(response, new[] { new { id = 7, fileName = "scph5501.bin", size = 4 } });
                else if (path == "/retro/firmware/7/content")
                    await response.OutputStream.WriteAsync(new byte[] { 1, 2, 3, 4 });
                else if (path == "/retro/saves/retroarch/info")
                {
                    if (RetroSaves is { } saves)
                        await Json(response, new { savedAt = saves.SavedAt });
                    else
                        response.StatusCode = 404;
                }
                else if (path == "/retro/saves/retroarch" && request.HttpMethod == "PUT")
                {
                    var body = new MemoryStream();
                    await request.InputStream.CopyToAsync(body);
                    RetroSaves = (body.ToArray(), (RetroSaves?.SavedAt ?? 0) + 1000);
                    await Json(response, new { savedAt = RetroSaves.Value.SavedAt });
                }
                else if (path == "/retro/saves/retroarch" && RetroSaves is { } current)
                    await response.OutputStream.WriteAsync(current.Zip);
                else if (path == "/retro/platforms/3/games")
                    await Json(response, new[] { new { id = 40, name = "Zelda", fileName = "Zelda (E).nes", size = RetroRom.LongLength, cover = (string?)null, summary = "A sword" } });
                else if (path == "/retro/games/40/content")
                {
                    response.ContentType = "application/octet-stream";
                    response.AddHeader("Content-Disposition", "attachment; filename*=UTF-8''Zelda%20%28E%29.nes");
                    response.ContentLength64 = RetroRom.LongLength;
                    await response.OutputStream.WriteAsync(RetroRom);
                }
                else if (path == "/status")
                    await Json(response, new { status = "OK", version = "1.0.0", freeSpace = 1L << 40, profiles = true, saveHistory = true });
                else if ((match = Regex.Match(path, @"^/savefiles/(\d+)$")).Success)
                    await Json(response, (SaveVersions.TryGetValue(int.Parse(match.Groups[1].Value), out var versions) ? versions : new())
                        .Select(v => (Name: v.Key, Size: v.Value.LongLength, Parts: v.Key.Split('_', '.')))
                        .OrderByDescending(v => long.Parse(v.Parts[0]))
                        .Select(v => new { name = v.Name, savedAt = long.Parse(v.Parts[0]), size = v.Size, installationId = v.Parts[1] }));
                else if ((match = Regex.Match(path, @"^/savefiles/(\d+)/([^/]+)$")).Success
                         && SaveVersions.TryGetValue(int.Parse(match.Groups[1].Value), out var gameSaves) && gameSaves.TryGetValue(Uri.UnescapeDataString(match.Groups[2].Value), out byte[]? save))
                {
                    response.ContentType = "application/zip";
                    await response.OutputStream.WriteAsync(save);
                }
                else if ((match = Regex.Match(path, @"^/profiles/(\d+)$")).Success)
                {
                    int userId = int.Parse(match.Groups[1].Value);
                    if (request.HttpMethod == "PUT")
                    {
                        Profiles[userId] = await new StreamReader(request.InputStream).ReadToEndAsync();
                        response.StatusCode = 204;
                    }
                    else if (Profiles.TryGetValue(userId, out string? profile))
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(profile);
                        response.ContentType = "application/json";
                        await response.OutputStream.WriteAsync(bytes);
                    }
                    else
                        response.StatusCode = 404;
                }
                else if (path == "/game-requests" && request.HttpMethod == "POST")
                {
                    using JsonDocument asked = JsonDocument.Parse(await new StreamReader(request.InputStream).ReadToEndAsync());
                    string game = asked.RootElement.GetProperty("game").GetString()!;
                    bool merged = GameRequests.Contains(game);
                    if (!merged)
                        GameRequests.Enqueue(game);
                    response.StatusCode = merged ? 200 : 201;
                    await Json(response, new { result = merged ? "merged" : "created" });
                }
                else if (path == "/game-requests")
                    await Json(response, GameRequests.Select((g, i) => new { id = i + 1, game_name = g, status = GameRequestStatuses.GetValueOrDefault(g, "pending") }).ToArray());
                else if (path == "/remote/devices" && request.HttpMethod == "POST")
                {
                    using JsonDocument device = JsonDocument.Parse(await new StreamReader(request.InputStream).ReadToEndAsync());
                    RemoteDevices[device.RootElement.GetProperty("id").GetString()!] = device.RootElement.GetProperty("name").GetString()!;
                    response.StatusCode = 204;
                }
                else if (path == "/remote/installs")
                {
                    string? device = request.QueryString["device"];
                    await Json(response, RemoteInstalls.Where(r => r.Value.DeviceId == device && r.Value.Status == "pending")
                        .Select(r => new { id = r.Key, gameId = r.Value.GameId, title = r.Value.Title }).ToArray());
                }
                else if ((match = Regex.Match(path, @"^/remote/installs/(\w+)$")).Success && request.HttpMethod == "PUT" && RemoteInstalls.TryGetValue(match.Groups[1].Value, out var asked))
                {
                    using JsonDocument report = JsonDocument.Parse(await new StreamReader(request.InputStream).ReadToEndAsync());
                    RemoteInstalls[match.Groups[1].Value] = asked with
                    {
                        Status = report.RootElement.GetProperty("status").GetString()!,
                        Message = report.RootElement.TryGetProperty("message", out var said) && said.ValueKind == JsonValueKind.String ? said.GetString() : null,
                    };
                    response.StatusCode = 204;
                }
                else if (path == "/api/status")
                    await Json(response, new { status = "HEALTHY", version = "17.0.1", registration_enabled = true, required_registration_fields = Array.Empty<string>(), available_authentication_methods = new[] { "basic" } });
                else if ((match = Regex.Match(path, @"^/api/games/(\d+)/download$")).Success)
                    await Download(context, int.Parse(match.Groups[1].Value));
                else if ((match = Regex.Match(path, @"^/api/games/(\d+)$")).Success && request.HttpMethod == "PUT" && games.TryGetValue(int.Parse(match.Groups[1].Value), out var mapped))
                    await Json(response, Map(mapped.Game, await new StreamReader(request.InputStream).ReadToEndAsync()));
                else if ((match = Regex.Match(path, @"^/api/games/(\d+)$")).Success && games.TryGetValue(int.Parse(match.Groups[1].Value), out var entry))
                    await Json(response, entry.Game);
                else if (path == "/api/metadata/providers")
                    await Json(response, new[] { new { slug = "igdb", name = "IGDB", priority = 10, enabled = true } });
                else if (path == "/api/metadata/providers/igdb/search")
                    await Json(response, SearchProvider(request.QueryString["query"] ?? ""));
                else if (path == "/api/games")
                    await Json(response, GameList(request));
                else if ((match = Regex.Match(path, @"^/api/media/(\d+)$")).Success && Media.TryGetValue(int.Parse(match.Groups[1].Value), out byte[]? image))
                {
                    if (MediaDelay > TimeSpan.Zero)
                        await Task.Delay(MediaDelay);
                    response.ContentType = "image/jpeg";
                    response.ContentLength64 = image.Length;
                    await response.OutputStream.WriteAsync(image);
                }
                else if ((match = Regex.Match(path, @"^/api/savefiles/user/\d+/game/(\d+)$")).Success)
                    await Savefile(context, int.Parse(match.Groups[1].Value));
                else if (path.StartsWith("/api/progresses"))
                    await Json(response, new PaginatedData<Progress> { Data = Progresses.ToArray(), Meta = new MetaData { TotalItems = Progresses.Count }, Links = new Links() });
                else if (path.StartsWith("/api/"))
                    await Json(response, new { data = Array.Empty<object>(), meta = new { totalItems = 0 }, links = new { } });
                else
                    response.StatusCode = 404;
            }
            catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
            {
                // The client closed the connection (pause, cancel)
            }
            finally
            {
                try { response.Close(); } catch { }
            }
        }

        private MinimalGame[] SearchProvider(string query)
        {
            string[] words = Regex.Split(query.ToLowerInvariant(), @"\W+").Where(w => w.Length > 1).ToArray();
            return ProviderCatalog.Where(g => words.Any(w => g.Title.ToLowerInvariant().Contains(w))).OrderBy(g => g.ProviderDataId).ToArray();
        }

        /// <summary>Applies the mapping requests of an update: the game gets the provider game's metadata.</summary>
        private Game Map(Game game, string body)
        {
            using var json = System.Text.Json.JsonDocument.Parse(body);
            foreach (var request in json.RootElement.GetProperty("mapping_requests").EnumerateArray())
            {
                string slug = request.GetProperty("provider_slug").GetString()!;
                string id = request.GetProperty("provider_data_id").GetString()!;
                Mappings.Enqueue((game.ID, slug, id));
                MinimalGame? known = ProviderCatalog.FirstOrDefault(g => g.ProviderDataId == id);
                var metadata = new gamevault.Models.GameMetadata { ProviderSlug = slug, ProviderDataId = id, Title = known?.Title ?? game.Title, ReleaseDate = known?.ReleaseDate };
                game.ProviderMetadata = new List<gamevault.Models.GameMetadata> { metadata };
                game.Metadata = metadata;
            }
            return game;
        }

        /// <summary>/api/games with the parts of the query the client relies on: search, newest first, limit.</summary>
        private PaginatedData<Game> GameList(HttpListenerRequest request)
        {
            IEnumerable<Game> list = games.Values.Select(g => g.Game);
            string? ids = request.QueryString["filter.id"];
            if (ids != null && ids.StartsWith("$in:"))
            {
                var wanted = ids[4..].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToHashSet();
                list = list.Where(g => wanted.Contains(g.ID));
            }
            string? search = request.QueryString["search"];
            if (!string.IsNullOrEmpty(search))
                list = list.Where(g => g.Title.Contains(search, StringComparison.OrdinalIgnoreCase));
            string sort = request.QueryString["sortBy"] ?? "";
            list = sort.StartsWith("created_at:DESC") ? list.OrderByDescending(g => g.CreatedAt).ThenBy(g => g.ID)
                : sort.StartsWith("updated_at:DESC") ? list.OrderByDescending(g => g.UpdatedAt ?? g.CreatedAt).ThenBy(g => g.ID)
                : sort.StartsWith("sort_title") ? list.OrderBy(g => g.SortTitle)
                : list.OrderBy(g => g.ID);
            var all = list.ToArray();
            int limit = int.TryParse(request.QueryString["limit"], out int l) && l > 0 ? l : all.Length;
            return new PaginatedData<Game> { Data = all.Take(limit).ToArray(), Meta = new MetaData { TotalItems = all.Length }, Links = new Links() };
        }

        /// <summary>Adds a game as it comes from the server with its metadata (the showcase uses real games).</summary>
        public void AddGame(Game game, byte[] file) => games[game.ID] = (game, file);

        private async Task Download(HttpListenerContext context, int id)
        {
            var response = context.Response;
            if (!games.TryGetValue(id, out var entry))
            {
                response.StatusCode = 404;
                return;
            }
            byte[] file = entry.File;
            if (DamageNextDownload.TryRemove(id, out _))
            {
                file = (byte[])file.Clone();
                for (int i = file.Length / 2; i < file.Length / 2 + 4096 && i < file.Length; i++)
                    file[i] ^= 0x5A;
            }
            long start = 0;
            long last = file.Length - 1;
            string? range = context.Request.Headers["Range"];
            if (range != null)
                RangeHeaders.Enqueue(range);
            if (range != null && !IgnoreRange)
            {
                Match match = Regex.Match(range, @"bytes=(\d+)-(\d*)");
                if (match.Success)
                {
                    start = long.Parse(match.Groups[1].Value);
                    if (match.Groups[2].Value.Length > 0)
                        last = Math.Min(last, long.Parse(match.Groups[2].Value));
                }
                if (StandardRanges)
                {
                    response.StatusCode = 206;
                    response.Headers["Content-Range"] = $"bytes {start}-{last}/{file.Length}";
                }
            }
            if (!IgnoreRange)
            {
                response.Headers["Accept-Ranges"] = "bytes";
                response.Headers["X-Download-Size"] = (last - start + 1).ToString();
            }
            response.Headers["Content-Disposition"] = $"attachment; filename=\"{Path.GetFileName(entry.Game.Path)}\"";
            response.ContentType = "application/octet-stream";
            long length = last - start + 1;
            response.ContentLength64 = length;

            long end = last + 1;
            bool cut = CutNextDownloadAfter.TryRemove(id, out long cutAfter);
            if (cut)
                end = Math.Min(file.Length, start + cutAfter);
            var output = response.OutputStream;
            for (long position = start; position < end;)
            {
                int count = (int)Math.Min(64 * 1024, end - position);
                await output.WriteAsync(file.AsMemory((int)position, count));
                await output.FlushAsync();
                position += count;
                if (ChunkDelay > TimeSpan.Zero)
                    await Task.Delay(ChunkDelay);
            }
            if (cut)
            {
                // Ends the connection although Content-Length promised more
                response.Abort();
            }
        }

        private async Task Savefile(HttpListenerContext context, int gameId)
        {
            var response = context.Response;
            if (context.Request.HttpMethod == "POST")
            {
                string installation = context.Request.Headers["X-Installation-Id"] ?? Guid.NewGuid().ToString();
                using var body = new MemoryStream();
                await context.Request.InputStream.CopyToAsync(body);
                Saves[gameId] = ($"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{installation}.zip", MultipartFile(body.ToArray(), context.Request.ContentType ?? ""));
                response.StatusCode = 201;
                return;
            }
            if (!Saves.TryGetValue(gameId, out var save))
            {
                response.StatusCode = 404;
                return;
            }
            response.Headers["Content-Disposition"] = $"attachment; filename=\"{save.FileName}\"";
            response.ContentType = "application/zip";
            response.ContentLength64 = save.Data.Length;
            await response.OutputStream.WriteAsync(save.Data);
        }

        /// <summary>The file of a multipart/form-data upload with one part.</summary>
        private static byte[] MultipartFile(byte[] body, string contentType)
        {
            string boundary = "--" + Regex.Match(contentType, "boundary=\"?([^\";]+)").Groups[1].Value;
            byte[] separator = Encoding.ASCII.GetBytes("\r\n\r\n");
            int headersEnd = IndexOf(body, separator, 0) + separator.Length;
            int end = IndexOf(body, Encoding.ASCII.GetBytes("\r\n" + boundary), headersEnd);
            return body[headersEnd..end];
        }

        private static int IndexOf(byte[] data, byte[] pattern, int start)
        {
            for (int i = start; i <= data.Length - pattern.Length; i++)
            {
                int j = 0;
                while (j < pattern.Length && data[i + j] == pattern[j])
                    j++;
                if (j == pattern.Length)
                    return i;
            }
            return -1;
        }

        private static async Task Json(HttpListenerResponse response, object value)
        {
            byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, value.GetType()));
            response.ContentType = "application/json";
            response.ContentLength64 = body.Length;
            await response.OutputStream.WriteAsync(body);
        }

        public void Dispose()
        {
            stop.Cancel();
            try { listener.Stop(); listener.Close(); } catch { }
        }
    }
}
