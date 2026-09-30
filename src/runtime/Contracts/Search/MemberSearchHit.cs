namespace Sherlock.MCP.Runtime.Contracts.Search;

public record MemberSearchHit(
    string DeclaringType,
    string MemberKind,
    string Name,
    string Signature,
    string TypeMetadataName);

public record SearchOptions(
    bool CaseSensitive = false,
    bool IncludeNonPublic = false,
    IReadOnlySet<string>? MemberKinds = null);
