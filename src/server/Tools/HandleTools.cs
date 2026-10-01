using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime.Handles;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Server.Schemas;
using Sherlock.MCP.Server.Shared;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class HandleTools
{
    [McpServerTool(Title = "Open Assembly", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolEnvelope<AssemblyHandleData>))]
    [Description("Opens an assembly and returns a short handle (asm_…) to pass as assemblyHandle instead of assemblyPath on later calls. The handle also carries additionalAssemblies, survives server restarts, and is pinned to the current build: after a rebuild, calls fail with StaleAssemblyHandle until you open it again. Reopening the same build returns the same handle.")]
    public static CallToolResult OpenAssembly(
        IAssemblyHandleRegistry handles,
        IInspectionContextProvider contexts,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Optional additional assembly paths that later calls with this handle should include in their search scope")] string[]? additionalAssemblies = null)
    {
        try
        {
            var scope = AssemblyScope.BuildAndValidate(assemblyPath, additionalAssemblies);
            if (scope.Error != null)
                return ToolResponse.Result(scope.Error);

            using var lease = contexts.Acquire(scope.Paths[0]);
            var name = lease.Assembly.GetName();
            var handle = handles.Open(scope.Paths[0], scope.Paths[1..]);

            var result = new
            {
                handle = handle.Id,
                name = name.Name,
                version = name.Version?.ToString(),
                targetFramework = ReflectionTools.ReadTargetFramework(lease.Assembly),
                assemblyPath = handle.AssemblyPath,
                additionalAssemblies = handle.AdditionalAssemblies
            };
            return ToolResponse.Result(JsonHelpers.Envelope("assembly.handle", result));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResponse.Result(ToolErrors.FromException(ex, "open assembly"));
        }
    }
}
