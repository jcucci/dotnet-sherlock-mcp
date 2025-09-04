namespace Sherlock.MCP.Runtime.Contracts.Common;

public sealed class PageRequest
{
    public int? MaxItems { get; init; }
    public string? ContinuationToken { get; init; }
}

