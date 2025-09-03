using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class TypeAnalysisTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    [McpServerTool]
    [Description("Gets public types from an assembly, including key metadata")]
    public static string GetTypesFromAssembly(
        ITypeAnalysisService typeAnalysis,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath)
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return JsonSerializer.Serialize(new { error = $"Assembly file not found: {assemblyPath}" });

            var types = typeAnalysis.GetTypesFromAssembly(assemblyPath);
            var result = new
            {
                assemblyPath,
                typeCount = types.Length,
                types
            };
            return JsonSerializer.Serialize(result, SerializerOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to get types: {ex.Message}" }, SerializerOptions);
        }
    }

    [McpServerTool]
    [Description("Gets detailed type info, with fallback for simple names")]
    public static string GetTypeInfo(
        ITypeAnalysisService typeAnalysis,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to analyze. Prefer full name (e.g., 'System.Collections.Generic.List`1')")] string typeName)
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return JsonSerializer.Serialize(new { error = $"Assembly file not found: {assemblyPath}" });

            var info = typeAnalysis.GetTypeInfo(assemblyPath, typeName);
            if (info == null)
            {
                // Fallback to simple-name lookup
                var asm = typeAnalysis.LoadAssembly(assemblyPath);
                var type = asm?.GetTypes().FirstOrDefault(t => t.Name == typeName);
                if (type != null)
                {
                    info = typeAnalysis.GetTypeInfo(type);
                }
            }
            if (info == null)
                return JsonSerializer.Serialize(new { error = $"Type '{typeName}' not found in assembly" });

            return JsonSerializer.Serialize(info, SerializerOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to analyze type: {ex.Message}" }, SerializerOptions);
        }
    }

    [McpServerTool]
    [Description("Gets inheritance chain, interfaces, and base types for a type")]
    public static string GetTypeHierarchy(
        ITypeAnalysisService typeAnalysis,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to analyze. Prefer full name")]
        string typeName)
    {
        try
        {
            var asm = typeAnalysis.LoadAssembly(assemblyPath);
            if (asm == null) return JsonSerializer.Serialize(new { error = $"Assembly file not found: {assemblyPath}" });
            var type = asm.GetType(typeName) ?? asm.GetTypes().FirstOrDefault(t => t.Name == typeName);
            if (type == null) return JsonSerializer.Serialize(new { error = $"Type '{typeName}' not found in assembly" });
            var hierarchy = typeAnalysis.GetTypeHierarchy(type);
            return JsonSerializer.Serialize(hierarchy, SerializerOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to get type hierarchy: {ex.Message}" }, SerializerOptions);
        }
    }

    [McpServerTool]
    [Description("Gets generic type information for a type")]
    public static string GetGenericTypeInfo(
        ITypeAnalysisService typeAnalysis,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to analyze. Prefer full name")]
        string typeName)
    {
        try
        {
            var asm = typeAnalysis.LoadAssembly(assemblyPath);
            if (asm == null) return JsonSerializer.Serialize(new { error = $"Assembly file not found: {assemblyPath}" });
            var type = asm.GetType(typeName) ?? asm.GetTypes().FirstOrDefault(t => t.Name == typeName);
            if (type == null) return JsonSerializer.Serialize(new { error = $"Type '{typeName}' not found in assembly" });
            var genericInfo = typeAnalysis.GetGenericTypeInfo(type);
            return JsonSerializer.Serialize(genericInfo, SerializerOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to get generic type info: {ex.Message}" }, SerializerOptions);
        }
    }

    [McpServerTool]
    [Description("Gets custom attributes declared on a type")]
    public static string GetTypeAttributes(
        ITypeAnalysisService typeAnalysis,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to analyze. Prefer full name")]
        string typeName)
    {
        try
        {
            var asm = typeAnalysis.LoadAssembly(assemblyPath);
            if (asm == null) return JsonSerializer.Serialize(new { error = $"Assembly file not found: {assemblyPath}" });
            var type = asm.GetType(typeName) ?? asm.GetTypes().FirstOrDefault(t => t.Name == typeName);
            if (type == null) return JsonSerializer.Serialize(new { error = $"Type '{typeName}' not found in assembly" });
            var attributes = typeAnalysis.GetTypeAttributes(type);
            return JsonSerializer.Serialize(attributes, SerializerOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to get attributes: {ex.Message}" }, SerializerOptions);
        }
    }

    [McpServerTool]
    [Description("Gets nested types declared under a type")]
    public static string GetNestedTypes(
        ITypeAnalysisService typeAnalysis,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to analyze. Prefer full name")]
        string typeName)
    {
        try
        {
            var asm = typeAnalysis.LoadAssembly(assemblyPath);
            if (asm == null) return JsonSerializer.Serialize(new { error = $"Assembly file not found: {assemblyPath}" });
            var type = asm.GetType(typeName) ?? asm.GetTypes().FirstOrDefault(t => t.Name == typeName);
            if (type == null) return JsonSerializer.Serialize(new { error = $"Type '{typeName}' not found in assembly" });
            var nested = typeAnalysis.GetNestedTypes(type);
            return JsonSerializer.Serialize(nested, SerializerOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to get nested types: {ex.Message}" }, SerializerOptions);
        }
    }
}

