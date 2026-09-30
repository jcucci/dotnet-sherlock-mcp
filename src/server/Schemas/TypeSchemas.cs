using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sherlock.MCP.Server.Schemas;

public sealed record TypeListData
{
    [JsonPropertyName("assemblyPath")] public required string AssemblyPath { get; init; }
    [JsonPropertyName("projection")] public required string Projection { get; init; }
    [JsonPropertyName("totalTypeCount")] public required int TotalTypeCount { get; init; }
    [JsonPropertyName("returnedTypeCount")] public required int ReturnedTypeCount { get; init; }
    [JsonPropertyName("nextToken")] public required string? NextToken { get; init; }
    [JsonPropertyName("types")] public required IReadOnlyList<TypeSummaryItem> Types { get; init; }
}

public sealed record TypeSummaryItem
{
    [JsonPropertyName("FullName")] public required string FullName { get; init; }
    [JsonPropertyName("Namespace")] public required string? Namespace { get; init; }
    [JsonPropertyName("Kind")] public required int Kind { get; init; }
}

public sealed record TypeInfoData
{
    [JsonPropertyName("FullName")] public required string FullName { get; init; }
    [JsonPropertyName("Name")] public required string Name { get; init; }
    [JsonPropertyName("Namespace")] public required string? Namespace { get; init; }
    [JsonPropertyName("Kind")] public required int Kind { get; init; }
    [JsonPropertyName("Accessibility")] public required int Accessibility { get; init; }
    [JsonPropertyName("IsAbstract")] public required bool IsAbstract { get; init; }
    [JsonPropertyName("IsSealed")] public required bool IsSealed { get; init; }
    [JsonPropertyName("IsStatic")] public required bool IsStatic { get; init; }
    [JsonPropertyName("IsGeneric")] public required bool IsGeneric { get; init; }
    [JsonPropertyName("IsNested")] public required bool IsNested { get; init; }
    [JsonPropertyName("AssemblyName")] public required string? AssemblyName { get; init; }
    [JsonPropertyName("BaseType")] public required string? BaseType { get; init; }
    [JsonPropertyName("Interfaces")] public required IReadOnlyList<string> Interfaces { get; init; }
    [JsonPropertyName("Attributes")] public required IReadOnlyList<JsonElement> Attributes { get; init; }
    [JsonPropertyName("GenericParameters")] public required IReadOnlyList<JsonElement> GenericParameters { get; init; }
    [JsonPropertyName("NestedTypes")] public required IReadOnlyList<JsonElement> NestedTypes { get; init; }
}
