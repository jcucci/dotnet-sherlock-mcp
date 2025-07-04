namespace Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;
public record ProjectInfo(
    string Name,
    string RelativePath,
    string FullPath,
    string ProjectGuid,
    string ProjectTypeGuid
);