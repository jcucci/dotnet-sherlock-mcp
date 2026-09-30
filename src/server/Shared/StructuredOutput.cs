using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Sherlock.MCP.Server.Shared;

public static class StructuredOutput
{
    public static CallToolResult Apply(CallToolResult result, McpServerTool? tool)
    {
        if (tool?.ProtocolTool.OutputSchema is null || result.IsError == true || result.StructuredContent is not null)
            return result;

        if (result.Content.OfType<TextContentBlock>().FirstOrDefault() is not { Text: ['{', ..] } text)
            return result;

        try
        {
            using var document = JsonDocument.Parse(text.Text);
            result.StructuredContent = document.RootElement.Clone();
        }
        catch (JsonException)
        {
        }

        return result;
    }
}
