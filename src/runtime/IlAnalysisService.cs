using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Sherlock.MCP.Runtime.Contracts.Il;
using Sherlock.MCP.Runtime.Contracts.ReverseLookup;
using Sherlock.MCP.Runtime.Il;
using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Runtime;

public class IlAnalysisService : IIlAnalysisService
{
    private readonly IInspectionContextProvider _contexts;

    public IlAnalysisService() : this(new SharedInspectionContextProvider(new RuntimeOptions()))
    {
    }

    public IlAnalysisService(IInspectionContextProvider contexts) => _contexts = contexts;

    public MethodCallsResult? GetMethodCalls(string assemblyPath, string typeName, string methodName, IlAnalysisOptions options)
    {
        if (!File.Exists(assemblyPath)) return null;

        var flags = BuildMemberFlags(options.IncludeNonPublic);
        Type? targetType = null;
        var targets = new List<MethodBase>();

        using (var lease = _contexts.Acquire(assemblyPath))
        {
            foreach (var candidate in SafeGetTypes(lease.Context))
            {
                if (!TypeNameMatcher.Matches(candidate, typeName, options.CaseSensitive)) continue;

                targetType = candidate;
                foreach (var method in SafeGetMethods(candidate, flags))
                {
                    if (NameEquals(method.Name, methodName, options.CaseSensitive)) targets.Add(method);
                }
                if (IsStaticConstructorName(methodName))
                {
                    var cctor = SafeGetTypeInitializer(candidate);
                    if (cctor != null) targets.Add(cctor);
                }
                else if (IsInstanceConstructorName(methodName))
                {
                    foreach (var ctor in SafeGetConstructors(candidate, flags))
                        if (!ctor.IsStatic) targets.Add(ctor);
                }
                break;
            }
        }

        if (targetType == null || targets.Count == 0) return null;

        var declaringFullName = TypeNameFormatter.FriendlyFullName(targetType);
        var calls = new List<MethodCallInfo>();
        var fieldAccesses = new List<FieldAccessInfo>();
        var anyBodyless = false;

        using (var stream = File.OpenRead(assemblyPath))
        using (var pe = new PEReader(stream))
        {
            if (!pe.HasMetadata) return null;
            var md = pe.GetMetadataReader();
            var resolver = new MetadataTokenResolver(md);

            foreach (var method in targets)
            {
                var il = TryGetMethodBody(pe, md, method.MetadataToken, out var hadBody);
                if (!hadBody)
                {
                    anyBodyless = true;
                    continue;
                }

                var sourceMethod = FormatSignature(method);
                foreach (var tokenRef in IlInstructionReader.ReadTokenInstructions(il))
                {
                    var resolved = resolver.Resolve(tokenRef.Token);
                    if (resolved == null) continue;

                    if (IsFieldAccess(tokenRef.Kind))
                        fieldAccesses.Add(new FieldAccessInfo(resolved.Value.Display, FieldAccessName(tokenRef.Kind), sourceMethod));
                    else
                        calls.Add(new MethodCallInfo(resolved.Value.Display, CallKindName(tokenRef.Kind), sourceMethod));
                }
            }
        }

        var distinctCalls = calls
            .GroupBy(c => (c.Target, c.Kind, c.SourceMethod))
            .Select(g => g.First())
            .OrderBy(c => c.Target, StringComparer.Ordinal)
            .ThenBy(c => c.Kind, StringComparer.Ordinal)
            .ThenBy(c => c.SourceMethod, StringComparer.Ordinal)
            .ToArray();

        var distinctFields = fieldAccesses
            .GroupBy(f => (f.Target, f.Access, f.SourceMethod))
            .Select(g => g.First())
            .OrderBy(f => f.Target, StringComparer.Ordinal)
            .ThenBy(f => f.Access, StringComparer.Ordinal)
            .ThenBy(f => f.SourceMethod, StringComparer.Ordinal)
            .ToArray();

        return new MethodCallsResult(declaringFullName, methodName, targets.Count, anyBodyless, distinctCalls, distinctFields);
    }

