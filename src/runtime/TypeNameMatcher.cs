namespace Sherlock.MCP.Runtime;

public static class TypeNameMatcher
{
    private static readonly Dictionary<string, string> BuiltInToClrName = new(StringComparer.Ordinal)
    {
        ["bool"] = "Boolean",
        ["byte"] = "Byte",
        ["sbyte"] = "SByte",
        ["char"] = "Char",
        ["decimal"] = "Decimal",
        ["double"] = "Double",
        ["float"] = "Single",
        ["int"] = "Int32",
        ["uint"] = "UInt32",
        ["long"] = "Int64",
        ["ulong"] = "UInt64",
        ["object"] = "Object",
        ["short"] = "Int16",
        ["ushort"] = "UInt16",
        ["string"] = "String",
        ["void"] = "Void",
        ["nint"] = "IntPtr",
        ["nuint"] = "UIntPtr"
    };

    public static bool Matches(Type? candidate, string? userSuppliedName, bool caseSensitive = false)
    {
        if (candidate == null) return false;
        if (string.IsNullOrWhiteSpace(userSuppliedName)) return false;
        if (candidate.IsGenericParameter) return false;

        if (candidate.IsArray || candidate.IsByRef || candidate.IsPointer)
        {
            var elem = candidate.GetElementType();
            return elem != null && Matches(elem, userSuppliedName, caseSensitive);
        }

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var candidateVariants = GetCandidateNameVariants(candidate).ToArray();

        foreach (var userVariant in ExpandUserNameVariants(userSuppliedName!, caseSensitive))
        {
            if (string.IsNullOrEmpty(userVariant)) continue;
            foreach (var candidateName in candidateVariants)
            {
                if (string.Equals(candidateName, userVariant, comparison)) return true;
            }
        }
        return false;
    }

    private static IEnumerable<string> ExpandUserNameVariants(string userSuppliedName, bool caseSensitive)
    {
        var s = userSuppliedName.Trim();

        var commaIdx = s.IndexOf(',');
        if (commaIdx >= 0) s = s.Substring(0, commaIdx).Trim();

        s = s.Replace('+', '.');

        yield return s;
        var stripped = StripArityAndArgs(s);
        yield return stripped;

        if (TryGetBuiltInClrName(stripped, caseSensitive, out var clr))
        {
            yield return clr;
            yield return "System." + clr;
        }

        if (s.EndsWith("?") && s.Length > 1)
        {
            var inner = s.Substring(0, s.Length - 1).Trim();
            yield return $"Nullable<{inner}>";
            yield return $"System.Nullable<{inner}>";
            yield return "Nullable";
            yield return "System.Nullable";
            if (TryGetBuiltInClrName(inner, caseSensitive, out var innerClr))
            {
                yield return innerClr;
                yield return "System." + innerClr;
            }
            else
            {
                yield return inner;
            }
        }
    }

    private static bool TryGetBuiltInClrName(string alias, bool caseSensitive, out string clrName)
    {
        if (caseSensitive)
            return BuiltInToClrName.TryGetValue(alias, out clrName!);

        foreach (var kvp in BuiltInToClrName)
        {
            if (string.Equals(kvp.Key, alias, StringComparison.OrdinalIgnoreCase))
            {
                clrName = kvp.Value;
                return true;
            }
        }
        clrName = string.Empty;
        return false;
    }

    private static IEnumerable<string> GetCandidateNameVariants(Type candidate)
    {
        var full = (candidate.FullName ?? candidate.Name).Replace('+', '.');
        var simple = candidate.Name;

        yield return full;
        yield return simple;

        var fullStripped = StripArityAndArgs(full);
        var simpleStripped = StripArityAndArgs(simple);
        yield return fullStripped;
        yield return simpleStripped;

        foreach (var suffix in DottedSuffixes(full))
            yield return suffix;
        foreach (var suffix in DottedSuffixes(fullStripped))
            yield return suffix;
    }

    private static IEnumerable<string> DottedSuffixes(string dotted)
    {
        var idx = 0;
        while (true)
        {
            var nextDot = dotted.IndexOf('.', idx);
            if (nextDot < 0) yield break;
            var suffix = dotted.Substring(nextDot + 1);
            if (suffix.Length > 0) yield return suffix;
            idx = nextDot + 1;
        }
    }

    private static string StripArityAndArgs(string name)
    {
        var bracketIdx = name.IndexOf('<');
        if (bracketIdx >= 0) name = name.Substring(0, bracketIdx);
        var backtickIdx = name.IndexOf('`');
        if (backtickIdx >= 0) name = name.Substring(0, backtickIdx);
        return name;
    }
}
