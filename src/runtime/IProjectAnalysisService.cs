using Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;

namespace Sherlock.MCP.Runtime;

public interface IProjectAnalysisService
{
    Task<ProjectInfo[]> AnalyzeSolutionFileAsync(string solutionFilePath);
    Task<ProjectAnalysisResult> AnalyzeProjectFileAsync(string projectFilePath);
    Task<string[]> GetProjectOutputPathsAsync(string projectFilePath, string? configuration = null);
    Task<PackageReference[]> ResolvePackageReferencesAsync(string projectFilePath, string? packageName = null);
    Task<RuntimeDependency[]> FindDepsJsonFilesAsync(string projectFilePath, string configuration = "Debug");
}

