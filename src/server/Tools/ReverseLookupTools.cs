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

    [McpServerTool]
    [Description("Finds types that implement an interface or derive from a base type, across one or more assemblies. Returns a lean summary ({ typeFullName, kind }) by default. Pass projection='full' for matchedInterfaces[] and baseTypeChain[]. Matches by simple name, full name, or open-generic form (e.g., 'IEnumerable', 'IEnumerable<T>', 'IEnumerable<>', 'IEnumerable`1').")]
    public static string FindImplementationsOf(
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
        [Description("Bypass cache for this request")] bool noCache = false)
    {
        try
        {
            var scope = BuildAndValidateScope(assemblyPath, additionalAssemblies);
            if (scope.Error != null) return scope.Error;

            var normalizedProjection = (projection ?? "summary").Trim().ToLowerInvariant();
            if (normalizedProjection != "summary" && normalizedProjection != "full")
                return JsonHelpers.Error("InvalidProjection", "projection must be 'summary' or 'full'");

            var scopeKey = string.Join(";", scope.Paths);
            var saltSeed = CacheKeyHelper.Build(
                "reverselookup.implementations.salt",
                scopeKey, typeName, caseSensitive, includeNonPublic);

            var cacheKey = CacheKeyHelper.Build(
                "reverselookup.implementations",
                scopeKey, typeName, caseSensitive, includeNonPublic, maxItems, continuationToken, skip, normalizedProjection);

            return middleware.Execute(cacheKey, () =>
            {
                var options = new ReverseLookupOptions(CaseSensitive: caseSensitive, IncludeNonPublic: includeNonPublic);
                var allHits = reverseLookup.FindImplementations(scope.Paths, typeName, options);

                var defaultPageSize = runtimeOptions.GetMaxItemsForTool("FindImplementationsOf");
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
                return JsonHelpers.Envelope("reverselookup.implementations", result);
            }, noCache);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to find implementations: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Finds methods whose return type matches the given type, across one or more assemblies. Returns a lean summary ({ declaringType, methodName, signature }) by default. Pass projection='full' for assemblyPath, returnType, isStatic. Open-generic match supported (e.g., 'Snapshot<>' matches methods returning 'Snapshot<int>').")]
    public static string FindMethodsReturning(
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
        [Description("Bypass cache for this request")] bool noCache = false)
    {
        try
        {
            var scope = BuildAndValidateScope(assemblyPath, additionalAssemblies);
            if (scope.Error != null) return scope.Error;

            var normalizedProjection = (projection ?? "summary").Trim().ToLowerInvariant();
            if (normalizedProjection != "summary" && normalizedProjection != "full")
                return JsonHelpers.Error("InvalidProjection", "projection must be 'summary' or 'full'");

            var scopeKey = string.Join(";", scope.Paths);
            var saltSeed = CacheKeyHelper.Build(
                "reverselookup.returning.salt",
                scopeKey, typeName, caseSensitive, includeNonPublic);

            var cacheKey = CacheKeyHelper.Build(
                "reverselookup.returning",
                scopeKey, typeName, caseSensitive, includeNonPublic, maxItems, continuationToken, skip, normalizedProjection);

            return middleware.Execute(cacheKey, () =>
            {
                var options = new ReverseLookupOptions(CaseSensitive: caseSensitive, IncludeNonPublic: includeNonPublic);
                var allHits = reverseLookup.FindMethodsReturning(scope.Paths, typeName, options);

                var defaultPageSize = runtimeOptions.GetMaxItemsForTool("FindMethodsReturning");
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
                return JsonHelpers.Envelope("reverselookup.returning", result);
            }, noCache);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to find methods by return type: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Finds all references to a type across one or more assemblies: base types, implemented interfaces, method returns/parameters, field/property/event types (recurses into generic arguments). Bounded sweep with hardCap to protect against runaway scans; check truncated=true. Returns lean summary by default; projection='full' adds assemblyPath, signature, dedupeKey.")]
    public static string FindReferencesTo(
        IReverseLookupService reverseLookup,
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        [Description("Path to the primary .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type to search references to. Simple name, full name, or open-generic form accepted.")] string typeName,
        [Description("Optional additional assembly paths to include in the search scope")] string[]? additionalAssemblies = null,
        [Description("Case sensitive type-name matching (default: false)")] bool caseSensitive = false,
        [Description("Include non-public members and types (default: false)")] bool includeNonPublic = false,
        [Description("Maximum items to return (default: 25)")] int? maxItems = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Response shape. 'summary' (default, token-lean): { declaringType, memberKind, memberName, referenceKind }. 'full': adds assemblyPath, signature, dedupeKey.")] string projection = "summary",
        [Description("Bypass cache for this request")] bool noCache = false)
    {
        try
        {
            var scope = BuildAndValidateScope(assemblyPath, additionalAssemblies);
            if (scope.Error != null) return scope.Error;

            var normalizedProjection = (projection ?? "summary").Trim().ToLowerInvariant();
            if (normalizedProjection != "summary" && normalizedProjection != "full")
                return JsonHelpers.Error("InvalidProjection", "projection must be 'summary' or 'full'");

            var scopeKey = string.Join(";", scope.Paths);
            var saltSeed = CacheKeyHelper.Build(
                "reverselookup.references.salt",
                scopeKey, typeName, caseSensitive, includeNonPublic);

            var cacheKey = CacheKeyHelper.Build(
                "reverselookup.references",
                scopeKey, typeName, caseSensitive, includeNonPublic, maxItems, continuationToken, skip, normalizedProjection);

            return middleware.Execute(cacheKey, () =>
            {
                var defaultPageSize = runtimeOptions.GetMaxItemsForTool("FindReferencesTo");
                var pageSize = Math.Max(1, maxItems ?? defaultPageSize);
                var hardCap = Math.Max(pageSize * 4, 500);
                var options = new ReverseLookupOptions(
                    CaseSensitive: caseSensitive,
                    IncludeNonPublic: includeNonPublic,
                    HardCap: hardCap);

                var scanResult = reverseLookup.FindReferences(scope.Paths, typeName, options);
                var allHits = scanResult.Hits;

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
                    truncated = scanResult.Truncated,
                    hardCap,
                    nextToken,
                    pagination = PaginationMetadata.Create(allHits.Length, page.Length, nextToken, resultsJson.Length),
                    results
                };

                var sizeError = ResponseSizeHelper.ValidateResponseSize(result, "FindReferencesTo");
                if (sizeError != null) return sizeError;

                return JsonHelpers.Envelope("reverselookup.references", result);
            }, noCache);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to find references: {ex.Message}");
        }
    }

    private readonly struct ScopeResult
    {
        public string[] Paths { get; init; }
        public string? Error { get; init; }
    }

    private static ScopeResult BuildAndValidateScope(string assemblyPath, string[]? additionalAssemblies)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            return new ScopeResult { Error = JsonHelpers.Error("InvalidArgument", "assemblyPath is required") };
        if (!File.Exists(assemblyPath))
            return new ScopeResult { Error = JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}") };

        var paths = new List<string> { assemblyPath };
        if (additionalAssemblies != null)
        {
            foreach (var extra in additionalAssemblies)
            {
                if (string.IsNullOrWhiteSpace(extra)) continue;
                if (!File.Exists(extra))
                    return new ScopeResult { Error = JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {extra}") };
                paths.Add(extra);
            }
        }
        return new ScopeResult { Paths = paths.ToArray() };
    }
}
