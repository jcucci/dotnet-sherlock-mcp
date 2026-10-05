namespace Sherlock.MCP.Runtime.ProjectEvaluation;

public sealed record EvaluatedItem(string Identity, IReadOnlyDictionary<string, string> Metadata)
{
    public string? GetMetadata(string name) =>
        Metadata.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value) ? value : null;
}

public sealed record ProjectEvaluationResult(
    bool Success,
    string? FailureReason,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyDictionary<string, EvaluatedItem[]> Items)
{
    public static ProjectEvaluationResult Failed(string reason) =>
        new(false, reason, new Dictionary<string, string>(), new Dictionary<string, EvaluatedItem[]>());

    public string? GetProperty(string name) =>
        Properties.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    public EvaluatedItem[] GetItems(string itemType) =>
        Items.TryGetValue(itemType, out var items) ? items : [];
}
