namespace Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;

/// <summary>
/// Represents information about a project found in a solution file.
/// </summary>
public record ProjectInfo(
    string Name,
    string RelativePath,
    string FullPath,
    string ProjectGuid,
    string ProjectTypeGuid
);