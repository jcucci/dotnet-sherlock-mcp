using System.Reflection;
using System.Text.Json;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Telemetry;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Tools;
using Sherlock.MCP.Tests.ReverseLookupFixtures;

namespace Sherlock.MCP.Tests;

public class ReverseLookupToolsTests
{
    private readonly IReverseLookupService _svc = new ReverseLookupService();
    private readonly RuntimeOptions _runtimeOptions = new();
    private readonly ToolMiddleware _middleware;
    private readonly string _testAssemblyPath = Assembly.GetExecutingAssembly().Location;

    public ReverseLookupToolsTests()
    {
        var cache = new InMemoryToolResponseCache();
        var telemetry = new NoopTelemetry();
        _middleware = new ToolMiddleware(cache, telemetry, _runtimeOptions);
    }

    [Fact]
    public void FindImplementationsOf_Envelope_AndSummaryShape()
    {
        var result = ReverseLookupTools.FindImplementationsOf(
            _svc, _middleware, _runtimeOptions,
            _testAssemblyPath, "ISampleEventReader", noCache: true);

        Assert.DoesNotContain("\"error\"", result);
        var doc = JsonDocument.Parse(result);
        Assert.Equal("reverselookup.implementations", doc.RootElement.GetProperty("kind").GetString());

        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("summary", data.GetProperty("projection").GetString());
        Assert.True(data.GetProperty("total").GetInt32() > 0);

        var results = data.GetProperty("results");
        Assert.True(results.GetArrayLength() > 0);
        var first = results[0];
        Assert.True(first.TryGetProperty("typeFullName", out _));
        Assert.True(first.TryGetProperty("kind", out _));
        Assert.False(first.TryGetProperty("matchedInterfaces", out _),
            "Summary should not include matchedInterfaces");
    }

    [Fact]
    public void FindImplementationsOf_FullProjection_IncludesHeavyFields()
    {
        var result = ReverseLookupTools.FindImplementationsOf(
            _svc, _middleware, _runtimeOptions,
            _testAssemblyPath, "ISampleEventReader", projection: "full", noCache: true);

        Assert.DoesNotContain("\"error\"", result);
        var data = JsonDocument.Parse(result).RootElement.GetProperty("data");
        Assert.Equal("full", data.GetProperty("projection").GetString());
        var first = data.GetProperty("results")[0];
        Assert.True(first.TryGetProperty("matchedInterfaces", out _));
        Assert.True(first.TryGetProperty("baseTypeChain", out _));
        Assert.True(first.TryGetProperty("assemblyPath", out _));
    }

    [Fact]
    public void FindImplementationsOf_InvalidProjection_ReturnsError()
    {
        var result = ReverseLookupTools.FindImplementationsOf(
            _svc, _middleware, _runtimeOptions,
            _testAssemblyPath, "ISampleEventReader", projection: "partial", noCache: true);

        Assert.Contains("InvalidProjection", result);
    }

    [Fact]
    public void FindImplementationsOf_AssemblyNotFound_ReturnsError()
    {
        var result = ReverseLookupTools.FindImplementationsOf(
            _svc, _middleware, _runtimeOptions,
            "/tmp/does-not-exist.dll", "ISampleEventReader", noCache: true);

        Assert.Contains("AssemblyNotFound", result);
    }

    [Fact]
    public void FindImplementationsOf_MissingAdditionalAssembly_ReturnsError()
    {
        var result = ReverseLookupTools.FindImplementationsOf(
            _svc, _middleware, _runtimeOptions,
            _testAssemblyPath, "ISampleEventReader",
            additionalAssemblies: new[] { "/tmp/also-missing.dll" },
            noCache: true);

        Assert.Contains("AssemblyNotFound", result);
        Assert.Contains("also-missing", result);
    }

    [Fact]
    public void FindImplementationsOf_ContinuationToken_RoundTrips()
    {
        var page1 = ReverseLookupTools.FindImplementationsOf(
            _svc, _middleware, _runtimeOptions,
            _testAssemblyPath, "ISampleEventReader", maxItems: 1, noCache: true);

        Assert.DoesNotContain("\"error\"", page1);
        var page1Data = JsonDocument.Parse(page1).RootElement.GetProperty("data");
        if (page1Data.GetProperty("total").GetInt32() < 2) return;

        var nextToken = page1Data.GetProperty("nextToken").GetString();
        Assert.False(string.IsNullOrEmpty(nextToken));

        var page2 = ReverseLookupTools.FindImplementationsOf(
            _svc, _middleware, _runtimeOptions,
            _testAssemblyPath, "ISampleEventReader",
            maxItems: 1, continuationToken: nextToken, noCache: true);

        Assert.DoesNotContain("InvalidContinuationToken", page2);
        Assert.DoesNotContain("\"error\"", page2);
        Assert.Equal(1, JsonDocument.Parse(page2).RootElement.GetProperty("data").GetProperty("count").GetInt32());
    }

