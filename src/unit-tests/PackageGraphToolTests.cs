using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Shared;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

public sealed class PackageGraphToolTests : IDisposable
{
    private readonly AssetsFixture _fixture = new();

    public PackageGraphToolTests() => ProjectAssetsTests.WriteStandardAssets(_fixture);

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Summary_ListsRestoredPackagesForTargetFramework()
    {
        var data = Data(Call(projectPath: _fixture.ProjectFile, targetFramework: "net8.0"));

        Assert.Equal("net8.0", data.GetProperty("targetFramework").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("runtimeIdentifier").ValueKind);
        Assert.Equal(3, data.GetProperty("total").GetInt32());
        var lib = Package(data, "Fake.Lib");
        Assert.Equal("1.0.0", lib.GetProperty("version").GetString());
        Assert.True(lib.GetProperty("direct").GetBoolean());
        Assert.Equal(1, lib.GetProperty("dependencyCount").GetInt32());
        Assert.False(Package(data, "Fake.Dep").GetProperty("direct").GetBoolean());
        Assert.Equal("project", Package(data, "Ref").GetProperty("type").GetString());
        Assert.False(lib.TryGetProperty("compileAssemblies", out _));
        Assert.Equal("[1.0.0, )", data.GetProperty("directDependencies")[0].GetProperty("requested").GetString());
    }

    [Fact]
    public void Full_AddsDependenciesAndCompileAssemblies()
    {
        var data = Data(Call(projectPath: _fixture.ProjectDirectory, targetFramework: "net8.0", projection: "full"));

        var lib = Package(data, "Fake.Lib");
        Assert.Equal("1.0.0", lib.GetProperty("dependencies").GetProperty("Fake.Dep").GetString());
        var compile = Assert.Single(lib.GetProperty("compileAssemblies").EnumerateArray());
        Assert.Contains(Path.Combine("fake.lib", "1.0.0"), compile.GetString(), StringComparison.Ordinal);
        Assert.Empty(Package(data, "Fake.Dep").GetProperty("compileAssemblies").EnumerateArray());
    }

    [Fact]
    public void RuntimeIdentifier_SelectsRidGraph()
    {
        var data = Data(Call(projectPath: _fixture.AssetsPath, targetFramework: "net8.0", runtimeIdentifier: "linux-x64"));

        Assert.Equal("linux-x64", data.GetProperty("runtimeIdentifier").GetString());
        Assert.Equal("Fake.Native", Package(data, "Fake.Native").GetProperty("id").GetString());
    }

    [Fact]
    public void AssemblyPath_UsesTargetOfItsOutputFolder()
    {
        var dll = Path.Combine(_fixture.BinDirectory("Debug", "net10.0"), "Sample.dll");
        File.WriteAllBytes(dll, []);

        var data = Data(Call(assemblyPath: dll));

        Assert.Equal("net10.0", data.GetProperty("targetFramework").GetString());
        Assert.Equal(1, data.GetProperty("total").GetInt32());
    }

