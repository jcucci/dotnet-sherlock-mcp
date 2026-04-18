using System.Reflection;
using System.Runtime.InteropServices;

namespace Sherlock.MCP.Runtime.Inspection;

internal static class MetadataResolverFactory
{
    public static PathAssemblyResolver Create(string assemblyPath)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var paths = new HashSet<string>(comparer);

        var fullPath = Path.GetFullPath(assemblyPath);
        if (File.Exists(fullPath)) paths.Add(fullPath);

        var assemblyDir = Path.GetDirectoryName(fullPath);
        AddDllsFromDirectory(paths, assemblyDir);
        AddDllsFromDirectory(paths, RuntimeEnvironment.GetRuntimeDirectory());

        return new PathAssemblyResolver(paths);
    }

    private static void AddDllsFromDirectory(HashSet<string> paths, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;

        try
        {
            if (!Directory.Exists(directory)) return;
            foreach (var dll in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                try { paths.Add(Path.GetFullPath(dll)); }
                catch { }
            }
        }
        catch { }
    }
}
