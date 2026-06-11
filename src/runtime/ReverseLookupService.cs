using System.Collections.Concurrent;
using System.Reflection;
using Sherlock.MCP.Runtime.Contracts.ReverseLookup;
using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Runtime;

public class ReverseLookupService : IReverseLookupService
{
    private readonly IInspectionContextProvider _contexts;

    public ReverseLookupService() : this(new SharedInspectionContextProvider(new RuntimeOptions()))
    {
    }

    public ReverseLookupService(IInspectionContextProvider contexts) => _contexts = contexts;

    public ImplementationHit[] FindImplementations(string[] assemblyPaths, string typeName, ReverseLookupOptions options)
    {
        var hits = new ConcurrentBag<ImplementationHit>();

        ScanInParallel(assemblyPaths, (path, ctx) =>
        {
            foreach (var candidate in GetScannableTypes(ctx, options))
            {
                var matchedInterfaces = GetInterfacesSafe(candidate)
                    .Where(i => TypeNameMatcher.Matches(i, typeName, options.CaseSensitive))
                    .Select(TypeNameFormatter.FriendlyFullName)
                    .ToArray();

                var baseTypeChain = GetBaseTypeChain(candidate);
                var matchedBases = baseTypeChain
                    .Where(b => TypeNameMatcher.Matches(b, typeName, options.CaseSensitive))
                    .Select(TypeNameFormatter.FriendlyFullName)
                    .ToArray();

                if (matchedInterfaces.Length == 0 && matchedBases.Length == 0) continue;

                var kind = matchedInterfaces.Length > 0 ? "interface" : "baseType";
                hits.Add(new ImplementationHit(
                    AssemblyPath: path,
                    TypeFullName: TypeNameFormatter.FriendlyFullName(candidate),
                    Kind: kind,
                    MatchedInterfaces: matchedInterfaces,
                    BaseTypeChain: baseTypeChain.Select(TypeNameFormatter.FriendlyFullName).ToArray()));
            }
        });

        return hits
            .OrderBy(h => h.AssemblyPath, StringComparer.Ordinal)
            .ThenBy(h => h.TypeFullName, StringComparer.Ordinal)
            .ToArray();
    }

    public MethodReturnHit[] FindMethodsReturning(string[] assemblyPaths, string typeName, ReverseLookupOptions options)
    {
        var hits = new ConcurrentBag<MethodReturnHit>();
        var flags = BuildMemberFlags(options);

        ScanInParallel(assemblyPaths, (path, ctx) =>
        {
            foreach (var candidate in GetScannableTypes(ctx, options))
            {
                foreach (var method in GetMethodsSafe(candidate, flags))
                {
                    Type? returnType;
                    try { returnType = method.ReturnType; }
                    catch { continue; }

                    if (!TypeNameMatcher.Matches(returnType, typeName, options.CaseSensitive)) continue;

                    hits.Add(new MethodReturnHit(
                        AssemblyPath: path,
                        DeclaringTypeFullName: TypeNameFormatter.FriendlyFullName(candidate),
                        MethodName: method.Name,
                        Signature: FormatMethodSignature(method),
                        ReturnTypeFriendlyName: FriendlyTypeName(returnType),
                        IsStatic: method.IsStatic));
                }
            }
        });

        return hits
            .OrderBy(h => h.AssemblyPath, StringComparer.Ordinal)
            .ThenBy(h => h.DeclaringTypeFullName, StringComparer.Ordinal)
            .ThenBy(h => h.MethodName, StringComparer.Ordinal)
            .ThenBy(h => h.Signature, StringComparer.Ordinal)
            .ToArray();
    }