    [Fact]
    public void FilterAndPagination_PageThroughMatches()
    {
        var first = Data(Call(projectPath: _fixture.ProjectFile, targetFramework: "net8.0", packageIdContains: "fake", maxItems: 1));
        var token = first.GetProperty("nextToken").GetString();
        var second = Data(Call(projectPath: _fixture.ProjectFile, targetFramework: "net8.0", packageIdContains: "fake", maxItems: 1, continuationToken: token));

        Assert.Equal(2, first.GetProperty("total").GetInt32());
        Assert.Equal("Fake.Dep", first.GetProperty("packages")[0].GetProperty("id").GetString());
        Assert.Equal("Fake.Lib", second.GetProperty("packages")[0].GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextToken").ValueKind);
    }

    [Fact]
    public void SeveralTargets_WithoutTargetFramework_ReturnsCandidates()
    {
        using var json = JsonDocument.Parse(Call(projectPath: _fixture.ProjectFile));

        Assert.Equal("AmbiguousTargetFramework", json.RootElement.GetProperty("code").GetString());
        var candidates = json.RootElement.GetProperty("recommendedParams").GetProperty("candidates").EnumerateArray().Select(c => c.GetString());
        Assert.Equal(["net8.0", "net10.0"], candidates);
    }

    [Fact]
    public void SeveralTargets_ClientCanElicit_AsksForTargetFramework()
    {
        var elicitation = new ElicitationContext(CanElicit: true, Responses: null);

        var ex = Assert.Throws<InputRequiredException>(() => Call(projectPath: _fixture.ProjectFile, elicitation: elicitation));

        var request = ex.Result.InputRequests![Elicitation.TfmKey].ElicitationParams!;
        var schema = Assert.IsType<ElicitRequestParams.UntitledSingleSelectEnumSchema>(request.RequestedSchema!.Properties[Elicitation.TfmKey]);
        Assert.Equal(["net8.0", "net10.0"], schema.Enum);
    }

    [Fact]
    public void ElicitedTargetFramework_IsUsedOnRetry()
    {
        var answer = new ElicitResult { Action = "accept", Content = new Dictionary<string, JsonElement> { [Elicitation.TfmKey] = JsonSerializer.SerializeToElement("net10.0") } };
        var elicitation = new ElicitationContext(CanElicit: true, Responses: new Dictionary<string, InputResponse> { [Elicitation.TfmKey] = InputResponse.FromElicitResult(answer) });

        var data = Data(Call(projectPath: _fixture.ProjectFile, elicitation: elicitation));

        Assert.Equal("net10.0", data.GetProperty("targetFramework").GetString());
    }

    [Fact]
    public void UnknownTargetFramework_ReturnsRestoredTargets()
    {
        using var json = JsonDocument.Parse(Call(projectPath: _fixture.ProjectFile, targetFramework: "net6.0"));

        Assert.Equal("TargetFrameworkNotFound", json.RootElement.GetProperty("code").GetString());
        Assert.Contains("net8.0/linux-x64", json.RootElement.GetProperty("recommendedParams").GetProperty("candidates").EnumerateArray().Select(c => c.GetString()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ArtifactsLayout_ProjectInput_FindsArtifactsAssets(bool passDirectory)
    {
        var artifactsAssets = Path.Combine(_fixture.Root, "artifacts", "obj", "Sample", "project.assets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(artifactsAssets)!);
        File.Move(_fixture.AssetsPath, artifactsAssets);

        var data = Data(Call(projectPath: passDirectory ? _fixture.ProjectDirectory : _fixture.ProjectFile, targetFramework: "net8.0"));

        Assert.Equal(artifactsAssets, data.GetProperty("assetsPath").GetString());
        Assert.Equal(3, data.GetProperty("total").GetInt32());
    }

    [Fact]
    public void MissingAssetsFile_SuggestsRestore()
    {
        File.Delete(_fixture.AssetsPath);

        using var json = JsonDocument.Parse(Call(projectPath: _fixture.ProjectFile, targetFramework: "net8.0"));

        Assert.Equal("AssetsFileNotFound", json.RootElement.GetProperty("code").GetString());
        Assert.Contains("dotnet restore", json.RootElement.GetProperty("suggestion").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedAssetsFile_ReturnsInvalidAssetsFile()
    {
        File.WriteAllText(_fixture.AssetsPath, "[");

        using var json = JsonDocument.Parse(Call(projectPath: _fixture.ProjectFile, targetFramework: "net8.0"));

        Assert.Equal("InvalidAssetsFile", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void ProjectAndAssemblyTogether_IsInvalid()
    {
        using var json = JsonDocument.Parse(Call(projectPath: _fixture.ProjectFile, assemblyPath: _fixture.ProjectFile));

        Assert.Equal("InvalidArgument", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void RepeatedCall_IsCachedUntilRestored()
    {
        var middleware = TestMiddleware.Fresh;

        var first = Call(projectPath: _fixture.ProjectFile, targetFramework: "net8.0", middleware: middleware);
        _fixture.WriteAssets(
            new JsonObject { ["net8.0"] = new JsonObject() },
            new JsonObject(),
            new JsonObject { ["net8.0"] = AssetsFixture.Framework("net8.0") });
        File.SetLastWriteTimeUtc(_fixture.AssetsPath, DateTime.UtcNow.AddMinutes(1));
        var second = Call(projectPath: _fixture.ProjectFile, targetFramework: "net8.0", middleware: middleware);

        Assert.Equal(3, Data(first).GetProperty("total").GetInt32());
        Assert.Equal(0, Data(second).GetProperty("total").GetInt32());
    }

    private static string Call(
        string? projectPath = null,
        string? assemblyPath = null,
        string? targetFramework = null,
        string? runtimeIdentifier = null,
        string? packageIdContains = null,
        string projection = "summary",
        int? maxItems = null,
        string? continuationToken = null,
        ToolMiddleware? middleware = null,
        ElicitationContext? elicitation = null) =>
        PackageGraphTools.GetPackageGraph(
            middleware ?? TestMiddleware.Fresh,
            new RuntimeOptions(),
            TestHandles.Registry,
            projectPath: projectPath,
            assemblyPath: assemblyPath,
            assemblyHandle: null,
            targetFramework: targetFramework,
            runtimeIdentifier: runtimeIdentifier,
            packageIdContains: packageIdContains,
            projection: projection,
            maxItems: maxItems,
            continuationToken: continuationToken,
            noCache: false,
            elicitation: elicitation ?? ElicitationContext.None);

    private static JsonElement Data(string json)
    {
        var root = JsonDocument.Parse(json).RootElement;
        Assert.Equal("project.packageGraph", root.GetProperty("kind").GetString());
        return root.GetProperty("data");
    }

    private static JsonElement Package(JsonElement data, string id) =>
        data.GetProperty("packages").EnumerateArray().Single(p => p.GetProperty("id").GetString() == id);
}
