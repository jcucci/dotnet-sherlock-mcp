using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Sherlock.MCP.Server.Prompts;
using Sherlock.MCP.Server.Shared;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

public class WorkflowPromptsTests
{
    private static readonly Assembly ServerAssembly = typeof(WorkflowPrompts).Assembly;

    private static readonly McpServerPrompt[] Prompts = ServerAssembly.GetTypes()
        .Where(t => t.GetCustomAttribute<McpServerPromptTypeAttribute>() != null)
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        .Where(m => m.GetCustomAttribute<McpServerPromptAttribute>() != null)
        .Select(m => McpServerPrompt.Create(m))
        .ToArray();

    private static readonly HashSet<string> WireToolNames = typeof(ConfigTools).Assembly.GetTypes()
        .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null)
        .Select(m => McpServerTool.Create(m).ProtocolTool.Name)
        .ToHashSet(StringComparer.Ordinal);

    private static readonly Regex SnakeCaseIdentifier = new(@"\b[a-z]+(?:_[a-z]+)+\b", RegexOptions.Compiled);

    public static IEnumerable<(string Name, string Text)> RenderedPrompts() =>
    [
        (PromptNames.ExplorePackage, WorkflowPrompts.ExplorePackage("Newtonsoft.Json", "13.0.3")),
        (PromptNames.ExplorePackage, WorkflowPrompts.ExplorePackage("Newtonsoft.Json")),
        (PromptNames.ExplainType, WorkflowPrompts.ExplainType("/libs/a.dll", "Ns.Widget")),
        (PromptNames.WhoCalls, WorkflowPrompts.WhoCalls("/libs/a.dll", "Ns.Widget", "Render", "/libs/b.dll, /libs/c.dll")),
        (PromptNames.WhoCalls, WorkflowPrompts.WhoCalls("/libs/a.dll", "Ns.Widget", "Render"))
    ];

    [Fact]
    public void Prompts_AreDiscoveredWithExpectedArguments()
    {
        var arguments = Prompts.ToDictionary(
            p => p.ProtocolPrompt.Name,
            p => p.ProtocolPrompt.Arguments!.Select(a => $"{a.Name}{(a.Required == true ? "" : "?")}").ToArray());

        Assert.Equal(3, arguments.Count);
        Assert.Equal(["packageId", "version?"], arguments[PromptNames.ExplorePackage]);
        Assert.Equal(["assemblyPath", "typeName"], arguments[PromptNames.ExplainType]);
        Assert.Equal(["assemblyPath", "typeName", "memberName", "additionalAssemblies?"], arguments[PromptNames.WhoCalls]);
    }

    [Fact]
    public void Prompts_HaveTitlesAndDescriptions() =>
        Assert.All(Prompts, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.ProtocolPrompt.Title));
            Assert.False(string.IsNullOrWhiteSpace(p.ProtocolPrompt.Description));
            Assert.All(p.ProtocolPrompt.Arguments!, a => Assert.False(string.IsNullOrWhiteSpace(a.Description)));
        });

    [Fact]
    public void PromptTexts_ReferenceOnlyCoreProfileTools()
    {
        var referenced = RenderedPrompts()
            .SelectMany(p => SnakeCaseIdentifier.Matches(p.Text).Select(m => (p.Name, Tool: m.Value)))
            .ToArray();

        Assert.NotEmpty(referenced);
        Assert.All(referenced, r =>
        {
            Assert.True(WireToolNames.Contains(r.Tool), $"{r.Name} references unknown tool '{r.Tool}'");
            Assert.True(ToolProfile.Core.Includes(r.Tool), $"{r.Name} references '{r.Tool}', which the core profile omits");
        });
    }

    [Fact]
    public void ExplorePackage_RendersVersionWhenGiven()
    {
        Assert.Contains("packageId 'Newtonsoft.Json' and version '13.0.3'", WorkflowPrompts.ExplorePackage("Newtonsoft.Json", "13.0.3"));

        var latest = WorkflowPrompts.ExplorePackage("Newtonsoft.Json");
        Assert.Contains("highest cached version", latest);
        Assert.DoesNotContain("and version", latest);
    }

    [Fact]
    public void ExplainType_RendersArguments()
    {
        var text = WorkflowPrompts.ExplainType("/libs/a.dll", "Ns.Widget");

        Assert.Contains("assemblyPath '/libs/a.dll'", text);
        Assert.Contains("typeName 'Ns.Widget'", text);
        Assert.Contains("analysisDepth='il' and includeNonPublic=true", text);
        Assert.Contains("truncated=true", text);
    }

    [Fact]
    public void WhoCalls_RendersAdditionalAssembliesWhenGiven()
    {
        var text = WorkflowPrompts.WhoCalls("/libs/a.dll", "Ns.Widget", "Render", " /libs/b.dll , /libs/c.dll ,");

        Assert.Contains("Ns.Widget.Render", text);
        Assert.Contains("includeNonPublic=true", text);
        Assert.Contains("get_Render and set_Render", text);
        Assert.Contains("truncated=true", text);
        Assert.DoesNotContain("which overload each one calls", text);
        Assert.Contains("additionalAssemblies ['/libs/b.dll', '/libs/c.dll']", text);
        Assert.DoesNotContain("additionalAssemblies", WorkflowPrompts.WhoCalls("/libs/a.dll", "Ns.Widget", "Render"));
    }
}
