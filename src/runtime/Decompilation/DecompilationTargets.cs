using System.Reflection;

namespace Sherlock.MCP.Runtime.Decompilation;

public static class DecompilationTargets
{
    public static IReadOnlyList<MemberInfo> SelectMembers(
        Type type, string memberName, string? parameterTypes, StringComparison comparison, bool includeNonPublic)
    {
        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        if (includeNonPublic) flags |= BindingFlags.NonPublic;

        var members = Candidates(type, memberName, flags, comparison);
        if (parameterTypes == null) return members;

        var wanted = SplitParameterTypes(parameterTypes);
        return members.Where(m => ParametersMatch(m, wanted, comparison)).ToArray();
    }

    public static string FormatSignature(MemberInfo member) => member switch
    {
        ConstructorInfo ctor => $"{(ctor.IsStatic ? "static " : "")}{ConstructorName(ctor)}({FormatParameters(ctor)})",
        MethodInfo method => $"{SafeName(() => method.ReturnType)} {method.Name}({FormatParameters(method)})",
        PropertyInfo property => FormatProperty(property),
        FieldInfo field => $"{SafeName(() => field.FieldType)} {field.Name}",
        EventInfo evt => $"event {SafeName(() => evt.EventHandlerType!)} {evt.Name}",
        _ => member.Name
    };

    public static IReadOnlyList<string> SplitParameterTypes(string parameterTypes)
    {
        if (string.IsNullOrWhiteSpace(parameterTypes)) return [];

        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < parameterTypes.Length; i++)
        {
            switch (parameterTypes[i])
            {
                case '<' or '[': depth++; break;
                case '>' or ']': depth--; break;
                case ',' when depth == 0:
                    parts.Add(Normalize(parameterTypes[start..i]));
                    start = i + 1;
                    break;
            }
        }
        parts.Add(Normalize(parameterTypes[start..]));
        return parts;
    }

    private static MemberInfo[] Candidates(Type type, string memberName, BindingFlags flags, StringComparison comparison)
    {
        if (IsStaticConstructorName(memberName))
            return Safe(() => type.TypeInitializer) is { } cctor && (flags.HasFlag(BindingFlags.NonPublic) || cctor.IsPublic) ? [cctor] : [];
        if (IsInstanceConstructorName(memberName))
            return Safe(() => type.GetConstructors(flags))?.Where(c => !c.IsStatic).ToArray<MemberInfo>() ?? [];

        return (Safe(() => type.GetMembers(flags)) ?? [])
            .Where(m => m is MethodInfo or PropertyInfo or FieldInfo or EventInfo)
            .Where(m => string.Equals(m.Name, memberName, comparison))
            .ToArray();
    }

    private static bool ParametersMatch(MemberInfo member, IReadOnlyList<string> wanted, StringComparison comparison)
    {
        var parameters = member switch
        {
            MethodBase method => Safe(method.GetParameters),
            PropertyInfo property => Safe(property.GetIndexParameters),
            _ => null
        };
        if (parameters == null || parameters.Length != wanted.Count) return false;

        return parameters.Zip(wanted).All(pair => TypeMatches(pair.First, pair.Second, comparison));
    }

    private static bool TypeMatches(ParameterInfo parameter, string wanted, StringComparison comparison)
    {
        var type = Safe(() => parameter.ParameterType);
        if (type == null) return false;

        string?[] names = [TypeNameFormatter.FriendlyName(type), TypeNameFormatter.FriendlyFullName(type), type.Name, type.FullName];
        return names.Any(name => name != null && string.Equals(Normalize(name), wanted, comparison));
    }

    private static string ConstructorName(ConstructorInfo ctor)
    {
        var name = ctor.DeclaringType?.Name ?? ctor.Name;
        var tick = name.IndexOf('`');
        return tick > 0 ? name[..tick] : name;
    }

    private static string FormatProperty(PropertyInfo property)
    {
        var indexParameters = Safe(property.GetIndexParameters) ?? [];
        var type = SafeName(() => property.PropertyType);
        return indexParameters.Length == 0
            ? $"{type} {property.Name}"
            : $"{type} this[{string.Join(", ", indexParameters.Select(FormatParameter))}]";
    }

    private static string FormatParameters(MethodBase method) =>
        string.Join(", ", (Safe(method.GetParameters) ?? []).Select(FormatParameter));

    private static string FormatParameter(ParameterInfo parameter) =>
        $"{SafeName(() => parameter.ParameterType)} {parameter.Name}";

    private static string SafeName(Func<Type> type) =>
        Safe(type) is { } resolved ? TypeNameFormatter.FriendlyName(resolved) : "?";

    private static string Normalize(string typeName) =>
        string.Concat(typeName.Where(c => !char.IsWhiteSpace(c)));

    private static bool IsInstanceConstructorName(string memberName) => memberName is ".ctor" or "ctor";

    private static bool IsStaticConstructorName(string memberName) => memberName is ".cctor" or "cctor";

    private static T? Safe<T>(Func<T?> read) where T : class
    {
        try { return read(); }
        catch { return null; }
    }
}
