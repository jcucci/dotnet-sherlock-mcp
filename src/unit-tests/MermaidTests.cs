using Sherlock.MCP.Runtime.Contracts.Il;
using Sherlock.MCP.Runtime.Contracts.TypeAnalysis;
using Sherlock.MCP.Server.Shared;

namespace Sherlock.MCP.Tests;

public class MermaidTests
{
    [Fact]
    public void ClassDiagram_DrawsInheritanceRealizationAndDerivedArrows()
    {
        var hierarchy = Hierarchy(
            "App.Repo",
            chain: ["App.RepoBase", "System.Object"],
            interfaces: ["App.IRepo"],
            derived: [new DerivedTypeRef("App.SqlRepo", "/a.dll", "baseType")]);

        var diagram = Mermaid.ClassDiagram(hierarchy, maxNodes: 50);

        Assert.Equal(
            """
            classDiagram
              class n0["App.Repo"]
              class n1["App.RepoBase"]
              class n2["System.Object"]
              class n3["App.IRepo"]
              <<interface>> n3
              class n4["App.SqlRepo"]
              n1 <|-- n0
              n2 <|-- n1
              n3 <|.. n0
              n0 <|-- n4
            """.ReplaceLineEndings("\n"),
            diagram.Diagram);
        Assert.Equal(5, diagram.NodeCount);
        Assert.Equal(4, diagram.EdgeCount);
        Assert.False(diagram.Truncated);
    }

    [Fact]
    public void ClassDiagram_InterfaceTarget_UsesRealizationForImplementers()
    {
        var hierarchy = Hierarchy("App.IRepo", chain: [], interfaces: [], derived: [new DerivedTypeRef("App.Repo", "/a.dll", "interface")]);

        Assert.Contains("n0 <|.. n1", Mermaid.ClassDiagram(hierarchy, maxNodes: 50).Diagram);
    }

    [Fact]
    public void ClassDiagram_IndirectDescendants_HangOffTheirNearestKnownAncestor()
    {
        var hierarchy = Hierarchy("App.A", chain: ["System.Object"], interfaces: [], derived:
        [
            new DerivedTypeRef("App.C", "/a.dll", "baseType"),
            new DerivedTypeRef("App.B", "/a.dll", "baseType")
        ]);
        var chains = new Dictionary<string, string[]>
        {
            ["App.B"] = ["App.A", "System.Object"],
            ["App.C"] = ["App.B", "App.A", "System.Object"]
        };

        var diagram = Mermaid.ClassDiagram(hierarchy, maxNodes: 50, chains).Diagram;

        Assert.Contains("class n2[\"App.B\"]", diagram);
        Assert.Contains("class n3[\"App.C\"]", diagram);
        Assert.Contains("n2 <|-- n3", diagram);
        Assert.Contains("n0 <|-- n2", diagram);
        Assert.DoesNotContain("n0 <|-- n3", diagram);
    }

    [Fact]
    public void ClassDiagram_InterfaceInheritedThroughBase_IsNotDrawnAsDirectRealization()
    {
        var hierarchy = Hierarchy("App.IRepo<T>", chain: [], interfaces: [], derived:
        [
            new DerivedTypeRef("App.RepoBase<T>", "/a.dll", "interface"),
            new DerivedTypeRef("App.SqlRepo", "/a.dll", "interface")
        ]);
        var chains = new Dictionary<string, string[]>
        {
            ["App.RepoBase<T>"] = ["System.Object"],
            ["App.SqlRepo"] = ["App.RepoBase<System.Int32>", "System.Object"]
        };

        var diagram = Mermaid.ClassDiagram(hierarchy, maxNodes: 50, chains).Diagram;

        Assert.Contains("n0 <|.. n1", diagram);
        Assert.Contains("n1 <|-- n2", diagram);
        Assert.DoesNotContain("n0 <|.. n2", diagram);
    }

    [Fact]
    public void ClassDiagram_EscapesGenericsAndQuotes()
    {
        var hierarchy = Hierarchy("App.Box<T>", chain: ["App.\"Odd\"+Nested"], interfaces: [], derived: null);

        var diagram = Mermaid.ClassDiagram(hierarchy, maxNodes: 50).Diagram;

        Assert.Contains("class n0[\"App.Box#lt;T#gt;\"]", diagram);
        Assert.Contains("class n1[\"App.#quot;Odd#quot;+Nested\"]", diagram);
    }

    [Fact]
    public void ClassDiagram_StopsAtMaxNodes()
    {
        var hierarchy = Hierarchy("App.Repo", chain: ["App.RepoBase", "System.Object"], interfaces: ["App.IRepo"], derived: null);

        var diagram = Mermaid.ClassDiagram(hierarchy, maxNodes: 2);

        Assert.Equal(2, diagram.NodeCount);
        Assert.Equal(1, diagram.EdgeCount);
        Assert.True(diagram.Truncated);
    }

    [Fact]
    public void Flowchart_StylesExternalNodesAndLabelsNonCallEdges()
    {
        var graph = new CallGraph(
            "App.Svc", "Run", 1, false, 2,
            [new CallGraphNode("n0", "App.Svc.Run", false), new CallGraphNode("n1", "System.Collections.Generic.List`1..ctor", true)],
            [new CallGraphEdge("n0", "n1", "newobj"), new CallGraphEdge("n0", "n0", "call")],
            Truncated: false);

        var diagram = Mermaid.Flowchart(graph);

        Assert.Equal(
            """
            flowchart LR
              n0["App.Svc.Run"]
              n1["System.Collections.Generic.List`1..ctor"]:::external
              n0 -->|newobj| n1
              n0 --> n0
              classDef external stroke-dasharray: 4 4
            """.ReplaceLineEndings("\n"),
            diagram.Diagram);
    }

    [Theory]
    [InlineData(null, "json")]
    [InlineData(" Mermaid ", "mermaid")]
    [InlineData("dot", null)]
    public void NormalizeFormat_AcceptsJsonAndMermaidOnly(string? input, string? expected) =>
        Assert.Equal(expected, Mermaid.NormalizeFormat(input));

    private static TypeHierarchy Hierarchy(string type, string[] chain, string[] interfaces, DerivedTypeRef[]? derived) =>
        new(type, chain, interfaces, [], derived, null);
}
