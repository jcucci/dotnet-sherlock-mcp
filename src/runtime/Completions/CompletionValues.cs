namespace Sherlock.MCP.Runtime.Completions;

public sealed record CompletionValues(string[] Values, int Total, bool HasMore)
{
    public static CompletionValues Empty { get; } = new([], 0, false);
}
