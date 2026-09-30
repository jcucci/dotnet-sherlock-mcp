using System.Text.Json;
using ModelContextProtocol;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Server.Resources;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

[Collection(nameof(EnvVarCollection))]
public class NuGetResourcesTests
{
    private readonly IProjectAnalysisService _projectAnalysis = new ProjectAnalysisService();

    [Fact]
    public async Task ReadPackage_MatchesFindAssemblyByNugetPackageTool()
    {
        using var cache = new TempDir();
        var libDir = Path.Combine(cache.Path, "sharp.events", "41.0.1", "lib", "net9.0");
        Directory.CreateDirectory(libDir);
        File.WriteAllText(Path.Combine(libDir, "Sharp.Events.dll"), "");
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        var resource = await NuGetResources.ReadPackage(_projectAnalysis, "Sharp.Events", "41.0.1");
        var tool = await ReflectionTools.FindAssemblyByNugetPackage(_projectAnalysis, "Sharp.Events", "41.0.1");

        Assert.Equal(tool, resource);
        Assert.Equal("reflection.findByNugetPackage", JsonDocument.Parse(resource).RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task ReadPackage_UnknownPackage_ThrowsInvalidParams()
    {
        using var cache = new TempDir();
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        var ex = await Assert.ThrowsAsync<McpProtocolException>(() => NuGetResources.ReadPackage(_projectAnalysis, "Missing.Package", "1.0.0"));

        Assert.Equal(McpErrorCode.InvalidParams, ex.ErrorCode);
    }

    [Fact]
    public async Task ReadPackage_TraversalVersion_ThrowsInvalidParams()
    {
        var ex = await Assert.ThrowsAsync<McpProtocolException>(() => NuGetResources.ReadPackage(_projectAnalysis, "Some.Package", ".."));

        Assert.Equal(McpErrorCode.InvalidParams, ex.ErrorCode);
    }
}
