using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.ReverseLookup;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Shared;
using System.ComponentModel;
using System.Text.Json;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class ReverseLookupTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    [McpServerTool(Title = "Find Implementations Of", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Finds types that implement an interface or derive from a base type, across one or more assemblies. Returns a lean summary ({ typeFullName, kind }) by default. Pass projection='full' for matchedInterfaces[] and baseTypeChain[]. Matches by simple name, full name, or open-generic form (e.g., 'IEnumerable', 'IEnumerable<T>', 'IEnumerable<>', 'IEnumerable`1').")]
    public static CallToolResult FindImplementationsOf(
        IReverseLookupService reverseLookup,
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        [Description("Path to the primary .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to find implementers of. Interface name (e.g., 'IDisposable') or base class name (e.g., 'Stream'). Simple name, full name, or generic variants accepted.")] string typeName,
        [Description("Optional additional assembly paths to include in the search scope")] string[]? additionalAssemblies = null,
        [Description("Case sensitive type-name matching (default: false)")] bool caseSensitive = false,
        [Description("Include non-public types (default: false)")] bool includeNonPublic = false,
        [Description("Maximum items to return (default: 50)")] int? maxItems = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Response shape. 'summary' (default, token-lean): { typeFullName, kind }. 'full': adds assemblyPath, matchedInterfaces[], baseTypeChain[].")] string projection = "summary",
        [Description("Bypass cache for this request")] bool noCache = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var scope = AssemblyScope.BuildAndValidate(assemblyPath, additionalAssemblies);
            if (scope.Error != null) return ToolResponse.Result(scope.Error);

            var normalizedProjection = (projection ?? "summary").Trim().ToLowerInvariant();
            if (normalizedProjection != "summary" && normalizedProjection != "full")
                return ToolResponse.Result(JsonHelpers.Error("InvalidProjection", "projection must be 'summary' or 'full'"));

            var scopeKey = string.Join(";", scope.Paths.Select(CacheKeyHelper.FileStamp));
            var saltSeed = CacheKeyHelper.Build(
                "reverselookup.implementations.salt",
                scopeKey, typeName, caseSensitive, includeNonPublic);

            var cacheKey = CacheKeyHelper.Build(
                "reverselookup.implementations",
                scopeKey, typeName, caseSensitive, includeNonPublic, maxItems, continuationToken, skip, normalizedProjection);

            return middleware.Execute(cacheKey, () =>
            {
                var options = new ReverseLookupOptions(CaseSensitive: caseSensitive, IncludeNonPublic: includeNonPublic);
                var allHits = reverseLookup.FindImplementations(scope.Paths, typeName, options, ProgressAdapter.ForPhase(progress), cancellationToken);

                var defaultPageSize = runtimeOptions.GetMaxItemsForTool("find_implementations_of");
                var pageSize = Math.Max(1, maxItems ?? defaultPageSize);
                var offset = 0;
                var salt = TokenHelper.MakeSalt(saltSeed);

                if (!string.IsNullOrWhiteSpace(continuationToken))
                {
                    if (!TokenHelper.TryParse(continuationToken!, out offset, out var parsedSalt) || parsedSalt != salt)
                        return JsonHelpers.Error("InvalidContinuationToken", "The continuation token is invalid or expired.");
                }
                else if (skip.HasValue && skip.Value > 0)
                {
                    offset = skip.Value;
                }

                var page = allHits.Skip(offset).Take(pageSize).ToArray();
                string? nextToken = null;
                var nextOffset = offset + page.Length;
                if (nextOffset < allHits.Length) nextToken = TokenHelper.Make(nextOffset, salt);

                object results = normalizedProjection == "summary"
                    ? page.Select(h => new { typeFullName = h.TypeFullName, kind = h.Kind }).ToArray()
                    : page.Select(h => new
                    {
                        typeFullName = h.TypeFullName,
                        kind = h.Kind,
                        assemblyPath = h.AssemblyPath,
                        matchedInterfaces = h.MatchedInterfaces,
                        baseTypeChain = h.BaseTypeChain
                    }).ToArray();

                var resultsJson = JsonSerializer.Serialize(results, SerializerOptions);
                var result = new
                {
                    typeName,
                    scope = scope.Paths,
                    projection = normalizedProjection,
                    total = allHits.Length,
                    count = page.Length,
                    nextToken,
                    pagination = PaginationMetadata.Create(allHits.Length, page.Length, nextToken, resultsJson.Length),
                    results
                };
                return new ToolResponse(JsonHelpers.Envelope("reverselookup.implementations", result), ResourceUris.TypeLinks(page.Select(h => (h.AssemblyPath, h.TypeMetadataName ?? h.TypeFullName))));
            }, noCache);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResponse.Result(ToolErrors.FromException(ex, "find implementations"));
        }
    }

    [McpServerTool(Title = "Find Methods Returning", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Finds methods whose return type matches the given type, across one or more assemblies. Returns a lean summary ({ declaringType, methodName, signature }) by default. Pass projection='full' for assemblyPath, returnType, isStatic. Open-generic match supported (e.g., 'Snapshot<>' matches methods returning 'Snapshot<int>').")]
    public static CallToolResult FindMethodsReturning(
        IReverseLookupService reverseLookup,
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        [Description("Path to the primary .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Return type to search for. Simple name, full name, or open-generic form accepted.")] string typeName,
        [Description("Optional additional assembly paths to include in the search scope")] string[]? additionalAssemblies = null,
        [Description("Case sensitive type-name matching (default: false)")] bool caseSensitive = false,
        [Description("Include non-public methods and types (default: false)")] bool includeNonPublic = false,
        [Description("Maximum items to return (default: 50)")] int? maxItems = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Response shape. 'summary' (default, token-lean): { declaringType, methodName, signature }. 'full': adds assemblyPath, returnType, isStatic.")] string projection = "summary",
        [Description("Bypass cache for this request")] bool noCache = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var scope = AssemblyScope.BuildAndValidate(assemblyPath, additionalAssemblies);
            if (scope.Error != null) return ToolResponse.Result(scope.Error);

            var normalizedProjection = (projection ?? "summary").Trim().ToLowerInvariant();
            if (normalizedProjection != "summary" && normalizedProjection != "full")
                return ToolResponse.Result(JsonHelpers.Error("InvalidProjection", "projection must be 'summary' or 'full'"));

            var scopeKey = string.Join(";", scope.Paths.Select(CacheKeyHelper.FileStamp));
            var saltSeed = CacheKeyHelper.Build(
                "reverselookup.returning.salt",
                scopeKey, typeName, caseSensitive, includeNonPublic);

            var cacheKey = CacheKeyHelper.Build(
                "reverselookup.returning",
                scopeKey, typeName, caseSensitive, includeNonPublic, maxItems, continuationToken, skip, normalizedProjection);

            return middleware.Execute(cacheKey, () =>
            {
                var options = new ReverseLookupOptions(CaseSensitive: caseSensitive, IncludeNonPublic: includeNonPublic);
                var allHits = reverseLookup.FindMethodsReturning(scope.Paths, typeName, options, ProgressAdapter.ForPhase(progress), cancellationToken);

                var defaultPageSize = runtimeOptions.GetMaxItemsForTool("find_methods_returning");
                var pageSize = Math.Max(1, maxItems ?? defaultPageSize);
                var offset = 0;
                var salt = TokenHelper.MakeSalt(saltSeed);

                if (!string.IsNullOrWhiteSpace(continuationToken))
                {
                    if (!TokenHelper.TryParse(continuationToken!, out offset, out var parsedSalt) || parsedSalt != salt)
                        return JsonHelpers.Error("InvalidContinuationToken", "The continuation token is invalid or expired.");
                }
                else if (skip.HasValue && skip.Value > 0)
                {
                    offset = skip.Value;
                }

                var page = allHits.Skip(offset).Take(pageSize).ToArray();
                string? nextToken = null;
                var nextOffset = offset + page.Length;
                if (nextOffset < allHits.Length) nextToken = TokenHelper.Make(nextOffset, salt);

                object results = normalizedProjection == "summary"
                    ? page.Select(h => new
                    {
                        declaringType = h.DeclaringTypeFullName,
                        methodName = h.MethodName,
                        signature = h.Signature
                    }).ToArray()
                    : page.Select(h => new
                    {
                        declaringType = h.DeclaringTypeFullName,
                        methodName = h.MethodName,
                        signature = h.Signature,
                        assemblyPath = h.AssemblyPath,
                        returnType = h.ReturnTypeFriendlyName,
                        isStatic = h.IsStatic
                    }).ToArray();

                var resultsJson = JsonSerializer.Serialize(results, SerializerOptions);
                var result = new
                {
                    typeName,
                    scope = scope.Paths,
                    projection = normalizedProjection,
                    total = allHits.Length,
                    count = page.Length,
                    nextToken,
                    pagination = PaginationMetadata.Create(allHits.Length, page.Length, nextToken, resultsJson.Length),
                    results
                };
                return new ToolResponse(JsonHelpers.Envelope("reverselookup.returning", result), ResourceUris.TypeLinks(page.Select(h => (h.AssemblyPath, h.TypeMetadataName ?? h.DeclaringTypeFullName))));
            }, noCache);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResponse.Result(ToolErrors.FromException(ex, "find methods by return type"));
        }
    }

    [McpServerTool(Title = "Find Extension Methods For", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Finds extension methods that extend the given type, across one or more assemblies. Scans static classes for methods whose first ('this') parameter matches the target type. Returns a lean summary ({ declaringType, methodName, signature }) by default. Pass projection='full' for assemblyPath and extendedType. Open-generic match supported (e.g., 'IEnumerable<>' matches extensions on 'IEnumerable<T>').")]
    public static CallToolResult FindExtensionMethodsFor(
        IReverseLookupService reverseLookup,
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        [Description("Path to the primary .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type to find extension methods for. Simple name, full name, or open-generic form accepted (e.g., 'IEnumerable<>').")] string typeName,
        [Description("Optional additional assembly paths to include in the search scope")] string[]? additionalAssemblies = null,
        [Description("Case sensitive type-name matching (default: false)")] bool caseSensitive = false,
        [Description("Include non-public methods and types (default: false)")] bool includeNonPublic = false,
        [Description("Maximum items to return (default: 50)")] int? maxItems = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Response shape. 'summary' (default, token-lean): { declaringType, methodName, signature }. 'full': adds assemblyPath, extendedType.")] string projection = "summary",
        [Description("Bypass cache for this request")] bool noCache = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var scope = AssemblyScope.BuildAndValidate(assemblyPath, additionalAssemblies);
            if (scope.Error != null) return ToolResponse.Result(scope.Error);

            var normalizedProjection = (projection ?? "summary").Trim().ToLowerInvariant();
            if (normalizedProjection != "summary" && normalizedProjection != "full")
                return ToolResponse.Result(JsonHelpers.Error("InvalidProjection", "projection must be 'summary' or 'full'"));

            var scopeKey = string.Join(";", scope.Paths.Select(CacheKeyHelper.FileStamp));
            var saltSeed = CacheKeyHelper.Build(
                "reverselookup.extensions.salt",
                scopeKey, typeName, caseSensitive, includeNonPublic);

            var cacheKey = CacheKeyHelper.Build(
                "reverselookup.extensions",
                scopeKey, typeName, caseSensitive, includeNonPublic, maxItems, continuationToken, skip, normalizedProjection);

            return middleware.Execute(cacheKey, () =>
            {
                var options = new ReverseLookupOptions(CaseSensitive: caseSensitive, IncludeNonPublic: includeNonPublic);
                var allHits = reverseLookup.FindExtensionMethodsFor(scope.Paths, typeName, options, ProgressAdapter.ForPhase(progress), cancellationToken);

                var defaultPageSize = runtimeOptions.GetMaxItemsForTool("find_extension_methods_for");
                var pageSize = Math.Max(1, maxItems ?? defaultPageSize);
                var offset = 0;
                var salt = TokenHelper.MakeSalt(saltSeed);

                if (!string.IsNullOrWhiteSpace(continuationToken))
                {
                    if (!TokenHelper.TryParse(continuationToken!, out offset, out var parsedSalt) || parsedSalt != salt)
                        return JsonHelpers.Error("InvalidContinuationToken", "The continuation token is invalid or expired.");
                }
                else if (skip.HasValue && skip.Value > 0)
                {
                    offset = skip.Value;
                }

                var page = allHits.Skip(offset).Take(pageSize).ToArray();
                string? nextToken = null;
                var nextOffset = offset + page.Length;
                if (nextOffset < allHits.Length) nextToken = TokenHelper.Make(nextOffset, salt);

                object results = normalizedProjection == "summary"
                    ? page.Select(h => new
                    {
                        declaringType = h.DeclaringTypeFullName,
                        methodName = h.MethodName,
                        signature = h.Signature
                    }).ToArray()
                    : page.Select(h => new
                    {
                        declaringType = h.DeclaringTypeFullName,
                        methodName = h.MethodName,
                        signature = h.Signature,
                        assemblyPath = h.AssemblyPath,
                        extendedType = h.ExtendedTypeFriendlyName
                    }).ToArray();

                var resultsJson = JsonSerializer.Serialize(results, SerializerOptions);
                var result = new
                {
                    typeName,
                    scope = scope.Paths,
                    projection = normalizedProjection,
                    total = allHits.Length,
                    count = page.Length,
                    nextToken,
                    pagination = PaginationMetadata.Create(allHits.Length, page.Length, nextToken, resultsJson.Length),
                    results
                };
                return new ToolResponse(JsonHelpers.Envelope("reverselookup.extensions", result), ResourceUris.TypeLinks(page.Select(h => (h.AssemblyPath, h.TypeMetadataName ?? h.DeclaringTypeFullName))));
            }, noCache);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResponse.Result(ToolErrors.FromException(ex, "find extension methods"));
        }
    }

    [McpServerTool(Title = "Find References To", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Finds all references to a type across one or more assemblies: base types, implemented interfaces, method returns/parameters, field/property/event types (recurses into generic arguments). With analysisDepth='il', also scans method bodies for inbound callers ('who calls into this type?'). Bounded sweep with hardCap to protect against runaway scans; check truncated=true. Returns lean summary by default; projection='full' adds assemblyPath, signature, dedupeKey.")]
    public static CallToolResult FindReferencesTo(
        IReverseLookupService reverseLookup,
        IIlAnalysisService ilAnalysis,
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        [Description("Path to the primary .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type to search references to. Simple name, full name, or open-generic form accepted.")] string typeName,
        [Description("Optional additional assembly paths to include in the search scope")] string[]? additionalAssemblies = null,
        [Description("Case sensitive type-name matching (default: false)")] bool caseSensitive = false,
        [Description("Include non-public members and types (default: false)")] bool includeNonPublic = false,
        [Description("Analysis depth. 'signatures' (default): type usage in signatures and member declarations. 'il': additionally scan method bodies for inbound callers (referenceKind 'ilCall'/'ilFieldRead'/'ilFieldWrite'). IL scanning is slower.")] string analysisDepth = "signatures",
        [Description("Maximum items to return (default: 25)")] int? maxItems = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Response shape. 'summary' (default, token-lean): { declaringType, memberKind, memberName, referenceKind }. 'full': adds assemblyPath, signature, dedupeKey.")] string projection = "summary",
        [Description("Bypass cache for this request")] bool noCache = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var scope = AssemblyScope.BuildAndValidate(assemblyPath, additionalAssemblies);
            if (scope.Error != null) return ToolResponse.Result(scope.Error);

            var normalizedProjection = (projection ?? "summary").Trim().ToLowerInvariant();
            if (normalizedProjection != "summary" && normalizedProjection != "full")
                return ToolResponse.Result(JsonHelpers.Error("InvalidProjection", "projection must be 'summary' or 'full'"));

            var normalizedDepth = (analysisDepth ?? "signatures").Trim().ToLowerInvariant();
            if (normalizedDepth != "signatures" && normalizedDepth != "il")
                return ToolResponse.Result(JsonHelpers.Error("InvalidAnalysisDepth", "analysisDepth must be 'signatures' or 'il'"));

            var scopeKey = string.Join(";", scope.Paths.Select(CacheKeyHelper.FileStamp));
            var saltSeed = CacheKeyHelper.Build(
                "reverselookup.references.salt",
                scopeKey, typeName, caseSensitive, includeNonPublic, normalizedDepth);

            var cacheKey = CacheKeyHelper.Build(
                "reverselookup.references",
                scopeKey, typeName, caseSensitive, includeNonPublic, normalizedDepth, maxItems, continuationToken, skip, normalizedProjection);

            return middleware.Execute(cacheKey, () =>
            {
                var defaultPageSize = runtimeOptions.GetMaxItemsForTool("find_references_to");
                var pageSize = Math.Max(1, maxItems ?? defaultPageSize);
                var hardCap = Math.Max(pageSize * 4, 500);
                var options = new ReverseLookupOptions(
                    CaseSensitive: caseSensitive,
                    IncludeNonPublic: includeNonPublic,
                    HardCap: hardCap);

                var phaseCount = normalizedDepth == "il" ? 2 : 1;
                var scanResult = reverseLookup.FindReferences(
                    scope.Paths, typeName, options, ProgressAdapter.ForPhase(progress, phase: 0, phaseCount), cancellationToken);
                var allHits = scanResult.Hits;
                var truncated = scanResult.Truncated;

                if (normalizedDepth == "il")
                {
                    var inbound = ilAnalysis.FindInboundCallers(
                        scope.Paths, typeName, options, ProgressAdapter.ForPhase(progress, phase: 1, phaseCount), cancellationToken);
                    if (inbound.Length > 0)
                    {
                        var inboundHits = inbound.Select(h => new ReferenceHit(
                            AssemblyPath: h.AssemblyPath,
                            DeclaringTypeFullName: h.CallerTypeFullName,
                            MemberKind: "method",
                            MemberName: h.CallerMethod,
                            ReferenceKind: h.ReferenceKind,
                            Signature: $"{h.CallerMethod} -> {h.TargetMember}",
                            DedupeKey: $"{h.CallerTypeFullName}|method|{h.CallerMethod}|{h.ReferenceKind}|{h.TargetMember}",
                            TypeMetadataName: h.CallerTypeFullName));

                        allHits = allHits.Concat(inboundHits)
                            .OrderBy(h => h.AssemblyPath, StringComparer.Ordinal)
                            .ThenBy(h => h.DeclaringTypeFullName, StringComparer.Ordinal)
                            .ThenBy(h => h.MemberKind, StringComparer.Ordinal)
                            .ThenBy(h => h.MemberName, StringComparer.Ordinal)
                            .ThenBy(h => h.ReferenceKind, StringComparer.Ordinal)
                            .ThenBy(h => h.DedupeKey, StringComparer.Ordinal)
                            .ToArray();
                    }
                    truncated = truncated || inbound.Length >= options.HardCap;
                }

                var offset = 0;
                var salt = TokenHelper.MakeSalt(saltSeed);

                if (!string.IsNullOrWhiteSpace(continuationToken))
                {
                    if (!TokenHelper.TryParse(continuationToken!, out offset, out var parsedSalt) || parsedSalt != salt)
                        return JsonHelpers.Error("InvalidContinuationToken", "The continuation token is invalid or expired.");
                }
                else if (skip.HasValue && skip.Value > 0)
                {
                    offset = skip.Value;
                }

                var page = allHits.Skip(offset).Take(pageSize).ToArray();
                string? nextToken = null;
                var nextOffset = offset + page.Length;
                if (nextOffset < allHits.Length) nextToken = TokenHelper.Make(nextOffset, salt);

                object results = normalizedProjection == "summary"
                    ? page.Select(h => new
                    {
                        declaringType = h.DeclaringTypeFullName,
                        memberKind = h.MemberKind,
                        memberName = h.MemberName,
                        referenceKind = h.ReferenceKind
                    }).ToArray()
                    : page.Select(h => new
                    {
                        declaringType = h.DeclaringTypeFullName,
                        memberKind = h.MemberKind,
                        memberName = h.MemberName,
                        referenceKind = h.ReferenceKind,
                        assemblyPath = h.AssemblyPath,
                        signature = h.Signature,
                        dedupeKey = h.DedupeKey
                    }).ToArray();

                var resultsJson = JsonSerializer.Serialize(results, SerializerOptions);
                var result = new
                {
                    typeName,
                    scope = scope.Paths,
                    projection = normalizedProjection,
                    total = allHits.Length,
                    count = page.Length,
                    truncated,
                    hardCap,
                    nextToken,
                    pagination = PaginationMetadata.Create(allHits.Length, page.Length, nextToken, resultsJson.Length),
                    results
                };

                var links = ResourceUris.TypeLinks(page.Select(h => (h.AssemblyPath, h.TypeMetadataName ?? h.DeclaringTypeFullName)));
                var sizeError = ResponseSizeHelper.ValidateResponseSize(new { result, links }, "find_references_to");
                if (sizeError != null) return sizeError;

                return new ToolResponse(JsonHelpers.Envelope("reverselookup.references", result), links);
            }, noCache);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResponse.Result(ToolErrors.FromException(ex, "find references"));
        }
    }
}
