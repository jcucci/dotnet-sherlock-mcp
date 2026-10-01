namespace Sherlock.MCP.Runtime.Decompilation;

public interface IDecompilerService
{
    string DecompileType(string assemblyPath, int typeMetadataToken, CancellationToken cancellationToken = default);

    IReadOnlyList<string> DecompileMembers(
        string assemblyPath, IReadOnlyList<int> memberMetadataTokens, CancellationToken cancellationToken = default);
}
