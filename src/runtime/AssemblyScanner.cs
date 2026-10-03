using System.Reflection;
using Sherlock.MCP.Runtime.Contracts.ReverseLookup;
using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Runtime;

internal static class AssemblyScanner
{
    internal static void ScanInParallel(
        IInspectionContextProvider contexts, string[] assemblyPaths, Action<string, IAssemblyInspectionContext> scan,
        IProgress<ScanProgress>? progress, CancellationToken cancellationToken) =>
        ForEachAssembly(assemblyPaths, path => TryScan(() =>
        {
            using var lease = contexts.Acquire(path);
            scan(path, lease.Context);
        }), progress, cancellationToken);

    internal static void ScanMetadataInParallel(
        IMetadataReaderProvider readers, string[] assemblyPaths, Action<string, MetadataReaderLease> scan,
        IProgress<ScanProgress>? progress, CancellationToken cancellationToken) =>
        ForEachAssembly(assemblyPaths, path => TryScan(() =>
        {
            using var lease = readers.AcquireMetadata(path);
            scan(path, lease);
        }), progress, cancellationToken);

    internal static BindingFlags BuildMemberFlags(ReverseLookupOptions options)
    {
        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        if (options.IncludeNonPublic) flags |= BindingFlags.NonPublic;
        return flags;
    }

    internal static IEnumerable<Type> GetScannableTypes(
        IAssemblyInspectionContext ctx, ReverseLookupOptions options, CancellationToken cancellationToken)
    {
        IEnumerable<Type> types;
        try { types = ctx.GetTypes(); }
        catch { yield break; }

        foreach (var t in types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (t == null) continue;
            if (t.IsGenericParameter) continue;
            if (!options.IncludeNonPublic && !t.IsPublic && !t.IsNestedPublic) continue;
            yield return t;
        }
    }

    internal static Type[] GetInterfacesSafe(Type t)
    {
        try { return t.GetInterfaces(); }
        catch { return Array.Empty<Type>(); }
    }

    internal static List<Type> GetBaseTypeChain(Type t)
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

    internal static MethodInfo[] GetMethodsSafe(Type t, BindingFlags flags)
    {
        try { return t.GetMethods(flags).Where(m => !m.IsSpecialName).ToArray(); }
        catch { return Array.Empty<MethodInfo>(); }
    }

    internal static PropertyInfo[] GetPropertiesSafe(Type t, BindingFlags flags)
    {
        try { return t.GetProperties(flags); }
        catch { return Array.Empty<PropertyInfo>(); }
    }

    internal static FieldInfo[] GetFieldsSafe(Type t, BindingFlags flags)
    {
        try { return t.GetFields(flags); }
        catch { return Array.Empty<FieldInfo>(); }
    }

    internal static EventInfo[] GetEventsSafe(Type t, BindingFlags flags)
    {
        try { return t.GetEvents(flags); }
        catch { return Array.Empty<EventInfo>(); }
    }

    private static void ForEachAssembly(
        string[] assemblyPaths, Action<string> scan, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var paths = assemblyPaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var counter = new ProgressCounter(progress, paths.Length);
        Parallel.ForEach(
            paths,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken },
            path =>
            {
                scan(path);
                counter.Increment(Path.GetFileName(path));
            });
    }

    private static void TryScan(Action scan)
    {
        try { scan(); }
        catch (BadImageFormatException) { }
        catch (FileLoadException) { }
        catch (FileNotFoundException) { }
        catch (ReflectionTypeLoadException) { }
        catch (IOException) { }
    }
}
