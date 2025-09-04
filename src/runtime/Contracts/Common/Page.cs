namespace Sherlock.MCP.Runtime.Contracts.Common;

public sealed class Page<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public string? NextToken { get; init; }
    public int? Total { get; init; }
}

