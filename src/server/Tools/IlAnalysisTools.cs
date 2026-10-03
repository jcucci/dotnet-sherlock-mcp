using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.Il;
using Sherlock.MCP.Runtime.Handles;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Schemas;
using Sherlock.MCP.Server.Shared;
using System.ComponentModel;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class IlAnalysisTools
{
    private static readonly string[] MethodNotFoundAlternatives = { "get_type_members", "analyze_method" };

    [McpServerTool(Title = "Get Method Calls", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolEnvelope<MethodCallsData>))]
    [Description("Analyzes a method's IL body to list what it calls and which fields it touches (the 'what does this method call?' question that signature-level tools can't answer). Aggregates across all overloads of the method name. Returns a lean summary by default (distinct target names); projection='full' adds per-call kind (call/callvirt/newobj/ldftn) and the source overload signature. format='mermaid' returns a Mermaid flowchart call graph instead, following same-assembly callees transitively up to depth levels (external calls are leaves).")]
    public static CallToolResult GetMethodCalls(
        IIlAnalysisService ilAnalysis,
        ToolMiddleware middleware,
        IAssemblyHandleRegistry handles,
        [Description("Type that declares the method. Simple name, full name, or open-generic form accepted.")] string typeName,
        [Description("Method name to analyze. Use '.ctor' for instance constructors or '.cctor' for the static constructor. All overloads with this name are aggregated.")] string methodName,
        [Description("Path to the .NET assembly file (.dll or .exe) that declares the method. Omit when passing assemblyHandle.")] string? assemblyPath = null,
        [Description("Handle returned by open_assembly; pass instead of assemblyPath")] string? assemblyHandle = null,
        [Description("Case sensitive type-name matching (default: false)")] bool caseSensitive = false,
        [Description("Include non-public methods and the non-public declaring type (default: false)")] bool includeNonPublic = false,
        [Description("Response shape. 'summary' (default, token-lean): distinct target names only. 'full': adds { target, kind, sourceMethod } per call and { target, access, sourceMethod } per field access.")] string projection = "summary",
        [Description("Output format. 'json' (default): calls and field accesses per projection. 'mermaid': { diagram, nodeCount, edgeCount, truncated, note } where diagram is a Mermaid flowchart of the call graph.")] string format = "json",
        [Description("Call-graph depth when format='mermaid' (default 1, max 5). Only methods defined in the same assembly are expanded.")] int depth = 1,
        [Description("Maximum diagram nodes when format='mermaid' (default 50, max 200)")] int maxNodes = Mermaid.DefaultMaxNodes,
        [Description("Bypass cache for this request")] bool noCache = false,
        RequestContext<CallToolRequestParams>? context = null,
        CancellationToken cancellationToken = default)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            var target = AssemblyScope.ResolveTarget(handles, assemblyPath, assemblyHandle);
            if (target.Error != null)
                return ToolResponse.Result(target.Error);
            assemblyPath = target.Path;
            if (string.IsNullOrWhiteSpace(typeName))
                return ToolResponse.Result(JsonHelpers.Error("InvalidArgument", "typeName is required"));
            if (string.IsNullOrWhiteSpace(methodName))
                return ToolResponse.Result(JsonHelpers.Error("InvalidArgument", "methodName is required"));

            var normalizedProjection = (projection ?? "summary").Trim().ToLowerInvariant();
            if (normalizedProjection != "summary" && normalizedProjection != "full")
                return ToolResponse.Result(JsonHelpers.Error("InvalidProjection", "projection must be 'summary' or 'full'"));

            var normalizedFormat = Mermaid.NormalizeFormat(format);
            if (normalizedFormat == null)
                return ToolResponse.Result(Mermaid.InvalidFormat());
            if (depth < 1 || depth > Mermaid.MaxDepth)
                return ToolResponse.Result(JsonHelpers.Error("InvalidArgument", $"depth must be between 1 and {Mermaid.MaxDepth}"));
            if (depth > 1 && normalizedFormat != "mermaid")
                return ToolResponse.Result(JsonHelpers.Error("InvalidArgument", "depth > 1 requires format='mermaid'"));
            maxNodes = Math.Clamp(maxNodes, 1, Mermaid.MaxNodesLimit);

            var options = new IlAnalysisOptions(CaseSensitive: caseSensitive, IncludeNonPublic: includeNonPublic);
            var notFound = () => JsonHelpers.ErrorWithGuidance(
                "MethodNotFound",
                $"No method named '{methodName}' was found on type '{typeName}' in {Path.GetFileName(assemblyPath)}.",
                "Verify the type and method names. Use get_type_members with kinds=method to list available methods, or set includeNonPublic=true for private methods.",
                MethodNotFoundAlternatives);

            if (normalizedFormat == "mermaid")
            {
                var graphKey = CacheKeyHelper.Build(
                    "il.methodCalls.mermaid",
                    CacheKeyHelper.AssemblyStamp(assemblyPath), typeName, methodName, caseSensitive, includeNonPublic, depth, maxNodes);
                return ToolResponse.Result(middleware.Execute(graphKey, () =>
                {
                    var graph = ilAnalysis.GetCallGraph(assemblyPath, typeName, methodName, options, depth, maxNodes, cancellationToken);
                    return graph == null ? notFound() : CallGraphDiagram(graph, maxNodes);
                }, noCache));
            }

            var cacheKey = CacheKeyHelper.Build(
                "il.methodCalls",
                CacheKeyHelper.AssemblyStamp(assemblyPath), typeName, methodName, caseSensitive, includeNonPublic, normalizedProjection);

            return ToolResponse.Result(middleware.Execute(cacheKey, () =>
            {
                var analysis = ilAnalysis.GetMethodCalls(assemblyPath, typeName, methodName, options, cancellationToken);
                if (analysis == null) return notFound();

                object result = normalizedProjection == "summary"
                    ? new
                    {
                        declaringType = analysis.DeclaringTypeFullName,
                        methodName = analysis.MethodName,
                        matchedOverloads = analysis.MatchedOverloads,
                        anyBodyless = analysis.AnyBodyless,
                        format = normalizedFormat,
                        projection = normalizedProjection,
                        calls = analysis.Calls.Select(c => c.Target).Distinct(StringComparer.Ordinal).ToArray(),
                        fieldAccesses = analysis.FieldAccesses.Select(f => f.Target).Distinct(StringComparer.Ordinal).ToArray()
                    }
                    : new
                    {
                        declaringType = analysis.DeclaringTypeFullName,
                        methodName = analysis.MethodName,
                        matchedOverloads = analysis.MatchedOverloads,
                        anyBodyless = analysis.AnyBodyless,
                        format = normalizedFormat,
                        projection = normalizedProjection,
                        calls = analysis.Calls.Select(c => new { target = c.Target, kind = c.Kind, sourceMethod = c.SourceMethod }).ToArray(),
                        fieldAccesses = analysis.FieldAccesses.Select(f => new { target = f.Target, access = f.Access, sourceMethod = f.SourceMethod }).ToArray()
                    };

                var sizeError = ResponseSizeHelper.ValidateResponseSize(result, "get_method_calls");
                if (sizeError != null) return sizeError;

                return JsonHelpers.Envelope("il.methodCalls", result);
            }, noCache));
        }
        catch (AmbiguousTypeNameException ex)
        {
            return ToolResponse.Result(Elicitation.AmbiguousType(elicitation, ex));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResponse.Result(ToolErrors.FromException(ex, "analyze method calls"));
        }
    }

    private static string CallGraphDiagram(CallGraph graph, int maxNodes)
    {
        var diagram = Mermaid.Flowchart(graph);
        return JsonHelpers.Envelope("il.methodCalls", new
        {
            declaringType = graph.DeclaringTypeFullName,
            methodName = graph.MethodName,
            matchedOverloads = graph.MatchedOverloads,
            anyBodyless = graph.AnyBodyless,
            format = "mermaid",
            depth = graph.Depth,
            diagram = diagram.Diagram,
            nodeCount = diagram.NodeCount,
            edgeCount = diagram.EdgeCount,
            truncated = diagram.Truncated,
            note = diagram.Truncated ? Mermaid.TruncationNote(maxNodes) : null
        });
    }
}
