namespace Sherlock.MCP.Runtime.Decompilation;

public interface IDecompilerService
{
    string DecompileType(
        string assemblyPath, int typeMetadataToken, IReadOnlyList<string>? searchDirectories = null,
        CancellationToken cancellationToken = default);

    IReadOnlyList<string> DecompileMembers(
        string assemblyPath, IReadOnlyList<int> memberMetadataTokens, IReadOnlyList<string>? searchDirectories = null,
        CancellationToken cancellationToken = default);
}
