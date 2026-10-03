using System.Reflection;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.Il;
using Sherlock.MCP.Tests.IlAnalysisFixtures;

namespace Sherlock.MCP.Tests;

public class CallGraphTests
{
    private readonly IIlAnalysisService _svc = new IlAnalysisService();
    private readonly string _testAssemblyPath = Assembly.GetExecutingAssembly().Location;
    private readonly IlAnalysisOptions _options = new();

    private static readonly string Subject = typeof(CallChainSubject).FullName!;
    private const string ListFullName = "System.Collections.Generic.List`1";

    [Fact]
    public void Depth1_ListsDirectCalleesOnly()
    {
        var graph = Graph(nameof(CallChainSubject.Entry), depth: 1);

        Assert.Equal(new[] { $"{Subject}.Entry", $"{Subject}.StepOne", "System.Console.WriteLine" }, Displays(graph));
        Assert.Equal(2, graph.Edges.Length);
        Assert.False(graph.Truncated);
    }

    [Fact]
    public void Depth3_ExpandsSameAssemblyCallees_AndKeepsExternalCallsAsLeaves()
    {
        var graph = Graph(nameof(CallChainSubject.Entry), depth: 3);

        var stepTwo = Node(graph, $"{Subject}.StepTwo");
        var console = Node(graph, "System.Console.WriteLine");
        Assert.False(stepTwo.External);
        Assert.True(console.External);
        Assert.Contains(graph.Edges, e => e.From == Node(graph, $"{Subject}.StepOne").Id && e.To == stepTwo.Id);
        Assert.Contains(graph.Edges, e => e.From == stepTwo.Id && e.To == console.Id);
        Assert.Single(graph.Nodes, n => n.Display == "System.Console.WriteLine");
    }

    [Fact]
    public void SelfRecursion_ProducesSelfEdge_AndTerminates()
    {
        var graph = Graph(nameof(CallChainSubject.Countdown), depth: 5);

        var root = Assert.Single(graph.Nodes);
        Assert.Contains(graph.Edges, e => e.From == root.Id && e.To == root.Id);
    }

    [Fact]
    public void MutualRecursion_VisitsEachMethodOnce()
    {
        var graph = Graph(nameof(CallChainSubject.Ping), depth: 5);

        Assert.Equal(new[] { $"{Subject}.Ping", $"{Subject}.Pong" }, Displays(graph));
        Assert.Equal(2, graph.Edges.Length);
    }

    [Fact]
    public void MaxNodes_TruncatesGraph()
    {
        var graph = Graph(nameof(CallChainSubject.Entry), depth: 3, maxNodes: 2);

        Assert.Equal(2, graph.Nodes.Length);
        Assert.True(graph.Truncated);
        Assert.All(graph.Edges, e => Assert.Contains(graph.Nodes, n => n.Id == e.To));
    }

    [Fact]
    public void GenericTypeCallees_InSameAssembly_AreExpanded()
    {
        var generic = typeof(GenericCallSubject<>).FullName!;
        var cache = typeof(LocalCache<>).FullName!;
        var graph = _svc.GetCallGraph(_testAssemblyPath, generic, nameof(GenericCallSubject<int>.Save), _options, depth: 3, maxNodes: 50)!;

        Assert.False(Node(graph, $"{generic}.Validate").External);
        Assert.False(Node(graph, $"{cache}.Put").External);
        Assert.False(Node(graph, $"{cache}.Store").External);
        Assert.True(Node(graph, "System.Console.WriteLine").External);
        Assert.Contains(graph.Edges, e => e.From == Node(graph, $"{generic}.Validate").Id && e.To == Node(graph, $"{ListFullName}.Add").Id);
    }

    [Fact]
    public void UnknownMethod_ReturnsNull() =>
        Assert.Null(_svc.GetCallGraph(_testAssemblyPath, Subject, "Missing", _options, depth: 1, maxNodes: 50));

    private CallGraph Graph(string methodName, int depth, int maxNodes = 50) =>
        _svc.GetCallGraph(_testAssemblyPath, Subject, methodName, _options, depth, maxNodes)
        ?? throw new InvalidOperationException($"{methodName} not found");

    private static string[] Displays(CallGraph graph) => graph.Nodes.Select(n => n.Display).ToArray();

    private static CallGraphNode Node(CallGraph graph, string display) => Assert.Single(graph.Nodes, n => n.Display == display);
}
