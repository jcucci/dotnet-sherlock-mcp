using System.Reflection;
using System.Text.Json;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Telemetry;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

public class SearchToolsTests
{
    private readonly ISearchService _svc = new SearchService();
    private readonly RuntimeOptions _runtimeOptions = new();
    private readonly ToolMiddleware _middleware;
    private readonly string _testAssemblyPath = Assembly.GetExecutingAssembly().Location;

    public SearchToolsTests()
    {
        var cache = new InMemoryToolResponseCache();
        var telemetry = new NoopTelemetry();
        _middleware = new ToolMiddleware(cache, telemetry, _runtimeOptions);
    }

    [Fact]
    public void SearchMembers_Envelope_AndHitShape()
    {
        var result = SearchTools.SearchMembers(
            _svc, _middleware, _runtimeOptions,
            _testAssemblyPath, "Method", noCache: true);

        Assert.DoesNotContain("\"error\"", result);
        var doc = JsonDocument.Parse(result);
        Assert.Equal("search.members", doc.RootElement.GetProperty("kind").GetString());

        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(_testAssemblyPath, data.GetProperty("assemblyPath").GetString());
        Assert.Equal("Method", data.GetProperty("nameContains").GetString());
        Assert.True(data.GetProperty("total").GetInt32() > 0);

        var first = data.GetProperty("results")[0];
        Assert.True(first.TryGetProperty("declaringType", out _));
        Assert.True(first.TryGetProperty("memberKind", out _));
        Assert.True(first.TryGetProperty("name", out _));
        Assert.True(first.TryGetProperty("signature", out _));
        Assert.False(first.TryGetProperty("assemblyPath", out _),
            "assemblyPath should be echoed once at the top level, not on every hit");
    }

    [Fact]
    public void SearchMembers_MemberKindsFilter_RestrictsKinds()
    {
        var result = SearchTools.SearchMembers(
            _svc, _middleware, _runtimeOptions,
            _testAssemblyPath, "Property", memberKinds: "property", noCache: true);

        Assert.DoesNotContain("\"error\"", result);
        var results = JsonDocument.Parse(result).RootElement.GetProperty("data").GetProperty("results");
        Assert.True(results.GetArrayLength() > 0);
        foreach (var hit in results.EnumerateArray())
            Assert.Equal("property", hit.GetProperty("memberKind").GetString());
    }

    [Fact]
    public void SearchMembers_InvalidMemberKinds_ReturnsError()
    {
        var result = SearchTools.SearchMembers(
            _svc, _middleware, _runtimeOptions,
            _testAssemblyPath, "Method", memberKinds: "method,bogus", noCache: true);

        Assert.Contains("InvalidArgument", result);
        Assert.Contains("bogus", result);
    }

    [Fact]
    public void SearchMembers_MissingNameContains_ReturnsError()
    {
        var result = SearchTools.SearchMembers(
            _svc, _middleware, _runtimeOptions,
            _testAssemblyPath, "  ", noCache: true);

        Assert.Contains("InvalidArgument", result);
    }

    [Fact]
    public void SearchMembers_AssemblyNotFound_ReturnsError()
    {
        var result = SearchTools.SearchMembers(
            _svc, _middleware, _runtimeOptions,
            "/tmp/does-not-exist.dll", "Method", noCache: true);

        Assert.Contains("AssemblyNotFound", result);
    }

    [Fact]
    public void SearchMembers_ContinuationToken_RoundTrips()
    {
        var page1 = SearchTools.SearchMembers(
            _svc, _middleware, _runtimeOptions,
            _testAssemblyPath, "Method", maxItems: 1, noCache: true);

        Assert.DoesNotContain("\"error\"", page1);
        var page1Data = JsonDocument.Parse(page1).RootElement.GetProperty("data");
        if (page1Data.GetProperty("total").GetInt32() < 2) return;

        var nextToken = page1Data.GetProperty("nextToken").GetString();
        Assert.False(string.IsNullOrEmpty(nextToken));

        var page2 = SearchTools.SearchMembers(
            _svc, _middleware, _runtimeOptions,
            _testAssemblyPath, "Method",
            maxItems: 1, continuationToken: nextToken, noCache: true);

        Assert.DoesNotContain("InvalidContinuationToken", page2);
        Assert.DoesNotContain("\"error\"", page2);
        Assert.Equal(1, JsonDocument.Parse(page2).RootElement.GetProperty("data").GetProperty("count").GetInt32());
    }

    [Fact]
    public void SearchMembers_InvalidContinuationToken_ReturnsError()
    {
        var result = SearchTools.SearchMembers(
            _svc, _middleware, _runtimeOptions,
            _testAssemblyPath, "Method",
            continuationToken: "not-a-real-token", noCache: true);

        Assert.Contains("InvalidContinuationToken", result);
    }
}
