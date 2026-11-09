using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GameVault.Uploader
{
    /// <summary>
    /// The players' chat: conversations between two players ("d-2-7") or in a group ("g-…"), their messages (a text, a
    /// picture, or both) and the pictures. Everything older than <see cref="Kept"/> goes. The conversations are one JSON
    /// file written whole; each conversation's messages a file of JSON lines, appended; the pictures beside them.
    /// The messages stay in memory once read: 90 days of a friends' server are a few MB.
    /// </summary>
    public sealed partial class ChatStore
    {
        public static readonly TimeSpan Kept = TimeSpan.FromDays(90);
        public const int MaxText = 2000;
        public const int MaxGroupName = 60;
        public const int MaxGroupMembers = 20;
        public const long MaxPicture = 10 * 1024 * 1024;
        /// <summary>The pictures are kept with their own extension only: never served as a page.</summary>
        public static readonly IReadOnlyDictionary<string, string> PictureTypes = new Dictionary<string, string>
        {
            ["png"] = "image/png", ["jpg"] = "image/jpeg", ["gif"] = "image/gif", ["webp"] = "image/webp",
        };

        private readonly string directory;
        private readonly object gate = new();
        private readonly ILogger<ChatStore> logger;
        private List<Conversation>? conversations;
        private readonly Dictionary<string, List<Message>> messages = new();
        private DateTime prunedAt;
        private readonly ChatCipher? cipher;

        public ChatStore(UploaderOptions options, ILogger<ChatStore> logger)
        {
            this.logger = logger;
            directory = options.ChatDirectory;
            Enabled = ProfileStore.IsWritable(directory, out string error);
            if (!Enabled)
            {
                logger.LogWarning("The chat is off: {Directory} can not be written ({Error}). Mount a volume there to offer it.", directory, error);
                return;
            }
            // Beside the folder, not in it: a copy of the chat's folder alone can not be read
            string keyFile = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar))!, Path.GetFileName(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)) + ".key");
            try
            {
                cipher = ChatCipher.Load(options.ChatKey, keyFile);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or IOException or UnauthorizedAccessException)
            {
                Enabled = false;
                logger.LogError(ex, "The chat is off: its key (CHAT_KEY or {KeyFile}) can not be used", keyFile);
            }
        }

        private ChatCipher Cipher => cipher ?? throw new InvalidOperationException("The chat is off.");

        /// <summary>A line or a file of the chat as it is on the disk: sealed, or plain when written before the sealing.</summary>
        private string Opened(string stored)
        {
            string text = stored.Trim();
            return text.StartsWith('{') || text.StartsWith('[') ? text : Cipher.OpenText(text);
        }

        public bool Enabled { get; }

        public sealed class Conversation
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            /// <summary>"direct" or "group".</summary>
            [JsonPropertyName("kind")] public string Kind { get; set; } = "direct";
            [JsonPropertyName("name")] public string? Name { get; set; }
            [JsonPropertyName("members")] public List<int> Members { get; set; } = new();
            [JsonPropertyName("createdBy")] public int CreatedBy { get; set; }
            [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
            /// <summary>The last message, or the creation: the conversations are listed by it.</summary>
            [JsonPropertyName("activeAt")] public DateTime ActiveAt { get; set; }
            /// <summary>When each member last read it: the messages after are unread.</summary>
            [JsonPropertyName("readAt")] public Dictionary<int, DateTime> ReadAt { get; set; } = new();
        }

        public sealed class Message
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            [JsonPropertyName("conversationId")] public string ConversationId { get; set; } = "";
            [JsonPropertyName("senderId")] public int SenderId { get; set; }
            [JsonPropertyName("senderName")] public string SenderName { get; set; } = "";
            [JsonPropertyName("text")] public string? Text { get; set; }
            /// <summary>The picture's file name, in the conversation's pictures.</summary>
            [JsonPropertyName("picture")] public string? Picture { get; set; }
            [JsonPropertyName("sentAt")] public DateTime SentAt { get; set; }
        }

        /// <summary>A conversation as a member sees it in their list.</summary>
        public sealed record Summary(Conversation Conversation, Message? Last, int Unread);

        [GeneratedRegex("^(d-[0-9]{1,10}-[0-9]{1,10}|g-[0-9a-f]{16})$")]
        private static partial Regex IdPattern();

        [GeneratedRegex("^[0-9a-f]{24}\\.(png|jpg|gif|webp)$")]
        private static partial Regex PicturePattern();

        public static bool IsValidId(string? id) => id != null && IdPattern().IsMatch(id);
        public static bool IsValidPicture(string? name) => name != null && PicturePattern().IsMatch(name);

        /// <summary>The conversation of two players: the same id whoever writes first.</summary>
        public static string DirectId(int a, int b) => $"d-{Math.Min(a, b)}-{Math.Max(a, b)}";

        /// <summary>The players of a conversation between two, from its id; null for a group or a wrong id.</summary>
        public static (int A, int B)? DirectMembers(string id)
        {
            if (!IsValidId(id) || !id.StartsWith("d-", StringComparison.Ordinal))
                return null;
            string[] parts = id.Split('-');
            return int.TryParse(parts[1], out int a) && int.TryParse(parts[2], out int b) && a > 0 && b > 0 && a < b ? (a, b) : null;
        }

        /// <summary>Its type from its first bytes: a file named .png that is a page is no picture.</summary>
        public static string? PictureType(ReadOnlySpan<byte> start)
        {
            if (start.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
                return "png";
            if (start.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
                return "jpg";
            if (start.StartsWith("GIF87a"u8) || start.StartsWith("GIF89a"u8))
                return "gif";
            if (start.Length >= 12 && start[..4].SequenceEqual("RIFF"u8) && start[8..12].SequenceEqual("WEBP"u8))
                return "webp";
            return null;
        }

        private string ConversationsFile => Path.Combine(directory, "conversations.json");
        private string MessagesFile(string id) => Path.Combine(directory, "messages", $"{id}.jsonl");
        public string PicturesDirectory(string id) => Path.Combine(directory, "pictures", id);

        private List<Conversation> All()
        {
            if (conversations != null)
                return conversations;
            try
            {
                conversations = File.Exists(ConversationsFile)
                    ? JsonSerializer.Deserialize<List<Conversation>>(Opened(File.ReadAllText(ConversationsFile))) ?? new()
                    : new();
            }
            catch (CryptographicException ex)
            {
                // Another key: nothing is written over what it sealed (the chat answers an error until the key is back)
                logger.LogError(ex, "The chat's files were sealed with another key than CHAT_KEY or chat.key");
                throw new InvalidOperationException("The chat's key does not open its files.", ex);
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                // A damaged file must not take the chat down: it is kept aside, the chat starts again
                logger.LogError(ex, "The chat's conversations could not be read; the file is kept as conversations.json.broken");
                File.Copy(ConversationsFile, ConversationsFile + ".broken", overwrite: true);
                conversations = new();
            }
            return conversations;
        }

        private void WriteConversations()
        {
            string part = ConversationsFile + ".part";
            File.WriteAllText(part, Cipher.SealText(JsonSerializer.Serialize(All())));
            File.Move(part, ConversationsFile, overwrite: true);
        }

        private List<Message> MessagesOf(string id)
        {
            if (messages.TryGetValue(id, out List<Message>? known))
                return known;
            var list = new List<Message>();
            string file = MessagesFile(id);
            if (File.Exists(file))
                foreach (string line in File.ReadLines(file))
                {
                    // A line cut by a crash is passed: the others are whole
                    try
                    {
                        if (line.Length > 0 && JsonSerializer.Deserialize<Message>(Opened(line)) is Message message)
                            list.Add(message);
                    }
                    catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException) { }
                }
            return messages[id] = list;
        }

        /// <summary>The conversation, when this player is in it; a conversation between two exists before its first message.</summary>
        public Conversation? Of(string id, int userId)
        {
            lock (gate)
            {
                Prune();
                if (All().FirstOrDefault(c => c.Id == id) is Conversation known)
                    return known.Members.Contains(userId) ? known : null;
                return DirectMembers(id) is (int a, int b) && (a == userId || b == userId)
                    ? new Conversation { Id = id, Kind = "direct", Members = new() { a, b }, CreatedAt = DateTime.UtcNow, ActiveAt = DateTime.UtcNow }
                    : null;
            }
        }

        /// <summary>The player's conversations, the last active first, with their last message and what they did not read.</summary>
        public List<Summary> ListFor(int userId)
        {
            lock (gate)
            {
                Prune();
                return All().Where(c => c.Members.Contains(userId)).OrderByDescending(c => c.ActiveAt).Select(c =>
                {
                    List<Message> list = MessagesOf(c.Id);
                    DateTime read = c.ReadAt.GetValueOrDefault(userId);
                    return new Summary(c, list.LastOrDefault(), list.Count(m => m.SentAt > read && m.SenderId != userId));
                }).ToList();
            }
        }

        /// <summary>The messages before one (none: the last ones), oldest first.</summary>
        public List<Message> Messages(string id, string? before, int count)
        {
            lock (gate)
            {
                List<Message> list = MessagesOf(id);
                int end = before == null ? list.Count : list.FindIndex(m => m.Id == before);
                if (end < 0)
                    end = list.Count;
                return list.GetRange(Math.Max(0, end - count), Math.Min(count, end));
            }
        }

        public Conversation CreateGroup(int userId, string name, IEnumerable<int> members)
        {
            lock (gate)
            {
                var group = new Conversation
                {
                    Id = "g-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(),
                    Kind = "group",
                    Name = name,
                    Members = members.Append(userId).Where(m => m > 0).Distinct().Take(MaxGroupMembers).ToList(),
                    CreatedBy = userId,
                    CreatedAt = DateTime.UtcNow,
                    ActiveAt = DateTime.UtcNow,
                };
                All().Add(group);
                WriteConversations();
                return group;
            }
        }

        /// <summary>A member renames the group or adds players to it.</summary>
        public Conversation? ChangeGroup(string id, int userId, string? name, IEnumerable<int>? added)
        {
            lock (gate)
            {
                if (All().FirstOrDefault(c => c.Id == id && c.Kind == "group" && c.Members.Contains(userId)) is not Conversation group)
                    return null;
                if (name != null)
                    group.Name = name;
                if (added != null)
                    group.Members = group.Members.Concat(added.Where(m => m > 0)).Distinct().Take(MaxGroupMembers).ToList();
                WriteConversations();
                return group;
            }
        }

        /// <summary>The player leaves the group; the last one to leave takes it away, with its messages.</summary>
        public Conversation? Leave(string id, int userId)
        {
            lock (gate)
            {
                if (All().FirstOrDefault(c => c.Id == id && c.Kind == "group" && c.Members.Contains(userId)) is not Conversation group)
                    return null;
                group.Members.Remove(userId);
                group.ReadAt.Remove(userId);
                if (group.Members.Count == 0)
                    Delete(group);
                WriteConversations();
                return group;
            }
        }

        /// <param name="pictureType">"png", ...: the type of <paramref name="picture"/>, kept sealed under the message's name.</param>
        public Message Add(Conversation conversation, int userId, string userName, string? text, string? pictureType = null, byte[]? picture = null)
        {
            lock (gate)
            {
                Prune();
                // Two first messages at once: one conversation (the same id), kept once
                conversation = All().FirstOrDefault(c => c.Id == conversation.Id) ?? conversation;
                var message = new Message
                {
                    Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(),
                    ConversationId = conversation.Id,
                    SenderId = userId,
                    SenderName = userName,
                    Text = text,
                    SentAt = DateTime.UtcNow,
                };
                if (pictureType != null && picture != null)
                {
                    message.Picture = $"{message.Id}.{pictureType}";
                    Directory.CreateDirectory(PicturesDirectory(conversation.Id));
                    File.WriteAllBytes(Path.Combine(PicturesDirectory(conversation.Id), message.Picture), Cipher.Seal(picture));
                }
                // A conversation between two is kept from its first message
                if (!All().Contains(conversation))
                    All().Add(conversation);
                conversation.ActiveAt = message.SentAt;
                // What one writes, one has read
                conversation.ReadAt[userId] = message.SentAt;
                // Read before the line is added: a first read after it would count the message twice
                List<Message> list = MessagesOf(conversation.Id);
                Directory.CreateDirectory(Path.GetDirectoryName(MessagesFile(conversation.Id))!);
                File.AppendAllText(MessagesFile(conversation.Id), Cipher.SealText(JsonSerializer.Serialize(message)) + "\n");
                list.Add(message);
                WriteConversations();
                return message;
            }
        }

        public void MarkRead(Conversation conversation, int userId)
        {
            lock (gate)
            {
                if (All().FirstOrDefault(c => c.Id == conversation.Id) is not Conversation kept)
                    return;
                kept.ReadAt[userId] = DateTime.UtcNow;
                WriteConversations();
            }
        }

        /// <summary>A picture of the conversation, opened; null when there is none (or it can not be opened).</summary>
        public byte[]? Picture(string id, string name)
        {
            string file = Path.Combine(PicturesDirectory(id), name);
            if (!IsValidPicture(name) || !File.Exists(file))
                return null;
            byte[] stored = File.ReadAllBytes(file);
            try { return Cipher.Open(stored); }
            catch (CryptographicException) { return PictureType(stored) != null ? stored : null; }// one written before the sealing
        }

        private void Delete(Conversation conversation)
        {
            All().Remove(conversation);
            messages.Remove(conversation.Id);
            try { File.Delete(MessagesFile(conversation.Id)); } catch (IOException) { }
            try { if (Directory.Exists(PicturesDirectory(conversation.Id))) Directory.Delete(PicturesDirectory(conversation.Id), true); } catch (IOException) { }
        }

        /// <summary>Once an hour: the messages and pictures older than <see cref="Kept"/> go, and a silent conversation with them.</summary>
        private void Prune()
        {
            if (DateTime.UtcNow - prunedAt < TimeSpan.FromHours(1))
                return;
            prunedAt = DateTime.UtcNow;
            DateTime limit = DateTime.UtcNow - Kept;
            bool changed = false;
            foreach (Conversation conversation in All().ToList())
            {
                if (conversation.ActiveAt < limit)
                {
                    Delete(conversation);
                    changed = true;
                    continue;
                }
                List<Message> list = MessagesOf(conversation.Id);
                List<Message> old = list.Where(m => m.SentAt < limit).ToList();
                if (old.Count == 0)
                    continue;
                foreach (Message message in old.Where(m => m.Picture != null))
                    try { File.Delete(Path.Combine(PicturesDirectory(conversation.Id), message.Picture!)); } catch (IOException) { }
                list.RemoveAll(m => m.SentAt < limit);
                string file = MessagesFile(conversation.Id), part = file + ".part";
                File.WriteAllLines(part, list.Select(m => Cipher.SealText(JsonSerializer.Serialize(m))));
                File.Move(part, file, overwrite: true);
            }
            if (changed)
                WriteConversations();
        }

        /// <summary>For the tests: what was sent long ago, as if the hour had passed.</summary>
        internal void PruneNow()
        {
            lock (gate)
            {
                prunedAt = default;
                Prune();
            }
        }

        /// <summary>For the tests: a message dated in the past.</summary>
        internal void Backdate(string conversationId, TimeSpan ago)
        {
            lock (gate)
            {
                foreach (Message message in MessagesOf(conversationId))
                    message.SentAt -= ago;
                if (All().FirstOrDefault(c => c.Id == conversationId) is Conversation conversation)
                    conversation.ActiveAt -= ago;
            }
        }
    }
}
