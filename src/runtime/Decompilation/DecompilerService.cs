using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;

namespace Sherlock.MCP.Runtime.Decompilation;

public sealed class DecompilerService : IDecompilerService
{
    public string DecompileType(string assemblyPath, int typeMetadataToken, CancellationToken cancellationToken = default) =>
        Decompile(assemblyPath, [typeMetadataToken], cancellationToken)[0];

    public IReadOnlyList<string> DecompileMembers(
        string assemblyPath, IReadOnlyList<int> memberMetadataTokens, CancellationToken cancellationToken = default) =>
        Decompile(assemblyPath, memberMetadataTokens, cancellationToken);

    private static string[] Decompile(string assemblyPath, IReadOnlyList<int> metadataTokens, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var module = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage);
        var decompiler = new CSharpDecompiler(module, CreateResolver(assemblyPath, module), CreateSettings())
        {
            CancellationToken = cancellationToken
        };

        return metadataTokens
            .Select(token =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return decompiler.DecompileAsString(MetadataTokens.EntityHandle(token)).Trim();
            })
            .ToArray();
    }

    private static UniversalAssemblyResolver CreateResolver(string assemblyPath, PEFile module)
    {
        var resolver = new UniversalAssemblyResolver(
            assemblyPath,
            throwOnError: false,
            targetFramework: module.DetectTargetFrameworkId(),
            streamOptions: PEStreamOptions.PrefetchMetadata);
        resolver.AddSearchDirectory(RuntimeEnvironment.GetRuntimeDirectory());
        return resolver;
    }

    private static DecompilerSettings CreateSettings() => new()
    {
        ThrowOnAssemblyResolveErrors = false
    };
}
