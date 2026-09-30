using System.Collections.Concurrent;
using System.Reflection;
using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Runtime.Completions;

public sealed class CompletionService : ICompletionService
{
    public const int MaxValues = 100;

    private const int MaxCachedTypeNameLists = 32;

    private static readonly string[] AssemblyExtensions = [".dll", ".exe"];

    private sealed record CachedTypeNames(long StampTicks, long Length, string[] Names);

    private readonly ConcurrentDictionary<string, CachedTypeNames> _typeNames = new(PathComparers.Comparer);
    private readonly IInspectionContextProvider _contexts;
    private readonly IXmlDocService _xmlDocs;
    private readonly IRecentAssemblyRegistry _recentAssemblies;

    public CompletionService(IInspectionContextProvider contexts, IXmlDocService xmlDocs, IRecentAssemblyRegistry recentAssemblies)
    {
        _contexts = contexts;
        _xmlDocs = xmlDocs;
        _recentAssemblies = recentAssemblies;
    }

    public CompletionValues CompleteAssemblyPath(string value)
    {
        var expanded = ExpandHome(value);
        var recent = _recentAssemblies.GetRecent()
            .Where(path => path.Contains(expanded, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path.StartsWith(expanded, StringComparison.OrdinalIgnoreCase) ? 0 : 1);
        var listed = ListDirectoryCandidates(expanded);
        return Page(recent.Concat(listed).Distinct(PathComparers.Comparer));
    }

    public CompletionValues CompleteTypeName(string assemblyPath, string value)
    {
        var names = GetTypeNames(assemblyPath);
        return names.Length == 0 ? CompletionValues.Empty : Page(Rank(names, value, SimpleTypeName));
    }

    public CompletionValues CompleteMemberId(string assemblyPath, string value)
    {
        if (!File.Exists(assemblyPath)) return CompletionValues.Empty;

        return Page(Rank(_xmlDocs.GetDocumentedMemberIds(assemblyPath), value, SimpleMemberName));
    }

    public CompletionValues CompletePackageId(string value)
    {
        var packageIds = SafeListDirectoryNames(NuGetCacheProbe.GetCacheRoot());
        return Page(Rank(packageIds, value, simpleName: null));
    }

    public CompletionValues CompletePackageVersion(string packageId, string value)
    {
        if (!IsSafeSegment(packageId)) return CompletionValues.Empty;

        var versions = SafeListDirectoryNames(Path.Combine(NuGetCacheProbe.GetCacheRoot(), packageId.ToLowerInvariant()))
            .Where(version => version.StartsWith(value, StringComparison.OrdinalIgnoreCase));
        return Page(NuGetVersions.SortDescending(versions));
    }

    private string[] GetTypeNames(string assemblyPath)
    {
        var file = new FileInfo(assemblyPath);
        if (!file.Exists) return [];

        var key = file.FullName;
        var stampTicks = file.LastWriteTimeUtc.Ticks;
        if (_typeNames.TryGetValue(key, out var cached) && cached.StampTicks == stampTicks && cached.Length == file.Length)
            return cached.Names;

        var names = LoadTypeNames(key);
        if (_typeNames.Count >= MaxCachedTypeNameLists) _typeNames.Clear();
        _typeNames[key] = new CachedTypeNames(stampTicks, file.Length, names);
        return names;
    }

    private string[] LoadTypeNames(string assemblyPath)
    {
        using var lease = _contexts.Acquire(assemblyPath);
        IEnumerable<Type?> types;
        try
        {
            types = lease.Context.GetTypes().ToArray();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types;
        }

        return types
            .OfType<Type>()
            .Where(type => type.IsPublic || type.IsNestedPublic)
            .Select(type => type.FullName)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> Rank(IEnumerable<string> candidates, string value, Func<string, string>? simpleName) =>
        candidates
            .Select(candidate => (candidate, rank: MatchRank(candidate, value, simpleName)))
            .Where(match => match.rank >= 0)
            .OrderBy(match => match.rank)
            .ThenBy(match => match.candidate, StringComparer.Ordinal)
            .Select(match => match.candidate);

    private static int MatchRank(string candidate, string value, Func<string, string>? simpleName)
    {
        if (candidate.StartsWith(value, StringComparison.OrdinalIgnoreCase)) return 0;
        if (simpleName is not null && simpleName(candidate).StartsWith(value, StringComparison.OrdinalIgnoreCase)) return 1;
        return candidate.Contains(value, StringComparison.OrdinalIgnoreCase) ? 2 : -1;
    }

    private static string SimpleTypeName(string fullName) =>
        fullName[(fullName.LastIndexOfAny(['.', '+']) + 1)..];

    private static string SimpleMemberName(string memberId)
    {
        var withoutPrefix = memberId.Length > 2 && memberId[1] == ':' ? memberId[2..] : memberId;
        var paren = withoutPrefix.IndexOf('(');
        var name = paren >= 0 ? withoutPrefix[..paren] : withoutPrefix;
        return name[(name.LastIndexOf('.') + 1)..];
    }

    private static CompletionValues Page(IEnumerable<string> ordered)
    {
        var all = ordered.ToArray();
        return new CompletionValues(all.Take(MaxValues).ToArray(), all.Length, all.Length > MaxValues);
    }

    private static string[] ListDirectoryCandidates(string expanded)
    {
        if (expanded.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0) return [];

        try
        {
            var directory = Path.GetDirectoryName(expanded);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return [];

            var fragment = Path.GetFileName(expanded);
            var subdirectories = Directory.EnumerateDirectories(directory)
                .Where(path => Path.GetFileName(path).StartsWith(fragment, StringComparison.OrdinalIgnoreCase))
                .Select(path => path + Path.DirectorySeparatorChar);
            var assemblies = Directory.EnumerateFiles(directory)
                .Where(path => Path.GetFileName(path).StartsWith(fragment, StringComparison.OrdinalIgnoreCase))
                .Where(path => AssemblyExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
            return assemblies.Concat(subdirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static string ExpandHome(string value) =>
        value == "~" || value.StartsWith("~/", StringComparison.Ordinal) || value.StartsWith($"~{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + value[1..]
            : value;

    private static string[] SafeListDirectoryNames(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.GetDirectories(directory).Select(Path.GetFileName).OfType<string>().ToArray()
                : [];
        }
        catch
        {
            return [];
        }
    }

    private static bool IsSafeSegment(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && value is not "." and not "..";
}
