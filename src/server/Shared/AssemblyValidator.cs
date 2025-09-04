namespace Sherlock.MCP.Server.Shared;
public static class AssemblyValidator
{
    public static async Task<string> WithAssemblyValidation(string assemblyPath, Func<Task<string>> operation)
    {
        if (!File.Exists(assemblyPath))
            return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Operation failed: {ex.Message}");
        }
    }
    public static string WithAssemblyValidation(string assemblyPath, Func<string> operation)
    {
        if (!File.Exists(assemblyPath))
            return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
        try
        {
            return operation();
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Operation failed: {ex.Message}");
        }
    }
}
