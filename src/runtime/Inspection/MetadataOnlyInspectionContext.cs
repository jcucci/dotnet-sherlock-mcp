using System.Reflection;

namespace Sherlock.MCP.Runtime.Inspection;

public sealed class MetadataOnlyInspectionContext : IAssemblyInspectionContext
{
    private readonly MetadataLoadContext _mlc;

    public MetadataOnlyInspectionContext(string assemblyPath)
    {
        var resolver = MetadataResolverFactory.Create(assemblyPath);
        var coreAssemblyName = typeof(object).Assembly.GetName().Name;
        _mlc = new MetadataLoadContext(resolver, coreAssemblyName);
        Assembly = _mlc.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
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
            return ex.Types.Where(t => t != null).Cast<Type>();
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

    public void Dispose() => _mlc.Dispose();
}
