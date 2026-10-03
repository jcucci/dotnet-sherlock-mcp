using System.Globalization;
using System.Text;
using Sherlock.MCP.Runtime.Contracts.Il;
using Sherlock.MCP.Runtime.Contracts.TypeAnalysis;

namespace Sherlock.MCP.Server.Shared;

public sealed record MermaidDiagram(string Diagram, int NodeCount, int EdgeCount, bool Truncated);

public static class Mermaid
{
    public const int DefaultMaxNodes = 50;
    public const int MaxNodesLimit = 200;
    public const int MaxDepth = 5;

    public static string? NormalizeFormat(string? format) =>
        (format ?? "json").Trim().ToLowerInvariant() is var normalized && normalized is "json" or "mermaid" ? normalized : null;

    public static string InvalidFormat() =>
        JsonHelpers.Error("InvalidFormat", "format must be 'json' or 'mermaid'");

    public static string TruncationNote(int maxNodes) =>
        $"Diagram truncated at maxNodes={maxNodes}; raise maxNodes (up to {MaxNodesLimit}) or narrow the request.";

    public static MermaidDiagram ClassDiagram(
        TypeHierarchy hierarchy, int maxNodes, IReadOnlyDictionary<string, string[]>? derivedBaseChains = null)
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        var interfaces = new HashSet<string>(hierarchy.AllInterfaces, StringComparer.Ordinal);
        var relations = new List<string>();
        var truncated = false;

        string? Node(string name)
        {
            if (ids.TryGetValue(name, out var id)) return id;
            if (ids.Count >= maxNodes)
            {
                truncated = true;
                return null;
            }
            id = $"n{ids.Count}";
            ids[name] = id;
            return id;
        }

        void Relate(string parent, string child, bool realization)
        {
            var parentId = Node(parent);
            var childId = Node(child);
            if (parentId != null && childId != null)
                relations.Add($"  {parentId} {(realization ? "<|.." : "<|--")} {childId}");
        }

        Node(hierarchy.TypeName);
        var child = hierarchy.TypeName;
        foreach (var baseType in hierarchy.InheritanceChain)
        {
            Relate(baseType, child, realization: false);
            child = baseType;
        }
        foreach (var iface in hierarchy.AllInterfaces)
            Relate(iface, hierarchy.TypeName, realization: true);
        var derivedTypes = hierarchy.DerivedTypes ?? [];
        var knownTypes = derivedTypes
            .Select(d => d.TypeFullName)
            .Prepend(hierarchy.TypeName)
            .GroupBy(GenericDefinitionKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var derived in derivedTypes)
        {
            var parent = NearestKnownAncestor(derived.TypeFullName, derivedBaseChains, knownTypes) ?? hierarchy.TypeName;
            var realization = parent == hierarchy.TypeName && derived.Kind == "interface";
            Relate(parent, derived.TypeFullName, realization);
        }

        var sb = new StringBuilder("classDiagram\n");
        foreach (var (name, id) in ids)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  class {id}[\"{Escape(name)}\"]\n");
            if (interfaces.Contains(name)) sb.Append(CultureInfo.InvariantCulture, $"  <<interface>> {id}\n");
        }
        foreach (var relation in relations)
            sb.Append(relation).Append('\n');

        return new MermaidDiagram(sb.ToString().TrimEnd('\n'), ids.Count, relations.Count, truncated);
    }

    public static MermaidDiagram Flowchart(CallGraph graph)
    {
        var sb = new StringBuilder("flowchart LR\n");
        foreach (var node in graph.Nodes)
            sb.Append(CultureInfo.InvariantCulture, $"  {node.Id}[\"{Escape(node.Display)}\"]{(node.External ? ":::external" : "")}\n");
        foreach (var edge in graph.Edges)
            sb.Append(edge.Kind is "call" or "callvirt"
                ? $"  {edge.From} --> {edge.To}\n"
                : $"  {edge.From} -->|{edge.Kind}| {edge.To}\n");
        if (graph.Nodes.Any(n => n.External))
            sb.Append("  classDef external stroke-dasharray: 4 4\n");

        return new MermaidDiagram(sb.ToString().TrimEnd('\n'), graph.Nodes.Length, graph.Edges.Length, graph.Truncated);
    }

    private static string? NearestKnownAncestor(
        string typeName, IReadOnlyDictionary<string, string[]>? baseChains, Dictionary<string, string> knownTypes)
    {
        if (baseChains == null || !baseChains.TryGetValue(typeName, out var chain)) return null;
        return chain
            .Select(GenericDefinitionKey)
            .Where(knownTypes.ContainsKey)
            .Select(key => knownTypes[key])
            .FirstOrDefault();
    }

    private static string GenericDefinitionKey(string typeName) =>
        typeName.IndexOf('<') is var open and >= 0 ? typeName[..open] : typeName;

    private static string Escape(string label) =>
        label.Replace("\"", "#quot;").Replace("<", "#lt;").Replace(">", "#gt;");
}
