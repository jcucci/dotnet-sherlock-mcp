namespace Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;

public record RuntimeDependency(
    string Name,
    string Version,
    string Type,
    string AssemblyPath,
    string[] Dependencies
);
