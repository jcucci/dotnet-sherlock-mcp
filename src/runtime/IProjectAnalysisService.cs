using Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;

namespace Sherlock.MCP.Runtime;

public interface IProjectAnalysisService
{
    public string EvaluationMode { get; }
    public Task<ProjectInfo[]> AnalyzeSolutionFileAsync(string solutionFilePath, CancellationToken cancellationToken = default);
    public Task<ProjectAnalysisResult> AnalyzeProjectFileAsync(string projectFilePath, CancellationToken cancellationToken = default);
    public Task<ProjectOutputPaths> GetProjectOutputPathsAsync(string projectFilePath, string? configuration = null, CancellationToken cancellationToken = default);
    public Task<ResolvedPackageReferences> ResolvePackageReferencesAsync(string projectFilePath, string? packageName = null, CancellationToken cancellationToken = default);
    public Task<RuntimeDependency[]> FindDepsJsonFilesAsync(string projectFilePath, string configuration = "Debug", CancellationToken cancellationToken = default);
    public Task<RuntimeDependency[]> ReadDepsJsonFilesAsync(string projectFilePath, IReadOnlyList<string> outputPaths, CancellationToken cancellationToken = default);
    public Task<NugetAssemblyLookup> FindAssemblyInNugetCacheAsync(string packageId, string? version = null, string? tfm = null);
}

