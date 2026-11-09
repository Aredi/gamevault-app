using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameVault.Core.Library;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>A friend or a group in the chat's list.</summary>
    internal sealed class ChatEntry : ViewModelBase
    {
        public string ConversationId { get; init; } = "";
        public User? User { get; init; }
        public bool IsGroup { get; init; }
        /// <summary>The administrators' announcements.</summary>
        public bool IsNews { get; init; }
        /// <summary>A group's icon, or the announcements'.</summary>
        public bool HasIcon => IsGroup || IsNews;

        private string title = "";
        public string Title { get => title; set { title = value; OnPropertyChanged(); } }
        private string subtitle = "";
        public string Subtitle { get => subtitle; set { subtitle = value; OnPropertyChanged(); } }
        private int unread;
        public int Unread { get => unread; set { unread = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnread)); } }
        public bool HasUnread => Unread > 0;
        private IBrush? dot;
        /// <summary>Green online, the accent playing, none offline.</summary>
        public IBrush? Dot { get => dot; set { dot = value; OnPropertyChanged(); } }
        private double opacity = 1;
        public double Opacity { get => opacity; set { opacity = value; OnPropertyChanged(); } }
        private bool isPicked;
        /// <summary>Chosen for a new group, or to be added to one.</summary>
        public bool IsPicked { get => isPicked; set { isPicked = value; OnPropertyChanged(); } }
        /// <summary>Playing first, then online, then offline.</summary>
        public int Rank { get; set; }
    }

    /// <summary>An emoji under a message, and how many put it.</summary>
    internal sealed class ChatReactionChip
    {
        public string Emoji { get; init; } = "";
        public int Count { get; init; }
        /// <summary>The player put it: a click takes it away.</summary>
        public bool IsMine { get; init; }
        public string Who { get; init; } = "";
        public string Text => $"{Emoji} {Count}";
    }

    /// <summary>A game to share or to invite to, as the picker lists it.</summary>
    internal sealed class ChatGamePick
    {
        public string Title { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public Game? Pc { get; init; }
        public RetroGame? Retro { get; init; }
        public ChatGame Shared(bool invite) => Pc != null ? ChatGame.Of(Pc, invite) : ChatGame.Of(Retro!, invite);
    }

    /// <summary>A message as the conversation shows it.</summary>
    internal sealed class ChatMessageRow : ViewModelBase
    {
        public ChatMessageRow(ChatMessage message) => Message = message;

        public ChatMessage Message { get; }
        public bool IsMine => Message.SenderId == ChatService.Me;
        public string Sender => Message.SenderName;
        public bool IsDeleted => Message.Deleted;
        public string? Text => Message.Deleted ? Loc.T("Message deleted") : Message.Text;
        public bool HasPicture => Message.Picture != null;
        public string Time => Message.SentAt.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);

        public bool HasReply => Message.ReplyTo != null && !Message.Deleted;
        public string ReplySender => Message.ReplyTo?.SenderName ?? "";
        public string ReplyText => Message.ReplyTo?.Text ?? Loc.T("Message deleted");

        public ChatGame? Game => Message.Deleted ? null : Message.Game;
        public bool HasGame => Game != null;
        /// <summary>"Zoé invites you to play", "Invitation sent", or the platform for a game shared.</summary>
        public string? GameHeader => Game is not { Invite: true } ? null : IsMine ? Loc.T("🎮 Invitation sent") : Loc.F("🎮 {0} invites you to play", Sender);
        private string? gameSubtitle;
        public string? GameSubtitle { get => gameSubtitle; set { gameSubtitle = value; OnPropertyChanged(); } }
        private Game? pcGame;
        /// <summary>The library's game, read from the server (its cover).</summary>
        public Game? PcGame { get => pcGame; set { pcGame = value; OnPropertyChanged(); } }
        private Bitmap? retroCover;
        public Bitmap? RetroCover { get => retroCover; set { retroCover = value; OnPropertyChanged(); } }

        public List<ChatReactionChip> Reactions { get; init; } = new();
        public bool HasReactions => Reactions.Count > 0;

        private string? day;
        /// <summary>"Today", "Yesterday", "9 November": above the first message of a day.</summary>
        public string? Day { get => day; set { day = value; OnPropertyChanged(); } }
        private bool showsSender;
        /// <summary>In a group, above the first of someone's messages in a row.</summary>
        public bool ShowsSender { get => showsSender; set { showsSender = value; OnPropertyChanged(); } }
        private bool showsTime;
        /// <summary>Under the last of a row of messages.</summary>
        public bool ShowsTime { get => showsTime; set { showsTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowsFooter)); } }
        private string? seen;
        /// <summary>"Seen", "Seen by Zoé, Malik": under the player's last message.</summary>
        public string? Seen { get => seen; set { seen = value; OnPropertyChanged(); OnPropertyChanged(nameof(Footer)); OnPropertyChanged(nameof(ShowsFooter)); } }
        public bool ShowsFooter => ShowsTime || Seen != null;
        /// <summary>"21:04 · edited · Seen".</summary>
        public string Footer => string.Join(" · ", new[] { Time, Message.EditedAt != null && !Message.Deleted ? Loc.T("edited") : null, Seen }.Where(p => p != null));
        private Bitmap? picture;
        public Bitmap? Picture { get => picture; set { picture = value; OnPropertyChanged(); } }
        public string? PictureFile { get; set; }
    }

    internal sealed class ChatPanelModel : ViewModelBase
    {
        public ObservableCollection<ChatEntry> Groups { get; } = new();
        public ObservableCollection<ChatEntry> Friends { get; } = new();
        public ObservableCollection<ChatEntry> Addable { get; } = new();
        public ObservableCollection<ChatMessageRow> Messages { get; } = new();

        public bool HasGroups => Groups.Count > 0;
        public bool HasMessages => Messages.Count > 0;

        private string friendsHeader = "";
        public string FriendsHeader { get => friendsHeader; set { friendsHeader = value; OnPropertyChanged(); } }
        private string? statusText;
        public string? StatusText { get => statusText; set { statusText = value; OnPropertyChanged(); } }
        private ChatEntry? open;
        public ChatEntry? Open { get => open; set { open = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsConversationOpen)); OnPropertyChanged(nameof(EmptyText)); } }
        public bool IsConversationOpen => Open != null;
        public string EmptyText => Open?.IsNews == true ? Loc.T("No announcement yet.") : Loc.T("No message yet. Say hello!");
        private bool isMakingGroup;
        public bool IsMakingGroup { get => isMakingGroup; set { isMakingGroup = value; OnPropertyChanged(); } }
        private bool isAddingPlayers;
        public bool IsAddingPlayers { get => isAddingPlayers; set { isAddingPlayers = value; OnPropertyChanged(); } }
        private bool hasOlder;
        public bool HasOlder { get => hasOlder; set { hasOlder = value; OnPropertyChanged(); } }
        private bool isSending;
        public bool IsSending { get => isSending; set { isSending = value; OnPropertyChanged(); } }
        private string? typingText;
        /// <summary>"Zoé is writing…" in the open conversation.</summary>
        public string? TypingText { get => typingText; set { typingText = value; OnPropertyChanged(); } }
        private ChatMessageRow? replyingTo;
        public ChatMessageRow? ReplyingTo { get => replyingTo; set { replyingTo = value; OnPropertyChanged(); OnPropertyChanged(nameof(DraftNote)); OnPropertyChanged(nameof(HasDraftNote)); } }
        private ChatMessageRow? editing;
        /// <summary>The player's message written again: the next Enter saves it.</summary>
        public ChatMessageRow? Editing { get => editing; set { editing = value; OnPropertyChanged(); OnPropertyChanged(nameof(DraftNote)); OnPropertyChanged(nameof(HasDraftNote)); } }
        /// <summary>Above the text box: "Answer to Zoé: …", "Editing the message".</summary>
        public string? DraftNote => Editing != null ? Loc.T("Editing the message") : ReplyingTo is ChatMessageRow row ? Loc.F("Answer to {0}: {1}", row.Sender, Preview(row.Message)) : null;
        public bool HasDraftNote => DraftNote != null;
        private bool canWrite = true;
        /// <summary>The announcements: only the administrators write.</summary>
        public bool CanWrite { get => canWrite; set { canWrite = value; OnPropertyChanged(); } }

        public ObservableCollection<ChatGamePick> GamePicks { get; } = new();
        private bool isPickingGame;
        public bool IsPickingGame { get => isPickingGame; set { isPickingGame = value; OnPropertyChanged(); } }
        private bool pickInvites;
        /// <summary>The game picked is an invitation to play it, else shared.</summary>
        public bool PickInvites { get => pickInvites; set { pickInvites = value; OnPropertyChanged(); OnPropertyChanged(nameof(PickTitle)); } }
        public string PickTitle => PickInvites ? Loc.T("Invite to play") : Loc.T("Share a game");

        private string myName = "";
        public string MyName { get => myName; set { myName = value; OnPropertyChanged(); } }
        private User? me;
        public User? Me { get => me; set { me = value; OnPropertyChanged(); } }
        private string myStatus = "";
        public string MyStatus { get => myStatus; set { myStatus = value; OnPropertyChanged(); } }
        private IBrush? myDot;
        public IBrush? MyDot { get => myDot; set { myDot = value; OnPropertyChanged(); } }

        private ChatEntry? news;
        /// <summary>The announcements, when there are some (or the player is an administrator).</summary>
        public ChatEntry? News { get => news; set { news = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNews)); } }
        public bool HasNews => News != null;

        internal static string Preview(ChatMessage message) =>
            message.Deleted ? Loc.T("Message deleted")
            : message.Game is ChatGame game && string.IsNullOrEmpty(message.Text) ? (game.Invite ? Loc.F("🎮 Invitation: {0}", game.Title) : $"🎮 {game.Title}")
            : string.IsNullOrEmpty(message.Text) ? Loc.T("📷 Picture") : message.Text!.ReplaceLineEndings(" ");

        private Bitmap? draftPicture;
        /// <summary>A picture pasted, dropped or chosen: sent with the next message.</summary>
        public Bitmap? DraftPicture { get => draftPicture; set { draftPicture = value; OnPropertyChanged(); } }
        public byte[]? DraftPictureData { get; set; }

        public void Changed() { OnPropertyChanged(nameof(HasGroups)); OnPropertyChanged(nameof(HasMessages)); }
    }

    /// <summary>
    /// The chat, on the right of the window as Steam's friends: the players online and what they play, the groups, and a
    /// conversation with texts, emojis and pictures (pasted, dropped or chosen).
    /// </summary>
    public partial class ChatPanel : UserControl
    {
        private const int PageSize = 50;
        private const int MaxPictureBytes = 10 * 1024 * 1024;

        /// <summary>The faces and signs a game night needs, in the order of a phone's keyboard.</summary>
        internal static readonly string[] Emojis =
        {
            "😀", "😂", "🤣", "😊", "😍", "😎", "🤔", "😅", "😭", "😡", "😴", "🥳",
            "👍", "👎", "👏", "🙏", "💪", "🤝", "👀", "🔥", "💯", "❤️", "💀", "🎉",
            "🎮", "🕹️", "🏆", "🥇", "⚔️", "🛡️", "💣", "🚀", "🍕", "🍺", "☕", "🌙",
            "✅", "❌", "⏳", "⚡", "🤯", "😱", "🫡", "😏", "🙃", "😬", "👋", "GG",
        };

        /// <summary>The quick reactions of a message's menu.</summary>
        internal static readonly string[] Reactions = { "👍", "❤️", "😂", "😮", "😢", "🔥", "🎉", "GG" };
        private static readonly TimeSpan TypingShown = TimeSpan.FromSeconds(5);

        private readonly ChatPanelModel model = new();
        private readonly Dictionary<int, User> users = new();
        private DateTime usersLoadedAt;
        /// <summary>Who writes where, until when (the live link says it every 3 s while they write).</summary>
        private readonly Dictionary<(string Conversation, int User), (string Name, DateTime Until)> typing = new();
        private readonly DispatcherTimer typingTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        /// <summary>Each minute: "for 25 min" goes on.</summary>
        private readonly DispatcherTimer minuteTimer = new() { Interval = TimeSpan.FromMinutes(1) };
        private readonly DispatcherTimer gameSearchDelay = new() { Interval = TimeSpan.FromMilliseconds(300) };
        private int gameSearchGeneration;
        private static readonly Dictionary<int, Game> pcGames = new();

        internal ChatPanelModel Model => model;

        public ChatPanel()
        {
            InitializeComponent();
            DataContext = model;
            uiEmojis.ItemsSource = Emojis;
            // Enter sends, Shift+Enter goes to the line; Ctrl+V takes a picture: before the text box handles them
            uiDraft.AddHandler(KeyDownEvent, Draft_KeyDown, RoutingStrategies.Tunnel);
            uiConversation.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None);
            uiConversation.AddHandler(DragDrop.DropEvent, Drop);
            ChatService.Instance.Changed += Rebuild;
            ChatService.Instance.MessageReceived += Received;
            ChatService.Instance.MessageChanged += MessageChanged;
            ChatService.Instance.Typing += Typing;
            ChatService.Instance.Read += id =>
            {
                if (model.Open?.ConversationId == id)
                    Arrange();
            };
            typingTimer.Tick += (_, _) => ShowTyping();
            minuteTimer.Tick += (_, _) => Rebuild();
            minuteTimer.Start();
            gameSearchDelay.Tick += async (_, _) =>
            {
                gameSearchDelay.Stop();
                await SearchGamesAsync(uiGameSearch.Text ?? "");
            };
            uiGameSearch.TextChanged += (_, _) =>
            {
                gameSearchDelay.Stop();
                gameSearchDelay.Start();
            };
            uiDraft.TextChanged += (_, _) =>
            {
                if (model.Open is ChatEntry open && model.Editing == null && !string.IsNullOrWhiteSpace(uiDraft.Text))
                    _ = ChatService.Instance.TypingAsync(open.ConversationId);
            };
            // The inputs in the window: "Away" where the system does not say how long nobody touched the PC
            AttachedToVisualTree += (_, _) =>
            {
                if (TopLevel.GetTopLevel(this) is TopLevel top)
                {
                    top.AddHandler(PointerMovedEvent, (_, _) => gamevault.Helper.Platform.IdleTime.InputHere(), RoutingStrategies.Tunnel, handledEventsToo: true);
                    top.AddHandler(KeyDownEvent, (_, _) => gamevault.Helper.Platform.IdleTime.InputHere(), RoutingStrategies.Tunnel, handledEventsToo: true);
                }
            };
            ActivityService.Instance.Changed += async () =>
            {
                await Converter.PresenceConverter.LoadTitlesAsync();
                Rebuild();
            };
            AttachedToVisualTree += async (_, _) =>
            {
                await LoadUsersAsync();
                Rebuild();
            };
        }

        /// <summary>The players of the server: their names and avatars (asked again after 10 minutes, a new player may have come).</summary>
        internal async Task LoadUsersAsync(bool again = false)
        {
            if (!again && users.Count > 0 && DateTime.UtcNow - usersLoadedAt < TimeSpan.FromMinutes(10))
                return;
            try
            {
                var all = JsonSerializer.Deserialize<User[]>(await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/users")) ?? Array.Empty<User>();
                users.Clear();
                foreach (User user in all.Where(u => u.DeletedAt == null && u.Activated != false))
                    users[user.ID] = user;
                usersLoadedAt = DateTime.UtcNow;
                await Converter.PresenceConverter.LoadTitlesAsync();
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                model.StatusText = Loc.T("The players could not be loaded.");
            }
        }

        private string NameOf(int userId) => users.TryGetValue(userId, out User? user) ? user.Username : "…";

        /// <summary>The list again: presence, unread messages, groups.</summary>
        internal void Rebuild()
        {
            // The button and its badge first: they show even when the panel is closed
            int unread = ChatService.Instance.TotalUnread;
            MainWindowViewModel.Instance.ChatBadge = unread > 0 ? (unread > 99 ? "99+" : unread.ToString()) : "";
            MainWindowViewModel.Instance.IsChatAvailable = ChatService.Instance.IsAvailable;
            int me = ChatService.Me;
            var conversations = ChatService.Instance.Conversations;
            model.StatusText = ChatService.Instance.IsAvailable || users.Count == 0 ? model.StatusText : null;

            // The announcements: when there are some, or for an administrator (who writes the first one)
            bool admin = LoginManager.Instance.GetCurrentUser()?.Role == PERMISSION_ROLE.ADMIN;
            ChatConversation? news = ChatService.Instance.Of(ChatService.NewsId);
            if (news == null && !admin)
                model.News = null;
            else
            {
                model.News ??= new ChatEntry { ConversationId = ChatService.NewsId, IsNews = true, Title = Loc.T("Announcements") };
                model.News.Unread = news?.Unread ?? 0;
                model.News.Subtitle = news?.Last is ChatMessage lastNews ? Preview(lastNews) : Loc.T("The server's news");
            }
            if (LoginManager.Instance.GetCurrentUser() is User current)
            {
                model.Me = current;
                model.MyName = current.Username;
                (model.MyStatus, model.MyDot) = ActivityService.ChosenState switch
                {
                    "away" => (Loc.T("Away"), Converter.PresenceConverter.Away),
                    "busy" => (Loc.T("Do not disturb"), Converter.PresenceConverter.Busy),
                    "invisible" => (Loc.T("Invisible"), (IBrush?)null),
                    _ => (Loc.T("Online"), Converter.PresenceConverter.Online),
                };
            }

            var groups = conversations.Where(c => c.IsGroup).ToList();
            Sync(model.Groups, groups.Select(c => c.Id).ToList(), id => new ChatEntry { ConversationId = id, IsGroup = true });
            foreach (ChatEntry entry in model.Groups)
            {
                ChatConversation group = groups.First(c => c.Id == entry.ConversationId);
                entry.Title = group.Name ?? Loc.T("Group");
                entry.Unread = group.Unread;
                entry.Subtitle = TypingIn(group.Id) ?? (group.Unread > 0 && group.Last != null ? Preview(group.Last)
                    : string.Join(", ", group.Members.Where(m => m != me).Select(NameOf)));
            }

            var friends = users.Values.Where(u => u.ID != me).ToList();
            var rows = friends.Select(user =>
            {
                ChatEntry entry = model.Friends.FirstOrDefault(f => f.User?.ID == user.ID) ?? new ChatEntry { ConversationId = ChatService.DirectId(user.ID), User = user };
                ChatConversation? direct = ChatService.Instance.Of(entry.ConversationId);
                entry.Title = user.Username;
                entry.Unread = direct?.Unread ?? 0;
                // A library game or a retro game (RetroArch, Eden...), and for how long; else away, do not disturb...
                var (presence, dot, rank) = Converter.PresenceConverter.Describe(user.ID);
                bool online = dot != null;
                entry.Rank = rank;
                entry.Subtitle = TypingIn(entry.ConversationId, user.ID) ?? (entry.Unread > 0 && direct?.Last != null ? Preview(direct.Last) : presence);
                entry.Dot = dot;
                entry.Opacity = !online && entry.Unread == 0 ? 0.6 : 1;
                return (entry, active: direct?.ActiveAt ?? DateTime.MinValue);
            })
                // Who plays, then who is online, then the last ones talked to, then by name
                .OrderBy(r => r.entry.Rank).ThenByDescending(r => r.entry.HasUnread).ThenByDescending(r => r.active).ThenBy(r => r.entry.Title, StringComparer.CurrentCultureIgnoreCase)
                .Select(r => r.entry).ToList();
            if (!rows.SequenceEqual(model.Friends))
            {
                model.Friends.Clear();
                foreach (ChatEntry row in rows)
                    model.Friends.Add(row);
            }
            int online = rows.Count(r => r.Rank < 3);
            model.FriendsHeader = Loc.Plural(online, "FRIENDS · {0} ONLINE", "FRIENDS · {0} ONLINE");
            if (model.Open is ChatEntry open)
            {
                if (open.IsNews)
                    open.Subtitle = Loc.T("The server's news");
                else if (open.IsGroup && model.Groups.All(g => g.ConversationId != open.ConversationId))
                    model.Open = null;// left on another PC
                else if (open.IsGroup)
                    open.Subtitle = string.Join(", ", (ChatService.Instance.Of(open.ConversationId)?.Members ?? new()).Where(m => m != me).Select(NameOf));
                else if (model.Friends.FirstOrDefault(f => f.ConversationId == open.ConversationId) is ChatEntry friend && friend.User != null)
                    open.Subtitle = Converter.PresenceConverter.Describe(friend.User.ID).Text;
            }
            model.Changed();
        }

        private static void Sync(ObservableCollection<ChatEntry> entries, List<string> ids, Func<string, ChatEntry> make)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
                if (!ids.Contains(entries[i].ConversationId))
                    entries.RemoveAt(i);
            for (int i = 0; i < ids.Count; i++)
            {
                int at = entries.ToList().FindIndex(e => e.ConversationId == ids[i]);
                if (at < 0)
                    entries.Insert(i, make(ids[i]));
                else if (at != i)
                    entries.Move(at, i);
            }
        }

        private static string Preview(ChatMessage message) => ChatPanelModel.Preview(message);

        #region Conversation
        private async void News_Click(object? sender, RoutedEventArgs e)
        {
            if (model.News is ChatEntry news)
                await OpenAsync(news);
        }

        private async void Entry_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is ChatEntry entry)
                await OpenAsync(entry);
        }

        /// <summary>A conversation: its last messages, read.</summary>
        internal async Task OpenAsync(ChatEntry entry)
        {
            model.IsMakingGroup = model.IsAddingPlayers = model.IsPickingGame = false;
            model.ReplyingTo = model.Editing = null;
            model.TypingText = null;
            model.Open = new ChatEntry { ConversationId = entry.ConversationId, User = entry.User, IsGroup = entry.IsGroup, IsNews = entry.IsNews, Title = entry.Title, Subtitle = entry.Unread > 0 ? "" : entry.Subtitle };
            model.CanWrite = !entry.IsNews || LoginManager.Instance.GetCurrentUser()?.Role == PERMISSION_ROLE.ADMIN;
            model.Messages.Clear();
            model.HasOlder = false;
            DropDraftPicture();
            Rebuild();
            try
            {
                List<ChatMessage> messages = await ChatService.Instance.MessagesAsync(entry.ConversationId);
                if (model.Open?.ConversationId != entry.ConversationId)
                    return;
                foreach (ChatMessage message in messages)
                    Show(message);
                model.HasOlder = messages.Count == PageSize;
                Arrange();
                ScrollToEnd();
                await ChatService.Instance.MarkReadAsync(entry.ConversationId);
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex);
            }
            uiDraft.Focus();
        }

        private async void Older_Click(object? sender, RoutedEventArgs e)
        {
            if (model.Open is not ChatEntry open || model.Messages.FirstOrDefault() is not ChatMessageRow first)
                return;
            try
            {
                List<ChatMessage> older = await ChatService.Instance.MessagesAsync(open.ConversationId, first.Message.Id);
                for (int i = older.Count - 1; i >= 0; i--)
                    if (model.Messages.All(m => m.Message.Id != older[i].Id))
                        model.Messages.Insert(0, Row(older[i]));
                model.HasOlder = older.Count == PageSize;
                Arrange();
            }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
        }

        private void Back_Click(object? sender, RoutedEventArgs e)
        {
            model.Open = null;
            model.IsAddingPlayers = model.IsPickingGame = false;
            model.ReplyingTo = model.Editing = null;
            Rebuild();
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => MainWindowViewModel.Instance.IsChatOpen = false;

        private ChatMessageRow Row(ChatMessage message, ChatMessageRow? before = null)
        {
            int me = ChatService.Me;
            var row = new ChatMessageRow(message)
            {
                Reactions = (message.Reactions ?? new()).Where(r => r.Value.Count > 0).Select(r => new ChatReactionChip
                {
                    Emoji = r.Key, Count = r.Value.Count, IsMine = r.Value.Contains(me),
                    Who = string.Join(", ", r.Value.Select(id => id == me ? Loc.T("You") : NameOf(id))),
                }).ToList(),
            };
            // The same picture or game as before the change: kept, not loaded again
            if (before != null && before.Message.Picture == message.Picture)
                (row.Picture, row.PictureFile) = (before.Picture, before.PictureFile);
            else if (message.Picture != null)
                _ = LoadPictureAsync(row);
            if (before != null && before.Game?.Kind == row.Game?.Kind && before.Game?.Id == row.Game?.Id)
                (row.PcGame, row.RetroCover, row.GameSubtitle) = (before.PcGame, before.RetroCover, before.GameSubtitle);
            else if (row.Game != null)
                _ = LoadGameAsync(row);
            return row;
        }

        /// <summary>A game's card: its cover and what it is (its size, its platform).</summary>
        private static async Task LoadGameAsync(ChatMessageRow row)
        {
            ChatGame game = row.Game!;
            try
            {
                if (game.IsRetro)
                {
                    row.GameSubtitle = game.PlatformName ?? game.Platform;
                    if (game.Cover != null)
                        row.RetroCover = await RetroService.MediaImageAsync(game.Cover, 160, game.CoverVersion);
                    return;
                }
                Game? known = pcGames.GetValueOrDefault(game.Id)
                    ?? InstallViewModel.Instance.InstalledGames.Select(g => g.Key).FirstOrDefault(g => g?.ID == game.Id)
                    ?? MainWindowViewModel.Instance.Library?.Model.GameCards.FirstOrDefault(g => g.ID == game.Id);
                known ??= JsonSerializer.Deserialize<Game>(await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games/{game.Id}"));
                if (known == null)
                    return;
                pcGames[game.Id] = known;
                row.PcGame = known;
                bool installed = InstallViewModel.Instance.InstalledGames.Any(g => g.Key?.ID == game.Id);
                row.GameSubtitle = string.Join(" · ", new[] { long.TryParse(known.Size, out long size) && size > 0 ? Converter.GameSizeConverter.Text(size) : null, installed ? Loc.T("Installed") : null }.Where(p => p != null));
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private void Show(ChatMessage message)
        {
            if (model.Messages.Any(m => m.Message.Id == message.Id))
                return;
            model.Messages.Add(Row(message));
        }

        /// <summary>A message changed (written again, taken back, an emoji): its row again, in its place.</summary>
        private void MessageChanged(ChatMessage message)
        {
            if (model.Open?.ConversationId != message.ConversationId)
                return;
            int at = model.Messages.ToList().FindIndex(m => m.Message.Id == message.Id);
            if (at < 0)
                return;
            ChatMessageRow before = model.Messages[at];
            model.Messages[at] = Row(message, before);
            if (model.Editing == before)
                CancelDraftNote();
            if (model.ReplyingTo == before)
                model.ReplyingTo = message.Deleted ? null : model.Messages[at];
            Arrange();
        }

        private async Task LoadPictureAsync(ChatMessageRow row)
        {
            try
            {
                string file = await ChatService.Instance.PictureFileAsync(row.Message.ConversationId, row.Message.Picture!);
                row.PictureFile = file;
                row.Picture = DecodedImages.TryGet(file, 480) ?? await Task.Run(() => DecodedImages.Load(file, 480));
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>The days, the senders of a group and the times, from the messages' order.</summary>
        private void Arrange()
        {
            bool group = model.Open?.IsGroup == true;
            for (int i = 0; i < model.Messages.Count; i++)
            {
                ChatMessageRow row = model.Messages[i];
                ChatMessageRow? previous = i > 0 ? model.Messages[i - 1] : null, next = i + 1 < model.Messages.Count ? model.Messages[i + 1] : null;
                DateTime day = row.Message.SentAt.ToLocalTime().Date;
                bool newDay = previous == null || previous.Message.SentAt.ToLocalTime().Date != day;
                row.Day = newDay ? DayText(day) : null;
                bool sameRun = previous != null && !newDay && previous.Message.SenderId == row.Message.SenderId && row.Message.SentAt - previous.Message.SentAt < TimeSpan.FromMinutes(5);
                row.ShowsSender = group && !row.IsMine && !sameRun;
                row.ShowsTime = next == null || next.Message.SenderId != row.Message.SenderId || next.Message.SentAt - row.Message.SentAt >= TimeSpan.FromMinutes(5)
                    || next.Message.SentAt.ToLocalTime().Date != day || row.Message.EditedAt != null;
                row.Seen = null;
            }
            // "Seen" under the player's last message: who read the conversation since
            if (model.Open is ChatEntry { IsNews: false } open && ChatService.Instance.Of(open.ConversationId)?.ReadAt is Dictionary<int, DateTime> readAt
                && model.Messages.LastOrDefault(m => m.IsMine && !m.IsDeleted) is ChatMessageRow last)
            {
                var readers = readAt.Where(r => r.Key != ChatService.Me && r.Value >= last.Message.SentAt).Select(r => r.Key).ToList();
                if (readers.Count > 0)
                    last.Seen = !group ? Loc.T("Seen") : Loc.F("Seen by {0}", string.Join(", ", readers.Select(NameOf)));
            }
            model.Changed();
        }

        private static string DayText(DateTime day) =>
            day == DateTime.Today ? Loc.T("Today") : day == DateTime.Today.AddDays(-1) ? Loc.T("Yesterday")
            : day.ToString(day.Year == DateTime.Today.Year ? "d MMMM" : "d MMMM yyyy", CultureInfo.CurrentCulture);

        private void ScrollToEnd() => Dispatcher.UIThread.Post(() => uiMessagesScroll.ScrollToEnd(), DispatcherPriority.Background);

        /// <summary>A message came: shown in its open conversation (and read), otherwise said in a notice.</summary>
        private async void Received(ChatMessage message)
        {
            bool shown = MainWindowViewModel.Instance.IsChatOpen && model.Open?.ConversationId == message.ConversationId;
            if (shown)
            {
                bool atEnd = uiMessagesScroll.Offset.Y >= uiMessagesScroll.Extent.Height - uiMessagesScroll.Viewport.Height - 40;
                Show(message);
                Arrange();
                if (atEnd || message.SenderId == ChatService.Me)
                    ScrollToEnd();
            }
            if (message.SenderId == ChatService.Me)
                return;
            // What they wrote is sent: they no longer write
            if (typing.Remove((message.ConversationId, message.SenderId)))
                ShowTyping();
            bool windowActive = TopLevel.GetTopLevel(this) is Window { IsActive: true, IsVisible: true } window && window.WindowState != WindowState.Minimized;
            bool seen = shown && windowActive;
            if (seen)
                await ChatService.Instance.MarkReadAsync(message.ConversationId);
            else
                MainWindowViewModel.Instance.AppBarText = $"{message.SenderName} : {Preview(message)}";
            if (!windowActive && !message.Deleted && WantsNotifications())
            {
                ChatConversation? conversation = ChatService.Instance.Of(message.ConversationId);
                string title = message.Game is { Invite: true } invite ? Loc.F("{0} invites you to play {1}", message.SenderName, invite.Title)
                    : conversation?.IsNews == true ? Loc.T("Announcements")
                    : conversation?.IsGroup == true ? $"{message.SenderName} · {conversation.Name}"
                    : message.SenderName;
                ToastMessageHelper.ShowAsIs(title, Preview(message));
            }
        }

        /// <summary>
        /// The desktop's notification for a message: unless the player turned them off, chose "Do not disturb", or plays
        /// on this PC (a game in full screen is not interrupted).
        /// </summary>
        internal static bool WantsNotifications() =>
            NotificationsOn && ActivityService.ChosenState != "busy" && !ActivityService.Instance.IsPlayingHere;

        internal static bool NotificationsOn
        {
            get
            {
                try { return Preferences.Get(AppConfigKey.ChatNotifications, LoginManager.Instance.GetUserProfile().UserConfigFile) != "0"; }
                catch (Exception ex) { Log.Ignored(ex); return true; }
            }
            set => Preferences.Set(AppConfigKey.ChatNotifications, value ? "1" : "0", LoginManager.Instance.GetUserProfile().UserConfigFile);
        }
        #endregion

        #region Writing
        private async void Draft_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                e.Handled = true;
                await SendAsync();
            }
            else if (e.Key == Key.Escape && model.HasDraftNote)
            {
                e.Handled = true;
                CancelDraftNote();
            }
            else if (e.Key == Key.V && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            {
                // A picture in the clipboard (a screenshot) waits to be sent; a text is pasted as usual
                if (await ClipboardPictureAsync() is byte[] picture)
                {
                    e.Handled = true;
                    SetDraftPicture(picture);
                }
            }
        }

        private async void Send_Click(object? sender, RoutedEventArgs e) => await SendAsync();

        internal async Task SendAsync()
        {
            if (model.Open is not ChatEntry open || model.IsSending)
                return;
            string text = (uiDraft.Text ?? "").Trim();
            byte[]? picture = model.DraftPictureData;
            if (text.Length == 0 && picture == null)
                return;
            model.IsSending = true;
            try
            {
                if (model.Editing is ChatMessageRow edited)
                    await ChatService.Instance.EditAsync(edited.Message, text);
                else if (picture != null)
                    await ChatService.Instance.SendPictureAsync(open.ConversationId, picture, text, model.ReplyingTo?.Message.Id);
                else
                    await ChatService.Instance.SendTextAsync(open.ConversationId, text, model.ReplyingTo?.Message.Id);
                uiDraft.Text = "";
                DropDraftPicture();
                model.ReplyingTo = model.Editing = null;
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = Loc.F("The message could not be sent: {0}", WebExceptionHelper.TryGetServerMessage(ex));
            }
            finally
            {
                model.IsSending = false;
                uiDraft.Focus();
            }
        }

        private void Emoji_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Content is not string emoji)
                return;
            // At the cursor, then the cursor after it
            string text = uiDraft.Text ?? "";
            int at = Math.Clamp(uiDraft.CaretIndex, 0, text.Length);
            uiDraft.Text = text.Insert(at, emoji);
            uiDraft.CaretIndex = at + emoji.Length;
            uiEmojiButton.Flyout?.Hide();
            uiDraft.Focus();
        }

        private async void AttachPicture_Click(object? sender, RoutedEventArgs e)
        {
            string? file = await StorageHelper.PickFileAsync(Loc.T("Choose a picture"), new Dictionary<string, string[]> { [Loc.T("Pictures")] = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp" } });
            if (file != null)
                await TakeFileAsync(file);
        }

        private async void Drop(object? sender, DragEventArgs e)
        {
            if (e.Data.GetFiles()?.FirstOrDefault()?.TryGetLocalPath() is string file)
                await TakeFileAsync(file);
        }

        internal async Task TakeFileAsync(string file)
        {
            try
            {
                byte[] data = await File.ReadAllBytesAsync(file);
                if (ChatPictures.Prepare(data) is byte[] picture)
                    SetDraftPicture(picture);
                else
                    MainWindowViewModel.Instance.AppBarText = Loc.T("Only PNG, JPEG, GIF and WebP pictures can be sent.");
            }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
        }

        private void SetDraftPicture(byte[] picture)
        {
            model.DraftPictureData = picture;
            using var stream = new MemoryStream(picture);
            try { model.DraftPicture = Bitmap.DecodeToWidth(stream, 320); }
            catch (Exception ex) { Log.Ignored(ex); model.DraftPicture = null; model.DraftPictureData = null; }
            uiDraft.Focus();
        }

        private void DropDraftPicture()
        {
            model.DraftPicture = null;
            model.DraftPictureData = null;
        }

        private void DropPicture_Click(object? sender, RoutedEventArgs e) => DropDraftPicture();

        /// <summary>A picture copied (a screenshot, an image of a page) as the systems put it in the clipboard.</summary>
        private async Task<byte[]?> ClipboardPictureAsync()
        {
            IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
                return null;
            try
            {
                string[] formats = await clipboard.GetFormatsAsync();
                foreach (string format in new[] { "image/png", "PNG", "public.png", "image/jpeg", "JFIF", "public.jpeg" })
                    if (formats.Contains(format) && await clipboard.GetDataAsync(format) is byte[] data && ChatPictures.Prepare(data) is byte[] picture)
                        return picture;
                // Windows' screenshots: a bitmap without its file header
                if (formats.Contains("DeviceIndependentBitmap") && await clipboard.GetDataAsync("DeviceIndependentBitmap") is byte[] dib)
                    return ChatPictures.FromDib(dib);
                // A picture file copied in the file manager
                if (formats.Contains(DataFormats.Files) && await clipboard.GetDataAsync(DataFormats.Files) is IEnumerable<IStorageItem> items
                    && items.FirstOrDefault()?.TryGetLocalPath() is string file && File.Exists(file))
                    return ChatPictures.Prepare(await File.ReadAllBytesAsync(file));
            }
            catch (Exception ex) { Log.Ignored(ex); }
            return null;
        }

        private async void Picture_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not ChatMessageRow { PictureFile: string file })
                return;
            // Full size, in the system's viewer
            try
            {
                if (TopLevel.GetTopLevel(this)?.Launcher is ILauncher launcher)
                    await launcher.LaunchFileInfoAsync(new FileInfo(file));
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private void CancelDraft_Click(object? sender, RoutedEventArgs e) => CancelDraftNote();

        private void CancelDraftNote()
        {
            if (model.Editing != null)
                uiDraft.Text = "";
            model.ReplyingTo = model.Editing = null;
            uiDraft.Focus();
        }
        #endregion

        #region A message's menu: react, answer, copy, change, take back
        private void Bubble_ContextRequested(object? sender, ContextRequestedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not ChatMessageRow row || row.IsDeleted)
                return;
            e.Handled = true;
            var menu = new MenuFlyout();
            // The quick emojis in a row, as the phones do
            var emojis = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 2 };
            foreach (string emoji in Reactions)
            {
                var button = new Button { Content = emoji, Classes = { "emoji" }, Width = 34, Height = 34, FontSize = 17 };
                button.Click += async (_, _) => { menu.Hide(); await ReactAsync(row, emoji); };
                emojis.Children.Add(button);
            }
            menu.Items.Add(new MenuItem { Header = emojis, StaysOpenOnClick = true });
            menu.Items.Add(new Separator());
            void Add(string header, Func<Task> action)
            {
                var item = new MenuItem { Header = Loc.T(header) };
                item.Click += async (_, _) => await action();
                menu.Items.Add(item);
            }
            if (model.CanWrite)
                Add("Answer", () => { Answer(row); return Task.CompletedTask; });
            if (!string.IsNullOrEmpty(row.Message.Text))
                Add("Copy the text", async () => { if (TopLevel.GetTopLevel(this)?.Clipboard is IClipboard clipboard) await clipboard.SetTextAsync(row.Message.Text); });
            if (row.IsMine && !string.IsNullOrEmpty(row.Message.Text))
                Add("Edit", () => { Edit(row); return Task.CompletedTask; });
            bool admin = LoginManager.Instance.GetCurrentUser()?.Role == PERMISSION_ROLE.ADMIN;
            if (row.IsMine || (admin && row.Message.ConversationId == ChatService.NewsId))
                Add("Delete", () => DeleteAsync(row));
            menu.ShowAt((Control)sender!, showAtPointer: true);
        }

        internal void Answer(ChatMessageRow row)
        {
            if (model.Editing != null)
                uiDraft.Text = "";
            model.Editing = null;
            model.ReplyingTo = row;
            uiDraft.Focus();
        }

        internal void Edit(ChatMessageRow row)
        {
            model.ReplyingTo = null;
            model.Editing = row;
            DropDraftPicture();
            uiDraft.Text = row.Message.Text;
            uiDraft.CaretIndex = uiDraft.Text?.Length ?? 0;
            uiDraft.Focus();
        }

        internal async Task DeleteAsync(ChatMessageRow row, bool confirm = true)
        {
            if (confirm && !await DialogService.ConfirmAsync(Loc.T("Delete this message? The others will no longer see it."), Loc.T("Delete")))
                return;
            try { await ChatService.Instance.DeleteAsync(row.Message); }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
        }

        internal async Task ReactAsync(ChatMessageRow row, string emoji)
        {
            try { await ChatService.Instance.ReactAsync(row.Message, emoji); }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
        }

        private async void Reaction_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is ChatReactionChip chip && (sender as Control)?.FindAncestorOfType<ItemsControl>()?.DataContext is ChatMessageRow row)
                await ReactAsync(row, chip.Emoji);
        }
        #endregion

        #region Who writes
        private void Typing(string conversationId, int userId, string name)
        {
            typing[(conversationId, userId)] = (name.Length > 0 ? name : NameOf(userId), DateTime.UtcNow + TypingShown);
            ShowTyping();
        }

        /// <summary>"Zoé is writing…" in the conversation and the list, gone a few seconds after the last word typed.</summary>
        private void ShowTyping()
        {
            foreach (var gone in typing.Where(t => t.Value.Until <= DateTime.UtcNow).Select(t => t.Key).ToList())
                typing.Remove(gone);
            if (typing.Count > 0)
                typingTimer.Start();
            else
                typingTimer.Stop();
            var here = model.Open == null ? new List<string>() : typing.Where(t => t.Key.Conversation == model.Open.ConversationId).Select(t => t.Value.Name).ToList();
            model.TypingText = here.Count switch
            {
                0 => null,
                1 => Loc.F("{0} is writing…", here[0]),
                _ => Loc.F("{0} are writing…", string.Join(", ", here)),
            };
            Rebuild();
        }

        private string? TypingIn(string conversationId, int? userId = null) =>
            typing.Any(t => t.Key.Conversation == conversationId && (userId == null || t.Key.User == userId) && t.Value.Until > DateTime.UtcNow) ? Loc.T("Writing…") : null;
        #endregion

        #region Games: shared, invitations
        private void ShareGame_Click(object? sender, RoutedEventArgs e) => OpenGamePicker(invite: false);

        private void InviteGame_Click(object? sender, RoutedEventArgs e) => OpenGamePicker(invite: true);

        /// <summary>The game played on this PC now, to invite to it in one click.</summary>
        internal static ChatGame? PlayingHere()
        {
            if (ActivityService.Instance.PlayingHereRetro is RetroGame retro)
                return ChatGame.Of(retro, invite: true);
            if (ActivityService.Instance.PlayingHereId is int id && InstallViewModel.Instance.InstalledGames.Select(g => g.Key).FirstOrDefault(g => g?.ID == id) is Game game)
                return ChatGame.Of(game, invite: true);
            return null;
        }

        private void GameMenu_Opening(object? sender, EventArgs e)
        {
            ChatGame? now = PlayingHere();
            uiInviteNow.IsVisible = now != null;
            uiInviteNow.Header = now == null ? null : Loc.F("Invite to play {0}", now.Title);
        }

        private async void InviteNow_Click(object? sender, RoutedEventArgs e)
        {
            if (PlayingHere() is ChatGame now)
                await SendGameAsync(now);
        }

        internal void OpenGamePicker(bool invite)
        {
            model.PickInvites = invite;
            model.IsPickingGame = true;
            uiGameSearch.Text = "";
            _ = SearchGamesAsync("");
            Dispatcher.UIThread.Post(() => uiGameSearch.Focus());
        }

        private void CloseGamePicker_Click(object? sender, RoutedEventArgs e) => model.IsPickingGame = false;

        /// <summary>The library's games (the server searches them) and the retro ones (those the Retro page listed, those installed).</summary>
        internal async Task SearchGamesAsync(string text)
        {
            int generation = ++gameSearchGeneration;
            text = text.Trim();
            var picks = new List<ChatGamePick>();
            if (PlayingHere() is ChatGame now)
                picks.Add(new ChatGamePick { Title = now.Title, Subtitle = Loc.T("You are playing it"), Pc = now.IsRetro ? null : pcGames.GetValueOrDefault(now.Id) ?? InstallViewModel.Instance.InstalledGames.Select(g => g.Key).FirstOrDefault(g => g?.ID == now.Id), Retro = now.IsRetro ? ActivityService.Instance.PlayingHereRetro : null });
            IEnumerable<Game> pc = InstallViewModel.Instance.InstalledGames.Select(g => g.Key).Where(g => g != null)!;
            List<RetroGame> retro;
            try { retro = RetroLibrary.InstalledGames().Concat(MainWindowViewModel.Instance.Retro.KnownGames).DistinctBy(RetroLibrary.Key).ToList(); }
            catch (Exception ex) { Log.Ignored(ex); retro = new(); }
            string wanted = QuickMatch.Normalize(text);
            if (text.Length >= 2)
            {
                try
                {
                    string json = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games?search={Uri.EscapeDataString(text)}&limit=15");
                    pc = JsonSerializer.Deserialize<PaginatedData<Game>>(json)?.Data ?? Array.Empty<Game>();
                }
                catch (Exception ex)
                {
                    Log.Ignored(ex);
                    pc = pc.Where(g => QuickMatch.Normalize(g.Title).Contains(wanted));
                }
                retro = retro.Where(g => QuickMatch.Normalize(g.Name).Contains(wanted)).ToList();
            }
            if (generation != gameSearchGeneration)
                return;
            picks.AddRange(pc.Take(15).Select(g => new ChatGamePick { Title = ChatGame.Of(g, false).Title, Subtitle = Loc.T("PC"), Pc = g }));
            picks.AddRange(retro.Take(15).Select(g => new ChatGamePick { Title = g.Name, Subtitle = g.Platform.Name, Retro = g }));
            model.GamePicks.Clear();
            foreach (ChatGamePick pick in picks.DistinctBy(p => p.Pc != null ? $"pc:{p.Pc.ID}" : $"retro:{p.Retro!.Platform.Slug}:{p.Retro.Id}"))
                model.GamePicks.Add(pick);
        }

        private async void GamePick_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is ChatGamePick pick)
                await PickAsync(pick);
        }

        internal async Task PickAsync(ChatGamePick pick)
        {
            model.IsPickingGame = false;
            await SendGameAsync(pick.Shared(model.PickInvites));
        }

        /// <summary>A game in the conversation: with the words written, if any.</summary>
        internal async Task SendGameAsync(ChatGame game)
        {
            if (model.Open is not ChatEntry open)
                return;
            string text = model.Editing == null ? (uiDraft.Text ?? "").Trim() : "";
            try
            {
                await ChatService.Instance.SendTextAsync(open.ConversationId, text, model.ReplyingTo?.Message.Id, game);
                if (model.Editing == null)
                    uiDraft.Text = "";
                model.ReplyingTo = null;
            }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = Loc.F("The message could not be sent: {0}", WebExceptionHelper.TryGetServerMessage(ex)); }
        }

        private async void OpenSharedGame_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is ChatMessageRow row)
                await OpenGameAsync(row);
        }

        /// <summary>The game's page: to play it, or to install it.</summary>
        internal static async Task OpenGameAsync(ChatMessageRow row)
        {
            if (row.Game is not ChatGame game)
                return;
            if (game.IsRetro)
                await MainWindowViewModel.Instance.Retro.ShowGameAsync(game.Platform ?? "", game.Id);
            else
                MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(row.PcGame ?? new Game { ID = game.Id }));
        }
        #endregion

        #region The player's status
        private void Status_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as MenuItem)?.Tag is string state)
                SetStatus(state);
        }

        internal void SetStatus(string state)
        {
            ActivityService.ChosenState = state;
            Rebuild();
        }

        private void StatusMenu_Opening(object? sender, EventArgs e) => uiNotifications.IsChecked = NotificationsOn;

        private void Notifications_Click(object? sender, RoutedEventArgs e) => NotificationsOn = uiNotifications.IsChecked == true;
        #endregion

        #region Groups
        private void NewGroup_Click(object? sender, RoutedEventArgs e)
        {
            foreach (ChatEntry friend in model.Friends)
                friend.IsPicked = false;
            uiGroupName.Text = "";
            model.IsMakingGroup = !model.IsMakingGroup;
            if (model.IsMakingGroup)
                uiGroupName.Focus();
        }

        private void CancelGroup_Click(object? sender, RoutedEventArgs e) => model.IsMakingGroup = false;

        private async void CreateGroup_Click(object? sender, RoutedEventArgs e)
        {
            string name = (uiGroupName.Text ?? "").Trim();
            var members = model.Friends.Where(f => f.IsPicked && f.User != null).Select(f => f.User!.ID).ToList();
            if (name.Length == 0 || members.Count == 0)
            {
                MainWindowViewModel.Instance.AppBarText = Loc.T("Give the group a name and choose who is in it.");
                return;
            }
            try
            {
                ChatConversation group = await ChatService.Instance.CreateGroupAsync(name, members);
                model.IsMakingGroup = false;
                Rebuild();
                if (model.Groups.FirstOrDefault(g => g.ConversationId == group.Id) is ChatEntry entry)
                    await OpenAsync(entry);
            }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
        }

        private void AddPlayers_Click(object? sender, RoutedEventArgs e)
        {
            if (model.Open is not ChatEntry open || ChatService.Instance.Of(open.ConversationId) is not ChatConversation group)
                return;
            model.Addable.Clear();
            foreach (User user in users.Values.Where(u => !group.Members.Contains(u.ID)).OrderBy(u => u.Username, StringComparer.CurrentCultureIgnoreCase))
                model.Addable.Add(new ChatEntry { User = user, Title = user.Username });
            model.IsAddingPlayers = true;
        }

        private void CancelAdd_Click(object? sender, RoutedEventArgs e) => model.IsAddingPlayers = false;

        private async void ConfirmAdd_Click(object? sender, RoutedEventArgs e)
        {
            if (model.Open is not ChatEntry open)
                return;
            var added = model.Addable.Where(a => a.IsPicked).Select(a => a.User!.ID).ToList();
            model.IsAddingPlayers = false;
            if (added.Count == 0)
                return;
            try
            {
                await ChatService.Instance.AddToGroupAsync(open.ConversationId, added);
                Rebuild();
            }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
        }

        private async void LeaveGroup_Click(object? sender, RoutedEventArgs e)
        {
            if (model.Open is not ChatEntry open)
                return;
            if (!await DialogService.ConfirmAsync(Loc.F("Leave the group « {0} »? Its messages will no longer be shown to you.", open.Title), Loc.T("Leave the group")))
                return;
            try
            {
                await ChatService.Instance.LeaveGroupAsync(open.ConversationId);
                model.Open = null;
                Rebuild();
            }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
        }
        #endregion
    }

    /// <summary>The pictures the chat sends: the kinds the service keeps, a screenshot made lighter.</summary>
    internal static class ChatPictures
    {
        /// <summary>Above this a picture is sent as a JPEG of at most <see cref="MaxWidth"/>: a 4K PNG screenshot weighs 10 MB.</summary>
        private const int LightEnough = 3 * 1024 * 1024;
        private const int MaxWidth = 2560;

        public static bool IsPicture(byte[] data) =>
            data.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }) || data.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })
            || data.AsSpan().StartsWith("GIF8"u8) || (data.Length >= 12 && data.AsSpan(0, 4).SequenceEqual("RIFF"u8) && data.AsSpan(8, 4).SequenceEqual("WEBP"u8));

        /// <returns>The picture to send, null when it is none.</returns>
        public static byte[]? Prepare(byte[] data)
        {
            if (!IsPicture(data))
                return null;
            // An animated GIF stays as it is (re-encoded it would no longer move)
            if (data.Length <= LightEnough || data.AsSpan().StartsWith("GIF8"u8))
                return data.Length <= 10 * 1024 * 1024 ? data : null;
            using SKBitmap? bitmap = SKBitmap.Decode(data);
            if (bitmap == null)
                return null;
            SKBitmap scaled = bitmap;
            try
            {
                if (bitmap.Width > MaxWidth)
                    scaled = bitmap.Resize(new SKImageInfo(MaxWidth, Math.Max(1, bitmap.Height * MaxWidth / bitmap.Width)), SKFilterQuality.High) ?? bitmap;
                using SKImage image = SKImage.FromBitmap(scaled);
                using SKData jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 88);
                return jpeg.ToArray();
            }
            finally
            {
                if (scaled != bitmap)
                    scaled.Dispose();
            }
        }

        /// <summary>A Windows clipboard bitmap (its header and pixels, without the file's header) as a PNG.</summary>
        public static byte[]? FromDib(byte[] dib)
        {
            if (dib.Length < 40)
                return null;
            int headerSize = BitConverter.ToInt32(dib, 0);
            int bits = BitConverter.ToInt16(dib, 14);
            int compression = BitConverter.ToInt32(dib, 16);
            int colors = BitConverter.ToInt32(dib, 32);
            if (colors == 0 && bits <= 8)
                colors = 1 << bits;
            int masks = compression == 3 && headerSize == 40 ? 12 : 0;
            int offset = 14 + headerSize + masks + colors * 4;
            var file = new byte[14 + dib.Length];
            file[0] = (byte)'B';
            file[1] = (byte)'M';
            BitConverter.GetBytes(file.Length).CopyTo(file, 2);
            BitConverter.GetBytes(offset).CopyTo(file, 10);
            dib.CopyTo(file, 14);
            using SKBitmap? bitmap = SKBitmap.Decode(file);
            if (bitmap == null)
                return null;
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
            return Prepare(png.ToArray());
        }
    }
}
