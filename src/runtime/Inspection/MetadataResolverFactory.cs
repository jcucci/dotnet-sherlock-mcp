using System.Reflection;
using Sherlock.MCP.Runtime.ProjectAssets;

namespace Sherlock.MCP.Runtime.Inspection;

internal static class MetadataResolverFactory
{
    public static (MetadataAssemblyResolver Resolver, FrameworkResolution Framework) Create(
        string assemblyPath, IReadOnlyList<string>? additionalSearchDirectories = null)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var paths = new HashSet<string>(comparer);
        var simpleNames = new HashSet<string>(comparer);

        var fullPath = Path.GetFullPath(assemblyPath);
        if (File.Exists(fullPath)) AddPath(paths, simpleNames, fullPath);

        AddDllsFromDirectory(paths, simpleNames, Path.GetDirectoryName(fullPath));

        var assets = ProjectAssetsLocator.Locate(fullPath);
        var framework = FrameworkReferenceResolver.Resolve(fullPath, assets);
        var frameworkAndPackageDlls = framework.SearchDirectories
            .SelectMany(EnumerateDlls)
            .Concat(assets?.ResolveDependencyPaths() ?? []);
        foreach (var dll in HighestVersionPerName(frameworkAndPackageDlls, simpleNames))
            AddPath(paths, simpleNames, dll);

        if (additionalSearchDirectories != null)
            foreach (var directory in additionalSearchDirectories)
                AddDllsFromDirectory(paths, simpleNames, directory);

        if (assets == null && NuGetCacheProbe.TryParseCacheLayout(fullPath, out var consumingTfm, out var packageId))
            foreach (var dll in NuGetCacheProbe.EnumerateCandidateDependencyDlls(consumingTfm, packageId))
                AddPath(paths, simpleNames, dll);

        if (framework.Kind == FrameworkResolutionKind.ReferencePack)
            AddDllsFromDirectory(paths, simpleNames, FrameworkReferenceResolver.HostRuntimeDirectory());

        return (new InMemoryAssemblyResolver(paths), framework);
    }

    private static void AddDllsFromDirectory(HashSet<string> paths, HashSet<string> simpleNames, string? directory)
    {
        foreach (var dll in EnumerateDlls(directory))
            AddPath(paths, simpleNames, dll);
    }

    private static string[] EnumerateDlls(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return [];

        try
        {
            return Directory.Exists(directory) ? Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly) : [];
        }
        catch { return []; }
    }

    private static IEnumerable<string> HighestVersionPerName(IEnumerable<string> dlls, HashSet<string> takenNames)
    {
        var chosen = new Dictionary<string, (string Path, Version? Version)>(StringComparer.OrdinalIgnoreCase);
        foreach (var dll in dlls)
        {
            var name = Path.GetFileNameWithoutExtension(dll);
            if (takenNames.Contains(name)) continue;

            if (!chosen.TryGetValue(name, out var current))
            {
                chosen[name] = (dll, null);
                continue;
            }

            var currentVersion = current.Version ?? ReadVersion(current.Path);
            var candidateVersion = ReadVersion(dll);
            chosen[name] = candidateVersion > currentVersion ? (dll, candidateVersion) : (current.Path, currentVersion);
        }
        return chosen.Values.Select(entry => entry.Path);
    }

    private static Version? ReadVersion(string dll)
    {
        try { return AssemblyName.GetAssemblyName(dll).Version; }
        catch { return null; }
    }

    private static void AddPath(HashSet<string> paths, HashSet<string> simpleNames, string dll)
    {
        if (!simpleNames.Add(Path.GetFileNameWithoutExtension(dll))) return;
        try { paths.Add(Path.GetFullPath(dll)); }
        catch { simpleNames.Remove(Path.GetFileNameWithoutExtension(dll)); }
    }
}
