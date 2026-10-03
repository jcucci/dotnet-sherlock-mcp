using BenchmarkDotNet.Attributes;
using ModelContextProtocol.Protocol;

namespace Sherlock.MCP.Benchmarks;

public class TypeMembersBenchmarks : WarmBenchmark
{
    [Params(BenchmarkCorpus.Small, BenchmarkCorpus.Large)]
    public string Corpus { get; set; } = BenchmarkCorpus.Small;

    protected override void Prime()
    {
        if (Scenarios.TypeMembers(Graph, Corpus, noCache: false).IsError == true)
            throw new InvalidOperationException($"get_type_members failed for {Scenarios.TypeMembersTarget(Corpus)}");
    }

    [Benchmark(Baseline = true)]
    public CallToolResult WarmContexts() => Scenarios.TypeMembers(Graph, Corpus, noCache: true);

    [Benchmark]
    public CallToolResult ResponseCacheHit() => Scenarios.TypeMembers(Graph, Corpus, noCache: false);
}

[InvocationCount(1)]
public class TypeMembersColdBenchmarks : ColdBenchmark
{
    [Params(BenchmarkCorpus.Small, BenchmarkCorpus.Large)]
    public string Corpus { get; set; } = BenchmarkCorpus.Small;

    [Benchmark]
    public CallToolResult Cold() => Scenarios.TypeMembers(Graph, Corpus, noCache: false);
}
