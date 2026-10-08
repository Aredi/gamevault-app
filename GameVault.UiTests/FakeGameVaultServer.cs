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

        private async Task Handle(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            string path = request.Url!.AbsolutePath;
            Requests.Enqueue($"{request.HttpMethod} {request.Url.PathAndQuery}");
            try
            {
                Match match;
                if (path == "/api/auth/basic/login")
                    await Json(response, new { id = "1", access_token = "access", refresh_token = "refresh" });
                else if (path == "/api/auth/refresh")
                    await Json(response, new { id = "1", access_token = "access", refresh_token = "refresh" });
                else if (path == "/api/users/me")
                    await Json(response, Users.TryGetValue(1, out User? me) ? me : Admin);
                else if (path == "/api/users")
                    await Json(response, Users.IsEmpty ? new[] { Admin } : Users.Values.OrderBy(u => u.ID).ToArray());
                else if ((match = Regex.Match(path, @"^/api/users/(\d+)$")).Success && Users.TryGetValue(int.Parse(match.Groups[1].Value), out User? user))
                    await Json(response, user);
                else if (path == "/api/status")
                    await Json(response, new { status = "HEALTHY" });
                else if ((match = Regex.Match(path, @"^/api/games/(\d+)/download$")).Success)
                    await Download(context, int.Parse(match.Groups[1].Value));
                else if ((match = Regex.Match(path, @"^/api/games/(\d+)$")).Success && games.TryGetValue(int.Parse(match.Groups[1].Value), out var entry))
                    await Json(response, entry.Game);
                else if (path == "/api/games")
                    await Json(response, GameList(request));
                else if ((match = Regex.Match(path, @"^/api/media/(\d+)$")).Success && Media.TryGetValue(int.Parse(match.Groups[1].Value), out byte[]? image))
                {
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

        /// <summary>/api/games with the parts of the query the client relies on: search, newest first, limit.</summary>
        private PaginatedData<Game> GameList(HttpListenerRequest request)
        {
            IEnumerable<Game> list = games.Values.Select(g => g.Game);
            string? search = request.QueryString["search"];
            if (!string.IsNullOrEmpty(search))
                list = list.Where(g => g.Title.Contains(search, StringComparison.OrdinalIgnoreCase));
            string sort = request.QueryString["sortBy"] ?? "";
            list = sort.StartsWith("created_at:DESC") ? list.OrderByDescending(g => g.CreatedAt).ThenBy(g => g.ID)
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
