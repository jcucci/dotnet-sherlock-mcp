namespace Sherlock.MCP.Runtime.Indexing;

public sealed class NoopAssemblyIndexService : IAssemblyIndexService
{
    private readonly IndexStore _store = new();

    public void Enqueue(string assemblyPath)
    {
        var fi = new FileInfo(assemblyPath);
        var status = new IndexStatus(
            AssemblyPath: assemblyPath,
            Indexed: fi.Exists,
            LastIndexedAt: DateTimeOffset.UtcNow,
            Hash: null,
            FileSize: fi.Exists ? fi.Length : null
        );
        _store.Set(status);
    }

    public IndexStatus GetStatus(string assemblyPath)
    {
        return _store.Get(assemblyPath) ?? new IndexStatus(assemblyPath, false, null, null, null);
    }
}

