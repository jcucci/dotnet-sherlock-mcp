using Sherlock.MCP.Runtime.Contracts.ReverseLookup;

namespace Sherlock.MCP.Runtime;

public interface IReverseLookupService
{
    ImplementationHit[] FindImplementations(string[] assemblyPaths, string typeName, ReverseLookupOptions options);

    MethodReturnHit[] FindMethodsReturning(string[] assemblyPaths, string typeName, ReverseLookupOptions options);

    ReferencesResult FindReferences(string[] assemblyPaths, string typeName, ReverseLookupOptions options);
}

public record ReferencesResult(ReferenceHit[] Hits, bool Truncated);
