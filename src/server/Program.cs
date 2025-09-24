using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Indexing;
using Sherlock.MCP.Runtime.Telemetry;
using Sherlock.MCP.Server.Middleware;
using System.Reflection;

// Handle version command before starting the application
if (args.Length > 0 && (args[0] == "--version" || args[0] == "-v"))
{
    var assembly = Assembly.GetExecutingAssembly();
    var version = assembly.GetName().Version?.ToString() ?? "Unknown";
    Console.WriteLine($"Sherlock MCP Server {version}");
    return 0;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddSingleton<RuntimeOptions>()
    .AddSingleton<IToolResponseCache, InMemoryToolResponseCache>()
    .AddSingleton<IAssemblyIndexService, NoopAssemblyIndexService>()
    .AddSingleton<ITelemetry, NoopTelemetry>()
    .AddSingleton<IMemberAnalysisService, MemberAnalysisService>()
    .AddSingleton<ITypeAnalysisService, TypeAnalysisService>()
    .AddSingleton<IXmlDocService, XmlDocService>()
    .AddSingleton<IProjectAnalysisService, ProjectAnalysisService>()
    .AddSingleton<ToolMiddleware>()
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
return 0;
