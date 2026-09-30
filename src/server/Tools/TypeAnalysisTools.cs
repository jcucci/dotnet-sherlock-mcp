using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.ReverseLookup;
using Sherlock.MCP.Runtime.Contracts.TypeAnalysis;
using Sherlock.MCP.Server.Shared;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class TypeAnalysisTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    [McpServerTool(Title = "Get Types from Assembly", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Lists public types from an assembly. Returns a lean summary ({ FullName, Namespace, Kind }) by default - use this to browse or search large assemblies. Pass projection='full' when you need attributes, inheritance, interfaces, generic params, and nested types; prefer get_type_info for a single type instead. Returns totalTypeCount for pagination planning; use maxItems=25 for very large assemblies.")]
    public static CallToolResult GetTypesFromAssembly(
        ITypeAnalysisService typeAnalysis,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Maximum number of types to return (default: 50)")] int? maxItems = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Response shape. 'summary' (default, token-lean): { FullName, Namespace, Kind } only - use for browsing/searching. 'full': adds attributes, base type, interfaces, generic params, nested types - use only when you need those fields on every item.")] string projection = "summary",
        [Description("Optional paths to dependency assemblies (.dll) to help resolve types. Only needed when types fail to resolve and the assembly's dependencies are not next to it or in the NuGet cache - e.g. point at sibling DLLs in a build-output folder.")] string[]? additionalAssemblies = null)
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return ToolResponse.Result(JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}"));

            var normalizedProjection = (projection ?? "summary").Trim().ToLowerInvariant();
            if (normalizedProjection != "summary" && normalizedProjection != "full")
                return ToolResponse.Result(JsonHelpers.Error("InvalidProjection", "projection must be 'summary' or 'full'"));

            string[]? searchDirectories = null;
            if (additionalAssemblies is { Length: > 0 })
            {
                var scope = AssemblyScope.BuildAndValidate(assemblyPath, additionalAssemblies);
                if (scope.Error != null)
                    return ToolResponse.Result(scope.Error);
                searchDirectories = scope.Paths
                    .Select(Path.GetDirectoryName)
                    .Where(d => !string.IsNullOrEmpty(d))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Cast<string>()
                    .ToArray();
            }

            var allTypes = typeAnalysis.GetTypesFromAssembly(assemblyPath, searchDirectories);

            // Pagination logic
            var defaultPageSize = 50;
            var pageSize = Math.Max(1, maxItems ?? defaultPageSize);
            var offset = 0;

            var dependencyScope = searchDirectories is { Length: > 0 }
                ? string.Join("|", searchDirectories.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                : "";
            var cacheKey = $"types_from_assembly_{CacheKeyHelper.FileStamp(assemblyPath)}_{pageSize}_{dependencyScope}";
            var salt = TokenHelper.MakeSalt(cacheKey);

            if (!string.IsNullOrWhiteSpace(continuationToken))
            {
                if (!TokenHelper.TryParse(continuationToken, out offset, out var parsedSalt) || parsedSalt != salt)
                    return ToolResponse.Result(JsonHelpers.Error("InvalidContinuationToken", "The continuation token is invalid or expired."));
            }
            else if (skip.HasValue && skip.Value > 0)
            {
                offset = skip.Value;
            }

            var pageTypes = allTypes.Skip(offset).Take(pageSize).ToArray();
            string? nextToken = null;
            var nextOffset = offset + pageTypes.Length;
            if (nextOffset < allTypes.Length)
                nextToken = TokenHelper.Make(nextOffset, salt);

            object types = normalizedProjection == "summary"
                ? pageTypes.Select(t => new { t.FullName, t.Namespace, t.Kind }).ToArray()
                : pageTypes;

            var result = new
            {
                assemblyPath,
                projection = normalizedProjection,
                totalTypeCount = allTypes.Length,
                returnedTypeCount = pageTypes.Length,
                nextToken,
                types
            };
            var links = searchDirectories is { Length: > 0 }
                ? []
                : ResourceUris.TypeLinks(pageTypes.Select(t => (assemblyPath, t.MetadataName ?? t.FullName)));
            return new ToolResponse(JsonHelpers.Envelope("type.list", result), links).ToCallToolResult();
        }
        catch (DependencyResolutionException ex)
        {
            return ToolResponse.Result(JsonHelpers.ErrorWithGuidance(
                "DependencyResolutionFailed",
                ex.Message,
                suggestion: $"The dependencies ({string.Join(", ", ex.UnresolvedDependencies)}) were not found next to the assembly or in the NuGet cache. Re-run with assemblyPath pointing at a copy of the assembly in a build-output folder (e.g. bin/Debug/<tfm>/Name.dll) whose sibling DLLs include these dependencies, or pass the dependency DLL file paths via additionalAssemblies."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResponse.Result(JsonHelpers.Error("InternalError", $"Failed to get types: {ex.Message}"));
        }
    }

    [McpServerTool(Title = "Get Type Info", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets detailed metadata for a single type including accessibility, inheritance, interfaces, and member counts. Lightweight response - use as entry point before exploring members with get_type_methods etc.")]
    public static string GetTypeInfo(
        ITypeAnalysisService typeAnalysis,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to analyze. Prefer full name (e.g., 'System.Collections.Generic.List`1')")] string typeName,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            if (!File.Exists(assemblyPath))
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");

            var info = typeAnalysis.GetTypeInfo(assemblyPath, typeName);
            if (info == null)
                return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");

            return JsonHelpers.Envelope("type.info", info);
        }
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze type: {ex.Message}");
        }
    }

    [McpServerTool(Title = "Get Type Hierarchy", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets full inheritance chain and implemented interfaces for a type. Use to understand type relationships and find inherited members. Lightweight response. By default derivedTypes is null with a note - pass additionalAssemblies to compute derived/implementing types via the same scan as find_implementations_of.")]
    public static string GetTypeHierarchy(
        ITypeAnalysisService typeAnalysis,
        IReverseLookupService reverseLookup,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to analyze. Prefer full name")]
        string typeName,
        [Description("Optional additional assembly paths to include in the search scope")]
        string[]? additionalAssemblies = null,
        IProgress<ProgressNotificationValue>? progress = null,
        RequestContext<CallToolRequestParams>? context = null,
        CancellationToken cancellationToken = default)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            if (!File.Exists(assemblyPath)) return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
            var hierarchy = typeAnalysis.GetTypeHierarchy(assemblyPath, typeName);
            if (hierarchy == null) return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");

            if (additionalAssemblies == null || additionalAssemblies.Length == 0)
                return JsonHelpers.Envelope("type.hierarchy", hierarchy with { Note = "derivedTypes not computed; pass additionalAssemblies to compute, or use find_implementations_of" });

            var scope = AssemblyScope.BuildAndValidate(assemblyPath, additionalAssemblies);
            if (scope.Error != null) return scope.Error;

            var hits = reverseLookup.FindImplementations(
                scope.Paths, hierarchy.TypeName, new ReverseLookupOptions(), ProgressAdapter.ForPhase(progress), cancellationToken);
            var derived = hits.Select(h => new DerivedTypeRef(h.TypeFullName, h.AssemblyPath, h.Kind)).ToArray();
            return JsonHelpers.Envelope("type.hierarchy", hierarchy with { DerivedTypes = derived, Note = null });
        }
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return JsonHelpers.Error("InternalError", $"Failed to get type hierarchy: {ex.Message}");
        }
    }

    [McpServerTool(Title = "Get Generic Type Info", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets generic type parameters, constraints, and variance for generic types. Only useful for types where IsGenericType=true. Lightweight response.")]
    public static string GetGenericTypeInfo(
        ITypeAnalysisService typeAnalysis,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to analyze. Prefer full name")]
        string typeName,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            if (!File.Exists(assemblyPath)) return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
            var genericInfo = typeAnalysis.GetGenericTypeInfo(assemblyPath, typeName);
            if (genericInfo == null) return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");
            return JsonHelpers.Envelope("type.generic", genericInfo);
        }
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return JsonHelpers.Error("InternalError", $"Failed to get generic type info: {ex.Message}");
        }
    }

    [McpServerTool(Title = "Get Type Attributes", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets custom attributes declared on a type (e.g., [Serializable], [Obsolete]). Returns attribute types and values. Lightweight response.")]
    public static string GetTypeAttributes(
        ITypeAnalysisService typeAnalysis,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to analyze. Prefer full name")]
        string typeName,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            if (!File.Exists(assemblyPath)) return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
            var lookup = typeAnalysis.GetTypeAttributes(assemblyPath, typeName);
            if (lookup == null) return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");
            var (typeFullName, attributes) = lookup.Value;
            return JsonHelpers.Envelope("type.attributes", new { typeName = typeFullName, attributeCount = attributes.Length, attributes });
        }
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return JsonHelpers.Error("InternalError", $"Failed to get attributes: {ex.Message}");
        }
    }

    [McpServerTool(Title = "Get Nested Types", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets nested/inner types declared within a type. Use for types with inner classes, structs, or enums. Lightweight response.")]
    public static string GetNestedTypes(
        ITypeAnalysisService typeAnalysis,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to analyze. Prefer full name")]
        string typeName,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            if (!File.Exists(assemblyPath)) return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
            var lookup = typeAnalysis.GetNestedTypes(assemblyPath, typeName);
            if (lookup == null) return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");
            var (typeFullName, nested) = lookup.Value;
            return JsonHelpers.Envelope("type.nested", new { typeName = typeFullName, nestedTypeCount = nested.Length, nested });
        }
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return JsonHelpers.Error("InternalError", $"Failed to get nested types: {ex.Message}");
        }
    }
}
