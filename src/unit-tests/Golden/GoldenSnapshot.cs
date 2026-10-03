using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;

namespace Sherlock.MCP.Tests.Golden;

internal static partial class GoldenSnapshot
{
    private static readonly Dictionary<string, string> VolatileKeys = new(StringComparer.Ordinal)
    {
        ["continuationToken"] = "{Token}",
        ["nextToken"] = "{Token}",
        ["estimatedTotalChars"] = "{Chars}",
        ["currentPageChars"] = "{Chars}"
    };

    private static readonly string[] PathPlaceholders = ["{Fixture}", "{NuGetCache}", "{HostRuntime}"];

    public static string Render(CallToolResult result, IReadOnlyList<(string Value, string Placeholder)> scrubs)
    {
        var replacements = Expand(scrubs);
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        var snapshot = new JsonObject
        {
            ["isError"] = result.IsError == true,
            ["hasStructuredContent"] = result.StructuredContent is not null,
            ["resourceLinks"] = new JsonArray(result.Content.OfType<ResourceLinkBlock>()
                .Select(link => (JsonNode)new JsonObject { ["name"] = link.Name, ["uri"] = Scrub(link.Uri, replacements) })
                .ToArray()),
            ["payload"] = Scrub(JsonNode.Parse(text), key: null, replacements)
        };
        return snapshot.ToJsonString();
    }

    private static (string Value, string Placeholder)[] Expand(IReadOnlyList<(string Value, string Placeholder)> scrubs) =>
        scrubs
            .Where(scrub => scrub.Value.Length > 0)
            .SelectMany(scrub => new[] { scrub, (Uri.EscapeDataString(scrub.Value), scrub.Placeholder) })
            .Distinct()
            .ToArray();

    private static JsonNode? Scrub(JsonNode? node, string? key, (string Value, string Placeholder)[] replacements) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(property => KeyValuePair.Create(
            Scrub(property.Key, replacements),
            Scrub(property.Value, property.Key, replacements)))),
        JsonArray array => new JsonArray(array.Select(item => Scrub(item, key, replacements)).ToArray()),
        JsonValue when key != null && VolatileKeys.TryGetValue(key, out var placeholder) => placeholder,
        JsonValue value when value.TryGetValue(out string? text) => Scrub(text, replacements),
        _ => node?.DeepClone()
    };

    private static string Scrub(string text, (string Value, string Placeholder)[] replacements)
    {
        foreach (var (value, placeholder) in replacements)
            text = text.Replace(value, placeholder, StringComparison.Ordinal);
        if (PathPlaceholders.Any(placeholder => text.Contains(placeholder, StringComparison.Ordinal)))
            text = text.Replace('\\', '/').Replace("%5C", "%2F", StringComparison.Ordinal);
        return HandlePattern().Replace(text, "{Handle}");
    }

    [GeneratedRegex("asm_[0-9A-Za-z_-]+")]
    private static partial Regex HandlePattern();
}
