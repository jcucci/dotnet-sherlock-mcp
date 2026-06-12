using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Indexing;
using Sherlock.MCP.Runtime.Inspection;
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

var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
});
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

const string serverInstructions =
    """
    Sherlock provides .NET assembly introspection via reflection. Prefer these tools over guessing about .NET APIs.

    Locate the assembly first with find_assembly_by_class_name, find_assembly_by_file_name, find_assembly_by_nuget_package, or get_project_output_paths rather than hardcoding bin/Debug/<tfm> paths (the target framework varies).

    Work narrow-to-wide and stay token-lean: use search_members when you know a member name but not its declaring type, or get_types_from_assembly to browse; then get_type_info; then filtered get_type_methods / get_type_properties (nameContains, hasAttributeContains). get_type_methods returns a lean 'summary' by default - pass projection='full' only when you need parameters, attributes, or modifiers (get_type_properties / get_type_fields / get_type_events / get_type_constructors have a single fixed shape and take no projection). Avoid get_all_type_members / analyze_type on large types.

    For relationships use find_implementations_of, find_methods_returning, find_extension_methods_for, and find_references_to (set analysisDepth='il' to resolve inbound callers); use get_method_calls to see what a method body invokes.

    Prefer full type names (Namespace.Type). Tool names are snake_case; argument names are camelCase.
    """;

builder.Services
    .AddSingleton<RuntimeOptions>()
    .AddSingleton<IInspectionContextProvider, SharedInspectionContextProvider>()
    .AddSingleton<IToolResponseCache, InMemoryToolResponseCache>()
    .AddSingleton<IAssemblyIndexService, NoopAssemblyIndexService>()
    .AddSingleton<ITelemetry, NoopTelemetry>()
    .AddSingleton<IMemberAnalysisService, MemberAnalysisService>()
    .AddSingleton<ITypeAnalysisService, TypeAnalysisService>()
    .AddSingleton<IXmlDocService, XmlDocService>()
    .AddSingleton<IProjectAnalysisService, ProjectAnalysisService>()
    .AddSingleton<IReverseLookupService, ReverseLookupService>()
    .AddSingleton<IIlAnalysisService, IlAnalysisService>()
    .AddSingleton<ISearchService, SearchService>()
    .AddSingleton<ToolMiddleware>()
    .AddMcpServer(options => options.ServerInstructions = serverInstructions)
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
return 0;
