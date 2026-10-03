using System.Reflection;
using System.Text.Json;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.FrameworkPatterns;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Server.Tools;
using Sherlock.MCP.Tests.FrameworkPatternFixtures;

namespace Sherlock.MCP.Tests;

public class FrameworkPatternToolsTests
{
    private static readonly SharedInspectionContextProvider Contexts = new(new RuntimeOptions());

    private readonly IFrameworkPatternService _svc = new FrameworkPatternService();
    private readonly RuntimeOptions _runtimeOptions = new();
    private readonly string _testAssemblyPath = Assembly.GetExecutingAssembly().Location;

    [Fact]
    public void FindServiceRegistrations_SummaryShape()
    {
        var data = Data(FrameworkPatternTools.FindServiceRegistrations(
            _svc, TestMiddleware.Fresh, _runtimeOptions, TestHandles.Registry,
            assemblyPath: _testAssemblyPath, serviceType: nameof(IClock), noCache: true).Text());

        Assert.Equal("summary", data.GetProperty("projection").GetString());
        var first = data.GetProperty("results")[0];
        Assert.Equal("singleton", first.GetProperty("lifetime").GetString());
        Assert.Equal(typeof(IClock).FullName, first.GetProperty("service").GetString());
        Assert.False(first.TryGetProperty("registrationKind", out _), "Summary should not include registrationKind");
    }

    [Fact]
    public void FindEndpoints_FullProjection_AddsHandlerDetailsAndLinks()
    {
        var result = FrameworkPatternTools.FindEndpoints(
            _svc, TestMiddleware.Fresh, _runtimeOptions, TestHandles.Registry,
            assemblyPath: _testAssemblyPath, routeContains: "api/Orders", projection: "full", noCache: true);

        var first = Data(result.Text()).GetProperty("results")[0];
        Assert.Equal(typeof(OrdersController).FullName, first.GetProperty("handlerType").GetString());
        Assert.True(first.TryGetProperty("assemblyPath", out _));
        Assert.Contains(result.Links(), l => l.Name == typeof(OrdersController).FullName);
    }

    [Fact]
    public void FindEfEntities_PagesWithContinuationToken()
    {
        var firstPage = Data(FrameworkPatternTools.FindEfEntities(
            _svc, TestMiddleware.Fresh, _runtimeOptions, TestHandles.Registry,
            assemblyPath: _testAssemblyPath, maxItems: 1, noCache: true).Text());

        Assert.True(firstPage.GetProperty("total").GetInt32() > 1);
        var token = firstPage.GetProperty("nextToken").GetString();
        Assert.False(string.IsNullOrEmpty(token));

        var secondPage = Data(FrameworkPatternTools.FindEfEntities(
            _svc, TestMiddleware.Fresh, _runtimeOptions, TestHandles.Registry,
            assemblyPath: _testAssemblyPath, maxItems: 1, continuationToken: token, noCache: true).Text());

        Assert.NotEqual(
            firstPage.GetProperty("results")[0].GetRawText(),
            secondPage.GetProperty("results")[0].GetRawText());
    }

    [Fact]
    public void FindHandlers_ViaHandle_MatchesViaPath()
    {
        var handles = TestHandles.Create();
        var handle = JsonDocument.Parse(HandleTools.OpenAssembly(handles, Contexts, _testAssemblyPath).Text())
            .RootElement.GetProperty("data").GetProperty("handle").GetString();

        var viaPath = FrameworkPatternTools.FindHandlers(
            _svc, TestMiddleware.Fresh, _runtimeOptions, handles, assemblyPath: _testAssemblyPath, noCache: true).Text();
        var viaHandle = FrameworkPatternTools.FindHandlers(
            _svc, TestMiddleware.Fresh, _runtimeOptions, handles, assemblyHandle: handle, noCache: true).Text();

        Assert.Equal(viaPath, viaHandle);
    }

    [Fact]
    public void InvalidProjection_ReturnsError() =>
        Assert.Equal("InvalidProjection", ErrorCode(FrameworkPatternTools.FindHandlers(
            _svc, TestMiddleware.Fresh, _runtimeOptions, TestHandles.Registry,
            assemblyPath: _testAssemblyPath, projection: "everything", noCache: true).Text()));

    [Fact]
    public void InvalidContinuationToken_ReturnsError() =>
        Assert.Equal("InvalidContinuationToken", ErrorCode(FrameworkPatternTools.FindEndpoints(
            _svc, TestMiddleware.Fresh, _runtimeOptions, TestHandles.Registry,
            assemblyPath: _testAssemblyPath, continuationToken: "bogus", noCache: true).Text()));

    [Fact]
    public void MissingAssembly_ReturnsAssemblyNotFound() =>
        Assert.Equal("AssemblyNotFound", ErrorCode(FrameworkPatternTools.FindEfEntities(
            _svc, TestMiddleware.Fresh, _runtimeOptions, TestHandles.Registry,
            assemblyPath: "/no/such/assembly.dll", noCache: true).Text()));

    [Fact]
    public void Tools_WithCancelledToken_Rethrow()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => FrameworkPatternTools.FindEndpoints(
            _svc, TestMiddleware.Fresh, _runtimeOptions, TestHandles.Registry, assemblyPath: _testAssemblyPath, noCache: true, cancellationToken: cts.Token));
        Assert.Throws<OperationCanceledException>(() => FrameworkPatternTools.FindServiceRegistrations(
            _svc, TestMiddleware.Fresh, _runtimeOptions, TestHandles.Registry, assemblyPath: _testAssemblyPath, noCache: true, cancellationToken: cts.Token));
        Assert.Throws<OperationCanceledException>(() => FrameworkPatternTools.FindEfEntities(
            _svc, TestMiddleware.Fresh, _runtimeOptions, TestHandles.Registry, assemblyPath: _testAssemblyPath, noCache: true, cancellationToken: cts.Token));
        Assert.Throws<OperationCanceledException>(() => FrameworkPatternTools.FindHandlers(
            _svc, TestMiddleware.Fresh, _runtimeOptions, TestHandles.Registry, assemblyPath: _testAssemblyPath, noCache: true, cancellationToken: cts.Token));
    }

    private static JsonElement Data(string text)
    {
        var root = JsonDocument.Parse(text).RootElement;
        Assert.NotEqual("error", root.GetProperty("kind").GetString());
        return root.GetProperty("data");
    }

    private static string? ErrorCode(string text) =>
        JsonDocument.Parse(text).RootElement.GetProperty("code").GetString();
}
