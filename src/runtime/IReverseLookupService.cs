using Sherlock.MCP.Runtime.Contracts.ReverseLookup;

namespace Sherlock.MCP.Runtime;

public interface IReverseLookupService
{
    ImplementationHit[] FindImplementations(
        string[] assemblyPaths, string typeName, ReverseLookupOptions options,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);

    MethodReturnHit[] FindMethodsReturning(
        string[] assemblyPaths, string typeName, ReverseLookupOptions options,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);

    ExtensionMethodHit[] FindExtensionMethodsFor(
        string[] assemblyPaths, string typeName, ReverseLookupOptions options,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);

    ReferencesResult FindReferences(
        string[] assemblyPaths, string typeName, ReverseLookupOptions options,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
}

public record ReferencesResult(ReferenceHit[] Hits, bool Truncated);
