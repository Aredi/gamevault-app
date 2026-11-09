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

        [JsonIgnore] public bool IsGroup => Kind == "group";
    }

    /// <summary>
    /// The chat with the other players, kept by the SanctuaryVault service: the conversations, their messages, the live
    /// link (a websocket) that brings a message as it is sent. Without the live link (a proxy without websockets), the
    /// list is asked every 15 s. Off when the service has no chat (an older one): <see cref="IsAvailable"/>.
    /// </summary>
    internal sealed class ChatService
    {
        public static ChatService Instance { get; } = new();

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
        }

        /// <summary>What the live link brought (also used by the tests).</summary>
        internal void Take(string json)
        {
            LiveEvent? live;
            try { live = JsonSerializer.Deserialize<LiveEvent>(json); }
            catch (JsonException ex) { Log.Ignored(ex); return; }
            Post(() =>
            {
                if (live?.Message is ChatMessage message)
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

        private void Received(ChatMessage message)
        {
            ChatConversation? conversation = Of(message.ConversationId);
            if (conversation == null)
            {
                // The first message of someone: the conversation comes with it
                conversation = new ChatConversation { Id = message.ConversationId, Kind = message.ConversationId.StartsWith("g-") ? "group" : "direct" };
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

        public async Task<ChatMessage> SendTextAsync(string conversationId, string text)
        {
            using HttpResponseMessage response = await RetroService.SendAsync(HttpMethod.Post, $"/chat/conversations/{conversationId}/messages", RetroService.Json(new { text }));
            return await Sent(response);
        }

        public async Task<ChatMessage> SendPictureAsync(string conversationId, byte[] picture, string? text)
        {
            using var content = new ByteArrayContent(picture);
            string query = string.IsNullOrWhiteSpace(text) ? "" : $"?text={Uri.EscapeDataString(text.Trim())}";
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
