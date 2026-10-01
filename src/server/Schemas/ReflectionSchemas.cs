using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sherlock.MCP.Server.Schemas;

public sealed record AssemblyInfoData
{
    [JsonPropertyName("projection")] public required string Projection { get; init; }
    [JsonPropertyName("name")] public required string? Name { get; init; }
    [JsonPropertyName("version")] public required string? Version { get; init; }
    [JsonPropertyName("fullName")] public required string? FullName { get; init; }
    [JsonPropertyName("location")] public required string? Location { get; init; }
    [JsonPropertyName("targetFramework")] public required string? TargetFramework { get; init; }
    [JsonPropertyName("referencedAssemblies")] public required IReadOnlyList<string> ReferencedAssemblies { get; init; }
    [JsonPropertyName("attributes")] public IReadOnlyList<JsonElement>? Attributes { get; init; }
}

public sealed record AssemblyHandleData
{
    [JsonPropertyName("handle")] public required string Handle { get; init; }
    [JsonPropertyName("name")] public required string? Name { get; init; }
    [JsonPropertyName("version")] public required string? Version { get; init; }
    [JsonPropertyName("targetFramework")] public required string? TargetFramework { get; init; }
    [JsonPropertyName("assemblyPath")] public required string AssemblyPath { get; init; }
    [JsonPropertyName("additionalAssemblies")] public required IReadOnlyList<string> AdditionalAssemblies { get; init; }
}
