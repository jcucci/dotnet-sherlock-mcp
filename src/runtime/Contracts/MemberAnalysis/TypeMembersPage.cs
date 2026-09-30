namespace Sherlock.MCP.Runtime.Contracts.MemberAnalysis;

public sealed record TypeMembersPage(
    int Total,
    IReadOnlyDictionary<MemberKind, int> CountsByKind,
    TypeMemberDetails[] Items
);
