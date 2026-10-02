using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.ApiDiff;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Completions;
using Sherlock.MCP.Runtime.Decompilation;
using Sherlock.MCP.Runtime.Handles;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Runtime.SourceLink;
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

if (!ToolProfile.TryResolve(args, Environment.GetEnvironmentVariable(ToolProfile.EnvironmentVariable), out var toolProfile, out var profileError))
{
    Console.Error.WriteLine(profileError);
    return 1;
}

var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
});
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

if (toolProfile.IsRestricted)
{
    builder.Services.PostConfigure<McpServerOptions>(options =>
    {
        if (options.ToolCollection is not { } tools)
            return;
        foreach (var tool in tools.ToArray().Where(tool => !toolProfile.Includes(tool.ProtocolTool.Name)))
            tools.Remove(tool);
    });
}

// The tool set is scanned from the assembly and narrowed by the tool profile once at startup; it
// never varies per caller, so clients may cache tools/list for a long time and share it across
// authorization contexts.
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
    .AddSingleton<IDecompilerService, DecompilerService>()
    .AddSingleton<ISourceFetcher>(services => new SourceFetcher(
        services.GetRequiredService<RuntimeOptions>(),
        new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan }))
    .AddSingleton<IOriginalSourceService, OriginalSourceService>()
    .AddSingleton<ISearchService, SearchService>()
    .AddSingleton<IApiDiffService, ApiDiffService>()
    .AddSingleton<ICompletionService, CompletionService>()
    .AddSingleton<IAssemblyHandleRegistry, AssemblyHandleRegistry>()
    .AddSingleton<ToolMiddleware>()
    .AddMcpServer(options => options.ServerInstructions = ServerInstructions.Text)
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .WithPromptsFromAssembly()
    .WithCompleteHandler(CompletionHandler.HandleAsync)
    .WithRequestFilters(filters => filters
        .AddCallToolFilter(next => async (request, cancellationToken) =>
            StructuredOutput.Apply(
                ToolErrorFlag.Apply(await next(request, cancellationToken)),
                request.MatchedPrimitive as ModelContextProtocol.Server.McpServerTool))
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
        .AddListPromptsFilter(next => async (request, cancellationToken) =>
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
