using System.Text.Json.Serialization;

namespace Sherlock.MCP.Server.Schemas;

public sealed record TypeMethodsData
{
    [JsonPropertyName("typeName")] public required string TypeName { get; init; }
    [JsonPropertyName("assemblyPath")] public required string AssemblyPath { get; init; }
    [JsonPropertyName("projection")] public required string Projection { get; init; }
    [JsonPropertyName("total")] public required int Total { get; init; }
    [JsonPropertyName("count")] public required int Count { get; init; }
    [JsonPropertyName("nextToken")] public required string? NextToken { get; init; }
    [JsonPropertyName("pagination")] public required PaginationInfo Pagination { get; init; }
    [JsonPropertyName("methods")] public required IReadOnlyList<MethodSummaryItem> Methods { get; init; }
}

public sealed record MethodSummaryItem
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("signature")] public required string Signature { get; init; }
}

public sealed record TypeMembersData
{
    [JsonPropertyName("typeName")] public required string TypeName { get; init; }
    [JsonPropertyName("assemblyPath")] public required string AssemblyPath { get; init; }
    [JsonPropertyName("projection")] public required string Projection { get; init; }
    [JsonPropertyName("kinds")] public required IReadOnlyList<string> Kinds { get; init; }
    [JsonPropertyName("total")] public required int Total { get; init; }
    [JsonPropertyName("count")] public required int Count { get; init; }
    [JsonPropertyName("countsByKind")] public required IReadOnlyDictionary<string, int> CountsByKind { get; init; }
    [JsonPropertyName("nextToken")] public required string? NextToken { get; init; }
    [JsonPropertyName("pagination")] public required PaginationInfo Pagination { get; init; }
    [JsonPropertyName("members")] public required IReadOnlyList<MemberSummaryItem> Members { get; init; }
}

public sealed record MemberSummaryItem
{
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("signature")] public required string Signature { get; init; }
}
