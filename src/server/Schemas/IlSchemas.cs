using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sherlock.MCP.Server.Schemas;

public sealed record MethodCallsData
{
    [JsonPropertyName("declaringType")] public required string DeclaringType { get; init; }
    [JsonPropertyName("methodName")] public required string MethodName { get; init; }
    [JsonPropertyName("matchedOverloads")] public required int MatchedOverloads { get; init; }
    [JsonPropertyName("anyBodyless")] public required bool AnyBodyless { get; init; }
    [JsonPropertyName("projection")] public required string Projection { get; init; }
    [JsonPropertyName("calls")] public required IReadOnlyList<JsonElement> Calls { get; init; }
    [JsonPropertyName("fieldAccesses")] public required IReadOnlyList<JsonElement> FieldAccesses { get; init; }
}
