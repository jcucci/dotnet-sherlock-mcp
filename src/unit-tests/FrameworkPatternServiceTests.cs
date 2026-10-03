using System.Reflection;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.ReverseLookup;
using Sherlock.MCP.Runtime.FrameworkPatterns;
using Sherlock.MCP.Tests.FrameworkPatternFixtures;

namespace Sherlock.MCP.Tests;

public class FrameworkPatternServiceTests
{
    private const string FixtureNamespace = "Sherlock.MCP.Tests.FrameworkPatternFixtures";

    private readonly IFrameworkPatternService _svc = new FrameworkPatternService();
    private readonly string[] _paths = [Assembly.GetExecutingAssembly().Location];
    private readonly ReverseLookupOptions _options = new();

    [Fact]
    public void FindEndpoints_ReadsControllerAttributeRoutes()
    {
        var hits = Endpoints().Where(h => h.Kind == "controller").ToArray();

        Assert.Contains(hits, h => h.HandlerMethod == "List" && h.RouteTemplate == "api/Orders" && h.HttpMethods.SequenceEqual(["GET"]));
        Assert.Contains(hits, h => h.HandlerMethod == "Get" && h.RouteTemplate == "api/Orders/{id}");
        Assert.Contains(hits, h => h.HandlerMethod == "CreateAsync" && h.RouteTemplate == "api/Orders" && h.HttpMethods.SequenceEqual(["POST"]));
        Assert.Contains(hits, h => h.HandlerMethod == "Health" && h.RouteTemplate == "/health");
        Assert.DoesNotContain(hits, h => h.HandlerMethod == "Helper");
        Assert.DoesNotContain(hits, h => h.HandlerTypeFullName == typeof(NotAController).FullName);
    }

    [Fact]
    public void FindEndpoints_ReadsMinimalApiRoutesAndHandlers()
    {
        var hits = Endpoints().Where(h => h.Kind.StartsWith("minimalApi", StringComparison.Ordinal)).ToArray();
        var registeredIn = $"{typeof(EndpointMappings).FullName}.Map";

        Assert.Contains(hits, h => h.RouteTemplate == "/widgets" && h.HttpMethods.SequenceEqual(["GET"]) &&
                                   h.HandlerTypeFullName == typeof(EndpointMappings).FullName && h.HandlerMethod == "Map (lambda)" &&
                                   h.RegisteredIn == registeredIn);
        Assert.Contains(hits, h => h.RouteTemplate == "/widgets" && h.HttpMethods.SequenceEqual(["POST"]) &&
                                   h.HandlerTypeFullName == typeof(WidgetHandlers).FullName && h.HandlerMethod == "Create");
        Assert.Contains(hits, h => h.RouteTemplate == "/widgets/{id}" && h.HttpMethods.SequenceEqual(["PUT", "PATCH"]) &&
                                   h.HandlerMethod == "Update");
        Assert.Contains(hits, h => h.Kind == "minimalApiGroup" && h.RouteTemplate == "/admin");
        Assert.DoesNotContain(hits, h => h.RouteTemplate == "ListWidgets");
    }

    [Fact]
    public void FindEndpoints_FiltersByRouteAndVerb()
    {
        var hits = _svc.FindEndpoints(_paths, new EndpointFilter(RouteContains: "widgets", HttpMethod: "post"), _options);

        var hit = Assert.Single(hits);
        Assert.Equal("Create", hit.HandlerMethod);
    }

    [Fact]
    public void FindServiceRegistrations_ReadsGenericTypeofFactoryAndKeyedRegistrations()
    {
        var hits = _svc.FindServiceRegistrations(_paths, new ServiceRegistrationFilter(), _options)
            .Where(h => h.RegisteringTypeFullName == typeof(WidgetServiceRegistrations).FullName)
            .ToArray();

        Assert.Contains(hits, h => h.RegistrationMethod == "AddScoped" && h.Lifetime == "scoped" && !h.IsKeyed &&
                                   h.ServiceTypeFullName == typeof(IWidgetRepository).FullName &&
                                   h.ImplementationTypeFullName == typeof(WidgetRepository).FullName &&
                                   h.RegistrationKind == "type");
        Assert.Contains(hits, h => h.RegistrationMethod == "TryAddSingleton" && h.IsTryAdd &&
                                   h.ServiceTypeFullName == typeof(IClock).FullName &&
                                   h.ImplementationTypeFullName == typeof(SystemClock).FullName);
        Assert.Contains(hits, h => h.RegistrationMethod == "AddTransient" && h.Lifetime == "transient" &&
                                   h.ServiceTypeFullName == typeof(IAuditSink).FullName &&
                                   h.ImplementationTypeFullName == typeof(AuditSink).FullName);
        Assert.Contains(hits, h => h.RegistrationMethod == "AddSingleton" && h.RegistrationKind == "factory" &&
                                   h.ServiceTypeFullName == typeof(WidgetOptions).FullName && h.ImplementationTypeFullName == null);
        Assert.Contains(hits, h => h.RegistrationMethod == "AddKeyedScoped" && h.IsKeyed && h.RegistrationKind == "type" &&
                                   h.ImplementationTypeFullName == typeof(WidgetRepository).FullName);
        Assert.All(hits, h => Assert.Equal("AddWidgets", h.RegisteringMethod));
    }

