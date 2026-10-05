using System.Net.Http.Headers;

namespace GameVault.Core
{
    /// <summary>
    /// Shared HttpClient for requests that don't target the user's GameVault server
    /// (GitHub, metadata images, ...). Creating a new HttpClient per request exhausts sockets.
    /// </summary>
    public static class HttpClients
    {
        private static readonly Lazy<HttpClient> shared = new Lazy<HttpClient>(() =>
        {
            var client = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            {
                Timeout = TimeSpan.FromSeconds(100)
            };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GameVault", "1.0"));
            return client;
        });

        public static HttpClient Shared => shared.Value;
    }
}
