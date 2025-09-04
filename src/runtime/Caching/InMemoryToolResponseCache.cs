using System.Collections.Concurrent;

namespace Sherlock.MCP.Runtime.Caching;

public sealed class InMemoryToolResponseCache : IToolResponseCache
{
    private readonly ConcurrentDictionary<string, (string Payload, DateTimeOffset Expires)> _store = new();

    public bool TryGet(string key, out string? payload)
    {
        payload = null;
        if (_store.TryGetValue(key, out var entry))
        {
            if (entry.Expires > DateTimeOffset.UtcNow)
            {
                payload = entry.Payload;
                return true;
            }

            _store.TryRemove(key, out _);
        }
        return false;
    }

    public void Set(string key, string payload, TimeSpan ttl)
    {
        _store[key] = (payload, DateTimeOffset.UtcNow.Add(ttl));
    }
}

