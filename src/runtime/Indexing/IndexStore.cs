using System.Collections.Concurrent;

namespace Sherlock.MCP.Runtime.Indexing;

// Minimal in-memory placeholder for future on-disk store
public sealed class IndexStore
{
    private readonly ConcurrentDictionary<string, IndexStatus> _status = new(StringComparer.OrdinalIgnoreCase);

    public void Set(IndexStatus status) => _status[status.AssemblyPath] = status;

    public IndexStatus? Get(string assemblyPath) => _status.TryGetValue(assemblyPath, out var s) ? s : null;
}

