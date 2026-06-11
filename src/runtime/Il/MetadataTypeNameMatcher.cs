namespace Sherlock.MCP.Runtime.Il;

// String-level type-name matcher for resolved metadata names (e.g. "System.Console",
// "System.Collections.Generic.List`1", "Outer+Nested"). Mirrors TypeNameMatcher's flexibility
// (simple/full name, backtick arity, built-in aliases) but works on the strings produced by
// MetadataTokenResolver, so it matches external types not loadable in the inspection context.
internal static class MetadataTypeNameMatcher
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

    public static bool Matches(string metadataDeclaringType, string userSuppliedName, bool caseSensitive)
    {
        if (string.IsNullOrEmpty(metadataDeclaringType)) return false;
        if (string.IsNullOrWhiteSpace(userSuppliedName)) return false;

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        var full = metadataDeclaringType.Replace('+', '.');
        var fullStripped = StripArity(full);

        foreach (var variant in ExpandUserNameVariants(userSuppliedName, caseSensitive))
        {
            if (string.IsNullOrEmpty(variant)) continue;
            if (string.Equals(full, variant, comparison)) return true;
            if (string.Equals(fullStripped, variant, comparison)) return true;
            foreach (var suffix in DottedSuffixes(fullStripped))
                if (string.Equals(suffix, variant, comparison)) return true;
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
        var stripped = StripArity(s);
        yield return stripped;

        if (TryGetBuiltInClrName(stripped, caseSensitive, out var clr))
        {
            yield return clr;
            yield return "System." + clr;
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

    private static string StripArity(string name)
    {
        var backtickIdx = name.IndexOf('`');
        if (backtickIdx >= 0) name = name.Substring(0, backtickIdx);
        return name;
    }
}
