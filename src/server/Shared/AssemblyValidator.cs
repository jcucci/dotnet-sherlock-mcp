namespace Sherlock.MCP.Server.Shared;
public static class AssemblyValidator
{
    public static async Task<string> WithAssemblyValidation(string assemblyPath, Func<Task<string>> operation)
    {
        if (!File.Exists(assemblyPath))
            return ToolErrors.AssemblyNotFound(assemblyPath);
        try
        {
            return await operation();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "complete the operation");
        }
    }
    public static string WithAssemblyValidation(string assemblyPath, Func<string> operation)
    {
        if (!File.Exists(assemblyPath))
            return ToolErrors.AssemblyNotFound(assemblyPath);
        try
        {
            return operation();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "complete the operation");
        }
    }
}