    public ReferencesResult FindReferences(string[] assemblyPaths, string typeName, ReverseLookupOptions options)
    {
        var hits = new ConcurrentBag<ReferenceHit>();
        var flags = BuildMemberFlags(options);
        var cap = Math.Max(1, options.HardCap);
        var reserved = 0;
        var truncated = 0;

        bool TryAdd(ReferenceHit hit)
        {
            if (Interlocked.Increment(ref reserved) > cap)
            {
                Interlocked.Exchange(ref truncated, 1);
                return false;
            }
            hits.Add(hit);
            return true;
        }

        ScanInParallel(assemblyPaths, (path, ctx) =>
        {
            foreach (var candidate in GetScannableTypes(ctx, options))
            {
                if (Volatile.Read(ref truncated) == 1) return;

                var declaringName = TypeNameFormatter.FriendlyFullName(candidate);

                Type? baseType = null;
                try { baseType = candidate.BaseType; } catch { }
                if (baseType != null && TypeNameMatcher.Matches(baseType, typeName, options.CaseSensitive))
                {
                    if (!TryAdd(MakeRefHit(path, declaringName, "type", declaringName, "baseType",
                        $"{declaringName} : {FriendlyTypeName(baseType)}",
                        disambiguator: TypeNameFormatter.FriendlyFullName(baseType)))) return;
                }

                foreach (var iface in GetInterfacesSafe(candidate))
                {
                    if (!TypeNameMatcher.Matches(iface, typeName, options.CaseSensitive)) continue;
                    if (!TryAdd(MakeRefHit(path, declaringName, "type", declaringName, "interface",
                        $"{declaringName} : {FriendlyTypeName(iface)}",
                        disambiguator: TypeNameFormatter.FriendlyFullName(iface)))) return;
                }

                if (candidate.IsGenericType)
                {
                    Type[] args;
                    try { args = candidate.GetGenericArguments(); } catch { args = Array.Empty<Type>(); }
                    foreach (var arg in args)
                    {
                        if (!TypeNameMatcher.Matches(arg, typeName, options.CaseSensitive)) continue;
                        if (!TryAdd(MakeRefHit(path, declaringName, "type", declaringName, "genericArg",
                            $"{declaringName}<{FriendlyTypeName(arg)}>",
                            disambiguator: TypeNameFormatter.FriendlyFullName(arg)))) return;
                    }
                }

                foreach (var method in GetMethodsSafe(candidate, flags))
                {
                    var sig = FormatMethodSignature(method);

                    Type? returnType = null;
                    try { returnType = method.ReturnType; } catch { }
                    var returnMatch = FindMatchingType(returnType, typeName, options.CaseSensitive);
                    if (returnMatch != null)
                    {
                        if (!TryAdd(MakeRefHit(path, declaringName, "method", method.Name, "return", sig,
                            disambiguator: TypeNameFormatter.FriendlyFullName(returnMatch)))) return;
                    }

                    ParameterInfo[] parameters;
                    try { parameters = method.GetParameters(); } catch { parameters = Array.Empty<ParameterInfo>(); }
                    foreach (var p in parameters)
                    {
                        Type? pt;
                        try { pt = p.ParameterType; } catch { continue; }
                        if (FindMatchingType(pt, typeName, options.CaseSensitive) == null) continue;
                        if (!TryAdd(MakeRefHit(path, declaringName, "method", method.Name, "parameter", sig,
                            disambiguator: p.Name ?? p.Position.ToString()))) return;
                    }
                }

                foreach (var prop in GetPropertiesSafe(candidate, flags))
                {
                    Type? pt;
                    try { pt = prop.PropertyType; } catch { continue; }
                    var propMatch = FindMatchingType(pt, typeName, options.CaseSensitive);
                    if (propMatch == null) continue;
                    if (!TryAdd(MakeRefHit(path, declaringName, "property", prop.Name, "property",
                        $"{FriendlyTypeName(pt)} {prop.Name}",
                        disambiguator: TypeNameFormatter.FriendlyFullName(propMatch)))) return;
                }

                foreach (var field in GetFieldsSafe(candidate, flags))
                {
                    Type? ft;
                    try { ft = field.FieldType; } catch { continue; }
                    var fieldMatch = FindMatchingType(ft, typeName, options.CaseSensitive);
                    if (fieldMatch == null) continue;
                    if (!TryAdd(MakeRefHit(path, declaringName, "field", field.Name, "field",
                        $"{FriendlyTypeName(ft)} {field.Name}",
                        disambiguator: TypeNameFormatter.FriendlyFullName(fieldMatch)))) return;
                }

                foreach (var evt in GetEventsSafe(candidate, flags))
                {
                    Type? et;
                    try { et = evt.EventHandlerType; } catch { continue; }
                    var eventMatch = FindMatchingType(et, typeName, options.CaseSensitive);
                    if (eventMatch == null) continue;
                    if (!TryAdd(MakeRefHit(path, declaringName, "event", evt.Name, "event",
                        $"event {FriendlyTypeName(et!)} {evt.Name}",
                        disambiguator: TypeNameFormatter.FriendlyFullName(eventMatch)))) return;
                }
            }
        });

        var sorted = hits
            .OrderBy(h => h.AssemblyPath, StringComparer.Ordinal)
            .ThenBy(h => h.DeclaringTypeFullName, StringComparer.Ordinal)
            .ThenBy(h => h.MemberKind, StringComparer.Ordinal)
            .ThenBy(h => h.MemberName, StringComparer.Ordinal)
            .ThenBy(h => h.ReferenceKind, StringComparer.Ordinal)
            .ThenBy(h => h.DedupeKey, StringComparer.Ordinal)
            .ToArray();

        return new ReferencesResult(sorted, truncated == 1);
    }

