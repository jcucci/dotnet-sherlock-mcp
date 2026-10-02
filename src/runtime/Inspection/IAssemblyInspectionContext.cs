using System.Reflection;

namespace Sherlock.MCP.Runtime.Inspection;

public interface IAssemblyInspectionContext : IDisposable
{
    Assembly Assembly { get; }

    FrameworkResolution Framework { get; }

    IReadOnlyList<string> UnresolvedDependencies { get; }

    IEnumerable<Type> GetTypes();

    MemberInfo[] GetMembers(Type type, BindingFlags flags);
}

