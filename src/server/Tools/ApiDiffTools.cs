using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.ApiDiff;
using Sherlock.MCP.Runtime.Handles;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Schemas;
using Sherlock.MCP.Server.Shared;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class ApiDiffTools
{
    private const string ToolName = "compare_api_surface";

    [McpServerTool(Title = "Compare API Surface", ReadOnly = true, Destructive = false, OpenWorld = false, Idempotent = true, UseStructuredContent = true, OutputSchemaType = typeof(ToolEnvelope<ApiDiffData>))]
    [Description("Diffs the public API of two versions of an assembly (left = old, right = new) and flags breaking changes: removed types/members, changed return/parameter types, reduced accessibility, sealed/abstract/static changes, removed base types or interfaces, abstract members added to interfaces or inheritable classes, and more. Each side is an assembly path, an asm_ handle from open_assembly, or a NuGet package as 'Package.Id@1.2.3' resolved from the local NuGet cache (omit the version for the highest cached one). Returns counts plus the breaking changes by default; projection='full' lists every added/removed/changed type and member with left/right signatures and reasons. Use when upgrading a package or reviewing a release for compatibility.")]
    public static async Task<CallToolResult> CompareApiSurface(
        IApiDiffService apiDiff,
        IProjectAnalysisService projects,
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        IAssemblyHandleRegistry handles,
        [Description("Old side: assembly path, asm_ handle, or 'Package.Id@version' from the NuGet cache")] string left,
        [Description("New side: assembly path, asm_ handle, or 'Package.Id@version' from the NuGet cache")] string right,
        [Description("Target framework for NuGet sides (e.g. 'net8.0'). Default: the best target framework both packages ship")] string? tfm = null,
        [Description("Only compare types in this namespace or its sub-namespaces (case-insensitive)")] string? namespaceFilter = null,
        [Description("With projection='full', list only breaking changes (default: false)")] bool breakingOnly = false,
        [Description("Response shape. 'summary' (default, token-lean): counts plus breaking changes as { change, scope, type, memberKind, member, breaking, signature, reason }. 'full': every change, adding leftSignature, rightSignature and reasons[].")] string projection = "summary",
        [Description("Maximum changes per page (default: 100)")] int? maxItems = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Bypass cache for this request")] bool noCache = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedProjection = (projection ?? "summary").Trim().ToLowerInvariant();
            if (normalizedProjection != "summary" && normalizedProjection != "full")
                return ToolResponse.Result(JsonHelpers.Error("InvalidProjection", "projection must be 'summary' or 'full'"));

            var leftResult = await ApiDiffSideResolver.ResolveAsync(nameof(left), left, tfm, handles, projects);
            if (leftResult.Error != null)
                return ToolResponse.Result(leftResult.Error);
            var rightResult = await ApiDiffSideResolver.ResolveAsync(nameof(right), right, tfm, handles, projects);
            if (rightResult.Error != null)
                return ToolResponse.Result(rightResult.Error);
            var (leftSide, rightSide, tfmWarning) = tfm == null
                ? await ApiDiffSideResolver.AlignTfmsAsync(leftResult.Side!, rightResult.Side!, projects)
                : (leftResult.Side!, rightResult.Side!, null);

            var leftScope = DependencyScope.Build(leftSide.AssemblyPath, leftSide.AdditionalAssemblies);
            if (leftScope.Error != null)
                return ToolResponse.Result(leftScope.Error);
            var rightScope = DependencyScope.Build(rightSide.AssemblyPath, rightSide.AdditionalAssemblies);
            if (rightScope.Error != null)
                return ToolResponse.Result(rightScope.Error);

            var normalizedNamespace = string.IsNullOrWhiteSpace(namespaceFilter) ? null : namespaceFilter.Trim();
            var saltSeed = CacheKeyHelper.Build(
                "api.compare.salt", leftScope.Stamp, rightScope.Stamp, normalizedNamespace, breakingOnly, normalizedProjection);
            var cacheKey = CacheKeyHelper.Build(
                "api.compare", leftScope.Stamp, rightScope.Stamp, leftSide.Source, rightSide.Source, leftSide.PackageId, leftSide.PackageVersion,
                rightSide.PackageId, rightSide.PackageVersion, normalizedNamespace, breakingOnly, normalizedProjection, maxItems, continuationToken);

            return ToolResponse.Result(middleware.Execute(cacheKey, () =>
            {
                var pageSize = Math.Max(1, maxItems ?? runtimeOptions.GetMaxItemsForTool(ToolName));
                var salt = TokenHelper.MakeSalt(saltSeed);
                var offset = 0;
                if (!string.IsNullOrWhiteSpace(continuationToken)
                    && (!TokenHelper.TryParse(continuationToken!, out offset, out var parsedSalt) || parsedSalt != salt || offset < 0))
                    return JsonHelpers.Error("InvalidContinuationToken", "The continuation token is invalid or expired.");

                var diff = apiDiff.Compare(
                    leftSide.AssemblyPath, leftScope.SearchDirectories, rightSide.AssemblyPath, rightScope.SearchDirectories,
                    normalizedNamespace, cancellationToken);

                var listed = diff.Changes
                    .Where(change => change.Breaking || (normalizedProjection == "full" && !breakingOnly))
                    .ToArray();
                var page = listed.Skip(offset).Take(pageSize).ToArray();
                var nextOffset = offset + page.Length;
                var nextToken = nextOffset < listed.Length ? TokenHelper.Make(nextOffset, salt) : null;

                object changes = normalizedProjection == "summary"
                    ? page.Select(SummaryItem).ToArray()
                    : page.Select(FullItem).ToArray();
                var changesJson = JsonSerializer.Serialize(changes, JsonHelpers.DefaultOptions);
                var result = new
                {
                    left = SideInfo(leftSide, diff.Left),
                    right = SideInfo(rightSide, diff.Right),
                    projection = normalizedProjection,
                    breakingOnly,
                    namespaceFilter = normalizedNamespace,
                    counts = Counts(diff.Changes),
                    total = listed.Length,
                    count = page.Length,
                    nextToken,
                    pagination = PaginationMetadata.Create(listed.Length, page.Length, nextToken, changesJson.Length),
                    warnings = tfmWarning == null ? diff.Warnings : [tfmWarning, .. diff.Warnings],
                    changes
                };

                return ResponseSizeHelper.ValidateResponseSize(result, ToolName)
                    ?? JsonHelpers.Envelope("api.compare", result);
            }, noCache));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResponse.Result(ToolErrors.FromException(ex, "compare API surfaces"));
        }
    }

    private static object SideInfo(ApiDiffSide side, ApiAssemblyIdentity identity) => new
    {
        source = side.Source,
        assemblyPath = side.AssemblyPath,
        assemblyName = identity.Name,
        assemblyVersion = identity.Version,
        packageId = side.PackageId,
        packageVersion = side.PackageVersion,
        tfm = side.Tfm
    };

    private static object Counts(IReadOnlyList<ApiChange> changes) => new
    {
        typesAdded = Count(changes, ApiChangeScope.Type, ApiChangeKind.Added),
        typesRemoved = Count(changes, ApiChangeScope.Type, ApiChangeKind.Removed),
        typesChanged = Count(changes, ApiChangeScope.Type, ApiChangeKind.Changed),
        membersAdded = Count(changes, ApiChangeScope.Member, ApiChangeKind.Added),
        membersRemoved = Count(changes, ApiChangeScope.Member, ApiChangeKind.Removed),
        membersChanged = Count(changes, ApiChangeScope.Member, ApiChangeKind.Changed),
        breaking = changes.Count(change => change.Breaking)
    };

    private static int Count(IReadOnlyList<ApiChange> changes, ApiChangeScope scope, ApiChangeKind kind) =>
        changes.Count(change => change.Scope == scope && change.Change == kind);

    private static object SummaryItem(ApiChange change) => new
    {
        change = ChangeName(change.Change),
        scope = ScopeName(change.Scope),
        type = change.TypeName,
        memberKind = change.MemberKind,
        member = change.MemberName,
        breaking = change.Breaking,
        signature = change.RightSignature ?? change.LeftSignature ?? string.Empty,
        reason = Reason(change)
    };

    private static object FullItem(ApiChange change) => new
    {
        change = ChangeName(change.Change),
        scope = ScopeName(change.Scope),
        type = change.TypeName,
        memberKind = change.MemberKind,
        member = change.MemberName,
        breaking = change.Breaking,
        signature = change.RightSignature ?? change.LeftSignature ?? string.Empty,
        reason = Reason(change),
        leftSignature = change.LeftSignature,
        rightSignature = change.RightSignature,
        reasons = change.Reasons.Select(r => new { description = r.Description, breaking = r.Breaking }).ToArray()
    };

    private static string? Reason(ApiChange change)
    {
        var reasons = change.Breaking ? change.Reasons.Where(r => r.Breaking) : change.Reasons;
        var text = string.Join("; ", reasons.Select(r => r.Description));
        return text.Length > 0 ? text : null;
    }

    private static string ChangeName(ApiChangeKind kind) => kind switch
    {
        ApiChangeKind.Added => "added",
        ApiChangeKind.Removed => "removed",
        _ => "changed"
    };

    private static string ScopeName(ApiChangeScope scope) => scope == ApiChangeScope.Type ? "type" : "member";
}
