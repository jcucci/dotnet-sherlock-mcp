using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.ReverseLookup;
using Sherlock.MCP.Runtime.FrameworkPatterns;
using Sherlock.MCP.Runtime.Handles;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Shared;
using System.ComponentModel;
using System.Text.Json;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class FrameworkPatternTools
{
    private sealed record ScanRequest(
        string ToolName,
        string Kind,
        string? AssemblyPath,
        string? AssemblyHandle,
        string[]? AdditionalAssemblies,
        int? MaxItems,
        int? Skip,
        string? ContinuationToken,
        string Projection,
        bool NoCache,
        object Filters);

    [McpServerTool(Title = "Find Endpoints", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Finds ASP.NET Core HTTP endpoints across one or more assemblies: MVC/API controller actions (attribute routes combined from [Route]/[Http*] with [controller]/[action] substituted) and minimal APIs (Map{Get,Post,Put,Delete,Patch,Methods,Group,Fallback} calls read from IL). Minimal-API routes and handlers come from string literals and method references next to the call, so routes held in variables or fields are reported as null. Returns a lean summary ({ kind, httpMethods, route, handler }) by default; projection='full' adds assemblyPath, handlerType, handlerMethod and registeredIn.")]
    public static CallToolResult FindEndpoints(
        IFrameworkPatternService patterns,
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        IAssemblyHandleRegistry handles,
        [Description("Path to the primary .NET assembly file (.dll or .exe). Omit when passing assemblyHandle.")] string? assemblyPath = null,
        [Description("Handle returned by open_assembly; pass instead of assemblyPath (it also supplies the additionalAssemblies it was opened with)")] string? assemblyHandle = null,
        [Description("Optional additional assembly paths to include in the search scope")] string[]? additionalAssemblies = null,
        [Description("Only endpoints whose route template contains this text (case-insensitive)")] string? routeContains = null,
        [Description("Only endpoints that accept this HTTP method (e.g. 'GET'); endpoints without an explicit method always match")] string? httpMethod = null,
        [Description("Include non-public controllers (default: false). Minimal-API calls are read from every method body regardless.")] bool includeNonPublic = false,
        [Description("Maximum items to return (default: 50)")] int? maxItems = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Response shape. 'summary' (default, token-lean): { kind, httpMethods, route, handler }. 'full': adds assemblyPath, handlerType, handlerMethod, registeredIn.")] string projection = "summary",
        [Description("Bypass cache for this request")] bool noCache = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default) =>
        Run(
            middleware, runtimeOptions, handles,
            new ScanRequest("find_endpoints", "frameworkpatterns.endpoints", assemblyPath, assemblyHandle, additionalAssemblies,
                maxItems, skip, continuationToken, projection, noCache, new { routeContains, httpMethod, includeNonPublic }),
            paths => patterns.FindEndpoints(paths, new EndpointFilter(routeContains, httpMethod), Options(includeNonPublic), ProgressAdapter.ForPhase(progress), cancellationToken),
            h => new { kind = h.Kind, httpMethods = h.HttpMethods, route = h.RouteTemplate, handler = Handler(h) },
            h => new
            {
                kind = h.Kind,
                httpMethods = h.HttpMethods,
                route = h.RouteTemplate,
                handler = Handler(h),
                assemblyPath = h.AssemblyPath,
                handlerType = h.HandlerTypeFullName,
                handlerMethod = h.HandlerMethod,
                registeredIn = h.RegisteredIn
            },
            h => (h.AssemblyPath, h.TypeMetadataName),
            "find endpoints");

    [McpServerTool(Title = "Find Service Registrations", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Finds Microsoft.Extensions.DependencyInjection registrations by reading IL for calls to Add{Singleton,Scoped,Transient}, TryAdd*, AddKeyed*, AddHostedService and ServiceDescriptor factories, across one or more assemblies. Service/implementation types come from generic arguments or typeof(...) operands; factory and instance registrations report implementation=null. Every method body is scanned (registrations usually live in non-public Program code). Returns a lean summary ({ lifetime, service, implementation, registeredIn }) by default; projection='full' adds assemblyPath, registrationMethod, registrationKind, isTryAdd and isKeyed.")]
    public static CallToolResult FindServiceRegistrations(
        IFrameworkPatternService patterns,
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        IAssemblyHandleRegistry handles,
        [Description("Path to the primary .NET assembly file (.dll or .exe). Omit when passing assemblyHandle.")] string? assemblyPath = null,
        [Description("Handle returned by open_assembly; pass instead of assemblyPath (it also supplies the additionalAssemblies it was opened with)")] string? assemblyHandle = null,
        [Description("Optional additional assembly paths to include in the search scope")] string[]? additionalAssemblies = null,
        [Description("Only registrations of this service type (simple or full name; open generic accepted, e.g. 'IRepository')")] string? serviceType = null,
        [Description("Only registrations with this implementation type (simple or full name)")] string? implementationType = null,
        [Description("Only registrations with this lifetime: 'singleton', 'scoped' or 'transient'")] string? lifetime = null,
        [Description("Case sensitive type-name matching (default: false)")] bool caseSensitive = false,
        [Description("Maximum items to return (default: 50)")] int? maxItems = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Response shape. 'summary' (default, token-lean): { lifetime, service, implementation, registeredIn }. 'full': adds assemblyPath, registrationMethod, registrationKind, isTryAdd, isKeyed.")] string projection = "summary",
        [Description("Bypass cache for this request")] bool noCache = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default) =>
        Run(
            middleware, runtimeOptions, handles,
            new ScanRequest("find_service_registrations", "frameworkpatterns.services", assemblyPath, assemblyHandle, additionalAssemblies,
                maxItems, skip, continuationToken, projection, noCache, new { serviceType, implementationType, lifetime, caseSensitive }),
            paths => patterns.FindServiceRegistrations(paths, new ServiceRegistrationFilter(serviceType, implementationType, lifetime),
                new ReverseLookupOptions(CaseSensitive: caseSensitive), ProgressAdapter.ForPhase(progress), cancellationToken),
            h => new { lifetime = h.Lifetime, service = h.ServiceTypeFullName, implementation = h.ImplementationTypeFullName, registeredIn = RegisteredIn(h) },
            h => new
            {
                lifetime = h.Lifetime,
                service = h.ServiceTypeFullName,
                implementation = h.ImplementationTypeFullName,
                registeredIn = RegisteredIn(h),
                assemblyPath = h.AssemblyPath,
                registrationMethod = h.RegistrationMethod,
                registrationKind = h.RegistrationKind,
                isTryAdd = h.IsTryAdd,
                isKeyed = h.IsKeyed
            },
            h => (h.AssemblyPath, h.TypeMetadataName),
            "find service registrations");

    [McpServerTool(Title = "Find EF Entities", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Finds Entity Framework Core entities: the DbSet<T> properties (declared or inherited) of every DbContext subclass across one or more assemblies. Entities configured only in OnModelCreating are not listed. Returns a lean summary ({ context, property, entity }) by default; projection='full' adds assemblyPath.")]
    public static CallToolResult FindEfEntities(
        IFrameworkPatternService patterns,
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        IAssemblyHandleRegistry handles,
        [Description("Path to the primary .NET assembly file (.dll or .exe). Omit when passing assemblyHandle.")] string? assemblyPath = null,
        [Description("Handle returned by open_assembly; pass instead of assemblyPath (it also supplies the additionalAssemblies it was opened with)")] string? assemblyHandle = null,
        [Description("Optional additional assembly paths to include in the search scope")] string[]? additionalAssemblies = null,
        [Description("Only this DbContext type (simple or full name)")] string? contextType = null,
        [Description("Only this entity type (simple or full name)")] string? entityType = null,
        [Description("Case sensitive type-name matching (default: false)")] bool caseSensitive = false,
        [Description("Include non-public DbContext types (default: false)")] bool includeNonPublic = false,
        [Description("Maximum items to return (default: 50)")] int? maxItems = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Response shape. 'summary' (default, token-lean): { context, property, entity }. 'full': adds assemblyPath.")] string projection = "summary",
        [Description("Bypass cache for this request")] bool noCache = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default) =>
        Run(
            middleware, runtimeOptions, handles,
            new ScanRequest("find_ef_entities", "frameworkpatterns.efentities", assemblyPath, assemblyHandle, additionalAssemblies,
                maxItems, skip, continuationToken, projection, noCache, new { contextType, entityType, caseSensitive, includeNonPublic }),
            paths => patterns.FindEfEntities(paths, new EfEntityFilter(contextType, entityType),
                new ReverseLookupOptions(CaseSensitive: caseSensitive, IncludeNonPublic: includeNonPublic), ProgressAdapter.ForPhase(progress), cancellationToken),
            h => new { context = h.ContextTypeFullName, property = h.PropertyName, entity = h.EntityTypeFullName },
            h => new { context = h.ContextTypeFullName, property = h.PropertyName, entity = h.EntityTypeFullName, assemblyPath = h.AssemblyPath },
            h => (h.AssemblyPath, h.TypeMetadataName),
            "find EF entities");

    [McpServerTool(Title = "Find Handlers", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Finds MediatR-style handlers across one or more assemblies: concrete types implementing IRequestHandler<,>/IRequestHandler<>, INotificationHandler<>, IStreamRequestHandler<,> or IPipelineBehavior<,> from the MediatR or Mediator namespaces. Returns a lean summary ({ kind, handler, message, response }) by default; projection='full' adds assemblyPath and interface.")]
    public static CallToolResult FindHandlers(
        IFrameworkPatternService patterns,
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        IAssemblyHandleRegistry handles,
        [Description("Path to the primary .NET assembly file (.dll or .exe). Omit when passing assemblyHandle.")] string? assemblyPath = null,
        [Description("Handle returned by open_assembly; pass instead of assemblyPath (it also supplies the additionalAssemblies it was opened with)")] string? assemblyHandle = null,
        [Description("Optional additional assembly paths to include in the search scope")] string[]? additionalAssemblies = null,
        [Description("Only handlers of this request/notification type (simple or full name)")] string? messageType = null,
        [Description("Only this handler kind: 'request', 'notification', 'stream' or 'behavior'")] string? kind = null,
        [Description("Case sensitive type-name matching (default: false)")] bool caseSensitive = false,
        [Description("Include non-public handler types (default: false)")] bool includeNonPublic = false,
        [Description("Maximum items to return (default: 50)")] int? maxItems = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Response shape. 'summary' (default, token-lean): { kind, handler, message, response }. 'full': adds assemblyPath, interface.")] string projection = "summary",
        [Description("Bypass cache for this request")] bool noCache = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default) =>
        Run(
            middleware, runtimeOptions, handles,
            new ScanRequest("find_handlers", "frameworkpatterns.handlers", assemblyPath, assemblyHandle, additionalAssemblies,
                maxItems, skip, continuationToken, projection, noCache, new { messageType, kind, caseSensitive, includeNonPublic }),
            paths => patterns.FindHandlers(paths, new HandlerFilter(messageType, kind),
                new ReverseLookupOptions(CaseSensitive: caseSensitive, IncludeNonPublic: includeNonPublic), ProgressAdapter.ForPhase(progress), cancellationToken),
            h => new { kind = h.Kind, handler = h.HandlerTypeFullName, message = h.MessageTypeFullName, response = h.ResponseTypeFullName },
            h => new
            {
                kind = h.Kind,
                handler = h.HandlerTypeFullName,
                message = h.MessageTypeFullName,
                response = h.ResponseTypeFullName,
                assemblyPath = h.AssemblyPath,
                @interface = h.InterfaceFullName
            },
            h => (h.AssemblyPath, h.TypeMetadataName),
            "find handlers");

    private static ReverseLookupOptions Options(bool includeNonPublic) => new(IncludeNonPublic: includeNonPublic);

    private static string? Handler(EndpointHit hit) =>
        hit.HandlerTypeFullName is null ? null : $"{hit.HandlerTypeFullName}.{hit.HandlerMethod}";

    private static string RegisteredIn(ServiceRegistrationHit hit) => $"{hit.RegisteringTypeFullName}.{hit.RegisteringMethod}";

    private static CallToolResult Run<THit>(
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        IAssemblyHandleRegistry handles,
        ScanRequest request,
        Func<string[], THit[]> scan,
        Func<THit, object> summary,
        Func<THit, object> full,
        Func<THit, (string AssemblyPath, string? TypeName)> link,
        string operation)
    {
        try
        {
            var target = AssemblyScope.ResolveTarget(handles, request.AssemblyPath, request.AssemblyHandle, request.AdditionalAssemblies);
            if (target.Error != null) return ToolResponse.Result(target.Error);
            var scope = AssemblyScope.BuildAndValidate(target.Path, target.AdditionalAssemblies);
            if (scope.Error != null) return ToolResponse.Result(scope.Error);

            var projection = (request.Projection ?? "summary").Trim().ToLowerInvariant();
            if (projection != "summary" && projection != "full")
                return ToolResponse.Result(JsonHelpers.Error("InvalidProjection", "projection must be 'summary' or 'full'"));

            var scopeKey = CacheKeyHelper.AssemblyScopeStamp(scope.Paths);
            var filtersKey = JsonSerializer.Serialize(request.Filters, JsonHelpers.DefaultOptions);
            var saltSeed = CacheKeyHelper.Build($"{request.Kind}.salt", scopeKey, filtersKey);
            var cacheKey = CacheKeyHelper.Build(request.Kind, scopeKey, filtersKey, request.MaxItems, request.ContinuationToken, request.Skip, projection);

            return middleware.Execute(cacheKey, () =>
            {
                var allHits = scan(scope.Paths);
                var pageSize = Math.Max(1, request.MaxItems ?? runtimeOptions.GetMaxItemsForTool(request.ToolName));
                var salt = TokenHelper.MakeSalt(saltSeed);
                var offset = 0;

                if (!string.IsNullOrWhiteSpace(request.ContinuationToken))
                {
                    if (!TokenHelper.TryParse(request.ContinuationToken!, out offset, out var parsedSalt) || parsedSalt != salt)
                        return JsonHelpers.Error("InvalidContinuationToken", "The continuation token is invalid or expired.");
                }
                else if (request.Skip is > 0)
                {
                    offset = request.Skip.Value;
                }

                var page = allHits.Skip(offset).Take(pageSize).ToArray();
                var nextOffset = offset + page.Length;
                var nextToken = nextOffset < allHits.Length ? TokenHelper.Make(nextOffset, salt) : null;
                var results = page.Select(projection == "summary" ? summary : full).ToArray();

                var resultsJson = JsonSerializer.Serialize(results, JsonHelpers.DefaultOptions);
                var result = new
                {
                    filters = request.Filters,
                    scope = scope.Paths,
                    projection,
                    total = allHits.Length,
                    count = page.Length,
                    nextToken,
                    pagination = PaginationMetadata.Create(allHits.Length, page.Length, nextToken, resultsJson.Length),
                    results
                };

                var links = ResourceUris.TypeLinks(page.Select(link).Where(l => l.TypeName is not null).Select(l => (l.AssemblyPath, l.TypeName!)));
                var sizeError = ResponseSizeHelper.ValidateResponseSize(new { result, links }, request.ToolName);
                if (sizeError != null) return sizeError;

                return new ToolResponse(JsonHelpers.Envelope(request.Kind, result), links);
            }, request.NoCache);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResponse.Result(ToolErrors.FromException(ex, operation));
        }
    }
}
