using System.Reflection;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Telemetry;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Resources;
using Sherlock.MCP.Server.Shared;
using Sherlock.MCP.Server.Tools;
using Sherlock.MCP.Tests.ResourceLinkFixtures;

namespace Sherlock.MCP.Tests;

public class AssemblyResourcesTests
{
    private const string UriPrefix = "sherlock://assembly/";

    private readonly ITypeAnalysisService _typeAnalysis = new TypeAnalysisService();
    private readonly IXmlDocService _xmlDocs = new XmlDocService();
    private readonly ISearchService _search = new SearchService();
    private readonly IReverseLookupService _reverseLookup = new ReverseLookupService();
    private readonly RuntimeOptions _runtimeOptions = new();
    private readonly ToolMiddleware _middleware;
    private readonly string _testAssemblyPath = Assembly.GetExecutingAssembly().Location;

    public AssemblyResourcesTests() =>
        _middleware = new ToolMiddleware(new InMemoryToolResponseCache(), new NoopTelemetry(), _runtimeOptions);

    [Fact]
    public void TypeUri_RoundTripsPathAndName()
    {
        var uri = ResourceUris.Type(_testAssemblyPath, "Outer+Inner`1");

        var (path, kind, value) = ParseUri(uri);

        Assert.Equal(Path.GetFullPath(_testAssemblyPath), path);
        Assert.Equal("type", kind);
        Assert.Equal("Outer+Inner`1", value);
    }

    [Fact]
    public void ReadType_KnownType_ReturnsTypeInfoEnvelope()
    {
        var json = AssemblyResources.ReadType(_typeAnalysis, _testAssemblyPath, typeof(TestSampleClass).FullName!);

        var root = JsonDocument.Parse(json).RootElement;
        Assert.Equal("type.info", root.GetProperty("kind").GetString());
    }

    [Fact]
    public void ReadType_FriendlyGenericName_Resolves()
    {
        var coreLib = typeof(string).Assembly.Location;

        var json = AssemblyResources.ReadType(_typeAnalysis, coreLib, "System.Collections.Generic.List<T>");

        Assert.Equal("type.info", JsonDocument.Parse(json).RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public void ReadType_UnknownType_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<McpProtocolException>(() =>
            AssemblyResources.ReadType(_typeAnalysis, _testAssemblyPath, "Does.Not.Exist"));

        Assert.Equal(McpErrorCode.InvalidParams, ex.ErrorCode);
    }

    [Fact]
    public void ReadType_MissingAssembly_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<McpProtocolException>(() =>
            AssemblyResources.ReadType(_typeAnalysis, Path.Combine(Path.GetTempPath(), "missing-sherlock.dll"), "X"));

        Assert.Equal(McpErrorCode.InvalidParams, ex.ErrorCode);
    }

    [Fact]
    public void ReadDocs_DocumentedType_ReturnsSummary()
    {
        var json = AssemblyResources.ReadDocs(_xmlDocs, _testAssemblyPath, $"T:{typeof(TestSampleClass).FullName}");

        var data = JsonDocument.Parse(json).RootElement.GetProperty("data");
        Assert.Contains("Sample class", data.GetProperty("docs").GetProperty("Summary").GetString());
    }

    [Fact]
    public void ReadDocs_UnknownId_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<McpProtocolException>(() =>
            AssemblyResources.ReadDocs(_xmlDocs, _testAssemblyPath, "T:Does.Not.Exist"));

