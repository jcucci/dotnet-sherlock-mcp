using System.Reflection;
using Sherlock.MCP.Runtime.Contracts.Common;
using Sherlock.MCP.Runtime.Contracts.Search;
using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Runtime;

public class SearchService : ISearchService
{
    private readonly IInspectionContextProvider _contexts;

    public SearchService() : this(new SharedInspectionContextProvider(new RuntimeOptions()))
    {
    }

    public SearchService(IInspectionContextProvider contexts) => _contexts = contexts;

    public PagedResult<MemberSearchHit> SearchMembers(
        string assemblyPath, string nameContains, SearchOptions options, int offset, int pageSize)
    {
        var comparison = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var flags = BuildMemberFlags(options);

        bool WantsKind(string kind) => options.MemberKinds == null || options.MemberKinds.Contains(kind);
        bool NameMatches(string name) => name.Contains(nameContains, comparison);

        var candidates = new List<Candidate>();

        try
        {
            using var lease = _contexts.Acquire(assemblyPath);
            var ctx = lease.Context;

            foreach (var type in GetScannableTypes(ctx, options))
            {
                if (WantsKind("type") && NameMatches(type.Name))
                    candidates.Add(new Candidate(DeclaringTypeOf(type), "type", FriendlyTypeName(type), TokenOf(type), type));

                var declaringName = TypeNameFormatter.FriendlyFullName(type);

                if (WantsKind("method"))
                    foreach (var method in GetMethodsSafe(type, flags))
                        if (NameMatches(method.Name))
                            candidates.Add(new Candidate(declaringName, "method", method.Name, TokenOf(method), method));

                if (WantsKind("property"))
                    foreach (var prop in GetPropertiesSafe(type, flags))
                        if (NameMatches(prop.Name))
                            candidates.Add(new Candidate(declaringName, "property", prop.Name, TokenOf(prop), prop));

                if (WantsKind("field"))
                    foreach (var field in GetFieldsSafe(type, flags))
                        if (NameMatches(field.Name))
                            candidates.Add(new Candidate(declaringName, "field", field.Name, TokenOf(field), field));

                if (WantsKind("event"))
                    foreach (var evt in GetEventsSafe(type, flags))
                        if (NameMatches(evt.Name))
                            candidates.Add(new Candidate(declaringName, "event", evt.Name, TokenOf(evt), evt));
            }

            var sorted = candidates
                .OrderBy(c => c.DeclaringType, StringComparer.Ordinal)
                .ThenBy(c => c.MemberKind, StringComparer.Ordinal)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .ThenBy(c => c.Token)
                .ToArray();

            var page = sorted
                .Skip(Math.Max(0, offset))
                .Take(Math.Max(0, pageSize))
                .Select(BuildHit)
                .ToArray();

            return new PagedResult<MemberSearchHit>(sorted.Length, page);
        }
        catch (BadImageFormatException) { return Empty; }
        catch (FileLoadException) { return Empty; }
        catch (FileNotFoundException) { return Empty; }
        catch (ReflectionTypeLoadException) { return Empty; }
        catch (IOException) { return Empty; }
    }

    private static readonly PagedResult<MemberSearchHit> Empty = new(0, Array.Empty<MemberSearchHit>());

    private readonly record struct Candidate(
        string DeclaringType, string MemberKind, string Name, int Token, object Member);

    private static MemberSearchHit BuildHit(Candidate c)
    {
        var signature = c.Member switch
        {
            MethodInfo m => FormatMethodSignature(m),
            PropertyInfo p => FormatPropertySignature(p),
            FieldInfo f => FormatFieldSignature(f),
            EventInfo e => FormatEventSignature(e),
            Type t => FormatTypeSignature(t),
            _ => c.Name
        };
        return new MemberSearchHit(c.DeclaringType, c.MemberKind, c.Name, signature);
    }

    private static string DeclaringTypeOf(Type type)
    {
        if (type.IsNested && type.DeclaringType != null)
            return TypeNameFormatter.FriendlyFullName(type.DeclaringType);
        return type.Namespace ?? string.Empty;
    }

    private static int TokenOf(MemberInfo member)
    {
        try { return member.MetadataToken; }
        catch { return 0; }
    }

    private static string FormatPropertySignature(PropertyInfo prop)
    {
        string pt;
        try { pt = FriendlyTypeName(prop.PropertyType); }
        catch { pt = "?"; }
        return $"{pt} {prop.Name}";
    }

    private static string FormatFieldSignature(FieldInfo field)
    {
        string ft;
        try { ft = FriendlyTypeName(field.FieldType); }
        catch { ft = "?"; }
        return $"{ft} {field.Name}";
    }

    private static string FormatEventSignature(EventInfo evt)
    {
        string et;
        try { et = evt.EventHandlerType != null ? FriendlyTypeName(evt.EventHandlerType) : "?"; }
        catch { et = "?"; }
        return $"event {et} {evt.Name}";
    }

    private static string FormatTypeSignature(Type type)
    {
        var keyword = type.IsInterface ? "interface"
            : type.IsEnum ? "enum"
            : type.IsValueType ? "struct"
            : "class";
        return $"{keyword} {TypeNameFormatter.FriendlyFullName(type)}";
    }

    private static BindingFlags BuildMemberFlags(SearchOptions options)
    {
        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        if (options.IncludeNonPublic) flags |= BindingFlags.NonPublic;
        return flags;
    }

    private static IEnumerable<Type> GetScannableTypes(IAssemblyInspectionContext ctx, SearchOptions options)
    {
        IEnumerable<Type> types;
        try { types = ctx.GetTypes(); }
        catch { yield break; }

        foreach (var t in types)
        {
            if (t == null) continue;
            if (t.IsGenericParameter) continue;
            if (!options.IncludeNonPublic && !t.IsPublic && !t.IsNestedPublic) continue;
            yield return t;
        }
    }

    private static MethodInfo[] GetMethodsSafe(Type t, BindingFlags flags)
    {
        try { return t.GetMethods(flags).Where(m => !m.IsSpecialName).ToArray(); }
        catch { return Array.Empty<MethodInfo>(); }
    }

    private static PropertyInfo[] GetPropertiesSafe(Type t, BindingFlags flags)
    {
        try { return t.GetProperties(flags); }
        catch { return Array.Empty<PropertyInfo>(); }
    }

    private static FieldInfo[] GetFieldsSafe(Type t, BindingFlags flags)
    {
        try { return t.GetFields(flags); }
        catch { return Array.Empty<FieldInfo>(); }
    }

    private static EventInfo[] GetEventsSafe(Type t, BindingFlags flags)
    {
        try { return t.GetEvents(flags); }
        catch { return Array.Empty<EventInfo>(); }
    }

    private static string FormatMethodSignature(MethodInfo m)
    {
        string ret;
        try { ret = FriendlyTypeName(m.ReturnType); }
        catch { ret = "?"; }

        ParameterInfo[] parameters;
        try { parameters = m.GetParameters(); }
        catch { parameters = Array.Empty<ParameterInfo>(); }

        var ps = string.Join(", ", parameters.Select(p =>
        {
            string pt;
            try { pt = FriendlyTypeName(p.ParameterType); }
            catch { pt = "?"; }
            return $"{pt} {p.Name}";
        }));

        return $"{ret} {m.Name}({ps})";
    }

    private static string FriendlyTypeName(Type t) => TypeNameFormatter.FriendlyName(t);
}
