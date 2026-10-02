using System.Text.Json.Serialization;

namespace Sherlock.MCP.Server.Schemas;

public sealed record ApiDiffData
{
    [JsonPropertyName("left")] public required ApiDiffSideInfo Left { get; init; }
    [JsonPropertyName("right")] public required ApiDiffSideInfo Right { get; init; }
    [JsonPropertyName("projection")] public required string Projection { get; init; }
    [JsonPropertyName("breakingOnly")] public required bool BreakingOnly { get; init; }
    [JsonPropertyName("namespaceFilter")] public required string? NamespaceFilter { get; init; }
    [JsonPropertyName("counts")] public required ApiDiffCounts Counts { get; init; }
    [JsonPropertyName("total")] public required int Total { get; init; }
    [JsonPropertyName("count")] public required int Count { get; init; }
    [JsonPropertyName("nextToken")] public required string? NextToken { get; init; }
    [JsonPropertyName("pagination")] public required PaginationInfo Pagination { get; init; }
    [JsonPropertyName("warnings")] public required IReadOnlyList<string> Warnings { get; init; }
    [JsonPropertyName("changes")] public required IReadOnlyList<ApiDiffChangeItem> Changes { get; init; }
}

public sealed record ApiDiffSideInfo
{
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("assemblyPath")] public required string AssemblyPath { get; init; }
    [JsonPropertyName("assemblyName")] public required string AssemblyName { get; init; }
    [JsonPropertyName("assemblyVersion")] public required string? AssemblyVersion { get; init; }
    [JsonPropertyName("packageId")] public required string? PackageId { get; init; }
    [JsonPropertyName("packageVersion")] public required string? PackageVersion { get; init; }
    [JsonPropertyName("tfm")] public required string? Tfm { get; init; }
}

public sealed record ApiDiffCounts
{
    [JsonPropertyName("typesAdded")] public required int TypesAdded { get; init; }
    [JsonPropertyName("typesRemoved")] public required int TypesRemoved { get; init; }
    [JsonPropertyName("typesChanged")] public required int TypesChanged { get; init; }
    [JsonPropertyName("membersAdded")] public required int MembersAdded { get; init; }
    [JsonPropertyName("membersRemoved")] public required int MembersRemoved { get; init; }
    [JsonPropertyName("membersChanged")] public required int MembersChanged { get; init; }
    [JsonPropertyName("breaking")] public required int Breaking { get; init; }
}

public sealed record ApiDiffChangeItem
{
    [JsonPropertyName("change")] public required string Change { get; init; }
    [JsonPropertyName("scope")] public required string Scope { get; init; }
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("memberKind")] public required string? MemberKind { get; init; }
    [JsonPropertyName("member")] public required string? Member { get; init; }
    [JsonPropertyName("breaking")] public required bool Breaking { get; init; }
    [JsonPropertyName("signature")] public required string Signature { get; init; }
    [JsonPropertyName("reason")] public required string? Reason { get; init; }
}
