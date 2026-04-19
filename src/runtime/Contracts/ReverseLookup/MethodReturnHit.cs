namespace Sherlock.MCP.Runtime.Contracts.ReverseLookup;

public record MethodReturnHit(
    string AssemblyPath,
    string DeclaringTypeFullName,
    string MethodName,
    string Signature,
    string ReturnTypeFriendlyName,
    bool IsStatic);
