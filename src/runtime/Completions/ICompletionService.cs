namespace Sherlock.MCP.Runtime.Completions;

public interface ICompletionService
{
    public CompletionValues CompleteAssemblyPath(string value);
    public CompletionValues CompleteTypeName(string assemblyPath, string value);
    public CompletionValues CompleteMemberId(string assemblyPath, string value);
    public CompletionValues CompletePackageId(string value);
    public CompletionValues CompletePackageVersion(string packageId, string value);
}
