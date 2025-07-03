namespace Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;

/// <summary>
/// Contains the results of analyzing a project file.
/// </summary>
public record ProjectAnalysisResult(
    string ProjectName,
    string TargetFramework,
    string[] TargetFrameworks,
    string OutputType,
    string AssemblyName,
    string RootNamespace,
    ProjectReference[] ProjectReferences,
    PackageReference[] PackageReferences,
    string[] OutputPaths
);