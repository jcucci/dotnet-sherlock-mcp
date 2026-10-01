using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sherlock.MCP.Server.Shared;

public static class SourcePager
{
    public const int DefaultMaxLines = 400;
    public const int MaxLinesLimit = 5000;
    public const int MaxLineLength = 2000;
    public const int PageCharacterBudget = ResponseSizeHelper.MaxResponseSize - 10_000;

    public static bool TryReadOffset(string? continuationToken, string salt, out int offset)
    {
        offset = 0;
        if (string.IsNullOrWhiteSpace(continuationToken)) return true;
        return TokenHelper.TryParse(continuationToken, out offset, out var parsedSalt) && parsedSalt == salt && offset >= 0;
    }

    public static string Page(string envelope, int offset, int maxLines, string salt, string toolName)
    {
        var root = JsonNode.Parse(envelope);
        if (root?["data"] is not JsonObject data || data["source"]?.GetValue<string>() is not { } source)
            return envelope;

        var lines = source.Split('\n').Select(Clip).ToArray();
        if (offset > 0 && offset >= lines.Length)
            return InvalidToken();

        var page = TakePage(lines, offset, maxLines);
        var clippedLines = page.Count(line => line.Clipped);
        var nextOffset = offset + page.Length;
        var truncated = nextOffset < lines.Length;

        data["source"] = string.Join('\n', page.Select(line => line.Text));
        data["startLine"] = offset + 1;
        data["lineCount"] = page.Length;
        data["totalLines"] = lines.Length;
        data["truncated"] = truncated;
        data["clippedLines"] = clippedLines;
        data["continuationToken"] = truncated ? TokenHelper.Make(nextOffset, salt) : null;

        return ResponseSizeHelper.ValidateResponseSize(data, toolName) ?? root.ToJsonString(JsonHelpers.DefaultOptions);
    }

    public static string InvalidToken() =>
        JsonHelpers.Error("InvalidContinuationToken", "The continuation token is invalid or expired.");

    private static (string Text, bool Clipped) Clip(string line) =>
        line.Length <= MaxLineLength
            ? (line, false)
            : ($"{line[..MaxLineLength]} /* {line.Length - MaxLineLength} more characters clipped */", true);

    private static (string Text, bool Clipped)[] TakePage((string Text, bool Clipped)[] lines, int offset, int maxLines)
    {
        var page = new List<(string Text, bool Clipped)>();
        var used = 0;
        foreach (var line in lines.Skip(offset).Take(maxLines))
        {
            var cost = JsonSerializer.Serialize(line.Text, JsonHelpers.DefaultOptions).Length;
            if (page.Count > 0 && used + cost > PageCharacterBudget) break;
            page.Add(line);
            used += cost;
        }
        return page.ToArray();
    }
}
