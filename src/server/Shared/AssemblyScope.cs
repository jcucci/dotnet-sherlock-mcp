namespace Sherlock.MCP.Server.Shared;

internal static class AssemblyScope
{
    internal readonly struct ScopeResult
    {
        public string[] Paths { get; init; }
        public string? Error { get; init; }
    }

    public static ScopeResult BuildAndValidate(string assemblyPath, string[]? additionalAssemblies)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            return new ScopeResult { Error = JsonHelpers.Error("InvalidArgument", "assemblyPath is required") };
        if (!File.Exists(assemblyPath))
            return new ScopeResult { Error = JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}") };

        var pathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var seen = new HashSet<string>(pathComparer);
        var paths = new List<string>();

        var normalizedPrimary = Path.GetFullPath(assemblyPath);
        if (seen.Add(normalizedPrimary)) paths.Add(normalizedPrimary);

        if (additionalAssemblies != null)
        {
            foreach (var extra in additionalAssemblies)
            {
                if (string.IsNullOrWhiteSpace(extra)) continue;
                if (!File.Exists(extra))
                    return new ScopeResult { Error = JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {extra}") };
                var normalizedExtra = Path.GetFullPath(extra);
                if (seen.Add(normalizedExtra)) paths.Add(normalizedExtra);
            }
        }
        return new ScopeResult { Paths = paths.ToArray() };
    }
}
