using BenchmarkDotNet.Attributes;

namespace Sherlock.MCP.Benchmarks;

public abstract class WarmBenchmark
{
    protected ServiceGraph Graph { get; private set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        Graph = ServiceGraph.Create();
        Prime();
    }

    [GlobalCleanup]
    public void Cleanup() => Graph.Dispose();

    protected abstract void Prime();
}

public abstract class ColdBenchmark
{
    protected ServiceGraph Graph { get; private set; } = null!;

    [IterationSetup]
    public void Setup() => Graph = ServiceGraph.Create();

    [IterationCleanup]
    public void Cleanup() => Graph.Dispose();
}
