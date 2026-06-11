using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.Search;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Shared;
using System.ComponentModel;
using System.Text.Json;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class SearchTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private static readonly HashSet<string> ValidKinds =
        new(StringComparer.OrdinalIgnoreCase) { "method", "property", "field", "event", "type" };

    [McpServerTool]
    [Description("Searches an assembly for members whose name contains a fragment, without needing to know the declaring type first. Answers the inverse of GetTypeMethods/Properties (e.g., 'where is ParseConnectionString defined?'). Returns a lean summary ({ declaringType, memberKind, name, signature }) by default. Pass projection='full' to add assemblyPath. Filter by memberKinds (csv: method|property|field|event|type).")]
    public static string SearchMembers(
        ISearchService searchService,
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Substring to match against member names (required). Case-insensitive unless caseSensitive=true.")] string nameContains,
        [Description("Member kinds to include, csv from: method|property|field|event|type. Default: all kinds.")] string? memberKinds = null,
        [Description("Include non-public members and types (default: false)")] bool includeNonPublic = false,
        [Description("Case sensitive name matching (default: false)")] bool caseSensitive = false,
        [Description("Maximum items to return (default: 50)")] int? maxItems = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Response shape. 'summary' (default, token-lean): { declaringType, memberKind, name, signature }. 'full': adds assemblyPath.")] string projection = "summary",
        [Description("Bypass cache for this request")] bool noCache = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
                return JsonHelpers.Error("InvalidArgument", "assemblyPath is required");
            if (!File.Exists(assemblyPath))
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
            if (string.IsNullOrWhiteSpace(nameContains))
                return JsonHelpers.Error("InvalidArgument", "nameContains is required");

            var normalizedProjection = (projection ?? "summary").Trim().ToLowerInvariant();
            if (normalizedProjection != "summary" && normalizedProjection != "full")
                return JsonHelpers.Error("InvalidProjection", "projection must be 'summary' or 'full'");

            IReadOnlySet<string>? kinds = null;
            string normalizedKinds = "all";
            if (!string.IsNullOrWhiteSpace(memberKinds))
            {
                var parsed = memberKinds
                    .Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(k => k.ToLowerInvariant())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var invalid = parsed.Where(k => !ValidKinds.Contains(k)).ToArray();
                if (invalid.Length > 0)
                    return JsonHelpers.Error("InvalidArgument", $"Unknown memberKinds: {string.Join(", ", invalid)}. Valid: method, property, field, event, type.");
                if (parsed.Count > 0)
                {
                    kinds = parsed;
                    normalizedKinds = string.Join(",", parsed.OrderBy(k => k, StringComparer.Ordinal));
                }
            }

            var assemblyStamp = CacheKeyHelper.FileStamp(assemblyPath);
            var saltSeed = CacheKeyHelper.Build(
                "search.members.salt",
                assemblyStamp, nameContains, normalizedKinds, caseSensitive, includeNonPublic);

            var cacheKey = CacheKeyHelper.Build(
                "search.members",
                assemblyStamp, nameContains, normalizedKinds, caseSensitive, includeNonPublic, maxItems, continuationToken, skip, normalizedProjection);

            return middleware.Execute(cacheKey, () =>
            {
                var defaultPageSize = runtimeOptions.GetMaxItemsForTool("SearchMembers");
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

                var options = new SearchOptions(
                    CaseSensitive: caseSensitive,
                    IncludeNonPublic: includeNonPublic,
                    MemberKinds: kinds);

                var pageResult = searchService.SearchMembers(assemblyPath, nameContains, options, offset, pageSize);
                var page = pageResult.Items;

                string? nextToken = null;
                var nextOffset = offset + page.Length;
                if (nextOffset < pageResult.Total) nextToken = TokenHelper.Make(nextOffset, salt);

                object results = normalizedProjection == "summary"
                    ? page.Select(h => new
                    {
                        declaringType = h.DeclaringType,
                        memberKind = h.MemberKind,
                        name = h.Name,
                        signature = h.Signature
                    }).ToArray()
                    : page.Select(h => new
                    {
                        declaringType = h.DeclaringType,
                        memberKind = h.MemberKind,
                        name = h.Name,
                        signature = h.Signature,
                        assemblyPath
                    }).ToArray();

                var resultsJson = JsonSerializer.Serialize(results, SerializerOptions);
                var result = new
                {
                    assemblyPath,
                    nameContains,
                    projection = normalizedProjection,
                    total = pageResult.Total,
                    count = page.Length,
                    nextToken,
                    pagination = PaginationMetadata.Create(pageResult.Total, page.Length, nextToken, resultsJson.Length),
                    results
                };

                var sizeError = ResponseSizeHelper.ValidateResponseSize(result, "SearchMembers");
                if (sizeError != null) return sizeError;

                return JsonHelpers.Envelope("search.members", result);
            }, noCache);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to search members: {ex.Message}");
        }
    }
}
