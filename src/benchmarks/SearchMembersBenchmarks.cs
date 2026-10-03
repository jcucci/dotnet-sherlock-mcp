using BenchmarkDotNet.Attributes;
using Sherlock.MCP.Runtime.Contracts.Common;
using Sherlock.MCP.Runtime.Contracts.Search;

namespace Sherlock.MCP.Benchmarks;

public class SearchMembersBenchmarks : WarmBenchmark
{
    [Params(BenchmarkCorpus.Small, BenchmarkCorpus.Large, BenchmarkCorpus.NuGet)]
    public string Corpus { get; set; } = BenchmarkCorpus.Small;

    protected override void Prime()
    {
        if (Scenarios.SearchMembers(Graph, Corpus).Total == 0)
            throw new InvalidOperationException($"search_members found nothing in {Corpus}");
    }

    [Benchmark]
    public PagedResult<MemberSearchHit> Warm() => Scenarios.SearchMembers(Graph, Corpus);
}

[InvocationCount(1)]
public class SearchMembersColdBenchmarks : ColdBenchmark
{
    [Params(BenchmarkCorpus.Small, BenchmarkCorpus.Large, BenchmarkCorpus.NuGet)]
    public string Corpus { get; set; } = BenchmarkCorpus.Small;

    [Benchmark]
    public PagedResult<MemberSearchHit> Cold() => Scenarios.SearchMembers(Graph, Corpus);
}
