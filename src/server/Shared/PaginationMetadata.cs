namespace Sherlock.MCP.Server.Shared;

public static class PaginationMetadata
{
    public static object Create(
        int total,
        int count,
        string? nextToken,
        int currentResponseChars,
        int warningThreshold = ResponseSizeHelper.WarningThreshold)
    {
        var estimatedTotalChars = count > 0 ? (currentResponseChars / count) * total : 0;
        var paginationAdvised = estimatedTotalChars > warningThreshold;
        var recommendedPageSize = paginationAdvised && count > 10
            ? Math.Max(10, count / 2)
            : count;

        return new
        {
            hasMore = nextToken != null,
            recommendedPageSize,
            estimatedTotalChars,
            currentPageChars = currentResponseChars,
            paginationAdvised
        };
    }

    public static object CreateSimple(int total, int count, string? nextToken) =>
        new
        {
            hasMore = nextToken != null,
            total,
            returned = count
        };
}
