using System.Text.Json;

namespace Sherlock.MCP.Runtime.SourceLink;

public sealed class SourceLinkMap
{
    private readonly (string Key, string Url, bool IsPrefix)[] _entries;

    private SourceLinkMap((string Key, string Url, bool IsPrefix)[] entries) => _entries = entries;

    public static SourceLinkMap? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("documents", out var documents) || documents.ValueKind != JsonValueKind.Object)
                return null;

            var entries = documents.EnumerateObject()
                .Where(entry => entry.Value.ValueKind == JsonValueKind.String)
                .Select(entry => ToEntry(entry.Name, entry.Value.GetString()!))
                .OrderByDescending(entry => entry.Key.Length)
                .ToArray();
            return new SourceLinkMap(entries);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string? GetUrl(string documentPath)
    {
        foreach (var (key, url, isPrefix) in _entries)
        {
            if (!isPrefix)
            {
                if (string.Equals(key, documentPath, StringComparison.OrdinalIgnoreCase)) return url;
                continue;
            }

            if (documentPath.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                return url.Replace("*", documentPath[key.Length..].Replace('\\', '/'), StringComparison.Ordinal);
        }
        return null;
    }

    private static (string Key, string Url, bool IsPrefix) ToEntry(string key, string url) =>
        key.EndsWith('*') ? (key[..^1], url, true) : (key, url, false);
}
