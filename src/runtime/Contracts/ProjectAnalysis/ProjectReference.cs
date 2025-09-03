namespace Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;

public record ProjectReference(
    string Name,
    string RelativePath,
    string FullPath
);
