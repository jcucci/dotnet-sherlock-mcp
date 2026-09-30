namespace Sherlock.MCP.Runtime.Inspection;

internal static class NuGetVersions
{
    public static IComparer<string> Comparer { get; } = Comparer<string>.Create(Compare);

    public static Version? TryParse(string raw)
    {
        var core = raw.AsSpan();
        var cut = core.IndexOfAny('-', '+');
        if (cut >= 0) core = core[..cut];
        return Version.TryParse(core, out var v) ? v : null;
    }

    public static string[] SortDescending(IEnumerable<string> versions) =>
        versions.OrderByDescending(v => v, Comparer).ToArray();

    public static int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        var byCore = Normalize(TryParse(left)).CompareTo(Normalize(TryParse(right)));
        if (byCore != 0) return byCore;

        var byLabel = ComparePrerelease(PrereleaseLabel(left), PrereleaseLabel(right));
        return byLabel != 0 ? byLabel : StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }

    private static Version Normalize(Version? version) =>
        version is null
            ? new Version(0, 0, 0, 0)
            : new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    private static string PrereleaseLabel(string raw)
    {
        var withoutMetadata = raw.Split('+', 2)[0];
        var dash = withoutMetadata.IndexOf('-');
        return dash >= 0 ? withoutMetadata[(dash + 1)..] : "";
    }

    private static int ComparePrerelease(string left, string right)
    {
        if (left.Length == 0 && right.Length == 0) return 0;
        if (left.Length == 0) return 1;
        if (right.Length == 0) return -1;

        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        for (var i = 0; i < Math.Min(leftParts.Length, rightParts.Length); i++)
        {
            var byPart = CompareIdentifier(leftParts[i], rightParts[i]);
            if (byPart != 0) return byPart;
        }
        return leftParts.Length.CompareTo(rightParts.Length);
    }

    private static int CompareIdentifier(string left, string right)
    {
        var leftIsNumeric = IsNumeric(left);
        var rightIsNumeric = IsNumeric(right);
        if (leftIsNumeric && rightIsNumeric)
        {
            var trimmedLeft = left.TrimStart('0');
            var trimmedRight = right.TrimStart('0');
            var byLength = trimmedLeft.Length.CompareTo(trimmedRight.Length);
            return byLength != 0 ? byLength : string.CompareOrdinal(trimmedLeft, trimmedRight);
        }
        if (leftIsNumeric != rightIsNumeric) return leftIsNumeric ? -1 : 1;
        return StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }

    private static bool IsNumeric(string identifier) =>
        identifier.Length > 0 && identifier.All(char.IsAsciiDigit);
}
