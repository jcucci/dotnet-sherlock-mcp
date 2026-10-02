using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Decompilation;
using Sherlock.MCP.Runtime.Handles;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Runtime.SourceLink;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Schemas;
using Sherlock.MCP.Server.Shared;
using System.ComponentModel;
using System.Reflection;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class SourceTools
{
    private const string AllOverloads = "<all>";
    private const string MixedOrigin = "mixed";

    [McpServerTool(Title = "Get Member Source", ReadOnly = true, Destructive = false, OpenWorld = true, UseStructuredContent = true, OutputSchemaType = typeof(ToolEnvelope<MemberSourceData>))]
    [Description("Returns a member's original source code (with comments and real names) from the assembly's portable PDB: source embedded in the PDB, the local file it was built from, or the Source Link URL (fetched from known source hosts only). Falls back to decompiled C# per overload when no source is available; origin says which (sourcelink, embedded, local, decompiled, or mixed) and note says why a fallback happened. Returns every overload unless parameterTypes narrows it. Paged by line: follow continuationToken while truncated is true. Prefer this over decompile_member.")]
    public static async Task<CallToolResult> GetMemberSource(
        IOriginalSourceService sources,
        IDecompilerService decompiler,
        IInspectionContextProvider contexts,
        ToolMiddleware middleware,
        IAssemblyHandleRegistry handles,
        [Description("Type that declares the member. Prefer the full name; use Outer+Inner for nested types.")] string typeName,
        [Description("Member name. Use '.ctor' for instance constructors or '.cctor' for the static constructor.")] string memberName,
        [Description("Comma-separated parameter types selecting one overload, e.g. 'string,int' or 'System.String,System.Int32'. Empty string selects the parameterless overload. Omit to return every overload.")] string? parameterTypes = null,
        [Description("Path to the .NET assembly file (.dll or .exe). Omit when passing assemblyHandle.")] string? assemblyPath = null,
        [Description("Handle returned by open_assembly; pass instead of assemblyPath")] string? assemblyHandle = null,
        [Description("Optional dependency assembly paths; their folders are searched when resolving referenced types (added to the handle's)")] string[]? additionalAssemblies = null,
        [Description("Case sensitive type/member matching (default: false)")] bool caseSensitive = false,
        [Description("Include non-public members (default: true)")] bool includeNonPublic = true,
        [Description("Maximum source lines per page (default: 400, max: 5000)")] int maxLines = SourcePager.DefaultMaxLines,
        [Description("Continuation token from a previous page")] string? continuationToken = null,
        [Description("Bypass cache for this request")] bool noCache = false,
        RequestContext<CallToolRequestParams>? context = null,
        CancellationToken cancellationToken = default)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            var target = AssemblyScope.ResolveTarget(handles, assemblyPath, assemblyHandle, additionalAssemblies);
            if (target.Error != null)
                return ToolResponse.Result(target.Error);
            assemblyPath = target.Path;
            if (string.IsNullOrWhiteSpace(typeName))
                return ToolResponse.Result(JsonHelpers.Error("InvalidArgument", "typeName is required"));
            if (string.IsNullOrWhiteSpace(memberName))
                return ToolResponse.Result(JsonHelpers.Error("InvalidArgument", "memberName is required"));
            if (DecompilationTools.MaxLinesError(maxLines) is { } maxLinesError)
                return ToolResponse.Result(maxLinesError);
            var scope = DependencyScope.Build(assemblyPath, target.AdditionalAssemblies);
            if (scope.Error != null)
                return ToolResponse.Result(scope.Error);

            var cacheKey = CacheKeyHelper.Build(
                "source.member", CacheKeyHelper.PdbStamp(scope.Stamp, assemblyPath), sources.PolicyStamp,
                typeName, memberName, parameterTypes ?? AllOverloads, caseSensitive, includeNonPublic);
            var salt = TokenHelper.MakeSalt(cacheKey);
            if (!SourcePager.TryReadOffset(continuationToken, salt, out var offset))
                return ToolResponse.Result(SourcePager.InvalidToken());

            var envelope = await middleware.ExecuteWhenCacheableAsync(cacheKey, async () =>
            {
                using var lease = contexts.Acquire(assemblyPath, scope.SearchDirectories);
                var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var type = TypeNameResolver.Resolve(lease.Assembly, typeName, comparison).OrThrowIfAmbiguous(typeName);
                if (type == null) return (ToolErrors.TypeNotFound(lease.Context, typeName), true);
                if (!Equals(type.Assembly, lease.Assembly)) return (DecompilationTools.TypeForwarded(type, assemblyPath), true);

                var named = DecompilationTargets.SelectMembers(type, memberName, parameterTypes: null, comparison, includeNonPublic);
                if (named.Count == 0) return (ToolErrors.MemberNotFound(type, memberName), true);

                var members = parameterTypes == null
                    ? named
                    : DecompilationTargets.SelectMembers(type, memberName, parameterTypes, comparison, includeNonPublic);
                if (members.Count == 0) return (DecompilationTools.OverloadNotFound(type, memberName, parameterTypes!, named), true);

                var originals = await sources.GetSourcesAsync(assemblyPath, members, cancellationToken);
                var texts = FillWithDecompiled(originals, members, assemblyPath, scope.SearchDirectories, decompiler, cancellationToken);
                var origins = originals.Select(o => o.Origin).Distinct(StringComparer.Ordinal).ToArray();
                var notes = originals.Select(o => o.Note).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
                var envelope = JsonHelpers.Envelope("source.member", new
                {
                    typeName = type.FullName ?? type.Name,
                    memberName = members[0].Name,
                    origin = origins.Length == 1 ? origins[0] : MixedOrigin,
                    overloads = members.Zip(originals, (m, o) => new
                    {
                        signature = DecompilationTargets.FormatSignature(m),
                        origin = o.Origin,
                        document = o.Document,
                        url = o.Url,
                        lines = o.StartLine is { } start && o.EndLine is { } end ? new { start, end } : null
                    }).ToArray(),
                    note = notes.Length == 0 ? null : string.Join(" ", notes),
                    source = DecompilationTools.CombineOverloads(members, texts)
                });
                return (envelope, !originals.Any(o => o.Transient));
            }, noCache);

            return ToolResponse.Result(SourcePager.Page(envelope, offset, maxLines, salt, "get_member_source"));
        }
        catch (AmbiguousTypeNameException ex)
        {
            return ToolResponse.Result(Elicitation.AmbiguousType(elicitation, ex));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResponse.Result(ToolErrors.FromException(ex, "get member source"));
        }
    }

    private static string[] FillWithDecompiled(
        IReadOnlyList<OriginalSource> originals, IReadOnlyList<MemberInfo> members, string assemblyPath,
        string[]? searchDirectories, IDecompilerService decompiler, CancellationToken cancellationToken)
    {
        var missing = Enumerable.Range(0, originals.Count).Where(i => originals[i].Text == null).ToArray();
        var decompiled = missing.Length == 0
            ? []
            : decompiler.DecompileMembers(assemblyPath, missing.Select(i => members[i].MetadataToken).ToArray(), searchDirectories, cancellationToken);

        var texts = originals.Select(o => o.Text ?? "").ToArray();
        for (var i = 0; i < missing.Length; i++)
            texts[missing[i]] = decompiled[i];
        return texts;
    }
}
