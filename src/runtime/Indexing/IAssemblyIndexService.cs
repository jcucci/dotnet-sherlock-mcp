namespace Sherlock.MCP.Runtime.Indexing;

public interface IAssemblyIndexService
{
    void Enqueue(string assemblyPath);

    IndexStatus GetStatus(string assemblyPath);
}

public sealed record IndexStatus(
    string AssemblyPath,
    bool Indexed,
    DateTimeOffset? LastIndexedAt,
    string? Hash,
    long? FileSize
);

