using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Telemetry;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Shared;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

public class ToolNamingTests
{
    private static readonly Assembly ServerAssembly = typeof(ConfigTools).Assembly;

    private static readonly MethodInfo[] ToolMethods = ServerAssembly.GetTypes()
        .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null)
        .ToArray();

    private static readonly Dictionary<string, string> WireNamesByMethodName = ToolMethods
        .DistinctBy(m => m.Name)
        .ToDictionary(m => m.Name, m => McpServerTool.Create(m).ProtocolTool.Name);

    private static readonly Regex PascalCaseToolName = new(
        $@"\b({string.Join("|", WireNamesByMethodName.Keys)})\b",
        RegexOptions.Compiled);

    [Fact]
    public void ToolMethods_AreDiscovered() =>
        Assert.True(WireNamesByMethodName.Count >= 30, $"Only found {WireNamesByMethodName.Count} tools");

    [Fact]
    public void ToolDescriptions_UseSnakeCaseToolNames()
    {
        var descriptions = ToolMethods.SelectMany(m =>
            new[] { (Source: m.Name, Text: m.GetCustomAttribute<DescriptionAttribute>()?.Description) }
                .Concat(m.GetParameters().Select(p =>
                    (Source: $"{m.Name}({p.Name})", Text: p.GetCustomAttribute<DescriptionAttribute>()?.Description))));

        AssertNoPascalCaseToolNames(descriptions);
    }

    [Fact]
    public void ServerInstructions_UseSnakeCaseToolNames() =>
        AssertNoPascalCaseToolNames([(nameof(ServerInstructions), ServerInstructions.Text)]);

    [Fact]
    public void PromptTexts_UseSnakeCaseToolNames() =>
        AssertNoPascalCaseToolNames(WorkflowPromptsTests.RenderedPrompts().Select(p => (p.Name, (string?)p.Text)));

    [Fact]
    public void StaticStringFields_UseSnakeCaseToolNames()
    {
        var fieldTexts = ServerAssembly.GetTypes()
            .Where(t => t.Namespace is "Sherlock.MCP.Server.Tools" or "Sherlock.MCP.Server.Shared")
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(f => !f.IsSpecialName && (f.FieldType == typeof(string) || f.FieldType == typeof(string[])))
                .SelectMany(f => FieldStrings(f).Select(s => (Source: $"{t.Name}.{f.Name}", Text: (string?)s))));

        AssertNoPascalCaseToolNames(fieldTexts);
    }

    [Fact]
    public void ResponseTooLargeGuidance_UsesWireNames()
    {
        var oversized = new { payload = new string('x', ResponseSizeHelper.MaxResponseSize + 1) };

        foreach (var toolName in WireNamesByMethodName.Values)
        {
            var error = ResponseSizeHelper.ValidateResponseSize(oversized, toolName);
            Assert.NotNull(error);
            AssertGuidanceUsesWireNames(toolName, error);
        }
    }

    [Fact]
    public void GetTypeHierarchyNote_UsesSnakeCaseToolNames()
    {
        var result = TypeAnalysisTools.GetTypeHierarchy(
            new TypeAnalysisService(),
            new SharedInspectionContextProvider(new RuntimeOptions()),
            new ReverseLookupService(),
            TestMiddleware.Fresh,
            TestHandles.Registry,
            assemblyPath: TestAssemblyPath,
            typeName: "BaseSample");

        var note = JsonDocument.Parse(result).RootElement.GetProperty("data").GetProperty("Note").GetString();
        Assert.False(string.IsNullOrWhiteSpace(note));
        AssertNoPascalCaseToolNames([(nameof(TypeAnalysisTools.GetTypeHierarchy), note)]);
    }

    [Fact]
    public void GetMethodCallsNotFoundGuidance_UsesWireNames()
    {
        var middleware = new ToolMiddleware(new InMemoryToolResponseCache(), new NoopTelemetry(), new RuntimeOptions());

        var result = IlAnalysisTools.GetMethodCalls(
            new IlAnalysisService(),
            middleware,
            TestHandles.Registry,
            assemblyPath: TestAssemblyPath,
            typeName: nameof(ToolNamingTests),
            methodName: "NoSuchMethod").Text();

        Assert.Equal("MethodNotFound", JsonDocument.Parse(result).RootElement.GetProperty("code").GetString());
        AssertGuidanceUsesWireNames(nameof(IlAnalysisTools.GetMethodCalls), result);
    }

    [Fact]
    public void ToolSpecificMaxItems_AcceptsMethodAndWireNameKeys()
    {
        var options = new RuntimeOptions();
        options.ToolSpecificMaxItems["GetTypeMethods"] = 7;

        Assert.Equal(7, options.GetMaxItemsForTool("get_type_methods"));
        Assert.Equal(7, options.ToolSpecificMaxItems["get_type_methods"]);
    }

    private static string TestAssemblyPath => Assembly.GetExecutingAssembly().Location;

    private static void AssertGuidanceUsesWireNames(string source, string errorJson)
    {
        var root = JsonDocument.Parse(errorJson).RootElement;
        var texts = new[] { "message", "suggestion" }
            .Where(property => root.TryGetProperty(property, out _))
            .Select(property => (Source: $"{source}.{property}", Text: root.GetProperty(property).GetString()));
        AssertNoPascalCaseToolNames(texts);

        if (!root.TryGetProperty("alternativeTools", out var alternatives))
            return;

        var wireNames = WireNamesByMethodName.Values.ToHashSet();
        foreach (var alternative in alternatives.EnumerateArray().Select(a => a.GetString()))
            Assert.True(wireNames.Contains(alternative!), $"{source} suggests unknown tool '{alternative}'");
    }

    private static IEnumerable<string> FieldStrings(FieldInfo field) =>
        field.GetValue(null) switch
        {
            string text => [text],
            string[] texts => texts,
            _ => []
        };

    private static void AssertNoPascalCaseToolNames(IEnumerable<(string Source, string? Text)> texts)
    {
        var violations = texts
            .Where(t => t.Text != null)
            .SelectMany(t => PascalCaseToolName.Matches(t.Text!)
                .Select(match => $"{t.Source}: '{match.Value}' should be '{WireNamesByMethodName[match.Value]}'"))
            .Distinct()
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }
}
