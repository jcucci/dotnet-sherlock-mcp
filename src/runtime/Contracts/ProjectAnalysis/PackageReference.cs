namespace Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;

public record PackageReference(
    string Name,
    string Version,
    string[] AssemblyPaths,
    bool IsResolved
);
