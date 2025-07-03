namespace Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;

/// <summary>
/// Represents a reference to another project.
/// </summary>
public record ProjectReference(
    string Name,
    string RelativePath,
    string FullPath
);