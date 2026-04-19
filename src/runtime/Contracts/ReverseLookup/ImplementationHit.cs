namespace Sherlock.MCP.Runtime.Contracts.ReverseLookup;

public record ImplementationHit(
    string AssemblyPath,
    string TypeFullName,
    string Kind,
    string[] MatchedInterfaces,
    string[] BaseTypeChain);
