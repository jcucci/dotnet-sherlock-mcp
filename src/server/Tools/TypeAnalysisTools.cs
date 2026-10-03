using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.ReverseLookup;
using Sherlock.MCP.Runtime.Contracts.TypeAnalysis;
using Sherlock.MCP.Runtime.Handles;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Schemas;
using Sherlock.MCP.Server.Shared;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class TypeAnalysisTools
{
    [McpServerTool(Title = "Get Types from Assembly", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolEnvelope<TypeListData>))]
    [Description("Lists public types from an assembly. Returns a lean summary ({ FullName, Namespace, Kind }) by default - use this to browse or search large assemblies. Pass projection='full' when you need attributes, inheritance, interfaces, generic params, and nested types; prefer get_type_info for a single type instead. Returns totalTypeCount for pagination planning; use maxItems=25 for very large assemblies.")]
    public static CallToolResult GetTypesFromAssembly(
        ITypeAnalysisService typeAnalysis,
        ToolMiddleware middleware,
        IAssemblyHandleRegistry handles,
        [Description("Path to the .NET assembly file (.dll or .exe). Omit when passing assemblyHandle.")] string? assemblyPath = null,
        [Description("Handle returned by open_assembly; pass instead of assemblyPath. The additionalAssemblies it was opened with are used as dependency folders, exactly as if passed here, so resource_link blocks are omitted.")] string? assemblyHandle = null,
        [Description("Maximum number of types to return (default: 50)")] int? maxItems = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Response shape. 'summary' (default, token-lean): { FullName, Namespace, Kind } only - use for browsing/searching. 'full': adds attributes, base type, interfaces, generic params, nested types - use only when you need those fields on every item.")] string projection = "summary",
        [Description("Optional paths to dependency assemblies (.dll) to help resolve types. Only needed when types fail to resolve and the assembly's dependencies are not next to it or in the NuGet cache - e.g. point at sibling DLLs in a build-output folder.")] string[]? additionalAssemblies = null,
        [Description("Bypass cache for this request")] bool noCache = false)
    {
        try
        {
            var target = AssemblyScope.ResolveTarget(handles, assemblyPath, assemblyHandle, additionalAssemblies);
            if (target.Error != null)
                return ToolResponse.Result(target.Error);
            assemblyPath = target.Path;
            additionalAssemblies = target.AdditionalAssemblies;

            var normalizedProjection = (projection ?? "summary").Trim().ToLowerInvariant();
            if (normalizedProjection != "summary" && normalizedProjection != "full")
                return ToolResponse.Result(JsonHelpers.Error("InvalidProjection", "projection must be 'summary' or 'full'"));

            string[] scopePaths = [assemblyPath];
            string[]? searchDirectories = null;
            if (additionalAssemblies is { Length: > 0 })
            {
                var scope = AssemblyScope.BuildAndValidate(assemblyPath, additionalAssemblies);
                if (scope.Error != null)
                    return ToolResponse.Result(scope.Error);
                scopePaths = scope.Paths;
                searchDirectories = scope.Paths
                    .Select(Path.GetDirectoryName)
                    .Where(d => !string.IsNullOrEmpty(d))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Cast<string>()
                    .ToArray();
            }

            var pageSize = Math.Max(1, maxItems ?? 50);
            var dependencyScope = searchDirectories is { Length: > 0 }
                ? string.Join("|", searchDirectories.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                : "";
            var saltSeed = $"types_from_assembly_{CacheKeyHelper.AssemblyStamp(assemblyPath)}_{pageSize}_{dependencyScope}";

            var cacheKey = CacheKeyHelper.Build(
                "type.list",
                CacheKeyHelper.AssemblyScopeStamp(scopePaths), dependencyScope, maxItems, skip, continuationToken, normalizedProjection);

            return middleware.Execute(cacheKey, () =>
            {
                var allTypes = typeAnalysis.GetTypesFromAssembly(assemblyPath, searchDirectories);
                var offset = 0;
                var salt = TokenHelper.MakeSalt(saltSeed);

                if (!string.IsNullOrWhiteSpace(continuationToken))
                {
                    if (!TokenHelper.TryParse(continuationToken, out offset, out var parsedSalt) || parsedSalt != salt)
                        return JsonHelpers.Error("InvalidContinuationToken", "The continuation token is invalid or expired.");
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
                return new ToolResponse(JsonHelpers.Envelope("type.list", result), links);
            }, noCache);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResponse.Result(ToolErrors.FromException(ex, "get types"));
        }
    }

    [McpServerTool(Title = "Get Type Info", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolEnvelope<TypeInfoData>))]
    [Description("Gets detailed metadata for a single type including accessibility, inheritance, interfaces, and member counts. Lightweight response - use as entry point before exploring members with get_type_members.")]
    public static CallToolResult GetTypeInfo(
        ITypeAnalysisService typeAnalysis,
        IInspectionContextProvider contexts,
        ToolMiddleware middleware,
        IAssemblyHandleRegistry handles,
        [Description("Type name to analyze. Prefer full name (e.g., 'System.Collections.Generic.List`1')")] string typeName,
        [Description("Path to the .NET assembly file (.dll or .exe). Omit when passing assemblyHandle.")] string? assemblyPath = null,
        [Description("Handle returned by open_assembly; pass instead of assemblyPath")] string? assemblyHandle = null,
        [Description("Bypass cache for this request")] bool noCache = false,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            var target = AssemblyScope.ResolveTarget(handles, assemblyPath, assemblyHandle);
            if (target.Error != null)
                return ToolResponse.Result(target.Error);
            assemblyPath = target.Path;

            var cacheKey = CacheKeyHelper.Build("type.info", CacheKeyHelper.AssemblyStamp(assemblyPath), typeName);
            return ToolResponse.Result(middleware.Execute(cacheKey, () =>
            {
                var info = typeAnalysis.GetTypeInfo(assemblyPath, typeName);
                return info == null
                    ? ToolErrors.TypeNotFound(contexts, assemblyPath, typeName)
                    : JsonHelpers.Envelope("type.info", info, ToolHints.ForType(info));
            }, noCache));
        }
        catch (AmbiguousTypeNameException ex)
        {
            return ToolResponse.Result(Elicitation.AmbiguousType(elicitation, ex));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResponse.Result(ToolErrors.FromException(ex, "analyze type"));
        }
    }

    [McpServerTool(Title = "Get Type Hierarchy", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets full inheritance chain and implemented interfaces for a type. Use to understand type relationships and find inherited members. Lightweight response. By default derivedTypes is null with a note - pass additionalAssemblies to compute derived/implementing types via the same scan as find_implementations_of. format='mermaid' returns a Mermaid classDiagram instead of JSON fields.")]
    public static string GetTypeHierarchy(
        ITypeAnalysisService typeAnalysis,
        IInspectionContextProvider contexts,
        IReverseLookupService reverseLookup,
        ToolMiddleware middleware,
        IAssemblyHandleRegistry handles,
        [Description("Type name to analyze. Prefer full name")]
        string typeName,
        [Description("Path to the .NET assembly file (.dll or .exe). Omit when passing assemblyHandle.")] string? assemblyPath = null,
        [Description("Handle returned by open_assembly; pass instead of assemblyPath (it also supplies the additionalAssemblies it was opened with)")] string? assemblyHandle = null,
        [Description("Optional additional assembly paths to include in the search scope")]
        string[]? additionalAssemblies = null,
        [Description("Output format. 'json' (default): hierarchy fields. 'mermaid': { typeName, diagram, nodeCount, edgeCount, truncated, note } where diagram is a Mermaid classDiagram.")] string format = "json",
        [Description("Maximum diagram nodes when format='mermaid' (default 50, max 200)")] int maxNodes = Mermaid.DefaultMaxNodes,
        [Description("Bypass cache for this request")] bool noCache = false,
        IProgress<ProgressNotificationValue>? progress = null,
        RequestContext<CallToolRequestParams>? context = null,
        CancellationToken cancellationToken = default)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            var target = AssemblyScope.ResolveTarget(handles, assemblyPath, assemblyHandle, additionalAssemblies);
            if (target.Error != null)
                return target.Error;
            assemblyPath = target.Path;
            additionalAssemblies = target.AdditionalAssemblies;

            var normalizedFormat = Mermaid.NormalizeFormat(format);
            if (normalizedFormat == null) return Mermaid.InvalidFormat();
            maxNodes = Math.Clamp(maxNodes, 1, Mermaid.MaxNodesLimit);

            string[]? scopePaths = null;
            if (additionalAssemblies is { Length: > 0 })
            {
                var scope = AssemblyScope.BuildAndValidate(assemblyPath, additionalAssemblies);
                if (scope.Error != null) return scope.Error;
                scopePaths = scope.Paths;
            }

            var cacheKey = CacheKeyHelper.Build(
                "type.hierarchy",
                CacheKeyHelper.AssemblyScopeStamp(scopePaths ?? [assemblyPath]), scopePaths != null, typeName, normalizedFormat, maxNodes);

            return middleware.Execute(cacheKey, () =>
            {
                var hierarchy = typeAnalysis.GetTypeHierarchy(assemblyPath, typeName);
                if (hierarchy == null) return ToolErrors.TypeNotFound(contexts, assemblyPath, typeName);

                IReadOnlyDictionary<string, string[]>? derivedBaseChains = null;
                if (scopePaths == null)
                    hierarchy = hierarchy with { Note = "derivedTypes not computed; pass additionalAssemblies to compute, or use find_implementations_of" };
                else
                {
                    var hits = reverseLookup.FindImplementations(
                        scopePaths, hierarchy.TypeName, new ReverseLookupOptions(), ProgressAdapter.ForPhase(progress), cancellationToken);
                    var derived = hits.Select(h => new DerivedTypeRef(h.TypeFullName, h.AssemblyPath, h.Kind)).ToArray();
                    hierarchy = hierarchy with { DerivedTypes = derived, Note = null };
                    derivedBaseChains = hits
                        .GroupBy(h => h.TypeFullName, StringComparer.Ordinal)
                        .ToDictionary(g => g.Key, g => g.First().BaseTypeChain, StringComparer.Ordinal);
                }

                return normalizedFormat == "mermaid"
                    ? HierarchyDiagram(hierarchy, maxNodes, derivedBaseChains)
                    : JsonHelpers.Envelope("type.hierarchy", hierarchy);
            }, noCache);
        }
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "get type hierarchy");
        }
    }

    private static string HierarchyDiagram(
        TypeHierarchy hierarchy, int maxNodes, IReadOnlyDictionary<string, string[]>? derivedBaseChains)
    {
        var diagram = Mermaid.ClassDiagram(hierarchy, maxNodes, derivedBaseChains);
        return JsonHelpers.Envelope("type.hierarchy", new
        {
            typeName = hierarchy.TypeName,
            format = "mermaid",
            diagram = diagram.Diagram,
            nodeCount = diagram.NodeCount,
            edgeCount = diagram.EdgeCount,
            truncated = diagram.Truncated,
            note = diagram.Truncated ? Mermaid.TruncationNote(maxNodes) : hierarchy.Note
        });
    }

    [McpServerTool(Title = "Get Generic Type Info", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets generic type parameters, constraints, and variance for generic types. Only useful for types where IsGenericType=true. Lightweight response.")]
    public static string GetGenericTypeInfo(
        ITypeAnalysisService typeAnalysis,
        IInspectionContextProvider contexts,
        ToolMiddleware middleware,
        IAssemblyHandleRegistry handles,
        [Description("Type name to analyze. Prefer full name")]
        string typeName,
        [Description("Path to the .NET assembly file (.dll or .exe). Omit when passing assemblyHandle.")] string? assemblyPath = null,
        [Description("Handle returned by open_assembly; pass instead of assemblyPath")] string? assemblyHandle = null,
        [Description("Bypass cache for this request")] bool noCache = false,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            var target = AssemblyScope.ResolveTarget(handles, assemblyPath, assemblyHandle);
            if (target.Error != null)
                return target.Error;
            assemblyPath = target.Path;

            var cacheKey = CacheKeyHelper.Build("type.generic", CacheKeyHelper.AssemblyStamp(assemblyPath), typeName);
            return middleware.Execute(cacheKey, () =>
            {
                var genericInfo = typeAnalysis.GetGenericTypeInfo(assemblyPath, typeName);
                return genericInfo == null
                    ? ToolErrors.TypeNotFound(contexts, assemblyPath, typeName)
                    : JsonHelpers.Envelope("type.generic", genericInfo);
            }, noCache);
        }
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "get generic type info");
        }
    }

    [McpServerTool(Title = "Get Type Attributes", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets custom attributes declared on a type (e.g., [Serializable], [Obsolete]). Returns attribute types and values. Lightweight response.")]
    public static string GetTypeAttributes(
        ITypeAnalysisService typeAnalysis,
        IInspectionContextProvider contexts,
        ToolMiddleware middleware,
        IAssemblyHandleRegistry handles,
        [Description("Type name to analyze. Prefer full name")]
        string typeName,
        [Description("Path to the .NET assembly file (.dll or .exe). Omit when passing assemblyHandle.")] string? assemblyPath = null,
        [Description("Handle returned by open_assembly; pass instead of assemblyPath")] string? assemblyHandle = null,
        [Description("Bypass cache for this request")] bool noCache = false,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            var target = AssemblyScope.ResolveTarget(handles, assemblyPath, assemblyHandle);
            if (target.Error != null)
                return target.Error;
            assemblyPath = target.Path;

            var cacheKey = CacheKeyHelper.Build("type.attributes", CacheKeyHelper.AssemblyStamp(assemblyPath), typeName);
            return middleware.Execute(cacheKey, () =>
            {
                var lookup = typeAnalysis.GetTypeAttributes(assemblyPath, typeName);
                if (lookup == null) return ToolErrors.TypeNotFound(contexts, assemblyPath, typeName);
                var (typeFullName, attributes) = lookup.Value;
                return JsonHelpers.Envelope("type.attributes", new { typeName = typeFullName, attributeCount = attributes.Length, attributes });
            }, noCache);
        }
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "get attributes");
        }
    }

    [McpServerTool(Title = "Get Nested Types", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets nested/inner types declared within a type. Use for types with inner classes, structs, or enums. Lightweight response.")]
    public static string GetNestedTypes(
        ITypeAnalysisService typeAnalysis,
        IInspectionContextProvider contexts,
        ToolMiddleware middleware,
        IAssemblyHandleRegistry handles,
        [Description("Type name to analyze. Prefer full name")]
        string typeName,
        [Description("Path to the .NET assembly file (.dll or .exe). Omit when passing assemblyHandle.")] string? assemblyPath = null,
        [Description("Handle returned by open_assembly; pass instead of assemblyPath")] string? assemblyHandle = null,
        [Description("Bypass cache for this request")] bool noCache = false,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            var target = AssemblyScope.ResolveTarget(handles, assemblyPath, assemblyHandle);
            if (target.Error != null)
                return target.Error;
            assemblyPath = target.Path;

            var cacheKey = CacheKeyHelper.Build("type.nested", CacheKeyHelper.AssemblyStamp(assemblyPath), typeName);
            return middleware.Execute(cacheKey, () =>
            {
                var lookup = typeAnalysis.GetNestedTypes(assemblyPath, typeName);
                if (lookup == null) return ToolErrors.TypeNotFound(contexts, assemblyPath, typeName);
                var (typeFullName, nested) = lookup.Value;
                return JsonHelpers.Envelope("type.nested", new { typeName = typeFullName, nestedTypeCount = nested.Length, nested });
            }, noCache);
        }
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "get nested types");
        }
    }
}
