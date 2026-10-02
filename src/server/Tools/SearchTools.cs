using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.Search;
using Sherlock.MCP.Runtime.Handles;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Schemas;
using Sherlock.MCP.Server.Shared;
using System.ComponentModel;
using System.Text.Json;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class SearchTools
{
    private static readonly HashSet<string> ValidKinds =
        new(StringComparer.OrdinalIgnoreCase) { "method", "property", "field", "event", "type" };

    private static readonly char[] KindSeparators = { ',', '|' };

    [McpServerTool(Title = "Search Members", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolEnvelope<SearchMembersData>))]
    [Description("Searches an assembly for members whose name contains a fragment, without needing to know the declaring type first. Answers the inverse of get_type_members (e.g., 'where is ParseConnectionString defined?'). Each hit is { declaringType, memberKind, name, signature }; the searched assemblyPath is echoed once at the top level. Filter by memberKinds (csv: method|property|field|event|type).")]
    public static CallToolResult SearchMembers(
        ISearchService searchService,
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        IAssemblyHandleRegistry handles,
        [Description("Substring to match against member names (required). Case-insensitive unless caseSensitive=true.")] string nameContains,
        [Description("Path to the .NET assembly file (.dll or .exe). Omit when passing assemblyHandle.")] string? assemblyPath = null,
        [Description("Handle returned by open_assembly; pass instead of assemblyPath")] string? assemblyHandle = null,
        [Description("Member kinds to include, csv from: method|property|field|event|type. Default: all kinds.")] string? memberKinds = null,
        [Description("Include non-public members and types (default: false)")] bool includeNonPublic = false,
        [Description("Case sensitive name matching (default: false)")] bool caseSensitive = false,
        [Description("Maximum items to return (default: 50)")] int? maxItems = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Bypass cache for this request")] bool noCache = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var target = AssemblyScope.ResolveTarget(handles, assemblyPath, assemblyHandle);
            if (target.Error != null)
                return ToolResponse.Result(target.Error);
            assemblyPath = target.Path;
            if (string.IsNullOrWhiteSpace(nameContains))
                return ToolResponse.Result(JsonHelpers.Error("InvalidArgument", "nameContains is required"));

            IReadOnlySet<string>? kinds = null;
            string normalizedKinds = "all";
            if (!string.IsNullOrWhiteSpace(memberKinds))
            {
                var parsed = memberKinds
                    .Split(KindSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(k => k.ToLowerInvariant())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var invalid = parsed.Where(k => !ValidKinds.Contains(k)).ToArray();
                if (invalid.Length > 0)
                    return ToolResponse.Result(JsonHelpers.Error("InvalidArgument", $"Unknown memberKinds: {string.Join(", ", invalid)}. Valid: method, property, field, event, type."));
                if (parsed.Count > 0)
                {
                    kinds = parsed;
                    normalizedKinds = string.Join(",", parsed.OrderBy(k => k, StringComparer.Ordinal));
                }
            }

            var assemblyStamp = CacheKeyHelper.AssemblyStamp(assemblyPath);
            var saltSeed = CacheKeyHelper.Build(
                "search.members.salt",
                assemblyStamp, nameContains, normalizedKinds, caseSensitive, includeNonPublic);

            var cacheKey = CacheKeyHelper.Build(
                "search.members",
                assemblyStamp, nameContains, normalizedKinds, caseSensitive, includeNonPublic, maxItems, continuationToken, skip);

            return middleware.Execute(cacheKey, () =>
            {
                var defaultPageSize = runtimeOptions.GetMaxItemsForTool("search_members");
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

                var pageResult = searchService.SearchMembers(assemblyPath, nameContains, options, offset, pageSize, cancellationToken);
                var page = pageResult.Items;

                string? nextToken = null;
                var nextOffset = offset + page.Length;
                if (nextOffset < pageResult.Total) nextToken = TokenHelper.Make(nextOffset, salt);

                var results = page.Select(h => new
                {
                    declaringType = h.DeclaringType,
                    memberKind = h.MemberKind,
                    name = h.Name,
                    signature = h.Signature
                }).ToArray();

                var resultsJson = JsonSerializer.Serialize(results, JsonHelpers.DefaultOptions);
                var result = new
                {
                    assemblyPath,
                    nameContains,
                    total = pageResult.Total,
                    count = page.Length,
                    nextToken,
                    pagination = PaginationMetadata.Create(pageResult.Total, page.Length, nextToken, resultsJson.Length),
                    results
                };

                var links = ResourceUris.TypeLinks(page.Select(h => (assemblyPath, h.TypeMetadataName)));
                var sizeError = ResponseSizeHelper.ValidateResponseSize(new { result, links }, "search_members");
                if (sizeError != null) return sizeError;

                return new ToolResponse(JsonHelpers.Envelope("search.members", result), links);
            }, noCache);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResponse.Result(ToolErrors.FromException(ex, "search members"));
        }
    }
}
