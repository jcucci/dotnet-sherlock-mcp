using System.Text.RegularExpressions;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;
using Sherlock.MCP.Runtime.Handles;

namespace Sherlock.MCP.Server.Shared;

internal sealed record ApiDiffSide(
    string Source,
    string AssemblyPath,
    string[]? AdditionalAssemblies,
    string? PackageId,
    string? PackageVersion,
    string? Tfm,
    IReadOnlyList<string> AvailableTfms);

internal sealed record ApiDiffSideResult(ApiDiffSide? Side, string? Error);

internal static partial class ApiDiffSideResolver
{
    public const string PathSource = "path";
    public const string HandleSource = "handle";
    public const string NuGetSource = "nuget";

    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.\-]*$")]
    private static partial Regex PackageIdPattern();

    public static async Task<ApiDiffSideResult> ResolveAsync(
        string side, string? input, string? tfm, IAssemblyHandleRegistry handles, IProjectAnalysisService projects)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Failed(JsonHelpers.Error("InvalidArgument", $"{side} is required: an assembly path, an asm_ handle, or packageId@version"));

        var trimmed = input.Trim();
        if (trimmed.StartsWith("asm_", StringComparison.Ordinal))
        {
            var target = AssemblyScope.ResolveTarget(handles, assemblyPath: null, assemblyHandle: trimmed);
            return target.Error != null
                ? Failed(target.Error)
                : new(new ApiDiffSide(HandleSource, target.Path, target.AdditionalAssemblies, null, null, null, []), null);
        }
        if (File.Exists(trimmed))
            return new(new ApiDiffSide(PathSource, trimmed, null, null, null, null, []), null);
        if (TryParsePackage(trimmed, out var packageId, out var version))
            return await ResolvePackageAsync(side, packageId, version, tfm, projects);

        return Failed(JsonHelpers.ErrorWithGuidance(
            "AssemblyNotFound",
            $"{side}: '{trimmed}' is not an existing assembly file, an asm_ handle, or a packageId@version",
            suggestion: "Pass an assembly path, a handle from open_assembly, or a NuGet package as 'Package.Id@1.2.3'.",
            alternativeTools: ["find_assembly_by_nuget_package", "find_assembly_by_file_name", "open_assembly"],
            details: new { side, input = trimmed }));
    }

    internal static bool TryParsePackage(string input, out string packageId, out string? version)
    {
        var at = input.LastIndexOf('@');
        packageId = at < 0 ? input : input[..at];
        version = at < 0 || at == input.Length - 1 ? null : input[(at + 1)..];
        if (!PackageIdPattern().IsMatch(packageId)) return false;
        if (at >= 0) return true;
        return !packageId.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            && !packageId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<(ApiDiffSide Left, ApiDiffSide Right, string? Warning)> AlignTfmsAsync(
        ApiDiffSide left, ApiDiffSide right, IProjectAnalysisService projects)
    {
        if (left.Source != NuGetSource || right.Source != NuGetSource || string.Equals(left.Tfm, right.Tfm, StringComparison.OrdinalIgnoreCase))
            return (left, right, null);

        var warning = $"The packages share no target framework; comparing {left.Tfm} with {right.Tfm}. Pass tfm to choose one.";
        var shared = left.AvailableTfms.Intersect(right.AvailableTfms, StringComparer.OrdinalIgnoreCase);
        if (ProjectAnalysisService.PickBestTargetFramework(shared) is not { } best)
            return (left, right, warning);

        var retargetedLeft = await RetargetAsync(nameof(left), left, best, projects);
        var retargetedRight = await RetargetAsync(nameof(right), right, best, projects);
        return retargetedLeft != null && retargetedRight != null
            ? (retargetedLeft, retargetedRight, null)
            : (left, right, warning);
    }

    private static async Task<ApiDiffSide?> RetargetAsync(string side, ApiDiffSide package, string tfm, IProjectAnalysisService projects) =>
        string.Equals(package.Tfm, tfm, StringComparison.OrdinalIgnoreCase)
            ? package
            : (await ResolvePackageAsync(side, package.PackageId!, package.PackageVersion, tfm, projects)).Side;

    private static async Task<ApiDiffSideResult> ResolvePackageAsync(
        string side, string packageId, string? version, string? tfm, IProjectAnalysisService projects)
    {
        NugetAssemblyLookup lookup;
        try
        {
            lookup = await projects.FindAssemblyInNugetCacheAsync(packageId, version, tfm);
        }
        catch (ArgumentException ex)
        {
            return Failed(JsonHelpers.Error("InvalidArgument", $"{side}: {ex.Message}", new { side }));
        }

        if (lookup.Failure is NugetLookupFailure failure)
            return Failed(JsonHelpers.Error(
                NuGetLookupResponse.FailureCode(failure),
                $"{side}: {NuGetLookupResponse.FailureMessage(lookup, failure)}",
                new { side, lookup = NuGetLookupResponse.FailureDetails(lookup) }));

        return new(new ApiDiffSide(NuGetSource, lookup.FoundAssembly!, null, lookup.PackageId, lookup.ResolvedVersion, lookup.ResolvedTfm, lookup.AvailableTfms), null);
    }

    private static ApiDiffSideResult Failed(string error) => new(null, error);
}
