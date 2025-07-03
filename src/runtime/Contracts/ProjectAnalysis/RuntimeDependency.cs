namespace Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;

/// <summary>
/// Represents a runtime dependency from a .deps.json file.
/// </summary>
public record RuntimeDependency(
    string Name,
    string Version,
    string Type,
    string AssemblyPath,
    string[] Dependencies
);