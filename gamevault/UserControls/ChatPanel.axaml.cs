using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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

    /// <summary>A message as the conversation shows it.</summary>
    internal sealed class ChatMessageRow : ViewModelBase
    {
        public ChatMessageRow(ChatMessage message) => Message = message;

        public ChatMessage Message { get; }
        public bool IsMine => Message.SenderId == ChatService.Me;
        public string Sender => Message.SenderName;
        public string? Text => Message.Text;
        public bool HasPicture => Message.Picture != null;
        public string Time => Message.SentAt.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);

        private string? day;
        /// <summary>"Today", "Yesterday", "9 November": above the first message of a day.</summary>
        public string? Day { get => day; set { day = value; OnPropertyChanged(); } }
        private bool showsSender;
        /// <summary>In a group, above the first of someone's messages in a row.</summary>
        public bool ShowsSender { get => showsSender; set { showsSender = value; OnPropertyChanged(); } }
        private bool showsTime;
        /// <summary>Under the last of a row of messages.</summary>
        public bool ShowsTime { get => showsTime; set { showsTime = value; OnPropertyChanged(); } }
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
        public ChatEntry? Open { get => open; set { open = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsConversationOpen)); } }
        public bool IsConversationOpen => Open != null;
        private bool isMakingGroup;
        public bool IsMakingGroup { get => isMakingGroup; set { isMakingGroup = value; OnPropertyChanged(); } }
        private bool isAddingPlayers;
        public bool IsAddingPlayers { get => isAddingPlayers; set { isAddingPlayers = value; OnPropertyChanged(); } }
        private bool hasOlder;
        public bool HasOlder { get => hasOlder; set { hasOlder = value; OnPropertyChanged(); } }
        private bool isSending;
        public bool IsSending { get => isSending; set { isSending = value; OnPropertyChanged(); } }
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

        private readonly ChatPanelModel model = new();
        private readonly Dictionary<int, User> users = new();
        private DateTime usersLoadedAt;

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

            var groups = conversations.Where(c => c.IsGroup).ToList();
            Sync(model.Groups, groups.Select(c => c.Id).ToList(), id => new ChatEntry { ConversationId = id, IsGroup = true });
            foreach (ChatEntry entry in model.Groups)
            {
                ChatConversation group = groups.First(c => c.Id == entry.ConversationId);
                entry.Title = group.Name ?? Loc.T("Group");
                entry.Unread = group.Unread;
                entry.Subtitle = group.Unread > 0 && group.Last != null ? Preview(group.Last)
                    : string.Join(", ", group.Members.Where(m => m != me).Select(NameOf));
            }

            var friends = users.Values.Where(u => u.ID != me).ToList();
            var rows = friends.Select(user =>
            {
                ChatEntry entry = model.Friends.FirstOrDefault(f => f.User?.ID == user.ID) ?? new ChatEntry { ConversationId = ChatService.DirectId(user.ID), User = user };
                ChatConversation? direct = ChatService.Instance.Of(entry.ConversationId);
                PlayerActivity? activity = ActivityService.Instance.Of(user.ID);
                entry.Title = user.Username;
                entry.Unread = direct?.Unread ?? 0;
                // A library game or a retro game (RetroArch, Eden...): playing either way
                string? playing = Converter.PresenceConverter.PlayingText(user.ID);
                bool online = activity != null || playing != null;
                entry.Rank = playing != null ? 0 : online ? 1 : 2;
                string presence = playing ?? (online ? Loc.T("Online") : Loc.T("Offline"));
                entry.Subtitle = entry.Unread > 0 && direct?.Last != null ? Preview(direct.Last) : presence;
                entry.Dot = !online ? null : playing != null && Application.Current!.TryGetResource("Brush.Accent", null, out object? accent) ? accent as IBrush : Converter.PresenceConverter.Online;
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
            int online = rows.Count(r => r.Rank < 2);
            model.FriendsHeader = Loc.Plural(online, "FRIENDS · {0} ONLINE", "FRIENDS · {0} ONLINE");
            if (model.Open is ChatEntry open)
            {
                if (open.IsGroup && model.Groups.All(g => g.ConversationId != open.ConversationId))
                    model.Open = null;// left on another PC
                else if (open.IsGroup)
                    open.Subtitle = string.Join(", ", (ChatService.Instance.Of(open.ConversationId)?.Members ?? new()).Where(m => m != me).Select(NameOf));
                else if (model.Friends.FirstOrDefault(f => f.ConversationId == open.ConversationId) is ChatEntry friend)
                    open.Subtitle = friend.Unread > 0 ? open.Subtitle : friend.Subtitle;
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

        private static string Preview(ChatMessage message) =>
            string.IsNullOrEmpty(message.Text) ? Loc.T("📷 Picture") : message.Text!.ReplaceLineEndings(" ");

        #region Conversation
        private async void Entry_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is ChatEntry entry)
                await OpenAsync(entry);
        }

        /// <summary>A conversation: its last messages, read.</summary>
        internal async Task OpenAsync(ChatEntry entry)
        {
            model.IsMakingGroup = model.IsAddingPlayers = false;
            model.Open = new ChatEntry { ConversationId = entry.ConversationId, User = entry.User, IsGroup = entry.IsGroup, Title = entry.Title, Subtitle = entry.Unread > 0 ? "" : entry.Subtitle };
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
            model.IsAddingPlayers = false;
            Rebuild();
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => MainWindowViewModel.Instance.IsChatOpen = false;

        private ChatMessageRow Row(ChatMessage message)
        {
            var row = new ChatMessageRow(message);
            if (message.Picture != null)
                _ = LoadPictureAsync(row);
            return row;
        }

        private void Show(ChatMessage message)
        {
            if (model.Messages.Any(m => m.Message.Id == message.Id))
                return;
            model.Messages.Add(Row(message));
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
                    || next.Message.SentAt.ToLocalTime().Date != day;
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
            bool seen = shown && TopLevel.GetTopLevel(this) is Window { IsActive: true };
            if (seen)
                await ChatService.Instance.MarkReadAsync(message.ConversationId);
            else
                MainWindowViewModel.Instance.AppBarText = $"{message.SenderName} : {Preview(message)}";
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
                if (picture != null)
                    await ChatService.Instance.SendPictureAsync(open.ConversationId, picture, text);
                else
                    await ChatService.Instance.SendTextAsync(open.ConversationId, text);
                uiDraft.Text = "";
                DropDraftPicture();
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
