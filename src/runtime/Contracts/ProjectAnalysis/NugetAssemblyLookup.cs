namespace Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;

public record NugetAssemblyLookup(
    string PackageId,
    string? RequestedVersion,
    string? RequestedTfm,
    string? ResolvedVersion,
    string? ResolvedTfm,
    string CacheRoot,
    string? FoundAssembly,
    string[] AvailableVersions,
    string[] AvailableTfms,
    NugetLookupFailure? Failure
);

public enum NugetLookupFailure
{
    PackageNotFound,
    VersionNotFound,
    AssemblyNotFound
}