    private void ScanInParallel(string[] assemblyPaths, Action<string, IAssemblyInspectionContext> scan)
    {
        var paths = assemblyPaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Parallel.ForEach(
            paths,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            path => TryScanAssembly(path, scan));
    }

    private static ReferenceHit MakeRefHit(
        string assemblyPath, string declaringType, string memberKind, string memberName,
        string referenceKind, string signature, string disambiguator)
    {
        var isMethodLike =
            string.Equals(memberKind, "method", StringComparison.Ordinal) ||
            string.Equals(memberKind, "constructor", StringComparison.Ordinal);

        var dedupeKey = isMethodLike
            ? $"{declaringType}|{memberKind}|{memberName}|{referenceKind}|{signature}|{disambiguator}"
            : $"{declaringType}|{memberKind}|{memberName}|{referenceKind}|{disambiguator}";

        return new(assemblyPath, declaringType, memberKind, memberName, referenceKind, signature, dedupeKey);
    }

    private bool TryScanAssembly(string path, Action<string, IAssemblyInspectionContext> scan)
    {
        try
        {
            using var lease = _contexts.Acquire(path);
            scan(path, lease.Context);
            return true;
        }
        catch (BadImageFormatException) { return false; }
        catch (FileLoadException) { return false; }
        catch (FileNotFoundException) { return false; }
        catch (ReflectionTypeLoadException) { return false; }
        catch (IOException) { return false; }
    }

    private static Type? FindMatchingType(Type? container, string userName, bool caseSensitive)
    {
        if (container == null) return null;
        if (TypeNameMatcher.Matches(container, userName, caseSensitive)) return container;

        Type? inner = null;
        try { inner = container.IsGenericType ? null : container.GetElementType(); }
        catch { inner = null; }
        if (inner != null)
        {
            var match = FindMatchingType(inner, userName, caseSensitive);
            if (match != null) return match;
        }

        if (container.IsGenericType)
        {
            Type[] args;
            try { args = container.GetGenericArguments(); } catch { args = Array.Empty<Type>(); }
            foreach (var arg in args)
            {
                var match = FindMatchingType(arg, userName, caseSensitive);
                if (match != null) return match;
            }
        }
        return null;
    }

    private static BindingFlags BuildMemberFlags(ReverseLookupOptions options)
    {
        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        if (options.IncludeNonPublic) flags |= BindingFlags.NonPublic;
        return flags;
    }

    private static IEnumerable<Type> GetScannableTypes(IAssemblyInspectionContext ctx, ReverseLookupOptions options)
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

    private static Type[] GetInterfacesSafe(Type t)
    {
        try { return t.GetInterfaces(); }
        catch { return Array.Empty<Type>(); }
    }

    private static List<Type> GetBaseTypeChain(Type t)
    {
        var chain = new List<Type>();
        try
        {
            var current = t.BaseType;
            while (current != null)
            {
                chain.Add(current);
                current = current.BaseType;
            }
        }
        catch { }
        return chain;
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
