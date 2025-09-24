using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Server.Shared;
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
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Maximum number of types to return (default: 50)")] int? maxItems = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Continuation token for paging")] string? continuationToken = null)
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");

            var allTypes = typeAnalysis.GetTypesFromAssembly(assemblyPath);

            // Pagination logic
            var defaultPageSize = 50;
            var pageSize = Math.Max(1, maxItems ?? defaultPageSize);
            var offset = 0;

            var cacheKey = $"types_from_assembly_{assemblyPath}_{pageSize}";
            var salt = TokenHelper.MakeSalt(cacheKey);

            if (!string.IsNullOrWhiteSpace(continuationToken))
            {
                if (!TokenHelper.TryParse(continuationToken, out offset, out var parsedSalt) || parsedSalt != salt)
                    return JsonHelpers.Error("InvalidContinuationToken", "The continuation token is invalid or expired.");
            }
            else if (skip.HasValue && skip.Value > 0)
            {
                offset = skip.Value;
            }

            var types = allTypes.Skip(offset).Take(pageSize).ToArray();
            string? nextToken = null;
            var nextOffset = offset + types.Length;
            if (nextOffset < allTypes.Length)
                nextToken = TokenHelper.Make(nextOffset, salt);

            var result = new
            {
                assemblyPath,
                totalTypeCount = allTypes.Length,
                returnedTypeCount = types.Length,
                nextToken,
                types
            };
            return JsonHelpers.Envelope("type.list", result);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to get types: {ex.Message}");
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
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");

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
                return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");

            return JsonHelpers.Envelope("type.info", info);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze type: {ex.Message}");
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
            if (asm == null) return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
            var type = asm.GetType(typeName) ?? asm.GetTypes().FirstOrDefault(t => t.Name == typeName);
            if (type == null) return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");
            var hierarchy = typeAnalysis.GetTypeHierarchy(type);
            return JsonHelpers.Envelope("type.hierarchy", hierarchy);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to get type hierarchy: {ex.Message}");
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
            if (asm == null) return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
            var type = asm.GetType(typeName) ?? asm.GetTypes().FirstOrDefault(t => t.Name == typeName);
            if (type == null) return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");
            var genericInfo = typeAnalysis.GetGenericTypeInfo(type);
            return JsonHelpers.Envelope("type.generic", genericInfo);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to get generic type info: {ex.Message}");
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
            if (asm == null) return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
            var type = asm.GetType(typeName) ?? asm.GetTypes().FirstOrDefault(t => t.Name == typeName);
            if (type == null) return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");
            var attributes = typeAnalysis.GetTypeAttributes(type);
            return JsonHelpers.Envelope("type.attributes", new { typeName = type.FullName, attributeCount = attributes.Length, attributes });
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to get attributes: {ex.Message}");
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
            if (asm == null) return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
            var type = asm.GetType(typeName) ?? asm.GetTypes().FirstOrDefault(t => t.Name == typeName);
            if (type == null) return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");
            var nested = typeAnalysis.GetNestedTypes(type);
            return JsonHelpers.Envelope("type.nested", new { typeName = type.FullName, nestedTypeCount = nested.Length, nested });
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to get nested types: {ex.Message}");
        }
    }
}
