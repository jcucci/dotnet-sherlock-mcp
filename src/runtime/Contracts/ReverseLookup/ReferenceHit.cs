namespace Sherlock.MCP.Runtime.Contracts.ReverseLookup;

public record ReferenceHit(
    string AssemblyPath,
    string DeclaringTypeFullName,
    string MemberKind,
    string MemberName,
    string ReferenceKind,
    string Signature,
    string DedupeKey);
