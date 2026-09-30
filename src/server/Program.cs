using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Completions;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Runtime.Telemetry;
using Sherlock.MCP.Server.Completions;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Shared;
using System.Reflection;

// Handle version command before starting the application
if (args.Length > 0 && (args[0] == "--version" || args[0] == "-v"))
{
    var assembly = Assembly.GetExecutingAssembly();
    var version = assembly.GetName().Version?.ToString() ?? "Unknown";
    Console.WriteLine($"Sherlock MCP Server {version}");
    return 0;
}

var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
});
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

// The tool set is scanned from the assembly once at startup and never varies per caller,
// so clients may cache tools/list for a long time and share it across authorization contexts.
var toolListTimeToLive = TimeSpan.FromHours(1);

// ttlMs and cacheScope were introduced by the 2026-07-28 revision; earlier revisions reject them
// as unrecognized keys. The SDK only defaults these fields, it does not strip ones we set, so the
// version gate has to live here. Revisions are YYYY-MM-DD, so an ordinal compare is chronological.
static bool SupportsCachingHints<TParams>(ModelContextProtocol.Server.RequestContext<TParams> request) =>
    (request.JsonRpcRequest.Context?.ProtocolVersion ?? request.Server.NegotiatedProtocolVersion) is { } version
    && string.CompareOrdinal(version, "2026-07-28") >= 0;

builder.Services
    .AddSingleton<RuntimeOptions>()
    .AddSingleton<IRecentAssemblyRegistry, RecentAssemblyRegistry>()
    .AddSingleton<IInspectionContextProvider, SharedInspectionContextProvider>()
    .AddSingleton<IToolResponseCache, InMemoryToolResponseCache>()
    .AddSingleton<ITelemetry, NoopTelemetry>()
    .AddSingleton<IMemberAnalysisService, MemberAnalysisService>()
    .AddSingleton<ITypeAnalysisService, TypeAnalysisService>()
    .AddSingleton<IXmlDocService, XmlDocService>()
    .AddSingleton<IProjectAnalysisService, ProjectAnalysisService>()
    .AddSingleton<IReverseLookupService, ReverseLookupService>()
    .AddSingleton<IIlAnalysisService, IlAnalysisService>()
    .AddSingleton<ISearchService, SearchService>()
    .AddSingleton<ICompletionService, CompletionService>()
    .AddSingleton<ToolMiddleware>()
    .AddMcpServer(options => options.ServerInstructions = ServerInstructions.Text)
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .WithCompleteHandler(CompletionHandler.HandleAsync)
    .WithRequestFilters(filters => filters
        .AddCallToolFilter(next => async (request, cancellationToken) =>
            ToolErrorFlag.Apply(await next(request, cancellationToken)))
        .AddListToolsFilter(next => async (request, cancellationToken) =>
        {
            var result = await next(request, cancellationToken);

            if (request.Params?.Cursor is null && result.NextCursor is null)
                result.Tools = [.. result.Tools.OrderBy(tool => tool.Name, StringComparer.Ordinal)];

            if (SupportsCachingHints(request))
            {
                result.TimeToLive = toolListTimeToLive;
                result.CacheScope = CacheScope.Public;
            }

            return result;
        })
        .AddListResourceTemplatesFilter(next => async (request, cancellationToken) =>
        {
            var result = await next(request, cancellationToken);

            if (SupportsCachingHints(request))
            {
                result.TimeToLive = toolListTimeToLive;
                result.CacheScope = CacheScope.Public;
            }

            return result;
        })
        .AddReadResourceFilter(next => async (request, cancellationToken) =>
        {
            var result = await next(request, cancellationToken);

            // Resource contents are derived from files on the caller's machine, so they are private
            // and only fresh for as long as the response cache would reuse the same payload.
            if (SupportsCachingHints(request))
            {
                var options = request.Services!.GetRequiredService<RuntimeOptions>();
                result.TimeToLive = TimeSpan.FromSeconds(Math.Max(1, options.CacheTtlSeconds));
                result.CacheScope = CacheScope.Private;
            }

            return result;
        }));

await builder.Build().RunAsync();
return 0;