    public InboundCallHit[] FindInboundCallers(string[] assemblyPaths, string typeName, ReverseLookupOptions options)
    {
        var hits = new ConcurrentBag<InboundCallHit>();
        var cap = Math.Max(1, options.HardCap);
        var reserved = 0;
        var truncated = 0;

        bool TryAdd(InboundCallHit hit)
        {
            if (Interlocked.Increment(ref reserved) > cap)
            {
                Interlocked.Exchange(ref truncated, 1);
                return false;
            }
            hits.Add(hit);
            return true;
        }

        var paths = assemblyPaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Parallel.ForEach(
            paths,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            path =>
            {
                if (Volatile.Read(ref truncated) == 1) return;
                ScanAssemblyForCallers(path, typeName, options, TryAdd, () => Volatile.Read(ref truncated) == 1);
            });

        return hits
            .OrderBy(h => h.AssemblyPath, StringComparer.Ordinal)
            .ThenBy(h => h.CallerTypeFullName, StringComparer.Ordinal)
            .ThenBy(h => h.CallerMethod, StringComparer.Ordinal)
            .ThenBy(h => h.ReferenceKind, StringComparer.Ordinal)
            .ThenBy(h => h.TargetMember, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ScanAssemblyForCallers(
        string path, string typeName, ReverseLookupOptions options,
        Func<InboundCallHit, bool> tryAdd, Func<bool> isTruncated)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return;
            var md = pe.GetMetadataReader();
            var resolver = new MetadataTokenResolver(md);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var typeHandle in md.TypeDefinitions)
            {
                if (isTruncated()) return;
                var typeDef = md.GetTypeDefinition(typeHandle);
                if (!options.IncludeNonPublic && !IsTypeVisible(typeDef.Attributes)) continue;

                var callerType = resolver.TypeDefName(typeHandle);

                foreach (var methodHandle in typeDef.GetMethods())
                {
                    var methodDef = md.GetMethodDefinition(methodHandle);
                    if (!options.IncludeNonPublic && !IsMethodPublic(methodDef.Attributes)) continue;
                    if (methodDef.RelativeVirtualAddress == 0) continue;

                    MethodBodyBlock body;
                    try { body = pe.GetMethodBody(methodDef.RelativeVirtualAddress); }
                    catch (BadImageFormatException) { continue; }

                    var callerMethod = md.GetString(methodDef.Name);
                    foreach (var tokenRef in IlInstructionReader.ReadTokenInstructions(body.GetILBytes()))
                    {
                        var resolved = resolver.Resolve(tokenRef.Token);
                        if (resolved == null) continue;
                        if (!MetadataTypeNameMatcher.Matches(resolved.Value.DeclaringType, typeName, options.CaseSensitive)) continue;

                        var referenceKind = ReferenceKindName(tokenRef.Kind);
                        if (!seen.Add($"{callerType}|{callerMethod}|{referenceKind}|{resolved.Value.Display}")) continue;

                        if (!tryAdd(new InboundCallHit(path, callerType, callerMethod, referenceKind, resolved.Value.Display)))
                            return;
                    }
                }
            }
        }
        catch (BadImageFormatException) { }
        catch (FileLoadException) { }
        catch (FileNotFoundException) { }
        catch (IOException) { }
    }

    private static byte[]? TryGetMethodBody(PEReader pe, MetadataReader md, int metadataToken, out bool hadBody)
    {
        hadBody = false;
        EntityHandle handle;
        try { handle = MetadataTokens.EntityHandle(metadataToken); }
        catch (ArgumentException) { return null; }
        if (handle.Kind != HandleKind.MethodDefinition) return null;

        var def = md.GetMethodDefinition((MethodDefinitionHandle)handle);
        if (def.RelativeVirtualAddress == 0) return null;

        MethodBodyBlock body;
        try { body = pe.GetMethodBody(def.RelativeVirtualAddress); }
        catch (BadImageFormatException) { return null; }

        hadBody = true;
        return body.GetILBytes();
    }

    private static BindingFlags BuildMemberFlags(bool includeNonPublic)
    {
        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        if (includeNonPublic) flags |= BindingFlags.NonPublic;
        return flags;
    }

    private static IEnumerable<Type> SafeGetTypes(IAssemblyInspectionContext ctx)
    {
        try { return ctx.GetTypes(); }
        catch { return Array.Empty<Type>(); }
    }

    private static MethodInfo[] SafeGetMethods(Type t, BindingFlags flags)
    {
        try { return t.GetMethods(flags); }
        catch { return Array.Empty<MethodInfo>(); }
    }

    private static ConstructorInfo[] SafeGetConstructors(Type t, BindingFlags flags)
    {
        try { return t.GetConstructors(flags); }
        catch { return Array.Empty<ConstructorInfo>(); }
    }

    private static ConstructorInfo? SafeGetTypeInitializer(Type t)
    {
        try { return t.TypeInitializer; }
        catch { return null; }
    }

    private static bool NameEquals(string a, string b, bool caseSensitive) =>
        string.Equals(a, b, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static bool IsInstanceConstructorName(string methodName) =>
        methodName is ".ctor" or "ctor";

    private static bool IsStaticConstructorName(string methodName) =>
        methodName is ".cctor" or "cctor";

    private static bool IsFieldAccess(IlRefKind kind) =>
        kind is IlRefKind.LdFld or IlRefKind.LdsFld or IlRefKind.StFld or IlRefKind.StsFld;

    private static string FieldAccessName(IlRefKind kind) =>
        kind is IlRefKind.LdFld or IlRefKind.LdsFld ? "read" : "write";

    private static string CallKindName(IlRefKind kind) => kind switch
    {
        IlRefKind.Call => "call",
        IlRefKind.CallVirt => "callvirt",
        IlRefKind.NewObj => "newobj",
        IlRefKind.LdFtn => "ldftn",
        IlRefKind.LdVirtFtn => "ldvirtftn",
        _ => "call"
    };

    private static string ReferenceKindName(IlRefKind kind) => kind switch
    {
        IlRefKind.LdFld or IlRefKind.LdsFld => "ilFieldRead",
        IlRefKind.StFld or IlRefKind.StsFld => "ilFieldWrite",
        _ => "ilCall"
    };

    private static bool IsTypeVisible(TypeAttributes attributes)
    {
        var visibility = attributes & TypeAttributes.VisibilityMask;
        return visibility is TypeAttributes.Public or TypeAttributes.NestedPublic;
    }

    private static bool IsMethodPublic(MethodAttributes attributes) =>
        (attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;

    private static string FormatSignature(MethodBase method)
    {
        string ret;
        try { ret = method is MethodInfo mi ? TypeNameFormatter.FriendlyName(mi.ReturnType) : "void"; }
        catch { ret = "?"; }

        ParameterInfo[] parameters;
        try { parameters = method.GetParameters(); }
        catch { parameters = Array.Empty<ParameterInfo>(); }

        var ps = string.Join(", ", parameters.Select(p =>
        {
            string pt;
            try { pt = TypeNameFormatter.FriendlyName(p.ParameterType); }
            catch { pt = "?"; }
            return $"{pt} {p.Name}";
        }));

        return $"{ret} {method.Name}({ps})";
    }
}
