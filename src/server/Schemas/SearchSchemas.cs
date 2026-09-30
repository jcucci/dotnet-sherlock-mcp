using System.Text.Json.Serialization;

namespace Sherlock.MCP.Server.Schemas;

public sealed record SearchMembersData
{
    [JsonPropertyName("assemblyPath")] public required string AssemblyPath { get; init; }
    [JsonPropertyName("nameContains")] public required string NameContains { get; init; }
    [JsonPropertyName("total")] public required int Total { get; init; }
    [JsonPropertyName("count")] public required int Count { get; init; }
    [JsonPropertyName("nextToken")] public required string? NextToken { get; init; }
    [JsonPropertyName("pagination")] public required PaginationInfo Pagination { get; init; }
    [JsonPropertyName("results")] public required IReadOnlyList<MemberSearchItem> Results { get; init; }
}

public sealed record MemberSearchItem
{
    [JsonPropertyName("declaringType")] public required string DeclaringType { get; init; }
    [JsonPropertyName("memberKind")] public required string MemberKind { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("signature")] public required string Signature { get; init; }
}
