using System.Text.Json;
using ModelContextProtocol.Protocol;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.ApiDiff;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

[Collection(nameof(EnvVarCollection))]
public class ApiDiffToolTests
{
    private static readonly RuntimeOptions Options = new();
    private static readonly IInspectionContextProvider Contexts = new SharedInspectionContextProvider(Options);
    private static readonly ApiDiffPair Pair = ApiDiffFixtures.EmitPair();

    private static Task<CallToolResult> Compare(
        string left, string right, string? tfm = null, string? namespaceFilter = null, bool breakingOnly = false,
        string projection = "summary", int? maxItems = null, string? continuationToken = null,
        ToolMiddleware? middleware = null, bool noCache = false, CancellationToken cancellationToken = default) =>
        ApiDiffTools.CompareApiSurface(
            new ApiDiffService(Contexts), new ProjectAnalysisService(), middleware ?? TestMiddleware.Fresh, Options, TestHandles.Registry,
            left, right, tfm, namespaceFilter, breakingOnly, projection, maxItems, continuationToken, noCache, cancellationToken);

    private static JsonElement Root(CallToolResult result) => JsonDocument.Parse(result.Text()).RootElement;

    private static JsonElement Data(CallToolResult result)
    {
        var root = Root(result);
        Assert.Equal("api.compare", root.GetProperty("kind").GetString());
        return root.GetProperty("data");
    }

    private static JsonElement[] Changes(JsonElement data) => data.GetProperty("changes").EnumerateArray().ToArray();

    [Fact]
    public async Task Summary_ReturnsCountsAndOnlyBreakingChanges()
    {
        var data = Data(await Compare(Pair.LeftPath, Pair.RightPath));

        var counts = data.GetProperty("counts");
        Assert.Equal(1, counts.GetProperty("typesAdded").GetInt32());
        Assert.Equal(2, counts.GetProperty("typesRemoved").GetInt32());
        Assert.Equal(3, counts.GetProperty("typesChanged").GetInt32());
        var breaking = counts.GetProperty("breaking").GetInt32();
        Assert.Equal(breaking, data.GetProperty("total").GetInt32());
        Assert.All(Changes(data), change => Assert.True(change.GetProperty("breaking").GetBoolean()));
        Assert.False(Changes(data)[0].TryGetProperty("reasons", out _));

        var left = data.GetProperty("left");
        Assert.Equal("path", left.GetProperty("source").GetString());
        Assert.Equal(ApiDiffFixtures.AssemblyName, left.GetProperty("assemblyName").GetString());
    }

    [Fact]
    public async Task Full_ListsEveryChangeWithBothSignatures()
    {
        var data = Data(await Compare(Pair.LeftPath, Pair.RightPath, projection: "full", maxItems: 500));

        var changes = Changes(data);
        Assert.Contains(changes, c => !c.GetProperty("breaking").GetBoolean());
        var size = Assert.Single(changes, c => c.GetProperty("member").GetString() == "Size");
        Assert.Equal("public long Size()", size.GetProperty("leftSignature").GetString());
        Assert.Equal("public int Size()", size.GetProperty("rightSignature").GetString());
        Assert.Equal("changed", size.GetProperty("change").GetString());
        Assert.Equal("member", size.GetProperty("scope").GetString());
        Assert.True(size.GetProperty("reasons")[0].GetProperty("breaking").GetBoolean());
    }

    [Fact]
    public async Task Full_BreakingOnly_FiltersNonBreaking()
    {
        var data = Data(await Compare(Pair.LeftPath, Pair.RightPath, projection: "full", breakingOnly: true, maxItems: 500));

        Assert.Equal(data.GetProperty("counts").GetProperty("breaking").GetInt32(), data.GetProperty("total").GetInt32());
        Assert.All(Changes(data), change => Assert.True(change.GetProperty("breaking").GetBoolean()));
    }

    [Fact]
    public async Task NamespaceFilter_IsApplied()
    {
        var data = Data(await Compare(Pair.LeftPath, Pair.RightPath, namespaceFilter: "Acme.Lib.Internal", projection: "full"));

        var change = Assert.Single(Changes(data));
        Assert.Equal("Drain", change.GetProperty("member").GetString());
        Assert.Equal("Acme.Lib.Internal", data.GetProperty("namespaceFilter").GetString());
    }

