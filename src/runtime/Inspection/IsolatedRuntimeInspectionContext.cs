using System.Reflection;
using System.Runtime.Loader;

namespace Sherlock.MCP.Runtime.Inspection;

// Placeholder that mirrors MetadataOnlyInspectionContext until isolation is added
public sealed class IsolatedRuntimeInspectionContext : IAssemblyInspectionContext
{
    private readonly AssemblyLoadContext _alc;

    public IsolatedRuntimeInspectionContext(string assemblyPath)
    {
        _alc = new AssemblyLoadContext($"sherlock_{Path.GetFileNameWithoutExtension(assemblyPath)}", isCollectible: true);
        using var stream = File.OpenRead(assemblyPath);
        var bytes = new byte[stream.Length];
        _ = stream.Read(bytes, 0, bytes.Length);
        Assembly = _alc.LoadFromStream(new MemoryStream(bytes));
    }

    public Assembly Assembly { get; }

    public IEnumerable<Type> GetTypes() => Assembly.GetTypes();

    public MemberInfo[] GetMembers(Type type, BindingFlags flags) => type.GetMembers(flags);

    public void Dispose()
    {
        _alc.Unload();
    }
}

