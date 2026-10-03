using System.Reflection;
using System.Text.Json;
using Json.Schema;
using Microsoft.Extensions.DependencyInjection;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Shared;
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

    private static readonly IServiceProvider ToolServices = BuildToolServices();

    public static McpServerPrimitiveCollection<McpServerTool> NewCollection()
    {
        var tools = new McpServerPrimitiveCollection<McpServerTool>();
        foreach (var method in ToolMethods())
            tools.Add(McpServerTool.Create(method, options: new McpServerToolCreateOptions { Services = ToolServices }));
        return tools;
    }

    private static IEnumerable<MethodInfo> ToolMethods() => typeof(ConfigTools).Assembly.GetTypes()
        .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null);

    private static IServiceProvider BuildToolServices()
    {
        Type[] concreteServices = [typeof(RuntimeOptions), typeof(ToolMiddleware), typeof(ToolGroupRegistry)];
        var services = new ServiceCollection();
        foreach (var type in ToolMethods().SelectMany(m => m.GetParameters()).Select(p => p.ParameterType).Distinct())
        {
            if ((type.IsInterface && type.Namespace?.StartsWith("Sherlock.MCP", StringComparison.Ordinal) == true) || concreteServices.Contains(type))
                services.AddSingleton(type, _ => throw new InvalidOperationException($"{type.Name} is not available in the tool catalog."));
        }
        return services.BuildServiceProvider();
    }

    public static void AssertMatchesOutputSchema(string methodName, JsonElement payload, string label)
    {
        var schema = JsonSchema.Build(ByMethodName[methodName].ProtocolTool.OutputSchema!.Value);
        var evaluation = schema.Evaluate(payload, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(evaluation.IsValid, $"{label} does not match its outputSchema: {JsonSerializer.Serialize(evaluation)}");
    }
}
