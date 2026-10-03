using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sherlock.MCP.Server.Schemas;

public sealed record MethodCallsData
{
    [JsonPropertyName("declaringType")] public required string DeclaringType { get; init; }
    [JsonPropertyName("methodName")] public required string MethodName { get; init; }
    [JsonPropertyName("matchedOverloads")] public required int MatchedOverloads { get; init; }
    [JsonPropertyName("anyBodyless")] public required bool AnyBodyless { get; init; }
    [JsonPropertyName("format")] public required string Format { get; init; }
    [JsonPropertyName("projection")] public string? Projection { get; init; }
    [JsonPropertyName("calls")] public IReadOnlyList<JsonElement>? Calls { get; init; }
    [JsonPropertyName("fieldAccesses")] public IReadOnlyList<JsonElement>? FieldAccesses { get; init; }
    [JsonPropertyName("depth")] public int? Depth { get; init; }
    [JsonPropertyName("diagram")] public string? Diagram { get; init; }
    [JsonPropertyName("nodeCount")] public int? NodeCount { get; init; }
    [JsonPropertyName("edgeCount")] public int? EdgeCount { get; init; }
    [JsonPropertyName("truncated")] public bool? Truncated { get; init; }
    [JsonPropertyName("note")] public string? Note { get; init; }
}
