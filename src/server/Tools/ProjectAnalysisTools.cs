using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Server.Shared;
using System.ComponentModel;
using System.Text.Json;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class ProjectAnalysisTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    [McpServerTool]
    [Description("Parses a .sln file and lists contained projects")]
    public static async Task<string> AnalyzeSolution(
        IProjectAnalysisService projectAnalysis,
        [Description("Path to the .sln file")] string solutionFilePath)
    {
        try
        {
            var projects = await projectAnalysis.AnalyzeSolutionFileAsync(solutionFilePath);
            return JsonHelpers.Envelope("project.solution", new { solutionFilePath, projectCount = projects.Length, projects });
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze solution: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Parses a .csproj/.vbproj/.fsproj and returns metadata, refs, and outputs")]
    public static async Task<string> AnalyzeProject(
        IProjectAnalysisService projectAnalysis,
        [Description("Path to the project file")] string projectFilePath)
    {
        try
        {
            var result = await projectAnalysis.AnalyzeProjectFileAsync(projectFilePath);
            return JsonHelpers.Envelope("project.project", result);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze project: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Computes project output folder(s) for a configuration")]
    public static async Task<string> GetProjectOutputPaths(
        IProjectAnalysisService projectAnalysis,
        [Description("Path to the project file")] string projectFilePath,
        [Description("Build configuration (e.g., Debug/Release). Optional")] string? configuration = null)
    {
        try
        {
            var paths = await projectAnalysis.GetProjectOutputPathsAsync(projectFilePath, configuration);
            return JsonHelpers.Envelope("project.outputs", new { projectFilePath, configuration, outputPaths = paths });
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to get output paths: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Resolves NuGet package references to local assembly paths (from cache)")]
    public static async Task<string> ResolvePackageReferences(
        IProjectAnalysisService projectAnalysis,
        [Description("Path to the project file")] string projectFilePath,
        [Description("Optional package name to filter")] string? packageName = null)
    {
        try
        {
            var packages = await projectAnalysis.ResolvePackageReferencesAsync(projectFilePath, packageName);
            return JsonHelpers.Envelope("project.packages", new { projectFilePath, packageName, packages });
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to resolve packages: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Scans build outputs for deps.json runtime dependencies")]
    public static async Task<string> FindDepsJsonDependencies(
        IProjectAnalysisService projectAnalysis,
        [Description("Path to the project file")] string projectFilePath,
        [Description("Build configuration, default 'Debug'")] string configuration = "Debug")
    {
        try
        {
            var deps = await projectAnalysis.FindDepsJsonFilesAsync(projectFilePath, configuration);
            return JsonHelpers.Envelope("project.deps", new { projectFilePath, configuration, dependencies = deps });
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to parse deps.json: {ex.Message}");
        }
    }
}
