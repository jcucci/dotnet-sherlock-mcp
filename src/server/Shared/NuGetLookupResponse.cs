using Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;

namespace Sherlock.MCP.Server.Shared;

public static class NuGetLookupResponse
{
    public const string Kind = "reflection.findByNugetPackage";

    public static object Success(NugetAssemblyLookup lookup) => new
    {
        packageId = lookup.PackageId,
        requestedVersion = lookup.RequestedVersion,
        requestedTfm = lookup.RequestedTfm,
        resolvedVersion = lookup.ResolvedVersion,
        resolvedTfm = lookup.ResolvedTfm,
        cacheRoot = lookup.CacheRoot,
        foundAssembly = lookup.FoundAssembly
    };

    public static string FailureCode(NugetLookupFailure failure) => failure switch
    {
        NugetLookupFailure.PackageNotFound => "PackageNotFound",
        NugetLookupFailure.VersionNotFound => "VersionNotFound",
        _ => "AssemblyNotFound"
    };

    public static string FailureMessage(NugetAssemblyLookup lookup, NugetLookupFailure failure) => failure switch
    {
        NugetLookupFailure.PackageNotFound => $"Package '{lookup.PackageId}' not found under NuGet cache '{lookup.CacheRoot}'.",
        NugetLookupFailure.VersionNotFound => lookup.RequestedVersion is null
            ? $"No versions available for package '{lookup.PackageId}' under '{lookup.CacheRoot}'."
            : $"Version '{lookup.RequestedVersion}' not found for package '{lookup.PackageId}'.",
        _ => $"No compatible assembly found for '{lookup.PackageId}' {lookup.ResolvedVersion} (tfm: {lookup.RequestedTfm ?? "any"})."
    };

    public static object FailureDetails(NugetAssemblyLookup lookup) => new
    {
        packageId = lookup.PackageId,
        requestedVersion = lookup.RequestedVersion,
        requestedTfm = lookup.RequestedTfm,
        resolvedVersion = lookup.ResolvedVersion,
        cacheRoot = lookup.CacheRoot,
        availableVersions = lookup.AvailableVersions,
        availableTfms = lookup.AvailableTfms
    };
}
