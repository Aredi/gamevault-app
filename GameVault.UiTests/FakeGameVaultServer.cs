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
        /// <summary>Delay before the PS1 games are listed (a platform chosen, then another one at once).</summary>
        private static async Task<bool> Delayed(TimeSpan delay)
        {
            await Task.Delay(delay);
            return true;
        }

        public TimeSpan PsxListDelay { get; set; } = TimeSpan.Zero;
        /// <summary>The NES games have one more, a file the emulator does not start (its preparation fails).</summary>
        public bool NesBrokenGame { get; set; }
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
        /// <summary>The SanctuaryVault service's LAN rooms (the signed-in player is user 1), and the games marked playable in LAN.</summary>
        public ConcurrentDictionary<string, FakeRoom> Rooms { get; } = new();
        /// <summary>The games playable in LAN, with their players when known.</summary>
        public ConcurrentDictionary<int, int?> LanGames { get; } = new();
        public sealed class FakeRoom
        {
            public string Id = "", Code = "", Name = "", OwnerName = "";
            public int OwnerId;
            public int? GameId;
            public string? GameTitle;
            public bool Public;
            public List<(int UserId, string UserName, string NodeId, string Ip)> Members = new();
        }
        /// <summary>This PC's ZeroTier service under /zt (its token "zt-test", its address aaaaaaaa01): the networks it joined, the PCs it reaches.</summary>
        public ConcurrentDictionary<string, bool> ZeroTierNetworks { get; } = new();
        public ConcurrentDictionary<string, (int Latency, bool Direct)> ZeroTierPeers { get; } = new();
        private object RoomJson(FakeRoom r) => new
        {
            id = r.Id, code = r.Code, name = r.Name, ownerId = r.OwnerId, ownerName = r.OwnerName, gameId = r.GameId, gameTitle = r.GameTitle,
            @public = r.Public, maxMembers = 8, member = r.Members.Any(m => m.UserId == 1),
            members = r.Members.Select(m => new { userId = m.UserId, userName = m.UserName, nodeId = m.NodeId, ip = m.Ip }).ToArray(),
        };
        /// <summary>Game requests sent through the service (to the RomM-ComM bot): name of the game.</summary>
        public ConcurrentQueue<string> GameRequests { get; } = new();
        public static readonly byte[] RetroRom = Encoding.ASCII.GetBytes("NES\x1A-test-rom");
        /// <summary>A PS1 game in two files, as RomM zips them.</summary>
        public static readonly byte[] RetroCdGame = Zip(("Castlevania.cue", "FILE \"Castlevania.bin\" BINARY"), ("Castlevania.bin", "track"));
        /// <summary>A PS3 game in its folder, as RomM zips it (not "multi" for RomM: one folder).</summary>
        /// <summary>The PS3's firmware in RomM; none when null.</summary>
        public byte[]? Ps3Firmware { get; set; }
        public static readonly byte[] Ps3FolderGame = Zip(("Infamous/PS3_DISC.SFB", "disc"), ("Infamous/PS3_GAME/PARAM.SFO", "sfo"), ("Infamous/PS3_GAME/USRDIR/EBOOT.BIN", "eboot"),
            ("Infamous/PS3_GAME/USRDIR/bin/EBOOT.BIN", "other"));
        /// <summary>A Switch game card in its folder with its dump's files, as RomM zips it.</summary>
        public static readonly byte[] SwitchFolderGame = Zip(("1-2-Switch.xci/1-2-Switch (Initial Data).bin", "data"), ("1-2-Switch.xci/1-2-Switch.xci", "xci"));
        /// <summary>A Switch game compressed by nsz (Eden reads none: made an .nsp again).</summary>
        public static readonly byte[] SwitchCompressedGame = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "game.nsz"));
        /// <summary>The Switch's keys and firmware (a zip of its .nca) in RomM; none when null.</summary>
        public (byte[] Keys, byte[] Firmware)? SwitchFirmware { get; set; }
        /// <summary>The player's retro saves, per game ("nes-40"): (zip, when saved).</summary>
        public ConcurrentDictionary<string, (byte[] Zip, long SavedAt)> RetroSaves { get; } = new();
        /// <summary>Older versions of the retro saves: "nes-40-1000.zip" → (when saved, zip).</summary>
        public ConcurrentDictionary<string, (long SavedAt, byte[] Zip)> RetroSavesHistory { get; } = new();
        /// <summary>The retro plays sent (JSON), and the time kept per game key ("nes:40").</summary>
        public ConcurrentQueue<string> RetroPlaysSent { get; } = new();
        public ConcurrentDictionary<string, int> RetroMinutes { get; } = new();
        /// <summary>The games' achievements a player shared, by game ("nes:40"), as the client sent them.</summary>
        public ConcurrentDictionary<string, JsonElement> RetroAchievements { get; } = new();
        public string RetroFavorites { get; set; } = "[]";
        /// <summary>RomM answers here too (its public address is this server's): the player's saves and the pairing.</summary>
        public bool RommLinkable { get; set; }
        public sealed record RommSave(int Id, int RomId, string FileName, string? Slot, string? Emulator, DateTime UpdatedAt, byte[] Content, string? Origin = null);
        /// <summary>Which device sent or took which save (a save never changes: it is that device's current one).</summary>
        public ConcurrentDictionary<(string Device, int Save), bool> RommSyncs { get; } = new();
        public ConcurrentQueue<RommSave> RommSaves { get; } = new();
        /// <summary>RomM's games: its Windows games are the library's files.</summary>
        public ConcurrentQueue<(int Id, string Platform, string FileName)> RommRoms { get; } = new();
        private int rommTokenCalls;
        /// <summary>The player's status and rating of each game in RomM, and the changes received (JSON, with the query).</summary>
        public ConcurrentDictionary<int, (string? Status, int Rating)> RommMine { get; } = new();
        public ConcurrentQueue<string> RommPropsSent { get; } = new();
        /// <summary>A link made before the client asked for the statuses: RomM refuses to change them.</summary>
        public bool RommOldScopes { get; set; }
        /// <summary>The forms of the matches fixed in RomM (PUT /api/roms/{id}); a viewer's account is refused.</summary>
        public ConcurrentQueue<string> RommMatches { get; } = new();
        public bool RommViewer { get; set; }
        /// <summary>The RetroAchievements account RomM has for the player, and whether its progression was taken again.</summary>
        public string? RommRaUser { get; set; }
        public int RommRaRefreshes;

        /// <summary>What /retro/latest answers (RomM's newest games).</summary>
        public List<object> RetroLatest { get; } = new();

        private object RommJson(RommSave s) => new
        {
            id = s.Id, rom_id = s.RomId, file_name = s.FileName, slot = s.Slot, emulator = s.Emulator, file_size_bytes = s.Content.Length,
            updated_at = s.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ"), origin_device_id = s.Origin,
            device_syncs = RommSyncs.Keys.Where(k => k.Save == s.Id).Select(k => new { device_id = k.Device, device_name = k.Device, is_current = true }).ToArray(),
        };

        /// <summary>The file of a multipart upload (RomM's saveFile).</summary>
        private static (string Name, byte[] Content) RommMultipartFile(byte[] body, string contentType)
        {
            string boundary = "--" + contentType.Split("boundary=")[1].Trim('"');
            string text = Encoding.Latin1.GetString(body);
            int part = text.IndexOf("name=saveFile", StringComparison.Ordinal);
            if (part < 0)
                part = text.IndexOf("name=\"saveFile\"", StringComparison.Ordinal);
            var name = Regex.Match(text[part..], "filename=\"?([^\";\r\n]+)\"?").Groups[1].Value;
            int start = text.IndexOf("\r\n\r\n", part, StringComparison.Ordinal) + 4;
            int end = text.IndexOf("\r\n" + boundary, start, StringComparison.Ordinal);
            return (name, body[start..end]);
        }

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
                bool romm = request.Headers["Authorization"] == "Bearer romm-token";
                // RomM's own answers to the linked player (its paths are the GameVault server's ones too)
                if (romm && path == "/api/users/me")
                    await Json(response, new
                    {
                        id = 1, username = "admin", ra_username = RommRaUser,
                        ra_progression = new { total = 1, results = new[] { new { rom_ra_id = 5626, earned_achievements = new[] { new { id = "379571", date = "2026-10-01" } } } } },
                    });
                else if (romm && path == "/api/users/1" && request.HttpMethod == "PUT")
                {
                    string form = await new StreamReader(request.InputStream).ReadToEndAsync();
                    RommRaUser = Uri.UnescapeDataString(form.Split('=', 2)[1]);
                    await Json(response, new { id = 1 });
                }
                else if (romm && path == "/api/users/1/ra/refresh")
                {
                    Interlocked.Increment(ref RommRaRefreshes);
                    await Json(response, new { });
                }
                else if (romm && path == "/api/search/roms")
                    await Json(response, request.QueryString["search_by"] == "id"
                        ? new object[] { new { igdb_id = 549, ss_id = (int?)null, platform_id = 1, name = "God of War", summary = "Kratos.", igdb_url_cover = "", is_identified = true, is_unidentified = false } }
                        : new object[]
                        {
                            new { igdb_id = 549, ss_id = (int?)19530, platform_id = 1, name = "God of War", summary = "Kratos.", igdb_url_cover = "", is_identified = true, is_unidentified = false },
                            new { igdb_id = 1352, ss_id = (int?)null, platform_id = 1, name = "God of War II", summary = "Kratos again.", igdb_url_cover = "", is_identified = true, is_unidentified = false },
                        });
                else if (romm && request.HttpMethod == "PUT" && (match = Regex.Match(path, @"^/api/roms/(\d+)$")).Success)
                {
                    if (RommViewer)
                    {
                        response.StatusCode = 403;
                        return;
                    }
                    int id = int.Parse(match.Groups[1].Value);
                    string body = await new StreamReader(request.InputStream).ReadToEndAsync();
                    var fields = Regex.Matches(body, "name=\"?(\\w+)\"?\r\n(?:[^\r\n]+\r\n)*\r\n(.*?)\r\n--").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
                    RommMatches.Enqueue(string.Join("&", fields.OrderBy(f => f.Key).Select(f => $"{f.Key}={f.Value}")));
                    await Json(response, new { id, name = fields["name"], summary = "Kratos.", path_cover_large = $"/assets/romm/resources/roms/1/{id}/cover/big.png?ts=2026" });
                }
                else if (romm && (match = Regex.Match(path, @"^/api/roms/(\d+)$")).Success)
                {
                    int id = int.Parse(match.Groups[1].Value);
                    var mine = RommMine.GetValueOrDefault(id);
                    await Json(response, new
                    {
                        id, ra_id = 5626,
                        rom_user = new { status = mine.Status, rating = mine.Rating },
                        merged_ra_metadata = new
                        {
                            achievements = new[]
                            {
                                new { ra_id = 109111, title = "Mission 1", description = "Clear it.", points = 3, badge_id = "379571", badge_url = (string?)null, badge_url_lock = (string?)null, display_order = 0 },
                                new { ra_id = 109112, title = "Mission 2", description = "Clear it with an S Rank.", points = 5, badge_id = "379572", badge_url = (string?)null, badge_url_lock = (string?)null, display_order = 1 },
                            },
                        },
                    });
                }
                else if (romm && (match = Regex.Match(path, @"^/api/roms/(\d+)/props$")).Success)
                {
                    if (RommOldScopes)
                    {
                        response.StatusCode = 403;
                        return;
                    }
                    int id = int.Parse(match.Groups[1].Value);
                    string body = await new StreamReader(request.InputStream).ReadToEndAsync();
                    RommPropsSent.Enqueue(request.Url.Query + body);
                    using var changes = JsonDocument.Parse(body);
                    var mine = RommMine.GetValueOrDefault(id);
                    if (changes.RootElement.TryGetProperty("status", out JsonElement status))
                        mine.Status = status.ValueKind == JsonValueKind.String ? status.GetString() : null;
                    if (changes.RootElement.TryGetProperty("rating", out JsonElement rating))
                        mine.Rating = rating.GetInt32();
                    RommMine[id] = mine;
                    await Json(response, new { });
                }
                else if (path == "/api/auth/basic/login" && !HasPassword(request))
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
                // RomM through the SanctuaryVault service: a NES game, and a platform the client has no emulator for (the PS4)
                else if (path == "/retro/platforms")
                    await Json(response, new object[] { new { id = 3, slug = "nes", name = "NES", count = 1 }, new { id = 19, slug = "psx", name = "PlayStation", count = 1 }, new { id = 21, slug = "ps3", name = "PlayStation 3", count = 2 }, new { id = 9, slug = "switch", name = "Switch", count = 3 }, new { id = 30, slug = "ps4", name = "PlayStation 4", count = 4 } });
                else if (path == "/retro/platforms/9/games")
                    await Json(response, new[]
                    {
                        new { id = 70, name = "1-2-Switch", fileName = "1-2-Switch.xci", size = SwitchFolderGame.LongLength, multi = true },
                        new { id = 71, name = "Assassin's Creed Brotherhood DLC", fileName = "Assassins Creed - Brotherhood [DLC][0100670014483001].nsp", size = 3L, multi = false },
                        new { id = 72, name = "Compressed Game", fileName = "Compressed Game.nsz", size = SwitchCompressedGame.LongLength, multi = false },
                    });
                else if (path == "/retro/games/70/content")
                {
                    response.ContentType = "application/zip";
                    response.AddHeader("Content-Disposition", "attachment; filename*=UTF-8''1-2-Switch.xci.zip");
                    response.ContentLength64 = SwitchFolderGame.LongLength;
                    await response.OutputStream.WriteAsync(SwitchFolderGame);
                }
                else if (path == "/retro/games/72/content")
                {
                    response.AddHeader("Content-Disposition", "attachment; filename*=UTF-8''Compressed%20Game.nsz");
                    response.ContentLength64 = SwitchCompressedGame.LongLength;
                    await response.OutputStream.WriteAsync(SwitchCompressedGame);
                }
                else if (path == "/retro/games/71/content")
                {
                    response.AddHeader("Content-Disposition", "attachment; filename*=UTF-8''Assassins%20Creed%20-%20Brotherhood%20%5BDLC%5D%5B0100670014483001%5D.nsp");
                    response.ContentLength64 = 3;
                    await response.OutputStream.WriteAsync("dlc"u8.ToArray());
                }
                else if (path == "/retro/platforms/9/firmware")
                    await Json(response, SwitchFirmware is not var (keys, firmware) ? Array.Empty<object>()
                        : new object[] { new { id = 10, fileName = "prod.keys", size = keys.LongLength }, new { id = 11, fileName = "Firmware 20.1.0.zip", size = firmware.LongLength } });
                else if (path == "/retro/firmware/10/content" && SwitchFirmware is var (switchKeys, _))
                    await response.OutputStream.WriteAsync(switchKeys);
                else if (path == "/retro/firmware/11/content" && SwitchFirmware is var (_, switchFirmware))
                    await response.OutputStream.WriteAsync(switchFirmware);
                else if (path == "/retro/platforms/21/games")
                    await Json(response, new[]
                    {
                        new { id = 60, name = "inFamous", fileName = "Infamous", size = Ps3FolderGame.LongLength, multi = false },
                        new { id = 61, name = "Fatal Frame", fileName = "Fatal Frame.pkg", size = 3601L << 20, multi = false },
                    });
                else if (path == "/retro/games/60/content")
                {
                    response.ContentType = "application/zip";
                    response.AddHeader("Content-Disposition", "attachment; filename*=UTF-8''Infamous.zip");
                    response.ContentLength64 = Ps3FolderGame.LongLength;
                    await response.OutputStream.WriteAsync(Ps3FolderGame);
                }
                else if (path == "/retro/platforms/21/firmware")
                    await Json(response, Ps3Firmware == null ? Array.Empty<object>() : new object[] { new { id = 8, fileName = "PS3UPDAT.PUP", size = Ps3Firmware.LongLength } });
                else if (path == "/retro/firmware/8/content" && Ps3Firmware != null)
                    await response.OutputStream.WriteAsync(Ps3Firmware);
                else if (path == "/retro/series")
                    await Json(response, new[] { new { name = "Castlevania", romIds = new[] { 50, 9050 } } });
                else if (path == "/retro/platforms/19/games" && await Delayed(PsxListDelay))
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
                else if (path == "/retro/players/me" && request.HttpMethod == "GET")
                    await Json(response, new
                    {
                        plays = RetroMinutes.Select(m => new { key = m.Key, romId = int.Parse(m.Key.Split(':')[1]), platform = m.Key.Split(':')[0], platformName = "NES", name = "Zelda", cover = (string?)null, minutes = m.Value, lastPlayed = DateTime.UtcNow }),
                        favorites = JsonSerializer.Deserialize<string[]>(RetroFavorites),
                    });
                else if (path == "/retro/players/me/plays")
                {
                    string body = await new StreamReader(request.InputStream).ReadToEndAsync();
                    RetroPlaysSent.Enqueue(body);
                    using JsonDocument play = JsonDocument.Parse(body);
                    string key = $"{play.RootElement.GetProperty("platform").GetString()}:{play.RootElement.GetProperty("romId").GetInt32()}";
                    RetroMinutes.AddOrUpdate(key, play.RootElement.GetProperty("minutes").GetInt32(), (_, m) => m + play.RootElement.GetProperty("minutes").GetInt32());
                    response.StatusCode = 204;
                }
                else if (path == "/retro/players/me/favorites")
                {
                    RetroFavorites = await new StreamReader(request.InputStream).ReadToEndAsync();
                    response.StatusCode = 204;
                }
                else if (path == "/retro/activity")
                    await Json(response, RetroMinutes.Select(m => new { userId = 2, username = "Mario", romId = int.Parse(m.Key.Split(':')[1]), platform = m.Key.Split(':')[0], platformName = "NES", name = "Zelda", cover = (string?)null, minutes = m.Value, lastPlayed = DateTime.UtcNow }));
                else if ((match = Regex.Match(path, @"^/retro/players/(\d+)$")).Success)
                    await Json(response, new { plays = RetroMinutes.Select(m => new { romId = int.Parse(m.Key.Split(':')[1]), platform = m.Key.Split(':')[0], platformName = "NES", name = "Zelda", minutes = m.Value, lastPlayed = DateTime.UtcNow }), achievements = RetroAchievements });
                else if ((match = Regex.Match(path, @"^/retro/players/me/achievements/([a-z0-9]+)/(\d+)$")).Success && request.HttpMethod == "PUT")
                {
                    using var reader = new StreamReader(request.InputStream);
                    RetroAchievements[$"{match.Groups[1].Value}:{match.Groups[2].Value}"] = JsonDocument.Parse(await reader.ReadToEndAsync()).RootElement.Clone();
                    response.StatusCode = 204;
                }
                else if (path == "/retro/activity/achievements")
                    await Json(response, RetroAchievements.SelectMany(a => a.Value.GetProperty("unlocks").EnumerateArray().Select(u => new
                    {
                        userId = 2, username = "Mario", romId = int.Parse(a.Key.Split(':')[1]), platform = a.Key.Split(':')[0], platformName = a.Value.GetProperty("platformName").GetString(),
                        name = a.Value.GetProperty("name").GetString(), title = u.GetProperty("title").GetString(), date = u.GetProperty("date").GetDateTime(),
                    })));
                else if ((match = Regex.Match(path, @"^/retro/games/([a-z0-9]+)/(\d+)/players$")).Success)
                    await Json(response, RetroMinutes.Where(m => m.Key == $"{match.Groups[1].Value}:{match.Groups[2].Value}")
                        .Select(m => new { userId = 2, username = "Mario", minutes = m.Value, lastPlayed = DateTime.UtcNow }));
                else if (path == "/retro/latest")
                    await Json(response, RetroLatest);
                else if ((match = Regex.Match(path, @"^/retro/saves/([a-z0-9-]+)/history$")).Success)
                    await Json(response, RetroSavesHistory.Where(v => v.Key.StartsWith(match.Groups[1].Value + "-")).Select(v => new { name = v.Key, savedAt = v.Value.SavedAt, size = v.Value.Zip.Length }).OrderByDescending(v => v.savedAt));
                else if ((match = Regex.Match(path, @"^/retro/saves/([a-z0-9-]+)/history/([^/]+)$")).Success && RetroSavesHistory.TryGetValue(Uri.UnescapeDataString(match.Groups[2].Value), out var version))
                    await response.OutputStream.WriteAsync(version.Zip);
                else if ((match = Regex.Match(path, @"^/retro/saves/([a-z0-9-]+)/info$")).Success)
                {
                    if (RetroSaves.TryGetValue(match.Groups[1].Value, out var saves))
                        await Json(response, new { savedAt = saves.SavedAt });
                    else
                        response.StatusCode = 404;
                }
                else if ((match = Regex.Match(path, @"^/retro/saves/([a-z0-9-]+)$")).Success && request.HttpMethod == "PUT")
                {
                    var body = new MemoryStream();
                    await request.InputStream.CopyToAsync(body);
                    string set = match.Groups[1].Value;
                    long savedAt = (RetroSaves.TryGetValue(set, out var before) ? before.SavedAt : 0) + 1000;
                    RetroSaves[set] = (body.ToArray(), savedAt);
                    await Json(response, new { savedAt });
                }
                else if ((match = Regex.Match(path, @"^/retro/saves/([a-z0-9-]+)$")).Success && RetroSaves.TryGetValue(match.Groups[1].Value, out var current))
                    await response.OutputStream.WriteAsync(current.Zip);
                else if (path == "/retro/platforms/3/games")
                    await Json(response, new[] { new { id = 40, name = "Zelda", fileName = "Zelda (E).nes", size = RetroRom.LongLength, cover = (string?)null, summary = "A sword" } }
                        .Concat(NesBrokenGame ? new[] { new { id = 41, name = "Readme", fileName = "Readme.txt", size = 4L, cover = (string?)null, summary = "Not a game" } } : []));
                else if (path == "/retro/games/41/content")
                {
                    response.AddHeader("Content-Disposition", "attachment; filename*=UTF-8''Readme.txt");
                    response.ContentLength64 = 4;
                    await response.OutputStream.WriteAsync("text"u8.ToArray());
                }
                else if (path == "/retro/games/40/content")
                {
                    response.ContentType = "application/octet-stream";
                    response.AddHeader("Content-Disposition", "attachment; filename*=UTF-8''Zelda%20%28E%29.nes");
                    response.ContentLength64 = RetroRom.LongLength;
                    await response.OutputStream.WriteAsync(RetroRom);
                }
                // RomM: the device pairing (pending once, then approved) and the player's saves
                else if (path == "/api/auth/device/init")
                {
                    rommTokenCalls = 0;
                    await Json(response, new { device_code = "device-code", user_code = "ABCD-1234", verification_path = "/pair/device", verification_path_complete = "/pair/device?user_code=ABCD-1234", expires_in = 60, interval = 2 });
                }
                else if (path == "/api/auth/device/token")
                {
                    if (Interlocked.Increment(ref rommTokenCalls) == 1)
                    {
                        response.StatusCode = 400;
                        await Json(response, new { detail = "authorization_pending" });
                    }
                    else
                        await Json(response, new { access_token = "romm-token", device_id = "pc-1", scopes = new[] { "assets.write" } });
                }
                else if (path == "/api/saves" && request.HttpMethod == "POST")
                {
                    var body = new MemoryStream();
                    await request.InputStream.CopyToAsync(body);
                    var (fileName, content) = RommMultipartFile(body.ToArray(), request.ContentType!);
                    var saved = new RommSave(RommSaves.Count + 1, int.Parse(request.QueryString["rom_id"]!), fileName, request.QueryString["slot"], request.QueryString["emulator"],
                        DateTime.UtcNow.AddSeconds(RommSaves.Count), content, request.QueryString["device_id"]);
                    RommSaves.Enqueue(saved);
                    if (request.QueryString["device_id"] is string sender)
                        RommSyncs[(sender, saved.Id)] = true;
                    await Json(response, RommJson(saved));
                }
                else if (path == "/api/roms")
                {
                    string term = request.QueryString["search_term"] ?? "";
                    var found = RommRoms.Where(r => r.FileName.Contains(term, StringComparison.OrdinalIgnoreCase))
                        .Select(r => new { id = r.Id, platform_slug = r.Platform, fs_name = r.FileName }).ToList();
                    await Json(response, new { items = found, total = found.Count });
                }
                else if (path == "/api/saves")
                    await Json(response, RommSaves.Where(s => s.RomId == int.Parse(request.QueryString["rom_id"]!)).Select(RommJson));
                else if ((match = Regex.Match(path, @"^/api/saves/(\d+)/content$")).Success && RommSaves.FirstOrDefault(s => s.Id == int.Parse(match.Groups[1].Value)) is RommSave found)
                {
                    if (request.QueryString["device_id"] is string taker)
                        RommSyncs[(taker, found.Id)] = true;
                    await response.OutputStream.WriteAsync(found.Content);
                }
                else if (path == "/status")
                    await Json(response, new { status = "OK", version = "1.0.0", freeSpace = 1L << 40, profiles = true, saveHistory = true, rommUrl = RommLinkable ? Url : null });
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
                else if (path == "/rooms" && request.HttpMethod == "GET")
                    await Json(response, Rooms.Values.Where(r => r.Public || r.Members.Any(m => m.UserId == 1)).Select(RoomJson).ToArray());
                else if (path == "/rooms" && request.HttpMethod == "POST")
                {
                    using JsonDocument roomAsked = JsonDocument.Parse(await new StreamReader(request.InputStream).ReadToEndAsync());
                    var room = new FakeRoom
                    {
                        Id = $"c4576f06160000{Rooms.Count + 10}", Code = "K7Q4MX", Name = roomAsked.RootElement.GetProperty("name").GetString()!, OwnerId = 1, OwnerName = "admin",
                        GameId = roomAsked.RootElement.GetProperty("gameId").ValueKind == JsonValueKind.Number ? roomAsked.RootElement.GetProperty("gameId").GetInt32() : null,
                        GameTitle = roomAsked.RootElement.GetProperty("gameTitle").GetString(), Public = roomAsked.RootElement.GetProperty("public").GetBoolean(),
                    };
                    room.Members.Add((1, "admin", roomAsked.RootElement.GetProperty("nodeId").GetString()!, "10.147.9.1"));
                    Rooms[room.Id] = room;
                    response.StatusCode = 201;
                    await Json(response, RoomJson(room));
                }
                else if (path == "/rooms/join")
                {
                    using JsonDocument roomAsked = JsonDocument.Parse(await new StreamReader(request.InputStream).ReadToEndAsync());
                    string? code = roomAsked.RootElement.GetProperty("code").GetString(), id = roomAsked.RootElement.GetProperty("roomId").GetString();
                    FakeRoom? room = Rooms.Values.FirstOrDefault(r => code != null ? r.Code == code.Replace("-", "").ToUpperInvariant() : r.Id == id);
                    if (room == null)
                    {
                        response.StatusCode = 404;
                        await Json(response, new { error = "No room has this code." });
                    }
                    else
                    {
                        room.Members.RemoveAll(m => m.UserId == 1);
                        room.Members.Add((1, "admin", roomAsked.RootElement.GetProperty("nodeId").GetString()!, $"10.147.9.{room.Members.Count + 1}"));
                        await Json(response, RoomJson(room));
                    }
                }
                else if ((match = Regex.Match(path, @"^/rooms/(\w+)/leave$")).Success && Rooms.TryGetValue(match.Groups[1].Value, out var left))
                {
                    left.Members.RemoveAll(m => m.UserId == 1);
                    if (left.Members.Count == 0)
                        Rooms.TryRemove(left.Id, out _);
                    response.StatusCode = 204;
                }
                else if ((match = Regex.Match(path, @"^/rooms/(\w+)/game$")).Success && Rooms.TryGetValue(match.Groups[1].Value, out var changed))
                {
                    using JsonDocument roomAsked = JsonDocument.Parse(await new StreamReader(request.InputStream).ReadToEndAsync());
                    changed.GameId = roomAsked.RootElement.GetProperty("gameId").GetInt32();
                    changed.GameTitle = roomAsked.RootElement.GetProperty("gameTitle").GetString();
                    response.StatusCode = 204;
                }
                else if (path == "/lan-games" && request.HttpMethod == "GET")
                    await Json(response, LanGames.Select(g => new { id = g.Key, players = g.Value }).ToArray());
                else if ((match = Regex.Match(path, @"^/lan-games/(\d+)$")).Success)
                {
                    using JsonDocument roomAsked = JsonDocument.Parse(await new StreamReader(request.InputStream).ReadToEndAsync());
                    int lanGame = int.Parse(match.Groups[1].Value);
                    if (!roomAsked.RootElement.GetProperty("lan").GetBoolean())
                        LanGames.TryRemove(lanGame, out _);
                    else
                        LanGames.TryAdd(lanGame, null);
                    response.StatusCode = 204;
                }
                // This PC's ZeroTier service
                else if (path.StartsWith("/zt/") && request.Headers["X-ZT1-Auth"] != "zt-test")
                    response.StatusCode = 401;
                else if (path == "/zt/status")
                    await Json(response, new { address = "aaaaaaaa01", online = true });
                else if (path == "/zt/peer")
                    await Json(response, ZeroTierPeers.Select(p => new { address = p.Key, latency = p.Value.Latency, paths = p.Value.Direct ? new[] { new { active = true } } : Array.Empty<object>() }).ToArray());
                else if (path == "/zt/network")
                    await Json(response, ZeroTierNetworks.Keys.Select(id => new { id, status = "OK" }).ToArray());
                else if ((match = Regex.Match(path, @"^/zt/network/(\w+)$")).Success && request.HttpMethod == "POST")
                {
                    ZeroTierNetworks[match.Groups[1].Value] = true;
                    await Json(response, new { id = match.Groups[1].Value });
                }
                else if ((match = Regex.Match(path, @"^/zt/network/(\w+)$")).Success && request.HttpMethod == "DELETE")
                    response.StatusCode = ZeroTierNetworks.TryRemove(match.Groups[1].Value, out _) ? 200 : 404;
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
