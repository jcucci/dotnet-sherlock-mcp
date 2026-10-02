using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Runtime.ProjectAssets;

namespace Sherlock.MCP.Runtime.Decompilation;

public sealed class DecompilerService : IDecompilerService
{
    public string DecompileType(
        string assemblyPath, int typeMetadataToken, IReadOnlyList<string>? searchDirectories = null,
        CancellationToken cancellationToken = default) =>
        Decompile(assemblyPath, [typeMetadataToken], searchDirectories, cancellationToken)[0];

    public IReadOnlyList<string> DecompileMembers(
        string assemblyPath, IReadOnlyList<int> memberMetadataTokens, IReadOnlyList<string>? searchDirectories = null,
        CancellationToken cancellationToken = default) =>
        Decompile(assemblyPath, memberMetadataTokens, searchDirectories, cancellationToken);

    private static string[] Decompile(
        string assemblyPath, IReadOnlyList<int> metadataTokens, IReadOnlyList<string>? searchDirectories,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var module = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage);
        var decompiler = new CSharpDecompiler(module, CreateResolver(assemblyPath, module, searchDirectories), CreateSettings())
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

    private static UniversalAssemblyResolver CreateResolver(
        string assemblyPath, PEFile module, IReadOnlyList<string>? searchDirectories)
    {
        var resolver = new UniversalAssemblyResolver(
            assemblyPath,
            throwOnError: false,
            targetFramework: module.DetectTargetFrameworkId(),
            streamOptions: PEStreamOptions.PrefetchMetadata);
        foreach (var directory in searchDirectories ?? [])
            resolver.AddSearchDirectory(directory);
        foreach (var directory in FrameworkReferenceResolver.Resolve(assemblyPath, ProjectAssetsLocator.Locate(assemblyPath)).SearchDirectories)
            resolver.AddSearchDirectory(directory);
        resolver.AddSearchDirectory(FrameworkReferenceResolver.HostRuntimeDirectory());
        return resolver;
    }

    private static DecompilerSettings CreateSettings() => new()
    {
        ThrowOnAssemblyResolveErrors = false
    };
}