        Assert.Equal(McpErrorCode.InvalidParams, ex.ErrorCode);
    }

    [Fact]
    public void SearchMembers_ReturnsDistinctTypeLinks_ThatResolve()
    {
        var result = SearchTools.SearchMembers(
            _search,
            _middleware,
            _runtimeOptions,
            TestHandles.Registry,
            assemblyPath: _testAssemblyPath,
            nameContains: "Method",
            noCache: true);

        var links = result.Links();
        Assert.NotEmpty(links);
        Assert.Equal(links.Length, links.Select(l => l.Uri).Distinct().Count());
        foreach (var link in links)
            AssertLinkResolves(link);
    }

    [Fact]
    public void SearchMembers_CachedResponse_KeepsLinks()
    {
        var first = SearchTools.SearchMembers(_search, _middleware, _runtimeOptions, TestHandles.Registry, assemblyPath: _testAssemblyPath, nameContains: "Method");
        var second = SearchTools.SearchMembers(_search, _middleware, _runtimeOptions, TestHandles.Registry, assemblyPath: _testAssemblyPath, nameContains: "Method");

        Assert.Equal(first.Text(), second.Text());
        Assert.Equal(first.Links().Select(l => l.Uri), second.Links().Select(l => l.Uri));
        Assert.NotEmpty(second.Links());
    }

    [Fact]
    public void FindImplementationsOf_LinksEachImplementer()
    {
        var result = ReverseLookupTools.FindImplementationsOf(
            _reverseLookup,
            _middleware,
            _runtimeOptions,
            TestHandles.Registry,
            assemblyPath: _testAssemblyPath,
            typeName: "ISampleEventReader",
            noCache: true);

        var resultCount = JsonDocument.Parse(result.Text()).RootElement.GetProperty("data").GetProperty("results")
            .GetArrayLength();
        var links = result.Links();
        Assert.Equal(resultCount, links.Length);
        foreach (var link in links)
            AssertLinkResolves(link);
    }

    [Fact]
    public void GetTypesFromAssembly_LinksEveryTypeOnThePage()
    {
        var result = TypeAnalysisTools.GetTypesFromAssembly(_typeAnalysis, TestHandles.Registry, assemblyPath: _testAssemblyPath, maxItems: 5);

        var data = JsonDocument.Parse(result.Text()).RootElement.GetProperty("data");
        Assert.Equal(data.GetProperty("returnedTypeCount").GetInt32(), result.Links().Length);
        AssertLinkResolves(result.Links()[0]);
    }

    [Fact]
    public void SearchMembers_NestedTypeInGenericOuter_LinksToItsOwnType()
    {
        var result = SearchTools.SearchMembers(
            _search,
            _middleware,
            _runtimeOptions,
            TestHandles.Registry,
            assemblyPath: _testAssemblyPath,
            nameContains: "Linked",
            memberKinds: "method",
            noCache: true);

        var names = result.Links().Select(l => l.Name).ToHashSet();
        Assert.Equal(
            new HashSet<string> { typeof(GenericOuter<>).FullName!, typeof(GenericOuter<>.LinkedInner).FullName! },
            names);

        var innerLink = result.Links().Single(l => l.Name == typeof(GenericOuter<>.LinkedInner).FullName);
        var (path, _, fullName) = ParseUri(innerLink.Uri);
        var json = AssemblyResources.ReadType(_typeAnalysis, path, fullName);
        var data = JsonDocument.Parse(json).RootElement.GetProperty("data");
        Assert.Equal("LinkedInner", data.GetProperty("Name").GetString());
        Assert.Equal("Sherlock.MCP.Tests.ResourceLinkFixtures.GenericOuter<T>+LinkedInner", data.GetProperty("FullName").GetString());
    }

    [Theory]
    [InlineData("Sherlock.MCP.Tests.ResourceLinkFixtures.GenericOuter<T>", "GenericOuter`1")]
    [InlineData("Sherlock.MCP.Tests.ResourceLinkFixtures.GenericOuter<T>+LinkedInner", "LinkedInner")]
    public void ReadType_FriendlyNamesOfNestedGenerics_ResolveToDistinctTypes(string friendlyName, string expectedName)
    {
        var json = AssemblyResources.ReadType(_typeAnalysis, _testAssemblyPath, friendlyName);

        var data = JsonDocument.Parse(json).RootElement.GetProperty("data");
        Assert.Equal(expectedName, data.GetProperty("Name").GetString());
    }

    [Fact]
    public void GetTypesFromAssembly_GenericTypeLink_UsesMetadataNameWithoutExposingIt()
    {
        var result = TypeAnalysisTools.GetTypesFromAssembly(_typeAnalysis, TestHandles.Registry, assemblyPath: _testAssemblyPath, maxItems: 1000, projection: "full");

        var metadataName = typeof(GenericOuter<>).FullName!;
        var link = Assert.Single(result.Links(), l => l.Name == metadataName);
        AssertLinkResolves(link);
        Assert.DoesNotContain("MetadataName", result.Text());
    }

    [Fact]
    public void GetTypesFromAssembly_WithAdditionalAssemblies_OmitsLinks()
    {
        var dependency = typeof(TypeAnalysisService).Assembly.Location;

        var result = TypeAnalysisTools.GetTypesFromAssembly(
            _typeAnalysis,
            TestHandles.Registry,
            assemblyPath: _testAssemblyPath,
            maxItems: 5,
            additionalAssemblies: [dependency]);

        Assert.Equal("type.list", JsonDocument.Parse(result.Text()).RootElement.GetProperty("kind").GetString());
        Assert.Empty(result.Links());
    }

    [Fact]
    public void ErrorResponse_HasNoLinks()
    {
        var result = SearchTools.SearchMembers(
            _search,
            _middleware,
            _runtimeOptions,
            TestHandles.Registry,
            assemblyPath: _testAssemblyPath,
            nameContains: "Method",
            memberKinds: "bogus");

        Assert.Contains("\"error\"", result.Text());
        Assert.Empty(result.Links());
    }

    private void AssertLinkResolves(ResourceLinkBlock link)
    {
        Assert.Equal(ResourceUris.JsonMimeType, link.MimeType);
        var (path, kind, fullName) = ParseUri(link.Uri);
        Assert.Equal("type", kind);
        Assert.Equal(link.Name, fullName);

        var json = AssemblyResources.ReadType(_typeAnalysis, path, fullName);
        Assert.Equal("type.info", JsonDocument.Parse(json).RootElement.GetProperty("kind").GetString());
    }

    private static (string Path, string Kind, string Value) ParseUri(string uri)
    {
        Assert.StartsWith(UriPrefix, uri);
        var segments = uri[UriPrefix.Length..].Split('/');
        Assert.Equal(3, segments.Length);
        return (Uri.UnescapeDataString(segments[0]), segments[1], Uri.UnescapeDataString(segments[2]));
    }
}