    [Fact]
    public async Task Pagination_WalksEveryChange()
    {
        var all = Changes(Data(await Compare(Pair.LeftPath, Pair.RightPath, projection: "full", maxItems: 500)));
        var collected = new List<string>();
        string? token = null;
        do
        {
            var data = Data(await Compare(Pair.LeftPath, Pair.RightPath, projection: "full", maxItems: 4, continuationToken: token));
            collected.AddRange(Changes(data).Select(c => c.GetRawText()));
            token = data.GetProperty("nextToken").GetString();
        } while (token != null);

        Assert.Equal(all.Select(c => c.GetRawText()), collected);
    }

    [Fact]
    public async Task ContinuationToken_FromAnotherProjection_IsRejected()
    {
        var token = Data(await Compare(Pair.LeftPath, Pair.RightPath, projection: "full", maxItems: 2)).GetProperty("nextToken").GetString();

        var root = Root(await Compare(Pair.LeftPath, Pair.RightPath, projection: "summary", continuationToken: token));

        Assert.Equal("InvalidContinuationToken", root.GetProperty("code").GetString());
    }

    [Fact]
    public async Task InvalidProjection_IsRejected() =>
        Assert.Equal("InvalidProjection", Root(await Compare(Pair.LeftPath, Pair.RightPath, projection: "everything")).GetProperty("code").GetString());

    [Fact]
    public async Task UnknownSide_ReportsWhichSide()
    {
        var root = Root(await Compare(Pair.LeftPath, "/no/such/Thing.dll"));

        Assert.Equal("AssemblyNotFound", root.GetProperty("code").GetString());
        Assert.Equal("right", root.GetProperty("details").GetProperty("side").GetString());
    }

    [Fact]
    public async Task Handle_IsAcceptedAsASide()
    {
        var handle = JsonDocument.Parse(HandleTools.OpenAssembly(TestHandles.Registry, Contexts, Pair.LeftPath).Text())
            .RootElement.GetProperty("data").GetProperty("handle").GetString()!;

        var data = Data(await Compare(handle, Pair.RightPath));

        Assert.Equal("handle", data.GetProperty("left").GetProperty("source").GetString());
        Assert.True(data.GetProperty("counts").GetProperty("breaking").GetInt32() > 0);
    }

