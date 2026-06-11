namespace Sherlock.MCP.Runtime.Contracts.ReverseLookup;

public record ExtensionMethodHit(
    string AssemblyPath,
    string DeclaringTypeFullName,
    string MethodName,
    string Signature,
    string ExtendedTypeFriendlyName);
