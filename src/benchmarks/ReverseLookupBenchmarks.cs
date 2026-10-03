using BenchmarkDotNet.Attributes;
using Sherlock.MCP.Runtime.Contracts.Il;
using Sherlock.MCP.Runtime.Contracts.ReverseLookup;

namespace Sherlock.MCP.Benchmarks;

public class ReverseLookupBenchmarks : WarmBenchmark
{
    protected override void Prime()
    {
        if (Scenarios.FindImplementations(Graph).Length == 0
            || Scenarios.FindReferences(Graph) == 0
            || Scenarios.FindInboundCallers(Graph).Length == 0)
            throw new InvalidOperationException("A reverse-lookup scenario returned no hits");
    }

    [Benchmark]
    public ImplementationHit[] FindImplementations() => Scenarios.FindImplementations(Graph);

    [Benchmark]
    public int FindReferences() => Scenarios.FindReferences(Graph);

    [Benchmark]
    public InboundCallHit[] FindInboundCallers() => Scenarios.FindInboundCallers(Graph);
}

[InvocationCount(1)]
public class ReverseLookupColdBenchmarks : ColdBenchmark
{
    [Benchmark]
    public ImplementationHit[] FindImplementations() => Scenarios.FindImplementations(Graph);

    [Benchmark]
    public int FindReferences() => Scenarios.FindReferences(Graph);

    [Benchmark]
    public InboundCallHit[] FindInboundCallers() => Scenarios.FindInboundCallers(Graph);
}
