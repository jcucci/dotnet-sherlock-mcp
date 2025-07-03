namespace Sherlock.MCP.Server.Shared;

/// <summary>
/// Helper utilities for assembly validation in MCP tools
/// </summary>
public static class AssemblyValidator
{
    /// <summary>
    /// Executes an operation with assembly path validation
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly file</param>
    /// <param name="operation">Operation to execute if validation passes</param>
    /// <returns>JSON response - either the operation result or an error</returns>
    public static async Task<string> WithAssemblyValidation(string assemblyPath, Func<Task<string>> operation)
    {
        if (!File.Exists(assemblyPath))
            return JsonHelpers.CreateErrorResponse($"Assembly file not found: {assemblyPath}");
        
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            return JsonHelpers.CreateErrorResponse($"Operation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Executes a synchronous operation with assembly path validation
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly file</param>
    /// <param name="operation">Operation to execute if validation passes</param>
    /// <returns>JSON response - either the operation result or an error</returns>
    public static string WithAssemblyValidation(string assemblyPath, Func<string> operation)
    {
        if (!File.Exists(assemblyPath))
            return JsonHelpers.CreateErrorResponse($"Assembly file not found: {assemblyPath}");
        
        try
        {
            return operation();
        }
        catch (Exception ex)
        {
            return JsonHelpers.CreateErrorResponse($"Operation failed: {ex.Message}");
        }
    }
}