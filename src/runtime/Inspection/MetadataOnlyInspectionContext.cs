using System.Reflection;

namespace Sherlock.MCP.Runtime.Inspection;

// Minimal placeholder using runtime reflection for now; will switch to MetadataLoadContext.
public sealed class MetadataOnlyInspectionContext : IAssemblyInspectionContext
{
    public MetadataOnlyInspectionContext(string assemblyPath)
    {
        Assembly = Assembly.LoadFrom(assemblyPath);
    }

    public Assembly Assembly { get; }

    public IEnumerable<Type> GetTypes() => Assembly.GetTypes();

    public MemberInfo[] GetMembers(Type type, BindingFlags flags) => type.GetMembers(flags);

    public void Dispose()
    {
    }
}

