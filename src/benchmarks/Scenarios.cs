using ModelContextProtocol.Protocol;
using Sherlock.MCP.Runtime.Contracts.Common;
using Sherlock.MCP.Runtime.Contracts.Il;
using Sherlock.MCP.Runtime.Contracts.ReverseLookup;
using Sherlock.MCP.Runtime.Contracts.Search;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Benchmarks;

public static class Scenarios
{
    public const string ReverseLookupTarget = "Microsoft.CodeAnalysis.ISymbol";
    public const string InboundCallersTarget = "Microsoft.CodeAnalysis.SyntaxTree";

    public static string TypeMembersTarget(string corpus) => corpus switch
    {
        BenchmarkCorpus.Small => "Sherlock.MCP.Runtime.MemberAnalysisService",
        BenchmarkCorpus.Large => "Microsoft.CodeAnalysis.CSharp.CSharpCompilation",
        _ => throw new ArgumentOutOfRangeException(nameof(corpus), corpus, "No type-members target for corpus entry")
    };

    public static CallToolResult TypeMembers(ServiceGraph graph, string corpus, bool noCache) =>
        MemberAnalysisTools.GetTypeMembers(
            graph.Members, graph.Contexts, graph.Middleware, graph.Options, graph.Handles,
            typeName: TypeMembersTarget(corpus),
            assemblyPath: BenchmarkCorpus.PathFor(corpus),
            kinds: "method",
            noCache: noCache);

    public static PagedResult<MemberSearchHit> SearchMembers(ServiceGraph graph, string corpus) =>
        graph.Search.SearchMembers(BenchmarkCorpus.PathFor(corpus), "Parse", new SearchOptions(), offset: 0, pageSize: 50);

    public static ImplementationHit[] FindImplementations(ServiceGraph graph) =>
        graph.ReverseLookup.FindImplementations(BenchmarkCorpus.ReverseLookupScope, ReverseLookupTarget, new ReverseLookupOptions());

    public static int FindReferences(ServiceGraph graph) =>
        graph.ReverseLookup.FindReferences(BenchmarkCorpus.ReverseLookupScope, ReverseLookupTarget, new ReverseLookupOptions()).Hits.Length;

    public static InboundCallHit[] FindInboundCallers(ServiceGraph graph) =>
        graph.Il.FindInboundCallers(BenchmarkCorpus.ReverseLookupScope, InboundCallersTarget, new ReverseLookupOptions());
}
