using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Handles;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Runtime.Telemetry;
using Sherlock.MCP.Server.Middleware;

namespace Sherlock.MCP.Benchmarks;

public sealed class ServiceGraph : IDisposable
{
    private ServiceGraph(RuntimeOptions options)
    {
        Options = options;
        Contexts = new SharedInspectionContextProvider(options);
        Members = new MemberAnalysisService(Contexts);
        Search = new SearchService(Contexts);
        ReverseLookup = new ReverseLookupService(Contexts);
        Il = new IlAnalysisService(Contexts, Contexts);
        Middleware = new ToolMiddleware(new InMemoryToolResponseCache(options), new NoopTelemetry(), options);
        Handles = new AssemblyHandleRegistry(options, new NoopTelemetry());
    }

    public RuntimeOptions Options { get; }

    public SharedInspectionContextProvider Contexts { get; }

    public IMemberAnalysisService Members { get; }

    public ISearchService Search { get; }

    public IReverseLookupService ReverseLookup { get; }

    public IIlAnalysisService Il { get; }

    public ToolMiddleware Middleware { get; }

    public IAssemblyHandleRegistry Handles { get; }

    public static ServiceGraph Create() => new(new RuntimeOptions
    {
        StateDirectory = Path.Combine(Path.GetTempPath(), "sherlock-benchmarks", Guid.NewGuid().ToString("N"))
    });

    public void Dispose()
    {
        Contexts.Dispose();
        if (Directory.Exists(Options.StateDirectory))
            Directory.Delete(Options.StateDirectory, recursive: true);
    }
}
