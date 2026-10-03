using System.Reflection;

namespace Sherlock.MCP.Runtime.Inspection;

public sealed class MetadataOnlyInspectionContext : IAssemblyInspectionContext
{
    private readonly MetadataLoadContext _mlc;
    private string[] _unresolvedDependencies = [];

    public MetadataOnlyInspectionContext(string assemblyPath, IReadOnlyList<string>? additionalSearchDirectories = null)
    {
        var (resolver, framework) = MetadataResolverFactory.Create(assemblyPath, additionalSearchDirectories);
        Framework = framework;
        _mlc = new MetadataLoadContext(resolver, framework.CoreAssemblyName);
        Assembly = AssemblyLocations.LoadInMemory(_mlc, Path.GetFullPath(assemblyPath));
    }

    public Assembly Assembly { get; }

    public FrameworkResolution Framework { get; }

    public IReadOnlyList<string> UnresolvedDependencies => _unresolvedDependencies;

    public IEnumerable<Type> GetTypes()
    {
        try
        {
            return Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            _unresolvedDependencies = DependencyDiagnostics.ExtractUnresolved(ex);
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