    [Fact]
    public void FindServiceRegistrations_FiltersByServiceTypeAndLifetime()
    {
        var hits = _svc.FindServiceRegistrations(_paths, new ServiceRegistrationFilter(ServiceType: "IWidgetRepository", Lifetime: "Scoped"), _options);

        Assert.Equal(2, hits.Count(h => h.RegisteringTypeFullName.StartsWith(FixtureNamespace, StringComparison.Ordinal)));
        Assert.All(hits, h => Assert.Equal("scoped", h.Lifetime));
    }

    [Fact]
    public void FindEfEntities_ListsDbSetsIncludingInherited()
    {
        var hits = _svc.FindEfEntities(_paths, new EfEntityFilter(), _options);

        Assert.Contains(hits, h => h.ContextTypeFullName == typeof(ShopContext).FullName && h.PropertyName == "Widgets" &&
                                   h.EntityTypeFullName == typeof(Widget).FullName);
        Assert.Contains(hits, h => h.ContextTypeFullName == typeof(ArchiveContext).FullName && h.PropertyName == "Gadgets");
        Assert.DoesNotContain(hits, h => h.PropertyName == "Name");
    }

    [Fact]
    public void FindEfEntities_FiltersByContextAndEntity()
    {
        var hits = _svc.FindEfEntities(_paths, new EfEntityFilter(ContextType: "ShopContext", EntityType: "Gadget"), _options);

        var hit = Assert.Single(hits);
        Assert.Equal(typeof(ShopContext).FullName, hit.ContextTypeFullName);
    }

    [Fact]
    public void FindHandlers_ReportsRequestAndNotificationHandlers()
    {
        var hits = _svc.FindHandlers(_paths, new HandlerFilter(), _options);

        Assert.Contains(hits, h => h.HandlerTypeFullName == typeof(GetWidgetHandler).FullName && h.Kind == "request" &&
                                   h.MessageTypeFullName == typeof(GetWidget).FullName && h.ResponseTypeFullName == typeof(Widget).FullName);
        Assert.Contains(hits, h => h.HandlerTypeFullName == typeof(WidgetCreatedHandler).FullName && h.Kind == "notification" &&
                                   h.ResponseTypeFullName == null);
        Assert.DoesNotContain(hits, h => h.HandlerTypeFullName == typeof(AbstractWidgetHandler).FullName);
    }

    [Fact]
    public void FindHandlers_FiltersByMessageAndKind()
    {
        Assert.Single(_svc.FindHandlers(_paths, new HandlerFilter(MessageType: "WidgetCreated"), _options));
        Assert.Empty(_svc.FindHandlers(_paths, new HandlerFilter(MessageType: "WidgetCreated", Kind: "request"), _options));
    }

    [Fact]
    public void Scans_WithCancelledToken_Throw()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => _svc.FindEndpoints(_paths, new EndpointFilter(), _options, cancellationToken: cts.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => _svc.FindServiceRegistrations(_paths, new ServiceRegistrationFilter(), _options, cancellationToken: cts.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => _svc.FindEfEntities(_paths, new EfEntityFilter(), _options, cancellationToken: cts.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => _svc.FindHandlers(_paths, new HandlerFilter(), _options, cancellationToken: cts.Token));
    }

    [Fact]
    public void Scans_ReportProgressPerAssembly()
    {
        var reports = new List<ScanProgress>();
        _svc.FindHandlers(_paths, new HandlerFilter(), _options, new SynchronousProgress(reports.Add));

        var last = Assert.Single(reports);
        Assert.Equal(1, last.Completed);
        Assert.Equal(1, last.Total);
    }

    [Theory]
    [InlineData("<Map>b__0_0", "Map (lambda)")]
    [InlineData("<<Main>$>b__0_0", "<Main>$ (lambda)")]
    [InlineData("<Run>g__Local|0_1", "Run (lambda)")]
    [InlineData("<Main>$", null)]
    [InlineData("Plain", null)]
    public void CompilerGeneratedMethodName_RecognisesLambdas(string methodName, string? expected) =>
        Assert.Equal(expected, IlCallSiteWalker.CompilerGeneratedMethodName(methodName));

    [Theory]
    [InlineData("App.Program+<<Main>$>d__0", "MoveNext", "App.Program", "<Main>$")]
    [InlineData("App.Startup+<ConfigureAsync>d__3", "MoveNext", "App.Startup", "ConfigureAsync")]
    [InlineData("App.Startup+<>c", "<Configure>b__0_0", "App.Startup", "Configure (lambda)")]
    [InlineData("App.Startup", "Configure", "App.Startup", "Configure")]
    public void UserFacingCaller_MapsCompilerGeneratedCallers(string type, string method, string expectedType, string expectedMethod) =>
        Assert.Equal((expectedType, expectedMethod), IlCallSiteWalker.UserFacingCaller(type, method));

    private EndpointHit[] Endpoints() =>
        _svc.FindEndpoints(_paths, new EndpointFilter(), _options)
            .Where(h => (h.HandlerTypeFullName ?? h.RegisteredIn ?? "").StartsWith(FixtureNamespace, StringComparison.Ordinal))
            .ToArray();

    private sealed class SynchronousProgress(Action<ScanProgress> report) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => report(value);
    }
}
