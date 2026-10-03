using System.Text.Json.Serialization;

namespace Sherlock.MCP.Server.Schemas;

public sealed record ToolEnvelope<TData>
{
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("version")] public required string Version { get; init; }
    [JsonPropertyName("data")] public required TData Data { get; init; }
    [JsonPropertyName("hints")] public IReadOnlyList<ToolHintItem>? Hints { get; init; }
}

public sealed record ToolHintItem
{
    [JsonPropertyName("tool")] public required string Tool { get; init; }
    [JsonPropertyName("reason")] public required string Reason { get; init; }
    [JsonPropertyName("group")] public string? Group { get; init; }
}

public sealed record PaginationInfo
{
    [JsonPropertyName("hasMore")] public required bool HasMore { get; init; }
    [JsonPropertyName("recommendedPageSize")] public required int RecommendedPageSize { get; init; }
    [JsonPropertyName("estimatedTotalChars")] public required int EstimatedTotalChars { get; init; }
    [JsonPropertyName("currentPageChars")] public required int CurrentPageChars { get; init; }
    [JsonPropertyName("paginationAdvised")] public required bool PaginationAdvised { get; init; }
}
