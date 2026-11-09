using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace GameVault.Uploader
{
    /// <summary>
    /// The chat's live link: each open client keeps a websocket, the service writes on it what concerns its player (a
    /// message, a group made or changed). A client without it (a proxy without websockets) asks the list now and then.
    /// </summary>
    public sealed class ChatHub(ILogger<ChatHub> logger)
    {
        private sealed class Link(WebSocket socket)
        {
            public WebSocket Socket { get; } = socket;
            /// <summary>A websocket takes one write at a time.</summary>
            public SemaphoreSlim Writing { get; } = new(1, 1);
        }

        private readonly ConcurrentDictionary<int, ConcurrentDictionary<Link, byte>> links = new();

        public bool IsOnline(int userId) => links.TryGetValue(userId, out var mine) && !mine.IsEmpty;

        /// <summary>Holds the websocket open until the client closes it (or the service stops).</summary>
        public async Task KeepAsync(int userId, WebSocket socket, CancellationToken cancellationToken)
        {
            var link = new Link(socket);
            links.GetOrAdd(userId, _ => new()).TryAdd(link, 0);
            try
            {
                var buffer = new byte[1024];
                // The client says nothing but its pings: it is read to see it close
                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    WebSocketReceiveResult received = await socket.ReceiveAsync(buffer, cancellationToken);
                    if (received.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
            {
                // A PC put to sleep, a network gone: the link just ends
            }
            finally
            {
                if (links.TryGetValue(userId, out var mine))
                    mine.TryRemove(link, out _);
            }
        }

        /// <summary>To every open client (what a player plays now).</summary>
        public Task SendAllAsync(object value) => SendAsync(links.Keys.ToList(), value);

        /// <summary>To the open clients of these players; a client that does not take it is left (it reads the list again).</summary>
        public async Task SendAsync(IEnumerable<int> userIds, object value)
        {
            byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
            var sending = new List<Task>();
            foreach (int userId in userIds.Distinct())
                if (links.TryGetValue(userId, out var mine))
                    foreach (Link link in mine.Keys)
                        sending.Add(WriteAsync(link, data));
            await Task.WhenAll(sending);
        }

        private async Task WriteAsync(Link link, byte[] data)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await link.Writing.WaitAsync(timeout.Token);
                try
                {
                    if (link.Socket.State == WebSocketState.Open)
                        await link.Socket.SendAsync(data, WebSocketMessageType.Text, true, timeout.Token);
                }
                finally
                {
                    link.Writing.Release();
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                logger.LogDebug(ex, "A chat client did not take a message");
            }
        }
    }
}
