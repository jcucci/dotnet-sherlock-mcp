using Sherlock.MCP.Runtime.Contracts.Il;
using Sherlock.MCP.Runtime.Contracts.ReverseLookup;

namespace Sherlock.MCP.Runtime;

public interface IIlAnalysisService
{
    MethodCallsResult? GetMethodCalls(
        string assemblyPath, string typeName, string methodName, IlAnalysisOptions options,
        CancellationToken cancellationToken = default);

    CallGraph? GetCallGraph(
        string assemblyPath, string typeName, string methodName, IlAnalysisOptions options, int depth, int maxNodes,
        CancellationToken cancellationToken = default);

    InboundCallHit[] FindInboundCallers(
        string[] assemblyPaths, string typeName, ReverseLookupOptions options,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
}
