namespace Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;

/// <summary>
/// Represents a NuGet package reference with resolved assembly paths.
/// </summary>
public record PackageReference(
    string Name,
    string Version,
    string[] AssemblyPaths,
    bool IsResolved
);