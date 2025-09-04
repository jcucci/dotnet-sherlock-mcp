namespace Sherlock.MCP.Runtime.Caching;

public interface IToolResponseCache
{
    bool TryGet(string key, out string? payload);

    void Set(string key, string payload, TimeSpan ttl);
}

