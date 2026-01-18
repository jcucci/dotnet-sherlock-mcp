using System.Reflection;

namespace Sherlock.MCP.Runtime.Inspection;

public sealed class MetadataOnlyInspectionContext : IAssemblyInspectionContext
{
    public MetadataOnlyInspectionContext(string assemblyPath)
    {
        Assembly = Assembly.LoadFrom(assemblyPath);
    }

    public Assembly Assembly { get; }

    public IEnumerable<Type> GetTypes()
    {
        try
        {
            return Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null)!;
        }
    }

    public MemberInfo[] GetMembers(Type type, BindingFlags flags)
    {
        try
        {
            return type.GetMembers(flags);
        }
        catch (TypeLoadException)
        {
            return [];
        }
    }

    public void Dispose()
    {
    }
}
