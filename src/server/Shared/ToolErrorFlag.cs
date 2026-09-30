using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Sherlock.MCP.Server.Shared;

public static class ToolErrorFlag
{
    private const int KindProbeLength = 64;

    public static CallToolResult Apply(CallToolResult result)
    {
        if (result.IsError != true && result.Content.OfType<TextContentBlock>().FirstOrDefault() is { } text && IsErrorPayload(text.Text))
            result.IsError = true;
        return result;
    }

    public static bool IsErrorPayload(string? text)
    {
        if (string.IsNullOrEmpty(text) || text[0] != '{') return false;

        var probe = text.AsSpan(0, Math.Min(text.Length, KindProbeLength));
        if (probe.IndexOf("\"error\"", StringComparison.Ordinal) < 0) return false;

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.TryGetProperty("kind", out var kind)
                && kind.ValueKind == JsonValueKind.String
                && kind.GetString() == "error";
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
