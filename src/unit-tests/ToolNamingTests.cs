using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
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
        var wireNames = WireNamesByMethodName.Values.ToHashSet();

        foreach (var toolName in wireNames)
        {
            var error = ResponseSizeHelper.ValidateResponseSize(oversized, toolName);
            Assert.NotNull(error);

            var root = JsonDocument.Parse(error).RootElement;
            AssertNoPascalCaseToolNames([(toolName, root.GetProperty("message").GetString())]);

            if (!root.TryGetProperty("alternativeTools", out var alternatives))
                continue;

            foreach (var alternative in alternatives.EnumerateArray().Select(a => a.GetString()))
                Assert.True(wireNames.Contains(alternative!), $"{toolName} suggests unknown tool '{alternative}'");
        }
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
