using Avalonia.Threading;
using GameVault.Core;
using gamevault.Helper.Platform;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    public sealed class ChatMessage
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("conversationId")] public string ConversationId { get; set; } = "";
        [JsonPropertyName("senderId")] public int SenderId { get; set; }
        [JsonPropertyName("senderName")] public string SenderName { get; set; } = "";
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("picture")] public string? Picture { get; set; }
        [JsonPropertyName("sentAt")] public DateTime SentAt { get; set; }
        [JsonPropertyName("replyTo")] public ChatReply? ReplyTo { get; set; }
        [JsonPropertyName("game")] public ChatGame? Game { get; set; }
        [JsonPropertyName("editedAt")] public DateTime? EditedAt { get; set; }
        /// <summary>Taken back by its sender: no text, no picture, no game.</summary>
        [JsonPropertyName("deleted")] public bool Deleted { get; set; }
        /// <summary>Emoji → the players who put it.</summary>
        [JsonPropertyName("reactions")] public Dictionary<string, List<int>>? Reactions { get; set; }
    }

    /// <summary>The message answered, a few words of it.</summary>
    public sealed class ChatReply
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("senderName")] public string SenderName { get; set; } = "";
        [JsonPropertyName("text")] public string? Text { get; set; }
    }

    /// <summary>A game in a message: shared, or an invitation to play it.</summary>
    public sealed class ChatGame
    {
        /// <summary>"pc" (the library) or "retro".</summary>
        [JsonPropertyName("kind")] public string Kind { get; set; } = "pc";
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("platform")] public string? Platform { get; set; }
        [JsonPropertyName("platformName")] public string? PlatformName { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("cover")] public string? Cover { get; set; }
        [JsonPropertyName("coverVersion")] public string? CoverVersion { get; set; }
        [JsonPropertyName("invite")] public bool Invite { get; set; }

        [JsonIgnore] public bool IsRetro => Kind == "retro";

        public static ChatGame Of(Models.Game game, bool invite) => new()
        {
            Kind = "pc", Id = game.ID, Invite = invite,
            Title = game.Metadata?.Title is { Length: > 0 } title && ViewModels.SettingsViewModel.Instance.ShowMappedTitle ? title : game.Title,
        };

        internal static ChatGame Of(RetroGame game, bool invite) => new()
        {
            Kind = "retro", Id = game.Id, Platform = game.Platform.Slug, PlatformName = game.Platform.Name, Title = game.Name,
            Cover = game.Cover, CoverVersion = game.CoverVersion, Invite = invite,
        };
    }

    public sealed class ChatConversation
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        /// <summary>"direct" or "group".</summary>
        [JsonPropertyName("kind")] public string Kind { get; set; } = "direct";
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("members")] public List<int> Members { get; set; } = new();
        [JsonPropertyName("activeAt")] public DateTime ActiveAt { get; set; }
        [JsonPropertyName("unread")] public int Unread { get; set; }
        [JsonPropertyName("last")] public ChatMessage? Last { get; set; }
        /// <summary>When each member last read it ("Seen"); none for the announcements.</summary>
        [JsonPropertyName("readAt")] public Dictionary<int, DateTime>? ReadAt { get; set; }

        [JsonIgnore] public bool IsGroup => Kind == "group";
        [JsonIgnore] public bool IsNews => Id == ChatService.NewsId;
    }

    /// <summary>
    /// The chat with the other players, kept by the SanctuaryVault service: the conversations, their messages, the live
    /// link (a websocket) that brings a message as it is sent. Without the live link (a proxy without websockets), the
    /// list is asked every 15 s. Off when the service has no chat (an older one): <see cref="IsAvailable"/>.
    /// </summary>
    internal sealed class ChatService
    {
        public static ChatService Instance { get; } = new();
        /// <summary>The administrators' announcements: every player reads them.</summary>
        public const string NewsId = "news";
        private static readonly TimeSpan TypingEvery = TimeSpan.FromSeconds(3);
        private readonly Dictionary<string, DateTime> typingSaid = new();

        private static readonly TimeSpan PollTime = TimeSpan.FromSeconds(15);
        private List<ChatConversation> conversations = new();
        private CancellationTokenSource? running;
        private ClientWebSocket? socket;

        public bool IsAvailable { get; private set; }
        public bool IsLive => socket?.State == WebSocketState.Open;
        public IReadOnlyList<ChatConversation> Conversations => conversations;
        public int TotalUnread => conversations.Sum(c => c.Unread);

        /// <summary>On the UI thread: a message came (from another player, or from this player on another PC).</summary>
        public event Action<ChatMessage>? MessageReceived;
        /// <summary>On the UI thread: the list changed (a group, the unread counts, the availability).</summary>
        public event Action? Changed;
        /// <summary>On the UI thread: a message was changed, taken back, or got an emoji.</summary>
        public event Action<ChatMessage>? MessageChanged;
        /// <summary>On the UI thread: someone writes in a conversation (conversation, player, their name).</summary>
        public event Action<string, int, string>? Typing;
        /// <summary>On the UI thread: someone read a conversation ("Seen").</summary>
        public event Action<string>? Read;

        private static string Service => SanctuaryService.Current.TrimEnd('/');
        public static int Me => LoginManager.Instance.GetCurrentUser()?.ID ?? 0;

        /// <summary>The conversation with a player: the same id on both sides.</summary>
        public static string DirectId(int other) => $"d-{Math.Min(Me, other)}-{Math.Max(Me, other)}";

        /// <summary>The other player of a conversation between two.</summary>
        public static int? OtherOf(ChatConversation conversation) => conversation.IsGroup ? null : conversation.Members.FirstOrDefault(m => m != Me) is int other and > 0 ? other : null;

        public ChatConversation? Of(string id) => conversations.FirstOrDefault(c => c.Id == id);

        /// <summary>From the sign-in to the sign-out.</summary>
        public void Start()
        {
            Stop();
            running = new CancellationTokenSource();
            _ = RunAsync(running.Token);
        }

        public void Stop()
        {
            running?.Cancel();
            running = null;
            try { socket?.Abort(); } catch (Exception ex) { Log.Ignored(ex); }
            socket = null;
        }

        private async Task RunAsync(CancellationToken stop)
        {
            TimeSpan wait = TimeSpan.FromSeconds(5);
            while (!stop.IsCancellationRequested)
            {
                await RefreshAsync();
                if (IsAvailable && await ListenAsync(stop))
                    wait = TimeSpan.FromSeconds(5);// it was live: try again soon
                // No live link: the list now and then, the live link tried again a little later each time
                DateTime retry = DateTime.UtcNow + wait;
                wait = TimeSpan.FromSeconds(Math.Min(wait.TotalSeconds * 2, 300));
                while (!stop.IsCancellationRequested && DateTime.UtcNow < retry)
                {
                    try { await Task.Delay(PollTime, stop); } catch (OperationCanceledException) { return; }
                    await RefreshAsync();
                }
            }
        }

        /// <returns>Whether the link was open (then lost): a refused link is not tried again at once.</returns>
        private async Task<bool> ListenAsync(CancellationToken stop)
        {
            var link = new ClientWebSocket();
            link.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            try
            {
                link.Options.SetRequestHeader("Authorization", $"Bearer {await WebHelper.AccessTokenAsync()}");
                string url = Service.Replace("https://", "wss://").Replace("http://", "ws://") + "/chat/live";
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    await link.ConnectAsync(new Uri(url), timeout.Token);
                }
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                link.Dispose();
                return false;
            }
            socket = link;
            // What was sent while the link was down
            await RefreshAsync();
            Post(() => Changed?.Invoke());
            try
            {
                var buffer = new byte[64 * 1024];
                using var received = new MemoryStream();
                while (!stop.IsCancellationRequested && link.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult part = await link.ReceiveAsync(buffer, stop);
                    if (part.MessageType == WebSocketMessageType.Close)
                        break;
                    received.Write(buffer, 0, part.Count);
                    if (!part.EndOfMessage)
                        continue;
                    Take(Encoding.UTF8.GetString(received.ToArray()));
                    received.SetLength(0);
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException)
            {
                Log.Ignored(ex);
            }
            finally
            {
                socket = null;
                link.Dispose();
                Post(() => Changed?.Invoke());
            }
            return true;
        }

        private sealed class LiveEvent
        {
            [JsonPropertyName("type")] public string Type { get; set; } = "";
            [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
            [JsonPropertyName("conversation")] public ChatConversation? Conversation { get; set; }
            [JsonPropertyName("userId")] public int UserId { get; set; }
            /// <summary>"retroPlaying": the retro game a player started, null when they stopped.</summary>
            [JsonPropertyName("playing")] public RetroActivity? Playing { get; set; }
            /// <summary>"presence": a player's status.</summary>
            [JsonPropertyName("status")] public PlayerStatus? Status { get; set; }
            [JsonPropertyName("conversationId")] public string? ConversationId { get; set; }
            [JsonPropertyName("userName")] public string? UserName { get; set; }
            [JsonPropertyName("readAt")] public DateTime ReadAt { get; set; }
        }

        /// <summary>What the live link brought (also used by the tests).</summary>
        internal void Take(string json)
        {
            LiveEvent? live;
            try { live = JsonSerializer.Deserialize<LiveEvent>(json); }
            catch (JsonException ex) { Log.Ignored(ex); return; }
            if (live?.Type == "retroPlaying")
            {
                ActivityService.Instance.ApplyRetro(live.Playing == null ? null : new[] { live.Playing }, replaceAll: false, stoppedBy: live.UserId);
                return;
            }
            if (live?.Type == "presence")
            {
                if (live.Status != null)
                    ActivityService.Instance.ApplyStatuses(new[] { live.Status }, replaceAll: false);
                return;
            }
            Post(() =>
            {
                if (live?.Type == "changed" && live.Message is ChatMessage changedMessage)
                    Changed_(changedMessage);
                else if (live?.Type == "typing" && live.ConversationId != null && live.UserId != Me)
                    Typing?.Invoke(live.ConversationId, live.UserId, live.UserName ?? "");
                else if (live?.Type == "read" && live.ConversationId != null)
                {
                    if (Of(live.ConversationId) is ChatConversation read)
                    {
                        (read.ReadAt ??= new())[live.UserId] = live.ReadAt;
                        // Read on another PC of the player's: not unread here either
                        if (live.UserId == Me && read.Unread > 0)
                        {
                            read.Unread = 0;
                            Changed?.Invoke();
                        }
                    }
                    Read?.Invoke(live.ConversationId);
                }
                else if (live?.Message is ChatMessage message)
                    Received(message);
                else if (live?.Conversation is ChatConversation changed)
                {
                    ChatConversation? known = Of(changed.Id);
                    if (!changed.Members.Contains(Me))
                        conversations.Remove(known!);
                    else if (known == null)
                        conversations.Insert(0, changed);
                    else
                        (known.Name, known.Members) = (changed.Name, changed.Members);
                    Changed?.Invoke();
                }
            });
        }

        /// <summary>A message changed: as the conversation's last one too.</summary>
        private void Changed_(ChatMessage message)
        {
            if (Of(message.ConversationId) is ChatConversation conversation && conversation.Last?.Id == message.Id)
            {
                conversation.Last = message;
                Changed?.Invoke();
            }
            MessageChanged?.Invoke(message);
        }

        private void Received(ChatMessage message)
        {
            ChatConversation? conversation = Of(message.ConversationId);
            if (conversation == null)
            {
                // The first message of someone: the conversation comes with it
                conversation = new ChatConversation { Id = message.ConversationId, Kind = message.ConversationId.StartsWith("g-") ? "group" : message.ConversationId == NewsId ? "news" : "direct" };
                if (ChatServiceIds.DirectMembers(message.ConversationId) is (int a, int b))
                    conversation.Members = new() { a, b };
                conversations.Insert(0, conversation);
            }
            if (conversation.Last?.Id == message.Id)
                return;
            conversation.Last = message;
            conversation.ActiveAt = message.SentAt;
            if (message.SenderId != Me)
                conversation.Unread++;
            conversations = conversations.OrderByDescending(c => c.ActiveAt).ToList();
            MessageReceived?.Invoke(message);
            Changed?.Invoke();
        }

        /// <summary>The list from the service: the unread counts, the groups; a message missed shows as the conversation's last.</summary>
        public async Task RefreshAsync()
        {
            try
            {
                using HttpResponseMessage response = await RetroService.SendAsync(HttpMethod.Get, "/chat/conversations");
                if (!response.IsSuccessStatusCode)
                {
                    // An older service, or a server without the chat's volume
                    if ((int)response.StatusCode is 404 or 503)
                        Post(() => { IsAvailable = false; Changed?.Invoke(); });
                    return;
                }
                var fresh = JsonSerializer.Deserialize<List<ChatConversation>>(await response.Content.ReadAsStringAsync()) ?? new();
                // The statuses with the list: away, do not disturb, since when they play
                _ = ActivityService.Instance.ReadStatusesAsync();
                Post(() =>
                {
                    var previous = conversations.ToDictionary(c => c.Id, c => c.Last?.Id);
                    conversations = fresh;
                    IsAvailable = true;
                    // Without the live link, a new last message is a message received
                    foreach (ChatConversation conversation in fresh)
                        if (conversation.Last is ChatMessage last && previous.Count > 0 && previous.GetValueOrDefault(conversation.Id) != last.Id)
                            MessageReceived?.Invoke(last);
                    Changed?.Invoke();
                });
            }
            catch (Exception ex)
            {
                // Offline: the chat waits
                Log.Ignored(ex);
            }
        }

        public async Task<List<ChatMessage>> MessagesAsync(string conversationId, string? before = null) =>
            await RetroService.GetAsync<List<ChatMessage>>($"/chat/conversations/{conversationId}/messages{(before != null ? $"?before={before}" : "")}");

        /// <param name="replyTo">The message answered.</param>
        /// <param name="game">A game shared, or an invitation to play it (the text may be empty then).</param>
        public async Task<ChatMessage> SendTextAsync(string conversationId, string text, string? replyTo = null, ChatGame? game = null)
        {
            typingSaid.Remove(conversationId);
            using HttpResponseMessage response = await RetroService.SendAsync(HttpMethod.Post, $"/chat/conversations/{conversationId}/messages", RetroService.Json(new { text, replyTo, game }));
            return await Sent(response);
        }

        public async Task<ChatMessage> SendPictureAsync(string conversationId, byte[] picture, string? text, string? replyTo = null)
        {
            typingSaid.Remove(conversationId);
            using var content = new ByteArrayContent(picture);
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(text))
                parts.Add($"text={Uri.EscapeDataString(text.Trim())}");
            if (replyTo != null)
                parts.Add($"replyTo={replyTo}");
            string query = parts.Count == 0 ? "" : "?" + string.Join("&", parts);
            using HttpResponseMessage response = await RetroService.SendAsync(HttpMethod.Post, $"/chat/conversations/{conversationId}/pictures{query}", content);
            return await Sent(response);
        }

        private async Task<ChatMessage> Sent(HttpResponseMessage response)
        {
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(ServerError(body) ?? response.ReasonPhrase, null, response.StatusCode);
            ChatMessage message = JsonSerializer.Deserialize<ChatMessage>(body)!;
            // Shown at once, before the live link brings it back
            Post(() => Received(message));
            return message;
        }

        public async Task<ChatMessage> EditAsync(ChatMessage message, string text)
        {
            using HttpResponseMessage response = await RetroService.SendAsync(HttpMethod.Put, $"/chat/conversations/{message.ConversationId}/messages/{message.Id}", RetroService.Json(new { text }));
            return await ChangedOn(response);
        }

        public async Task<ChatMessage> DeleteAsync(ChatMessage message)
        {
            using HttpResponseMessage response = await RetroService.SendAsync(HttpMethod.Delete, $"/chat/conversations/{message.ConversationId}/messages/{message.Id}");
            return await ChangedOn(response);
        }

        /// <summary>Puts the emoji, or takes it away when the player had put it.</summary>
        public async Task<ChatMessage> ReactAsync(ChatMessage message, string emoji)
        {
            using HttpResponseMessage response = await RetroService.SendAsync(HttpMethod.Put, $"/chat/conversations/{message.ConversationId}/messages/{message.Id}/reactions", RetroService.Json(new { emoji }));
            return await ChangedOn(response);
        }

        private async Task<ChatMessage> ChangedOn(HttpResponseMessage response)
        {
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(ServerError(body) ?? response.ReasonPhrase, null, response.StatusCode);
            ChatMessage message = JsonSerializer.Deserialize<ChatMessage>(body)!;
            Post(() => Changed_(message));
            return message;
        }

        /// <summary>The player writes: the others see it a few seconds (said every 3 s at most).</summary>
        public async Task TypingAsync(string conversationId)
        {
            if (conversationId == NewsId || !IsLive || typingSaid.TryGetValue(conversationId, out DateTime said) && DateTime.UtcNow - said < TypingEvery)
                return;
            typingSaid[conversationId] = DateTime.UtcNow;
            try { using var _ = await RetroService.SendAsync(HttpMethod.Post, $"/chat/conversations/{conversationId}/typing"); }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>An administrator's client: a new game of the library in the announcements (the service tells it once).</summary>
        public async Task AnnounceAsync(ChatGame game)
        {
            if (!IsAvailable || LoginManager.Instance.GetCurrentUser()?.Role != Models.PERMISSION_ROLE.ADMIN)
                return;
            try { using var _ = await RetroService.SendAsync(HttpMethod.Post, "/chat/news/games", RetroService.Json(game)); }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private static string? ServerError(string body)
        {
            try { return JsonDocument.Parse(body).RootElement.GetProperty("error").GetString(); }
            catch (Exception) { return null; }
        }

        public async Task MarkReadAsync(string conversationId)
        {
            if (Of(conversationId) is ChatConversation conversation && conversation.Unread > 0)
            {
                conversation.Unread = 0;
                Changed?.Invoke();
            }
            try { using var _ = await RetroService.SendAsync(HttpMethod.Put, $"/chat/conversations/{conversationId}/read"); }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        public async Task<ChatConversation> CreateGroupAsync(string name, IEnumerable<int> members)
        {
            using HttpResponseMessage response = await RetroService.SendAsync(HttpMethod.Post, "/chat/groups", RetroService.Json(new { name, members }));
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(ServerError(body) ?? response.ReasonPhrase, null, response.StatusCode);
            ChatConversation group = JsonSerializer.Deserialize<ChatConversation>(body)!;
            Post(() =>
            {
                if (Of(group.Id) == null)
                    conversations.Insert(0, group);
                Changed?.Invoke();
            });
            return group;
        }

        public async Task AddToGroupAsync(string id, IEnumerable<int> added)
        {
            using HttpResponseMessage response = await RetroService.SendAsync(HttpMethod.Put, $"/chat/groups/{id}", RetroService.Json(new { add = added }));
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(ServerError(await response.Content.ReadAsStringAsync()) ?? response.ReasonPhrase, null, response.StatusCode);
            await RefreshAsync();
        }

        public async Task LeaveGroupAsync(string id)
        {
            using HttpResponseMessage response = await RetroService.SendAsync(HttpMethod.Delete, $"/chat/groups/{id}/members/me");
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(ServerError(await response.Content.ReadAsStringAsync()) ?? response.ReasonPhrase, null, response.StatusCode);
            Post(() =>
            {
                conversations.RemoveAll(c => c.Id == id);
                Changed?.Invoke();
            });
        }

        /// <summary>A picture of a conversation, kept on disk: a picture sent never changes.</summary>
        public async Task<string> PictureFileAsync(string conversationId, string name)
        {
            string file = Path.Combine(PicturesDirectory, conversationId, name);
            if (File.Exists(file))
                return file;
            using HttpResponseMessage response = await RetroService.SendAsync(HttpMethod.Get, $"/chat/conversations/{conversationId}/pictures/{name}");
            response.EnsureSuccessStatusCode();
            byte[] data = await response.Content.ReadAsByteArrayAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string part = $"{file}.{Guid.NewGuid():N}.part";
            await File.WriteAllBytesAsync(part, data);
            File.Move(part, file, overwrite: true);
            return file;
        }

        /// <summary>The chat's pictures on this PC: part of the image cache (Settings).</summary>
        public static string PicturesDirectory => Path.Combine(PlatformInfo.LocalDataDirectory, "chat-pictures");

        private static void Post(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
                action();
            else
                Dispatcher.UIThread.Post(action);
        }
    }

    internal static class ChatServiceIds
    {
        /// <summary>The two players of "d-2-7".</summary>
        public static (int A, int B)? DirectMembers(string id)
        {
            string[] parts = id.Split('-');
            return parts.Length == 3 && parts[0] == "d" && int.TryParse(parts[1], out int a) && int.TryParse(parts[2], out int b) ? (a, b) : null;
        }
    }
}
