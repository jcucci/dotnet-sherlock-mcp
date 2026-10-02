namespace Sherlock.MCP.Runtime.ApiDiff;

public enum ApiChangeKind
{
    Added,
    Removed,
    Changed
}

public enum ApiChangeScope
{
    Type,
    Member
}

public sealed record ApiChangeReason(string Description, bool Breaking);

public sealed record ApiChange(
    ApiChangeScope Scope,
    ApiChangeKind Change,
    bool Breaking,
    string TypeName,
    string? MemberKind,
    string? MemberName,
    string? LeftSignature,
    string? RightSignature,
    IReadOnlyList<ApiChangeReason> Reasons);

public sealed record ApiAssemblyIdentity(string Name, string? Version);

public sealed record ApiDiffResult(
    ApiAssemblyIdentity Left,
    ApiAssemblyIdentity Right,
    IReadOnlyList<ApiChange> Changes,
    IReadOnlyList<string> Warnings);
