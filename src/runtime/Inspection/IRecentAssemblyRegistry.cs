namespace Sherlock.MCP.Runtime.Inspection;

public interface IRecentAssemblyRegistry
{
    void Record(string assemblyPath);

    IReadOnlyList<string> GetRecent();
}
