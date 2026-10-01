using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Decompilation;
using Sherlock.MCP.Runtime.Handles;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Schemas;
using Sherlock.MCP.Server.Shared;
using System.ComponentModel;
using System.Reflection;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class DecompilationTools
{
    private const string AllOverloads = "<all>";
    private static readonly string[] OverloadDiscoveryTools = ["analyze_method", "get_type_members"];
    private static readonly string[] AssemblyDiscoveryTools = ["find_assembly_by_class_name"];

    [McpServerTool(Title = "Decompile Member", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolEnvelope<DecompiledMemberData>))]
    [Description("Decompiles a single member (method, property, field, event or constructor) to C# source, showing its body - what signature-level tools can't. Returns every overload of the name unless parameterTypes narrows it. Source is paged by line: follow continuationToken while truncated is true. Prefer this over decompile_type.")]
    public static CallToolResult DecompileMember(
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
            if (MaxLinesError(maxLines) is { } maxLinesError)
                return ToolResponse.Result(maxLinesError);
            var scope = DependencyScope.Build(assemblyPath, target.AdditionalAssemblies);
            if (scope.Error != null)
                return ToolResponse.Result(scope.Error);

            var cacheKey = CacheKeyHelper.Build(
                "decompile.member", scope.Stamp, typeName, memberName, parameterTypes ?? AllOverloads, caseSensitive, includeNonPublic);
            var salt = TokenHelper.MakeSalt(cacheKey);
            if (!SourcePager.TryReadOffset(continuationToken, salt, out var offset))
                return ToolResponse.Result(SourcePager.InvalidToken());

            var envelope = middleware.Execute(cacheKey, () =>
            {
                using var lease = contexts.Acquire(assemblyPath, scope.SearchDirectories);
                var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var type = TypeNameResolver.Resolve(lease.Assembly, typeName, comparison).OrThrowIfAmbiguous(typeName);
                if (type == null) return ToolErrors.TypeNotFound(lease.Context, typeName);
                if (!Equals(type.Assembly, lease.Assembly)) return TypeForwarded(type, assemblyPath);

                var named = DecompilationTargets.SelectMembers(type, memberName, parameterTypes: null, comparison, includeNonPublic);
                if (named.Count == 0) return ToolErrors.MemberNotFound(type, memberName);

                var members = parameterTypes == null
                    ? named
                    : DecompilationTargets.SelectMembers(type, memberName, parameterTypes, comparison, includeNonPublic);
                if (members.Count == 0) return OverloadNotFound(type, memberName, parameterTypes!, named);

                var sources = decompiler.DecompileMembers(
                    assemblyPath, members.Select(m => m.MetadataToken).ToArray(), scope.SearchDirectories, cancellationToken);
                return JsonHelpers.Envelope("decompile.member", new
                {
                    typeName = type.FullName ?? type.Name,
                    memberName = members[0].Name,
                    overloads = members.Select(m => new { signature = DecompilationTargets.FormatSignature(m) }).ToArray(),
                    source = CombineOverloads(members, sources)
                });
            }, noCache);

            return ToolResponse.Result(SourcePager.Page(envelope, offset, maxLines, salt, "decompile_member"));
        }
        catch (AmbiguousTypeNameException ex)
        {
            return ToolResponse.Result(Elicitation.AmbiguousType(elicitation, ex));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResponse.Result(ToolErrors.FromException(ex, "decompile member"));
        }
    }

    [McpServerTool(Title = "Decompile Type", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Decompiles a whole type to C# source. Output can be large, so it is paged by line: follow continuationToken while truncated is true. Prefer decompile_member when you only need one member's body.")]
    public static string DecompileType(
        IDecompilerService decompiler,
        IInspectionContextProvider contexts,
        ToolMiddleware middleware,
        IAssemblyHandleRegistry handles,
        [Description("Type to decompile. Prefer the full name; use Outer+Inner for nested types.")] string typeName,
        [Description("Path to the .NET assembly file (.dll or .exe). Omit when passing assemblyHandle.")] string? assemblyPath = null,
        [Description("Handle returned by open_assembly; pass instead of assemblyPath")] string? assemblyHandle = null,
        [Description("Optional dependency assembly paths; their folders are searched when resolving referenced types (added to the handle's)")] string[]? additionalAssemblies = null,
        [Description("Case sensitive type-name matching (default: false)")] bool caseSensitive = false,
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
                return target.Error;
            assemblyPath = target.Path;
            if (string.IsNullOrWhiteSpace(typeName))
                return JsonHelpers.Error("InvalidArgument", "typeName is required");
            if (MaxLinesError(maxLines) is { } maxLinesError)
                return maxLinesError;
            var scope = DependencyScope.Build(assemblyPath, target.AdditionalAssemblies);
            if (scope.Error != null)
                return scope.Error;

            var cacheKey = CacheKeyHelper.Build("decompile.type", scope.Stamp, typeName, caseSensitive);
            var salt = TokenHelper.MakeSalt(cacheKey);
            if (!SourcePager.TryReadOffset(continuationToken, salt, out var offset))
                return SourcePager.InvalidToken();

            var envelope = middleware.Execute(cacheKey, () =>
            {
                using var lease = contexts.Acquire(assemblyPath, scope.SearchDirectories);
                var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var type = TypeNameResolver.Resolve(lease.Assembly, typeName, comparison).OrThrowIfAmbiguous(typeName);
                if (type == null) return ToolErrors.TypeNotFound(lease.Context, typeName);
                if (!Equals(type.Assembly, lease.Assembly)) return TypeForwarded(type, assemblyPath);

                return JsonHelpers.Envelope("decompile.type", new
                {
                    typeName = type.FullName ?? type.Name,
                    source = decompiler.DecompileType(assemblyPath, type.MetadataToken, scope.SearchDirectories, cancellationToken)
                });
            }, noCache);

            return SourcePager.Page(envelope, offset, maxLines, salt, "decompile_type");
        }
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "decompile type");
        }
    }

    private sealed record DependencyScope(string Stamp, string[]? SearchDirectories, string? Error)
    {
        public static DependencyScope Build(string assemblyPath, string[]? additionalAssemblies)
        {
            if (additionalAssemblies is not { Length: > 0 })
                return new DependencyScope(CacheKeyHelper.FileStamp(assemblyPath), null, null);

            var scope = AssemblyScope.BuildAndValidate(assemblyPath, additionalAssemblies);
            if (scope.Error != null)
                return new DependencyScope("", null, scope.Error);

            var directories = scope.Paths
                .Skip(1)
                .Select(Path.GetDirectoryName)
                .OfType<string>()
                .Where(directory => directory.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new DependencyScope(CacheKeyHelper.ScopeStamp(scope.Paths), directories, null);
        }
    }

    private static string? MaxLinesError(int maxLines) =>
        maxLines is < 1 or > SourcePager.MaxLinesLimit
            ? JsonHelpers.Error("InvalidArgument", $"maxLines must be between 1 and {SourcePager.MaxLinesLimit}")
            : null;

    private static string CombineOverloads(IReadOnlyList<MemberInfo> members, IReadOnlyList<string> sources) =>
        members.Count == 1
            ? sources[0]
            : string.Join("\n\n", members.Zip(sources, (m, s) => $"// {DecompilationTargets.FormatSignature(m)}\n{s}"));

    private static string TypeForwarded(Type type, string assemblyPath)
    {
        var target = type.Assembly.GetName().Name;
        var targetPath = string.IsNullOrEmpty(type.Assembly.Location) ? null : type.Assembly.Location;
        return JsonHelpers.ErrorWithGuidance(
            "TypeForwarded",
            $"'{type.FullName}' is not defined in {Path.GetFileName(assemblyPath)}; it is type-forwarded to {target}.",
            suggestion: targetPath != null
                ? "Decompile it from the assembly that defines it (recommendedParams.assemblyPath)."
                : $"Locate {target} and decompile the type from there.",
            alternativeTools: AssemblyDiscoveryTools,
            recommendedParams: new { assemblyName = target, assemblyPath = targetPath });
    }

    private static string OverloadNotFound(Type type, string memberName, string parameterTypes, IReadOnlyList<MemberInfo> named) =>
        JsonHelpers.ErrorWithGuidance(
            "OverloadNotFound",
            $"No overload of '{memberName}' on '{type.FullName ?? type.Name}' takes ({parameterTypes}).",
            suggestion: "Pick parameterTypes from one of the listed overloads, or omit parameterTypes to decompile every overload.",
            alternativeTools: OverloadDiscoveryTools,
            recommendedParams: new { overloads = named.Select(DecompilationTargets.FormatSignature).ToArray() });
}
