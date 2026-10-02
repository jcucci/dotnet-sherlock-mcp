using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Handles;
using Sherlock.MCP.Runtime.ProjectAssets;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Shared;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class PackageGraphTools
{
    private const string ToolName = "get_package_graph";
    private const string Kind = "project.packageGraph";

    [McpServerTool(Title = "Get Package Graph", ReadOnly = true, Destructive = false, OpenWorld = false, Idempotent = true)]
    [Description("Reads the restored NuGet graph from obj/project.assets.json: the exact package versions the project compiles against (direct and transitive), for one target framework and optional runtime identifier. Pass projectPath (a project file, its directory, or the assets file) or an assemblyPath/assemblyHandle from the project's bin output. Summary lists { id, version, type, direct, dependencyCount }; projection='full' adds each package's dependency ranges and resolved compile assemblies. Run 'dotnet restore' first if the assets file is missing; resolve_package_references only probes the NuGet cache for declared versions.")]
    public static string GetPackageGraph(
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        IAssemblyHandleRegistry handles,
        [Description("Project file (.csproj/.fsproj/.vbproj), its directory, or a project.assets.json. Omit when passing assemblyPath or assemblyHandle.")] string? projectPath = null,
        [Description("Assembly in the project's build output; its project.assets.json is found next to the bin folder. Omit when passing projectPath.")] string? assemblyPath = null,
        [Description("Handle returned by open_assembly; pass instead of assemblyPath")] string? assemblyHandle = null,
        [Description("Target framework alias (e.g. 'net8.0'). Default: the assembly's target framework, the only one restored, or the client is asked to choose")] string? targetFramework = null,
        [Description("Runtime identifier (e.g. 'linux-x64') when the project restores RID-specific graphs. Default: the RID-less graph")] string? runtimeIdentifier = null,
        [Description("Only list packages whose id contains this text (case-insensitive)")] string? packageIdContains = null,
        [Description("Response shape. 'summary' (default, token-lean): { id, version, type, direct, dependencyCount }. 'full': adds dependencies { id: range } and compileAssemblies (resolved paths).")] string projection = "summary",
        [Description("Maximum packages per page")] int? maxItems = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Bypass cache for this request")] bool noCache = false,
        RequestContext<CallToolRequestParams>? context = null) =>
        GetPackageGraph(
            middleware, runtimeOptions, handles, projectPath, assemblyPath, assemblyHandle, targetFramework, runtimeIdentifier,
            packageIdContains, projection, maxItems, continuationToken, noCache, ElicitationContext.From(context));

    public static string GetPackageGraph(
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        IAssemblyHandleRegistry handles,
        string? projectPath,
        string? assemblyPath,
        string? assemblyHandle,
        string? targetFramework,
        string? runtimeIdentifier,
        string? packageIdContains,
        string projection,
        int? maxItems,
        string? continuationToken,
        bool noCache,
        ElicitationContext elicitation)
    {
        try
        {
            var normalizedProjection = (projection ?? "summary").Trim().ToLowerInvariant();
            if (normalizedProjection != "summary" && normalizedProjection != "full")
                return JsonHelpers.Error("InvalidProjection", "projection must be 'summary' or 'full'");

            var located = LocateAssets(handles, projectPath, assemblyPath, assemblyHandle);
            if (located.Error != null) return located.Error;

            ProjectAssetsFile assets;
            try
            {
                assets = ProjectAssetsReader.Read(located.AssetsPath!);
            }
            catch (InvalidDataException ex)
            {
                return JsonHelpers.Error("InvalidAssetsFile", ex.Message, new { assetsPath = located.AssetsPath });
            }

            var selected = SelectTarget(assets, located.Match, targetFramework ?? elicitation.Answer(Elicitation.TfmKey), runtimeIdentifier, elicitation);
            if (selected.Error != null) return selected.Error;
            var target = selected.Target!;

            var filter = string.IsNullOrWhiteSpace(packageIdContains) ? null : packageIdContains.Trim();
            var assetsStamp = CacheKeyHelper.FileStamp(assets.AssetsPath);
            var saltSeed = CacheKeyHelper.Build("project.packageGraph.salt", assetsStamp, target.Key, filter, normalizedProjection);
            var cacheKey = CacheKeyHelper.Build(Kind, assetsStamp, target.Key, filter, normalizedProjection, maxItems, continuationToken);

            return middleware.Execute(cacheKey, () =>
            {
                var pageSize = Math.Max(1, maxItems ?? runtimeOptions.GetMaxItemsForTool(ToolName));
                var salt = TokenHelper.MakeSalt(saltSeed);
                var offset = 0;
                if (!string.IsNullOrWhiteSpace(continuationToken)
                    && (!TokenHelper.TryParse(continuationToken!, out offset, out var parsedSalt) || parsedSalt != salt || offset < 0))
                    return JsonHelpers.Error("InvalidContinuationToken", "The continuation token is invalid or expired.");

                var directDependencies = assets.DirectDependenciesFor(target.Alias);
                var directIds = directDependencies.Select(dependency => dependency.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var listed = target.Libraries
                    .Where(library => filter == null || library.Id.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(library => library.Id, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var page = listed.Skip(offset).Take(pageSize).ToArray();
                var nextOffset = offset + page.Length;
                var nextToken = nextOffset < listed.Length ? TokenHelper.Make(nextOffset, salt) : null;

                object packages = normalizedProjection == "summary"
                    ? page.Select(library => SummaryItem(library, directIds)).ToArray()
                    : page.Select(library => FullItem(assets, library, directIds)).ToArray();
                var packagesJson = JsonSerializer.Serialize(packages, JsonHelpers.DefaultOptions);
                var result = new
                {
                    assetsPath = assets.AssetsPath,
                    projectName = assets.ProjectName,
                    projectPath = assets.ProjectPath,
                    targetFramework = target.Alias,
                    runtimeIdentifier = target.RuntimeIdentifier,
                    availableTargets = assets.Targets.Select(t => t.Key).ToArray(),
                    projection = normalizedProjection,
                    packageIdContains = filter,
                    directDependencies = directDependencies.Select(dependency => new { id = dependency.Id, requested = dependency.Range }).ToArray(),
                    total = listed.Length,
                    count = page.Length,
                    nextToken,
                    pagination = PaginationMetadata.Create(listed.Length, page.Length, nextToken, packagesJson.Length),
                    packages
                };

                return ResponseSizeHelper.ValidateResponseSize(result, ToolName) ?? JsonHelpers.Envelope(Kind, result);
            }, noCache);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InputRequiredException)
        {
            return ToolErrors.FromException(ex, "read package graph", fileNotFoundCode: "AssetsFileNotFound");
        }
    }

    private static object SummaryItem(AssetsLibrary library, HashSet<string> directIds) => new
    {
        id = library.Id,
        version = library.Version,
        type = library.Type,
        direct = directIds.Contains(library.Id),
        dependencyCount = library.Dependencies.Count
    };

    private static object FullItem(ProjectAssetsFile assets, AssetsLibrary library, HashSet<string> directIds) => new
    {
        id = library.Id,
        version = library.Version,
        type = library.Type,
        direct = directIds.Contains(library.Id),
        dependencyCount = library.Dependencies.Count,
        path = library.IsProject ? assets.ResolveProjectFile(library) : library.Path,
        dependencies = library.Dependencies.ToDictionary(dependency => dependency.Id, dependency => dependency.Range, StringComparer.OrdinalIgnoreCase),
        compileAssemblies = assets.ResolvePackageAssets(library, library.CompileAssets)
    };

    private static LocatedAssets LocateAssets(IAssemblyHandleRegistry handles, string? projectPath, string? assemblyPath, string? assemblyHandle)
    {
        var hasProject = !string.IsNullOrWhiteSpace(projectPath);
        var hasAssembly = !string.IsNullOrWhiteSpace(assemblyPath) || !string.IsNullOrWhiteSpace(assemblyHandle);
        if (hasProject == hasAssembly)
            return LocatedAssets.Failed(JsonHelpers.Error("InvalidArgument", "Pass either projectPath, or assemblyPath/assemblyHandle"));

        if (hasProject)
        {
            if (!File.Exists(projectPath) && !Directory.Exists(projectPath))
                return LocatedAssets.Failed(JsonHelpers.ErrorWithGuidance(
                    "ProjectNotFound",
                    $"Project not found: {projectPath}",
                    suggestion: "Pass a project file, its directory, or its obj/project.assets.json.",
                    alternativeTools: ["analyze_solution"]));

            var assetsPath = ProjectAssetsLocator.AssetsPathForProject(projectPath!);
            return File.Exists(assetsPath) ? new LocatedAssets(assetsPath, null, null) : LocatedAssets.Failed(AssetsFileNotFound(assetsPath));
        }

        var target = AssemblyScope.ResolveTarget(handles, assemblyPath, assemblyHandle);
        if (target.Error != null) return LocatedAssets.Failed(target.Error);

        var found = ProjectAssetsLocator.FindAssetsFile(target.Path);
        return found != null
            ? new LocatedAssets(found, ProjectAssetsLocator.Locate(target.Path), null)
            : LocatedAssets.Failed(JsonHelpers.ErrorWithGuidance(
                "AssetsFileNotFound",
                $"No project.assets.json found for '{target.Path}'. Only assemblies in a project's bin output (or artifacts/bin) map to an assets file.",
                suggestion: "Pass projectPath instead, or run 'dotnet restore' on the project.",
                alternativeTools: ["get_project_output_paths", "resolve_package_references"]));
    }

    private static string AssetsFileNotFound(string assetsPath) =>
        JsonHelpers.ErrorWithGuidance(
            "AssetsFileNotFound",
            $"Assets file not found: {assetsPath}",
            suggestion: "Run 'dotnet restore' on the project, then retry.",
            alternativeTools: ["resolve_package_references", "analyze_project"],
            details: new { assetsPath });

    private static SelectedTarget SelectTarget(
        ProjectAssetsFile assets,
        ProjectAssetsMatch? match,
        string? targetFramework,
        string? runtimeIdentifier,
        ElicitationContext elicitation)
    {
        var aliases = assets.Aliases;
        if (aliases.Count == 0)
            return SelectedTarget.Failed(JsonHelpers.Error("InvalidAssetsFile", $"'{assets.AssetsPath}' contains no restored targets."));

        var alias = targetFramework?.Trim() ?? match?.Target.Alias ?? (aliases.Count == 1 ? aliases[0] : null);
        if (alias == null)
        {
            if (elicitation.CanElicit && !elicitation.HasResponse(Elicitation.TfmKey))
                throw Elicitation.ChooseOne(
                    key: Elicitation.TfmKey,
                    message: $"'{assets.ProjectName ?? assets.AssetsPath}' restores {aliases.Count} target frameworks. Which graph should be shown?",
                    options: aliases);

            return SelectedTarget.Failed(JsonHelpers.ErrorWithGuidance(
                "AmbiguousTargetFramework",
                $"The project restores {aliases.Count} target frameworks; pass targetFramework.",
                suggestion: "Retry with one of the candidates as targetFramework.",
                recommendedParams: new { candidates = aliases }));
        }

        var rid = string.IsNullOrWhiteSpace(runtimeIdentifier)
            ? match is { } located && located.Target.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase) ? located.Target.RuntimeIdentifier : null
            : runtimeIdentifier.Trim();
        return assets.FindTarget(alias, rid) is { } target
            ? new SelectedTarget(target, null)
            : SelectedTarget.Failed(JsonHelpers.ErrorWithGuidance(
                "TargetFrameworkNotFound",
                $"'{(rid == null ? alias : $"{alias}/{rid}")}' is not a restored target in '{assets.AssetsPath}'.",
                suggestion: "Retry with one of the restored targets (targetFramework, plus runtimeIdentifier for 'tfm/rid' entries).",
                recommendedParams: new { candidates = assets.Targets.Select(t => t.Key).ToArray() }));
    }

    private sealed record LocatedAssets(string? AssetsPath, ProjectAssetsMatch? Match, string? Error)
    {
        public static LocatedAssets Failed(string error) => new(null, null, error);
    }

    private sealed record SelectedTarget(AssetsTarget? Target, string? Error)
    {
        public static SelectedTarget Failed(string error) => new(null, error);
    }
}
