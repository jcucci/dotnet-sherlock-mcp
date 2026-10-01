using System.Text.Json.Serialization;

namespace Sherlock.MCP.Server.Schemas;

public sealed record DecompiledMemberData
{
    [JsonPropertyName("typeName")] public required string TypeName { get; init; }
    [JsonPropertyName("memberName")] public required string MemberName { get; init; }
    [JsonPropertyName("overloads")] public required IReadOnlyList<DecompiledOverload> Overloads { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("startLine")] public required int StartLine { get; init; }
    [JsonPropertyName("lineCount")] public required int LineCount { get; init; }
    [JsonPropertyName("totalLines")] public required int TotalLines { get; init; }
    [JsonPropertyName("truncated")] public required bool Truncated { get; init; }
    [JsonPropertyName("clippedLines")] public required int ClippedLines { get; init; }
    [JsonPropertyName("continuationToken")] public required string? ContinuationToken { get; init; }
}

public sealed record DecompiledOverload
{
    [JsonPropertyName("signature")] public required string Signature { get; init; }
}
