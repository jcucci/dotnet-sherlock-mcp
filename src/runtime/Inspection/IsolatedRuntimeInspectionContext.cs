using System.Reflection;

namespace Sherlock.MCP.Runtime.Inspection;

public sealed class IsolatedRuntimeInspectionContext : IAssemblyInspectionContext
{
    private readonly DependencyResolvingLoadContext _alc;

    public IsolatedRuntimeInspectionContext(string assemblyPath)
    {
        var assemblyDirectory = Path.GetDirectoryName(assemblyPath) ?? ".";
        var contextName = $"sherlock_{Path.GetFileNameWithoutExtension(assemblyPath)}_{Guid.NewGuid():N}";

        _alc = new DependencyResolvingLoadContext(contextName, assemblyDirectory);
        Assembly = _alc.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
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

    public void Dispose()
    {
        _alc.Unload();
    }
}
