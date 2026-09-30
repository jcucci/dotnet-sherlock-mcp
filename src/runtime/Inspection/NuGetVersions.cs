namespace Sherlock.MCP.Runtime.Inspection;

internal static class NuGetVersions
{
    public static Version? TryParse(string raw)
    {
        var core = raw.AsSpan();
        var cut = core.IndexOfAny('-', '+');
        if (cut >= 0) core = core[..cut];
        return Version.TryParse(core, out var v) ? v : null;
    }

    public static string[] SortDescending(IEnumerable<string> versions) =>
        versions
            .Select(v => (raw: v, parsed: TryParse(v)))
            .OrderByDescending(p => p.parsed ?? new Version(0, 0))
            .ThenByDescending(p => p.raw, StringComparer.OrdinalIgnoreCase)
            .Select(p => p.raw)
            .ToArray();
}
