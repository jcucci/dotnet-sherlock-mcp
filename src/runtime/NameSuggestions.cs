using System.Reflection;

namespace Sherlock.MCP.Runtime;

public static class NameSuggestions
{
    public const int DefaultMax = 5;

    private const BindingFlags AllMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static IReadOnlyList<string> ForTypes(Assembly assembly, string typeName, int max = DefaultMax) =>
        ForTypes(TypeNameResolver.LoadableTypes(assembly), typeName, max);

    public static IReadOnlyList<string> ForTypes(IEnumerable<Type> types, string typeName, int max = DefaultMax) =>
        Closest(
            types
                .Where(t => !IsCompilerGenerated(t.Name))
                .Select(t => t.FullName ?? t.Name),
            typeName,
            max);

    public static IReadOnlyList<string> ForMembers(Type type, string memberName, string? memberKind = null, BindingFlags bindingFlags = AllMembers, int max = DefaultMax) =>
        Closest(
            MembersOfKind(type, memberKind, bindingFlags)
                .Select(m => m.Name)
                .Where(name => !IsCompilerGenerated(name)),
            memberName,
            max);

    public static IReadOnlyList<string> Closest(IEnumerable<string> candidates, string input, int max = DefaultMax, bool compareSimpleNames = true)
    {
        Func<string, string> normalize = compareSimpleNames ? SimpleName : name => name;
        var needle = normalize(input.Trim()).ToLowerInvariant();
        if (needle.Length == 0 || max <= 0) return [];

        var threshold = Math.Max(2, needle.Length / 3);
        return candidates
            .Distinct(StringComparer.Ordinal)
            .Select(candidate => (candidate, score: Score(normalize(candidate).ToLowerInvariant(), needle, threshold)))
            .Where(match => match.score is not null)
            .OrderBy(match => match.score)
            .ThenBy(match => match.candidate.Length)
            .ThenBy(match => match.candidate, StringComparer.Ordinal)
            .Take(max)
            .Select(match => match.candidate)
            .ToArray();
    }

    public static int Distance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private static int? Score(string candidate, string needle, int threshold)
    {
        var distance = Distance(candidate, needle);
        var isSubstring = candidate.Contains(needle, StringComparison.Ordinal) || needle.Contains(candidate, StringComparison.Ordinal);
        if (!isSubstring) return distance <= threshold ? distance : null;

        return Math.Min(distance, Math.Abs(candidate.Length - needle.Length));
    }

    private static MemberInfo[] MembersOfKind(Type type, string? memberKind, BindingFlags bindingFlags) =>
        memberKind?.ToLowerInvariant() switch
        {
            "method" => type.GetMethods(bindingFlags),
            "property" => type.GetProperties(bindingFlags),
            "field" => type.GetFields(bindingFlags),
            "event" => type.GetEvents(bindingFlags),
            "constructor" => type.GetConstructors(bindingFlags),
            _ => type.GetMembers(bindingFlags)
        };

    private static string SimpleName(string name)
    {
        var generic = name.IndexOfAny(['<', '`', '[']);
        var trimmed = generic > 0 ? name[..generic] : name;
        var separator = trimmed.LastIndexOfAny(['.', '+', '/']);
        return separator >= 0 && separator < trimmed.Length - 1 ? trimmed[(separator + 1)..] : trimmed;
    }

    private static bool IsCompilerGenerated(string name) => name.Contains('<');
}
