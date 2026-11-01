using System.Collections.Concurrent;

namespace Zadatak24
{
    public sealed class InMemoryCache
    {
        public sealed class CachedResponse
        {
            public int StatusCode;
            public string Json;
            public ConcurrentDictionary<string, string> Headers;
        }

        private readonly ConcurrentDictionary<string, CachedResponse> _map =
            new ConcurrentDictionary<string, CachedResponse>();

        public bool TryGet(string key, out CachedResponse cr) => _map.TryGetValue(key, out cr);

        public void Set(string key, int status, string json, ConcurrentDictionary<string, string> headers)
        {
            _map[key] = new CachedResponse { StatusCode = status, Json = json, Headers = headers };
        }
    }
}
