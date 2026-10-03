using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sherlock.MCP.Runtime.Inspection;

public static class AssemblyLocations
{
    private static readonly ConditionalWeakTable<Assembly, string> Paths = new();

    public static Assembly Register(Assembly assembly, string path)
    {
        Paths.AddOrUpdate(assembly, path);
        return assembly;
    }

    public static string Of(Assembly assembly) =>
        Paths.TryGetValue(assembly, out var path) ? path : assembly.Location;

    public static Assembly LoadInMemory(MetadataLoadContext context, string path) =>
        Register(context.LoadFromByteArray(File.ReadAllBytes(path)), path);
}
