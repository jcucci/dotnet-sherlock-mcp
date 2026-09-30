using System.Text.Json.Serialization;

namespace Sherlock.MCP.Server.Schemas;

public sealed record ReverseLookupData<TItem>
{
    [JsonPropertyName("typeName")] public required string TypeName { get; init; }
    [JsonPropertyName("scope")] public required IReadOnlyList<string> Scope { get; init; }
    [JsonPropertyName("projection")] public required string Projection { get; init; }
    [JsonPropertyName("total")] public required int Total { get; init; }
    [JsonPropertyName("count")] public required int Count { get; init; }
    [JsonPropertyName("nextToken")] public required string? NextToken { get; init; }
    [JsonPropertyName("pagination")] public required PaginationInfo Pagination { get; init; }
    [JsonPropertyName("results")] public required IReadOnlyList<TItem> Results { get; init; }
}

public sealed record ReferencesData
{
    [JsonPropertyName("typeName")] public required string TypeName { get; init; }
    [JsonPropertyName("scope")] public required IReadOnlyList<string> Scope { get; init; }
    [JsonPropertyName("projection")] public required string Projection { get; init; }
    [JsonPropertyName("total")] public required int Total { get; init; }
    [JsonPropertyName("count")] public required int Count { get; init; }
    [JsonPropertyName("truncated")] public required bool Truncated { get; init; }
    [JsonPropertyName("hardCap")] public required int HardCap { get; init; }
    [JsonPropertyName("nextToken")] public required string? NextToken { get; init; }
    [JsonPropertyName("pagination")] public required PaginationInfo Pagination { get; init; }
    [JsonPropertyName("results")] public required IReadOnlyList<ReferenceItem> Results { get; init; }
}

public sealed record ImplementationItem
{
    [JsonPropertyName("typeFullName")] public required string TypeFullName { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
}

public sealed record MethodHitItem
{
    [JsonPropertyName("declaringType")] public required string DeclaringType { get; init; }
    [JsonPropertyName("methodName")] public required string MethodName { get; init; }
    [JsonPropertyName("signature")] public required string Signature { get; init; }
}

public sealed record ReferenceItem
{
    [JsonPropertyName("declaringType")] public required string DeclaringType { get; init; }
    [JsonPropertyName("memberKind")] public required string MemberKind { get; init; }
    [JsonPropertyName("memberName")] public required string MemberName { get; init; }
    [JsonPropertyName("referenceKind")] public required string ReferenceKind { get; init; }
}
