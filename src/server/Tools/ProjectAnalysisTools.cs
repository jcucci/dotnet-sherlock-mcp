using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Shared;
using System.ComponentModel;
using System.Text.Json;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class ProjectAnalysisTools
{
    [McpServerTool(Title = "Analyze Solution", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Parses a .sln file and lists all contained projects with paths. Use as entry point to discover project structure before analyze_project. Lightweight response.")]
    public static async Task<string> AnalyzeSolution(
        IProjectAnalysisService projectAnalysis,
        ToolMiddleware middleware,
        [Description("Path to the .sln file")] string solutionFilePath,
        [Description("Bypass cache for this request")] bool noCache = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = CacheKeyHelper.Build("project.solution", CacheKeyHelper.FileStamp(solutionFilePath));
            return await middleware.ExecuteAsync(cacheKey, async () =>
            {
                var projects = await projectAnalysis.AnalyzeSolutionFileAsync(solutionFilePath, cancellationToken);
                return JsonHelpers.Envelope("project.solution", new { solutionFilePath, projectCount = projects.Length, projects });
            }, noCache);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "analyze solution", fileNotFoundCode: "ProjectNotFound");
        }
    }

    [McpServerTool(Title = "Analyze Project", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Evaluates a project file (.csproj/.vbproj/.fsproj) with the installed .NET SDK's MSBuild, honouring Directory.Build.props, central package management and conditions, and returns target frameworks, package refs, project refs and output paths. Falls back to XML parsing when no SDK is available; evaluation.mode says which was used. Use get_project_output_paths to find compiled assemblies.")]
    public static async Task<string> AnalyzeProject(
        IProjectAnalysisService projectAnalysis,
        ToolMiddleware middleware,
        [Description("Path to the project file")] string projectFilePath,
        [Description("Bypass cache for this request")] bool noCache = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = CacheKeyHelper.Build("project.project", CacheKeyHelper.ProjectStamp(projectFilePath), projectAnalysis.EvaluationMode);
            return await middleware.ExecuteWhenCacheableAsync(cacheKey, async () =>
            {
                var result = await projectAnalysis.AnalyzeProjectFileAsync(projectFilePath, cancellationToken);
                return (JsonHelpers.Envelope("project.project", result), IsCacheable(projectAnalysis, result.Evaluation));
            }, noCache);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "analyze project", fileNotFoundCode: "ProjectNotFound");
        }
    }

    [McpServerTool(Title = "Get Project Output Paths", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets compiled assembly output directories for a project by configuration and target framework, evaluated with MSBuild (custom OutputPath, artifacts layout, Directory.Build.props) or parsed from XML when no SDK is available. Use to find DLL paths for assembly analysis tools. Lightweight response.")]
    public static async Task<string> GetProjectOutputPaths(
        IProjectAnalysisService projectAnalysis,
        ToolMiddleware middleware,
        [Description("Path to the project file")] string projectFilePath,
        [Description("Build configuration (e.g., Debug/Release). Optional")] string? configuration = null,
        [Description("Bypass cache for this request")] bool noCache = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await OutputPathsEnvelopeAsync(projectAnalysis, middleware, projectFilePath, configuration, noCache, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "get output paths", fileNotFoundCode: "ProjectNotFound");
        }
    }

    [McpServerTool(Title = "Resolve Package References", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Resolves NuGet package references to local assembly paths from NuGet cache. Use packageName filter to find specific packages. Returns paths for assembly analysis. Versions come from the MSBuild-evaluated project, including central package management. Probes the cache for declared versions only; use get_package_graph for the exact restored (including transitive) versions.")]
    public static async Task<string> ResolvePackageReferences(
        IProjectAnalysisService projectAnalysis,
        [Description("Path to the project file")] string projectFilePath,
        [Description("Optional package name to filter")] string? packageName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var resolved = await projectAnalysis.ResolvePackageReferencesAsync(projectFilePath, packageName, cancellationToken);
            return JsonHelpers.Envelope("project.packages", new { projectFilePath, packageName, packages = resolved.Packages, evaluation = resolved.Evaluation });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "resolve packages", fileNotFoundCode: "ProjectNotFound");
        }
    }

    [McpServerTool(Title = "Find deps.json Dependencies", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Parses deps.json from build output to list all runtime dependencies including transitive refs. Useful for understanding full dependency graph.")]
    public static async Task<string> FindDepsJsonDependencies(
        IProjectAnalysisService projectAnalysis,
        ToolMiddleware middleware,
        [Description("Path to the project file")] string projectFilePath,
        [Description("Build configuration, default 'Debug'")] string configuration = "Debug",
        [Description("Bypass cache for this request")] bool noCache = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var outputPaths = OutputPathsFrom(await OutputPathsEnvelopeAsync(projectAnalysis, middleware, projectFilePath, configuration, noCache, cancellationToken));
            var depsFileName = $"{Path.GetFileNameWithoutExtension(projectFilePath)}.deps.json";
            var depsStamp = CacheKeyHelper.ScopeStamp(outputPaths.Select(p => Path.Combine(p, depsFileName)));
            var cacheKey = CacheKeyHelper.Build("project.deps", CacheKeyHelper.ProjectStamp(projectFilePath), projectAnalysis.EvaluationMode, configuration, depsStamp);
            return await middleware.ExecuteAsync(cacheKey, async () =>
            {
                var deps = await projectAnalysis.ReadDepsJsonFilesAsync(projectFilePath, outputPaths, cancellationToken);
                return JsonHelpers.Envelope("project.deps", new { projectFilePath, configuration, dependencies = deps });
            }, noCache);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "parse deps.json", fileNotFoundCode: "FileNotFound");
        }
    }

    private static Task<string> OutputPathsEnvelopeAsync(
        IProjectAnalysisService projectAnalysis,
        ToolMiddleware middleware,
        string projectFilePath,
        string? configuration,
        bool noCache,
        CancellationToken cancellationToken)
    {
        var cacheKey = CacheKeyHelper.Build("project.outputs", CacheKeyHelper.ProjectStamp(projectFilePath), projectAnalysis.EvaluationMode, configuration);
        return middleware.ExecuteWhenCacheableAsync(cacheKey, async () =>
        {
            var outputs = await projectAnalysis.GetProjectOutputPathsAsync(projectFilePath, configuration, cancellationToken);
            var envelope = JsonHelpers.Envelope("project.outputs", new { projectFilePath, configuration, outputPaths = outputs.Paths, evaluation = outputs.Evaluation }, ToolHints.ForProjectOutputs());
            return (envelope, IsCacheable(projectAnalysis, outputs.Evaluation));
        }, noCache);
    }

    private static string[] OutputPathsFrom(string outputPathsEnvelope)
    {
        using var document = JsonDocument.Parse(outputPathsEnvelope);
        return document.RootElement.GetProperty("data").GetProperty("outputPaths")
            .EnumerateArray()
            .Select(path => path.GetString())
            .OfType<string>()
            .ToArray();
    }

    private static bool IsCacheable(IProjectAnalysisService projectAnalysis, ProjectEvaluationInfo evaluation) =>
        evaluation.Mode == projectAnalysis.EvaluationMode;
}
