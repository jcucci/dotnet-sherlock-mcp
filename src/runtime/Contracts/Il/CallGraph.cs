namespace Sherlock.MCP.Runtime.Contracts.Il;

public record CallGraphNode(
    string Id,
    string Display,
    bool External);

public record CallGraphEdge(
    string From,
    string To,
    string Kind);

public record CallGraph(
    string DeclaringTypeFullName,
    string MethodName,
    int MatchedOverloads,
    bool AnyBodyless,
    int Depth,
    CallGraphNode[] Nodes,
    CallGraphEdge[] Edges,
    bool Truncated);