    [Fact]
    public void FindMethodsReturning_Envelope_Summary()
    {
        var result = ReverseLookupTools.FindMethodsReturning(
            _svc, _middleware, _runtimeOptions,
            _testAssemblyPath, "Snapshot", noCache: true);

        Assert.DoesNotContain("\"error\"", result);
        var doc = JsonDocument.Parse(result);
        Assert.Equal("reverselookup.returning", doc.RootElement.GetProperty("kind").GetString());

        var data = doc.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("total").GetInt32() > 0);
        var first = data.GetProperty("results")[0];
        Assert.True(first.TryGetProperty("declaringType", out _));
        Assert.True(first.TryGetProperty("methodName", out _));
        Assert.True(first.TryGetProperty("signature", out _));
        Assert.False(first.TryGetProperty("isStatic", out _));
    }

    [Fact]
    public void FindMethodsReturning_FullIncludesExtraFields()
    {
        var result = ReverseLookupTools.FindMethodsReturning(
            _svc, _middleware, _runtimeOptions,
            _testAssemblyPath, "Snapshot", projection: "full", noCache: true);

        var data = JsonDocument.Parse(result).RootElement.GetProperty("data");
        var first = data.GetProperty("results")[0];
        Assert.True(first.TryGetProperty("returnType", out _));
        Assert.True(first.TryGetProperty("isStatic", out _));
        Assert.True(first.TryGetProperty("assemblyPath", out _));
    }

    [Fact]
    public void FindReferencesTo_Envelope_IncludesTruncatedFlag()
    {
        var result = ReverseLookupTools.FindReferencesTo(
            _svc, _middleware, _runtimeOptions,
            _testAssemblyPath, "RecordedEvent", noCache: true);

        Assert.DoesNotContain("\"error\"", result);
        var doc = JsonDocument.Parse(result);
        Assert.Equal("reverselookup.references", doc.RootElement.GetProperty("kind").GetString());

        var data = doc.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("truncated", out _));
        Assert.True(data.TryGetProperty("hardCap", out _));
    }

    [Fact]
    public void FindReferencesTo_SmallMaxItems_ReportsFloor500HardCap()
    {
        var result = ReverseLookupTools.FindReferencesTo(
            _svc, _middleware, _runtimeOptions,
            _testAssemblyPath, "RecordedEvent", maxItems: 1, noCache: true);

        var data = JsonDocument.Parse(result).RootElement.GetProperty("data");
        var hardCap = data.GetProperty("hardCap").GetInt32();
        Assert.Equal(500, hardCap);
        Assert.False(data.GetProperty("truncated").GetBoolean(),
            "Small test assembly should not exceed the 500-hit floor.");
    }

    [Fact]
    public void FindReferencesTo_LargeMaxItems_ScalesHardCapAboveFloor()
    {
        var result = ReverseLookupTools.FindReferencesTo(
            _svc, _middleware, _runtimeOptions,
            _testAssemblyPath, "RecordedEvent", maxItems: 200, noCache: true);

        var data = JsonDocument.Parse(result).RootElement.GetProperty("data");
        Assert.Equal(800, data.GetProperty("hardCap").GetInt32());
    }

    [Fact]
    public void FindReferencesTo_ContinuationToken_RoundTrips()
    {
        var page1 = ReverseLookupTools.FindReferencesTo(
            _svc, _middleware, _runtimeOptions,
            _testAssemblyPath, "RecordedEvent", maxItems: 1, noCache: true);

        var page1Data = JsonDocument.Parse(page1).RootElement.GetProperty("data");
        if (page1Data.GetProperty("total").GetInt32() < 2) return;

        var nextToken = page1Data.GetProperty("nextToken").GetString();
        Assert.False(string.IsNullOrEmpty(nextToken));

        var page2 = ReverseLookupTools.FindReferencesTo(
            _svc, _middleware, _runtimeOptions,
            _testAssemblyPath, "RecordedEvent",
            maxItems: 1, continuationToken: nextToken, noCache: true);

        Assert.DoesNotContain("InvalidContinuationToken", page2);
        Assert.Equal(1, JsonDocument.Parse(page2).RootElement.GetProperty("data").GetProperty("count").GetInt32());
    }
}
