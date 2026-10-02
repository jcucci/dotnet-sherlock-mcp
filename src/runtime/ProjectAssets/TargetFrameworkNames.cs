using System.Globalization;

namespace Sherlock.MCP.Runtime.ProjectAssets;

public static class TargetFrameworkNames
{
    public static string? ToShortName(string? frameworkName)
    {
        if (string.IsNullOrWhiteSpace(frameworkName)) return null;

        var parts = frameworkName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var versionPart = parts.FirstOrDefault(part => part.StartsWith("Version=", StringComparison.OrdinalIgnoreCase));
        if (versionPart is null) return null;
        if (!Version.TryParse(versionPart["Version=".Length..].TrimStart('v', 'V'), out var version)) return null;

        return parts[0].ToUpperInvariant() switch
        {
            ".NETCOREAPP" when version.Major >= 5 => $"net{version.Major}.{version.Minor}",
            ".NETCOREAPP" => $"netcoreapp{version.Major}.{version.Minor}",
            ".NETSTANDARD" => $"netstandard{version.Major}.{version.Minor}",
            ".NETFRAMEWORK" => "net" + string.Concat(Components(version).Select(c => c.ToString(CultureInfo.InvariantCulture))),
            _ => null
        };
    }

    public static string? BestMatch(IEnumerable<string> aliases, string shortName)
    {
        var candidates = aliases.ToArray();
        return candidates.FirstOrDefault(alias => alias.Equals(shortName, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(alias => alias.StartsWith(shortName + "-", StringComparison.OrdinalIgnoreCase));
    }

    public static string? AliasForTargetKey(IEnumerable<string> aliases, string framework) =>
        aliases
            .Where(alias => framework.Equals(alias, StringComparison.OrdinalIgnoreCase)
                || (framework.StartsWith(alias, StringComparison.OrdinalIgnoreCase) && IsPlatformVersionSuffix(framework[alias.Length..])))
            .OrderByDescending(alias => alias.Length)
            .FirstOrDefault();

    private static bool IsPlatformVersionSuffix(string suffix) =>
        suffix.Length > 0 && suffix.All(c => char.IsDigit(c) || c == '.');

    private static IEnumerable<int> Components(Version version)
    {
        yield return version.Major;
        yield return Math.Max(0, version.Minor);
        if (version.Build > 0) yield return version.Build;
    }
}