    [Fact]
    public async Task RebuiltAssembly_IsNotServedFromCache()
    {
        var middleware = TestMiddleware.Fresh;
        var right = ApiDiffFixtures.Emit(ApiDiffFixtures.Version1);
        Assert.Equal(0, Data(await Compare(Pair.LeftPath, right, middleware: middleware)).GetProperty("counts").GetProperty("breaking").GetInt32());

        File.Move(ApiDiffFixtures.Emit(ApiDiffFixtures.Version2), right, overwrite: true);

        Assert.True(Data(await Compare(Pair.LeftPath, right, middleware: middleware)).GetProperty("counts").GetProperty("breaking").GetInt32() > 0);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Compare(Pair.LeftPath, Pair.RightPath, noCache: true, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task NuGetPackages_AreResolvedFromTheCache()
    {
        using var cache = new TempDir();
        CopyIntoCache(cache.Path, "1.0.0", "net8.0", Pair.LeftPath);
        CopyIntoCache(cache.Path, "2.0.0", "net8.0", Pair.RightPath);
        CopyIntoCache(cache.Path, "2.0.0", "netstandard2.0", Pair.RightPath);
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        var data = Data(await Compare("Acme.Lib@1.0.0", "Acme.Lib@2.0.0"));

        var left = data.GetProperty("left");
        var right = data.GetProperty("right");
        Assert.Equal("nuget", left.GetProperty("source").GetString());
        Assert.Equal("1.0.0", left.GetProperty("packageVersion").GetString());
        Assert.Equal("net8.0", left.GetProperty("tfm").GetString());
        Assert.Equal("2.0.0", right.GetProperty("packageVersion").GetString());
        Assert.Equal("net8.0", right.GetProperty("tfm").GetString());
        Assert.True(data.GetProperty("counts").GetProperty("breaking").GetInt32() > 0);
    }

    [Fact]
    public async Task NuGetPackages_AreAlignedOnASharedTfm()
    {
        using var cache = new TempDir();
        CopyIntoCache(cache.Path, "1.0.0", "netstandard2.0", Pair.LeftPath);
        CopyIntoCache(cache.Path, "2.0.0", "net8.0", Pair.RightPath);
        CopyIntoCache(cache.Path, "2.0.0", "netstandard2.0", Pair.RightPath);
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        var data = Data(await Compare("Acme.Lib@1.0.0", "Acme.Lib@2.0.0"));

        Assert.Equal("netstandard2.0", data.GetProperty("left").GetProperty("tfm").GetString());
        Assert.Equal("netstandard2.0", data.GetProperty("right").GetProperty("tfm").GetString());
        Assert.Empty(data.GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public async Task NuGetPackages_FallBackToTheBestCommonTfm()
    {
        using var cache = new TempDir();
        CopyIntoCache(cache.Path, "1.0.0", "net8.0", Pair.LeftPath);
        CopyIntoCache(cache.Path, "1.0.0", "netstandard2.0", Pair.LeftPath);
        CopyIntoCache(cache.Path, "2.0.0", "net9.0", Pair.RightPath);
        CopyIntoCache(cache.Path, "2.0.0", "netstandard2.0", Pair.RightPath);
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        var data = Data(await Compare("Acme.Lib@1.0.0", "Acme.Lib@2.0.0"));

        Assert.Equal("netstandard2.0", data.GetProperty("left").GetProperty("tfm").GetString());
        Assert.Equal("netstandard2.0", data.GetProperty("right").GetProperty("tfm").GetString());
        Assert.Empty(data.GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public async Task NuGetPackages_WithoutASharedTfm_AreComparedWithAWarning()
    {
        using var cache = new TempDir();
        CopyIntoCache(cache.Path, "1.0.0", "netstandard2.0", Pair.LeftPath);
        CopyIntoCache(cache.Path, "2.0.0", "net8.0", Pair.RightPath);
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        var data = Data(await Compare("Acme.Lib@1.0.0", "Acme.Lib@2.0.0"));

        Assert.Equal("netstandard2.0", data.GetProperty("left").GetProperty("tfm").GetString());
        Assert.Equal("net8.0", data.GetProperty("right").GetProperty("tfm").GetString());
        Assert.Contains("share no target framework", data.GetProperty("warnings")[0].GetString());
    }

    [Fact]
    public async Task NuGetPackage_WithoutVersion_UsesHighest()
    {
        using var cache = new TempDir();
        CopyIntoCache(cache.Path, "1.0.0", "net8.0", Pair.LeftPath);
        CopyIntoCache(cache.Path, "2.0.0", "net8.0", Pair.RightPath);
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        var data = Data(await Compare("Acme.Lib@1.0.0", "Acme.Lib"));

        Assert.Equal("2.0.0", data.GetProperty("right").GetProperty("packageVersion").GetString());
    }

    [Fact]
    public async Task MissingNuGetVersion_ReportsWhichSide()
    {
        using var cache = new TempDir();
        CopyIntoCache(cache.Path, "2.0.0", "net8.0", Pair.RightPath);
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        var root = Root(await Compare("Acme.Lib@1.0.0", "Acme.Lib@2.0.0"));

        Assert.Equal("VersionNotFound", root.GetProperty("code").GetString());
        Assert.Equal("left", root.GetProperty("details").GetProperty("side").GetString());
    }

    [Theory]
    [InlineData("Missing.dll")]
    [InlineData("/tmp/Missing")]
    [InlineData("bin/Debug/App.exe")]
    public async Task PathLikeInput_IsNotTreatedAsAPackage(string input) =>
        Assert.Equal("AssemblyNotFound", Root(await Compare(input, Pair.RightPath)).GetProperty("code").GetString());

    [Theory]
    [InlineData("No.Such.Package@1.0.0")]
    [InlineData("No.Such.Package")]
    public async Task PackageLikeInput_IsLookedUpInTheCache(string input)
    {
        using var cache = new TempDir();
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        Assert.Equal("PackageNotFound", Root(await Compare(input, Pair.RightPath)).GetProperty("code").GetString());
    }

    private static void CopyIntoCache(string cacheRoot, string version, string tfm, string assemblyPath)
    {
        var lib = Path.Combine(cacheRoot, "acme.lib", version, "lib", tfm);
        Directory.CreateDirectory(lib);
        File.Copy(assemblyPath, Path.Combine(lib, "Acme.Lib.dll"));
    }
}
