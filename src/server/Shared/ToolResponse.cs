using ModelContextProtocol.Protocol;

namespace Sherlock.MCP.Server.Shared;

public sealed record ToolResponse(string Text, IReadOnlyList<ResourceLinkBlock> Links)
{
    public static implicit operator ToolResponse(string text) => new(text, []);

    public static CallToolResult Result(string text) => new ToolResponse(text, []).ToCallToolResult();

    public CallToolResult ToCallToolResult() => new()
    {
        Content = [new TextContentBlock { Text = Text }, .. Links]
    };
}
