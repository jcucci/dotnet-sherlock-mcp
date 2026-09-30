using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
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
        [Description("Path to the .sln file")] string solutionFilePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projects = await projectAnalysis.AnalyzeSolutionFileAsync(solutionFilePath, cancellationToken);
            return JsonHelpers.Envelope("project.solution", new { solutionFilePath, projectCount = projects.Length, projects });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "analyze solution", fileNotFoundCode: "ProjectNotFound");
        }
    }

    [McpServerTool(Title = "Analyze Project", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Parses a project file (.csproj/.vbproj/.fsproj) returning target framework, package refs, project refs, and output paths. Use get_project_output_paths to find compiled assemblies.")]
    public static async Task<string> AnalyzeProject(
        IProjectAnalysisService projectAnalysis,
        [Description("Path to the project file")] string projectFilePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await projectAnalysis.AnalyzeProjectFileAsync(projectFilePath, cancellationToken);
            return JsonHelpers.Envelope("project.project", result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "analyze project", fileNotFoundCode: "ProjectNotFound");
        }
    }

    [McpServerTool(Title = "Get Project Output Paths", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets compiled assembly output paths for a project by configuration. Use to find DLL paths for assembly analysis tools. Lightweight response.")]
    public static async Task<string> GetProjectOutputPaths(
        IProjectAnalysisService projectAnalysis,
        [Description("Path to the project file")] string projectFilePath,
        [Description("Build configuration (e.g., Debug/Release). Optional")] string? configuration = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var paths = await projectAnalysis.GetProjectOutputPathsAsync(projectFilePath, configuration, cancellationToken);
            return JsonHelpers.Envelope("project.outputs", new { projectFilePath, configuration, outputPaths = paths });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "get output paths", fileNotFoundCode: "ProjectNotFound");
        }
    }

    [McpServerTool(Title = "Resolve Package References", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Resolves NuGet package references to local assembly paths from NuGet cache. Use packageName filter to find specific packages. Returns paths for assembly analysis.")]
    public static async Task<string> ResolvePackageReferences(
        IProjectAnalysisService projectAnalysis,
        [Description("Path to the project file")] string projectFilePath,
        [Description("Optional package name to filter")] string? packageName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var packages = await projectAnalysis.ResolvePackageReferencesAsync(projectFilePath, packageName, cancellationToken);
            return JsonHelpers.Envelope("project.packages", new { projectFilePath, packageName, packages });
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
        [Description("Path to the project file")] string projectFilePath,
        [Description("Build configuration, default 'Debug'")] string configuration = "Debug",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var deps = await projectAnalysis.FindDepsJsonFilesAsync(projectFilePath, configuration, cancellationToken);
            return JsonHelpers.Envelope("project.deps", new { projectFilePath, configuration, dependencies = deps });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "parse deps.json", fileNotFoundCode: "FileNotFound");
        }
    }
}
