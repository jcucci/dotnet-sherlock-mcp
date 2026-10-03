using System.Reflection;
using System.Text.Json;
using Json.Schema;
using ModelContextProtocol.Server;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

internal static class ToolCatalog
{
    public static IReadOnlyDictionary<string, McpServerTool> ByMethodName { get; } = typeof(ConfigTools).Assembly.GetTypes()
        .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null)
        .ToDictionary(m => m.Name, m => McpServerTool.Create(m));

    public static void AssertMatchesOutputSchema(string methodName, JsonElement payload, string label)
    {
        var schema = JsonSchema.Build(ByMethodName[methodName].ProtocolTool.OutputSchema!.Value);
        var evaluation = schema.Evaluate(payload, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(evaluation.IsValid, $"{label} does not match its outputSchema: {JsonSerializer.Serialize(evaluation)}");
    }
}
