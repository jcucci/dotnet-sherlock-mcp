using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Sherlock.MCP.Runtime.Il;

namespace Sherlock.MCP.Runtime.Inspection;

public static class AssemblyLocator
{
    public static readonly IReadOnlyList<string> ExcludedDirectoryNames =
        ["obj", "ref", "refint", "node_modules", "packages", "TestResults"];

    private static readonly HashSet<string> ExcludedDirectories =
        new(ExcludedDirectoryNames, StringComparer.OrdinalIgnoreCase);

    private static readonly EnumerationOptions SingleLevel = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
    };

    public static IReadOnlyList<string> FindByFileName(string root, string fileName) =>
        Rank(EnumerateAssemblyFiles(root, fileName));

    public static IReadOnlyList<string> FindByClassName(string root, string className)
    {
        var files = EnumerateAssemblyFiles(root, "*.dll")
            .Concat(EnumerateAssemblyFiles(root, "*.exe"))
            .ToList();

        var matches = new ConcurrentBag<string>();
        Parallel.ForEach(files, path =>
        {
            if (DeclaresVisibleType(path, className))
                matches.Add(path);
        });

        return Rank(matches);
    }

    private static IEnumerable<string> EnumerateAssemblyFiles(string root, string pattern)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            foreach (var file in SafeEnumerate(() => Directory.EnumerateFiles(directory, pattern, SingleLevel)))
                yield return file;

            foreach (var child in SafeEnumerate(() => Directory.EnumerateDirectories(directory, "*", SingleLevel)))
            {
                if (!IsExcluded(Path.GetFileName(child)))
                    pending.Push(child);
            }
        }
    }

    private static List<string> SafeEnumerate(Func<IEnumerable<string>> enumerate)
    {
        try
        {
            return enumerate().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsExcluded(string directoryName) =>
        directoryName.StartsWith('.') || ExcludedDirectories.Contains(directoryName);

    private static List<string> Rank(IEnumerable<string> paths) =>
        paths
            .Select(path => (path, stamp: SafeLastWrite(path)))
            .OrderByDescending(entry => IsUnderBin(entry.path))
            .ThenByDescending(entry => entry.stamp)
            .ThenBy(entry => entry.path.Length)
            .ThenBy(entry => entry.path, StringComparer.Ordinal)
            .Select(entry => entry.path)
            .ToList();

    private static bool IsUnderBin(string path) =>
        Path.GetDirectoryName(path)?
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains("bin", StringComparer.OrdinalIgnoreCase) == true;

    private static DateTime SafeLastWrite(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private static bool DeclaresVisibleType(string assemblyPath, string className)
    {
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
                return false;

            var md = pe.GetMetadataReader();
            return md.TypeDefinitions.Any(handle =>
                TryGetVisibleName(md, handle, out var name) &&
                MetadataTypeNameMatcher.Matches(name, className, caseSensitive: false));
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryGetVisibleName(MetadataReader md, TypeDefinitionHandle handle, out string name)
    {
        name = string.Empty;
        var type = md.GetTypeDefinition(handle);
        var visibility = type.Attributes & TypeAttributes.VisibilityMask;

        if (visibility == TypeAttributes.Public)
        {
            var ns = md.GetString(type.Namespace);
            var simpleName = md.GetString(type.Name);
            name = ns.Length == 0 ? simpleName : $"{ns}.{simpleName}";
            return true;
        }

        if (visibility != TypeAttributes.NestedPublic)
            return false;

        if (!TryGetVisibleName(md, type.GetDeclaringType(), out var parentName))
            return false;

        name = $"{parentName}+{md.GetString(type.Name)}";
        return true;
    }
}
