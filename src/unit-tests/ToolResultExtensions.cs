using ModelContextProtocol.Protocol;

namespace Sherlock.MCP.Tests;

internal static class ToolResultExtensions
{
    public static string Text(this CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Single().Text;

    public static ResourceLinkBlock[] Links(this CallToolResult result) =>
        result.Content.OfType<ResourceLinkBlock>().ToArray();
}
