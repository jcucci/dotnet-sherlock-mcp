using System.Text.Json.Serialization;

namespace Sherlock.MCP.Server.Schemas;

public sealed record MemberSourceData
{
    [JsonPropertyName("typeName")] public required string TypeName { get; init; }
    [JsonPropertyName("memberName")] public required string MemberName { get; init; }
    [JsonPropertyName("origin")] public required string Origin { get; init; }
    [JsonPropertyName("overloads")] public required IReadOnlyList<MemberSourceOverload> Overloads { get; init; }
    [JsonPropertyName("note")] public required string? Note { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("startLine")] public required int StartLine { get; init; }
    [JsonPropertyName("lineCount")] public required int LineCount { get; init; }
    [JsonPropertyName("totalLines")] public required int TotalLines { get; init; }
    [JsonPropertyName("truncated")] public required bool Truncated { get; init; }
    [JsonPropertyName("clippedLines")] public required int ClippedLines { get; init; }
    [JsonPropertyName("continuationToken")] public required string? ContinuationToken { get; init; }
}

public sealed record MemberSourceOverload
{
    [JsonPropertyName("signature")] public required string Signature { get; init; }
    [JsonPropertyName("origin")] public required string Origin { get; init; }
    [JsonPropertyName("document")] public required string? Document { get; init; }
    [JsonPropertyName("url")] public required string? Url { get; init; }
    [JsonPropertyName("lines")] public required SourceLineRange? Lines { get; init; }
}

public sealed record SourceLineRange
{
    [JsonPropertyName("start")] public required int Start { get; init; }
    [JsonPropertyName("end")] public required int End { get; init; }
}
