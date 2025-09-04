using Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;

namespace Sherlock.MCP.Runtime;

public interface IProjectAnalysisService
{
    public Task<ProjectInfo[]> AnalyzeSolutionFileAsync(string solutionFilePath);
    public Task<ProjectAnalysisResult> AnalyzeProjectFileAsync(string projectFilePath);
    public Task<string[]> GetProjectOutputPathsAsync(string projectFilePath, string? configuration = null);
    public Task<PackageReference[]> ResolvePackageReferencesAsync(string projectFilePath, string? packageName = null);
    public Task<RuntimeDependency[]> FindDepsJsonFilesAsync(string projectFilePath, string configuration = "Debug");
}

