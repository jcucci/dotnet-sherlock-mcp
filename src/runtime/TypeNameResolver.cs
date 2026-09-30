using System.Reflection;

namespace Sherlock.MCP.Runtime;

public sealed record TypeResolution(Type? Type, IReadOnlyList<string> Candidates)
{
    public static readonly TypeResolution NotFound = new(Type: null, Candidates: []);

    public bool IsFound => Type != null;

    public bool IsAmbiguous => Candidates.Count > 1;

    public Type? OrThrowIfAmbiguous(string typeName) =>
        IsAmbiguous ? throw new AmbiguousTypeNameException(typeName, Candidates) : Type;
}

public static class TypeNameResolver
{
    public static TypeResolution Resolve(Assembly assembly, string typeName, StringComparison comparison = StringComparison.Ordinal) =>
        Resolve(assembly, () => LoadableTypes(assembly), typeName, comparison);

    public static TypeResolution Resolve(Assembly assembly, Func<IEnumerable<Type>> types, string typeName, StringComparison comparison = StringComparison.Ordinal)
    {
        var normalized = typeName.Trim().Replace('/', '+');
        if (TryGetType(assembly, normalized) is { } direct)
            return Single(direct);

        var all = types().ToArray();
        var exact = Match(all, normalized, StringComparison.Ordinal);
        if (exact.IsFound || exact.IsAmbiguous || comparison == StringComparison.Ordinal)
            return exact;

        return Match(all, normalized, comparison);
    }

    public static TypeResolution FromMatches(IEnumerable<Type> matches, string typeName, StringComparison comparison)
    {
        var distinct = matches.Distinct().ToArray();
        if (distinct.Length <= 1) return distinct.Length == 1 ? Single(distinct[0]) : TypeResolution.NotFound;

        var normalized = typeName.Trim().Replace('/', '+');
        var exact = distinct.Where(t => IsFullNameMatch(t, normalized, StringComparison.Ordinal)).ToArray();
        if (exact.Length == 1) return Single(exact[0]);

        var loose = distinct.Where(t => IsFullNameMatch(t, normalized, comparison)).ToArray();
        return loose.Length == 1 ? Single(loose[0]) : Ambiguous(distinct);
    }

    private static TypeResolution Match(Type[] types, string typeName, StringComparison comparison)
    {
        foreach (var predicate in Predicates(typeName, comparison))
        {
            var matches = types.Where(predicate).Distinct().ToArray();
            if (matches.Length == 1) return Single(matches[0]);
            if (matches.Length > 1) return Ambiguous(matches);
        }

        return TypeResolution.NotFound;
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    private static IEnumerable<Func<Type, bool>> Predicates(string typeName, StringComparison comparison)
    {
        yield return t => IsFullNameMatch(t, typeName, comparison);

        if (typeName.Contains('.'))
        {
            var nestedCandidate = typeName.Replace('.', '+');
            yield return t => string.Equals(t.FullName, nestedCandidate, comparison)
                              || string.Equals((t.FullName ?? t.Name).Replace('+', '.'), typeName, comparison);
        }

        if (typeName.Contains('<'))
            yield return t => string.Equals(TypeNameFormatter.FriendlyFullName(t), typeName, comparison);

        yield return t => string.Equals(t.Name, typeName, comparison);
    }

    private static bool IsFullNameMatch(Type type, string typeName, StringComparison comparison) =>
        string.Equals(type.FullName, typeName, comparison);

    private static Type? TryGetType(Assembly assembly, string typeName)
    {
        try
        {
            return assembly.GetType(typeName);
        }
        catch (Exception ex) when (ex is ArgumentException or FileLoadException or BadImageFormatException)
        {
            return null;
        }
    }

    private static TypeResolution Single(Type type) => new(type, [type.FullName ?? type.Name]);

    private static TypeResolution Ambiguous(IEnumerable<Type> matches) => new(
        Type: null,
        Candidates: matches.Select(t => t.FullName ?? t.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
}
