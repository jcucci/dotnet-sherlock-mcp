namespace Sherlock.MCP.Runtime.Handles;

public interface IAssemblyHandleRegistry
{
    AssemblyHandle Open(string assemblyPath, IReadOnlyList<string>? additionalAssemblies = null);

    HandleLookup Resolve(string handleId);
}
