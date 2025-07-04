namespace Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;
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