using System.Text.Json;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

[Collection(nameof(EnvVarCollection))]
public class NuGetTfmSelectionTests
{
    private readonly IProjectAnalysisService _projectAnalysis = new ProjectAnalysisService();

    [Fact]
    public async Task FindAssemblyByNugetPackage_MultipleTfms_WithoutElicitation_AutoPicksAndListsAlternatives()
    {
        using var cache = CreatePackage("netstandard2.0", "net8.0", "net10.0");
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        var data = Data(await ReflectionTools.FindAssemblyByNugetPackage(_projectAnalysis, "Multi.Tfm"));

        Assert.Equal("net10.0", data.GetProperty("resolvedTfm").GetString());
        Assert.Equal(["net10.0", "net8.0", "netstandard2.0"], Strings(data.GetProperty("availableTfms")));
        Assert.Equal(["1.0.0"], Strings(data.GetProperty("availableVersions")));
    }

    [Fact]
    public async Task FindAssemblyInNugetCache_TfmFolderWithoutDll_IsNotOffered()
    {
        using var cache = CreatePackage("net8.0");
        Directory.CreateDirectory(Path.Combine(cache.Path, "multi.tfm", "1.0.0", "lib", "net10.0"));
        File.WriteAllText(Path.Combine(cache.Path, "multi.tfm", "1.0.0", "lib", "net10.0", "_._"), "");
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        var lookup = await _projectAnalysis.FindAssemblyInNugetCacheAsync("Multi.Tfm");

        Assert.Equal(["net8.0"], lookup.AvailableTfms);
        Assert.Equal("net8.0", lookup.ResolvedTfm);
        Assert.NotNull(lookup.FoundAssembly);
    }

    private static TempDir CreatePackage(params string[] tfms)
    {
        var cache = new TempDir();
        foreach (var tfm in tfms)
        {
            var libDir = Path.Combine(cache.Path, "multi.tfm", "1.0.0", "lib", tfm);
            Directory.CreateDirectory(libDir);
            File.WriteAllText(Path.Combine(libDir, "Multi.Tfm.dll"), "");
        }
        return cache;
    }

    private static JsonElement Data(string json) => JsonDocument.Parse(json).RootElement.GetProperty("data");

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToArray();
}
