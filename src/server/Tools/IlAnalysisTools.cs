using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.Il;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Shared;
using System.ComponentModel;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class IlAnalysisTools
{
    private static readonly string[] MethodNotFoundAlternatives = { "GetTypeMethods", "AnalyzeType" };

    [McpServerTool]
    [Description("Analyzes a method's IL body to list what it calls and which fields it touches (the 'what does this method call?' question that signature-level tools can't answer). Aggregates across all overloads of the method name. Returns a lean summary by default (distinct target names); projection='full' adds per-call kind (call/callvirt/newobj/ldftn) and the source overload signature.")]
    public static string GetMethodCalls(
        IIlAnalysisService ilAnalysis,
        ToolMiddleware middleware,
        [Description("Path to the .NET assembly file (.dll or .exe) that declares the method")] string assemblyPath,
        [Description("Type that declares the method. Simple name, full name, or open-generic form accepted.")] string typeName,
        [Description("Method name to analyze. Use '.ctor' for instance constructors or '.cctor' for the static constructor. All overloads with this name are aggregated.")] string methodName,
        [Description("Case sensitive type-name matching (default: false)")] bool caseSensitive = false,
        [Description("Include non-public methods and the non-public declaring type (default: false)")] bool includeNonPublic = false,
        [Description("Response shape. 'summary' (default, token-lean): distinct target names only. 'full': adds { target, kind, sourceMethod } per call and { target, access, sourceMethod } per field access.")] string projection = "summary",
        [Description("Bypass cache for this request")] bool noCache = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
                return JsonHelpers.Error("InvalidArgument", "assemblyPath is required");
            if (!File.Exists(assemblyPath))
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
            if (string.IsNullOrWhiteSpace(typeName))
                return JsonHelpers.Error("InvalidArgument", "typeName is required");
            if (string.IsNullOrWhiteSpace(methodName))
                return JsonHelpers.Error("InvalidArgument", "methodName is required");

            var normalizedProjection = (projection ?? "summary").Trim().ToLowerInvariant();
            if (normalizedProjection != "summary" && normalizedProjection != "full")
                return JsonHelpers.Error("InvalidProjection", "projection must be 'summary' or 'full'");

            var cacheKey = CacheKeyHelper.Build(
                "il.methodCalls",
                CacheKeyHelper.FileStamp(assemblyPath), typeName, methodName, caseSensitive, includeNonPublic, normalizedProjection);

            return middleware.Execute(cacheKey, () =>
            {
                var options = new IlAnalysisOptions(CaseSensitive: caseSensitive, IncludeNonPublic: includeNonPublic);
                var analysis = ilAnalysis.GetMethodCalls(assemblyPath, typeName, methodName, options);

                if (analysis == null)
                    return JsonHelpers.ErrorWithGuidance(
                        "MethodNotFound",
                        $"No method named '{methodName}' was found on type '{typeName}' in {Path.GetFileName(assemblyPath)}.",
                        "Verify the type and method names. Use GetTypeMethods to list available methods, or set includeNonPublic=true for private methods.",
                        MethodNotFoundAlternatives);

                object result = normalizedProjection == "summary"
                    ? new
                    {
                        declaringType = analysis.DeclaringTypeFullName,
                        methodName = analysis.MethodName,
                        matchedOverloads = analysis.MatchedOverloads,
                        anyBodyless = analysis.AnyBodyless,
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
                        projection = normalizedProjection,
                        calls = analysis.Calls.Select(c => new { target = c.Target, kind = c.Kind, sourceMethod = c.SourceMethod }).ToArray(),
                        fieldAccesses = analysis.FieldAccesses.Select(f => new { target = f.Target, access = f.Access, sourceMethod = f.SourceMethod }).ToArray()
                    };

                var sizeError = ResponseSizeHelper.ValidateResponseSize(result, "GetMethodCalls");
                if (sizeError != null) return sizeError;

                return JsonHelpers.Envelope("il.methodCalls", result);
            }, noCache);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze method calls: {ex.Message}");
        }
    }
}
