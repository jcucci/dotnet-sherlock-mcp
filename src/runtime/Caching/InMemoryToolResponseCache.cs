using System.Collections.Concurrent;

namespace Sherlock.MCP.Runtime.Caching;

public sealed class InMemoryToolResponseCache : IToolResponseCache
{
    private readonly ConcurrentDictionary<string, (string Payload, DateTimeOffset Expires)> _store = new();
    private readonly RuntimeOptions? _options;
    private int _compacting;

    public InMemoryToolResponseCache()
    {
    }

    public InMemoryToolResponseCache(RuntimeOptions options) => _options = options;

    private int MaxEntries => Math.Max(16, _options?.MaxCachedResponses ?? 256);

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
        if (_store.Count > MaxEntries)
            Compact();
    }

    private void Compact()
    {
        if (Interlocked.CompareExchange(ref _compacting, 1, 0) != 0) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var pair in _store)
            {
                if (pair.Value.Expires <= now)
                    _store.TryRemove(pair);
            }

            var overflow = _store.Count - MaxEntries;
            if (overflow <= 0) return;

            foreach (var pair in _store.ToArray().OrderBy(p => p.Value.Expires).Take(overflow))
                _store.TryRemove(pair);
        }
        finally
        {
            Interlocked.Exchange(ref _compacting, 0);
        }
    }
}
