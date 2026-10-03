using System.Text.Json;
using System.Text.RegularExpressions;
using Sherlock.MCP.Runtime.Contracts.TypeAnalysis;
using Sherlock.MCP.Server.Shared;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

public class ToolGroupTests
{
    private static readonly string[] AllWireNames = ToolCatalog.ByMethodName.Values.Select(t => t.ProtocolTool.Name).ToArray();

    [Fact]
    public void Groups_PartitionEveryNonCoreTool()
    {
        var grouped = ToolGroups.All.SelectMany(group => group.Tools).ToArray();
        var nonCore = AllWireNames.Where(name => !ToolProfile.Core.Includes(name)).Order(StringComparer.Ordinal);

        Assert.Equal(grouped.Length, grouped.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(nonCore, grouped.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void GroupNames_AreUnique() =>
        Assert.Equal(ToolGroups.All.Length, ToolGroups.All.Select(group => group.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());

    [Fact]
    public void GroupSummaries_NameNoTools() =>
        Assert.All(ToolGroups.All, group =>
            Assert.DoesNotContain(AllWireNames, name => Regex.IsMatch(group.Summary, $@"\b{name}\b")));

    [Fact]
    public void LoadToolsDescription_ListsEveryGroup()
    {
        var description = ToolCatalog.ByMethodName[nameof(ToolGroupTools.LoadTools)].ProtocolTool.Description!;

        Assert.All(ToolGroups.All, group => Assert.Contains($"'{group.Name}'", description));
    }

    [Fact]
    public void ServerInstructions_NameEveryGroup() =>
        Assert.All(ToolGroups.All, group => Assert.Contains($"'{group.Name}'", ServerInstructions.Text));

    [Fact]
    public void Core_DetachesEveryGroupedToolAndKeepsLoaders()
    {
        var tools = ToolCatalog.NewCollection();
        var registry = new ToolGroupRegistry(ToolProfile.Core);

        registry.Detach(tools);

        Assert.Equal(ToolProfile.CoreToolNames.Order(StringComparer.Ordinal), tools.PrimitiveNames.Order(StringComparer.Ordinal));
        Assert.All(ToolGroups.All, group => Assert.False(registry.IsLoaded(group)));
    }

    [Fact]
    public void Full_DetachesOnlyTheLoaders()
    {
        var tools = ToolCatalog.NewCollection();
        var registry = new ToolGroupRegistry(ToolProfile.Full);

        registry.Detach(tools);

        Assert.Equal(AllWireNames.Length - ToolProfile.LoaderToolNames.Length, tools.Count);
        Assert.DoesNotContain(ToolProfile.LoaderToolNames, tools.PrimitiveNames.Contains);
        Assert.All(ToolGroups.All, group => Assert.True(registry.IsLoaded(group)));
    }

    [Fact]
    public void Load_AddsTheGroupWithOneChangeNotification_AndIsIdempotent()
    {
        var tools = ToolCatalog.NewCollection();
        var registry = new ToolGroupRegistry(ToolProfile.Core);
        registry.Detach(tools);
        var changes = 0;
        tools.Changed += (_, _) => changes++;
        var frameworks = ToolGroups.TryGet("frameworks")!;

        var first = registry.Load([frameworks], tools);
        var second = registry.Load([frameworks], tools);

        Assert.Equal(frameworks.Tools.Order(StringComparer.Ordinal), first.Loaded.Select(t => t.ProtocolTool.Name).Order(StringComparer.Ordinal));
        Assert.All(frameworks.Tools, name => Assert.Contains(name, tools.PrimitiveNames));
        Assert.Equal(1, changes);
        Assert.Empty(second.Loaded);
        Assert.Equal(["frameworks"], second.AlreadyLoaded);
        Assert.True(registry.IsLoaded(frameworks));
    }

    [Fact]
    public void LoadTools_WithoutGroups_ListsGroupsAndLoadState()
    {
        var registry = new ToolGroupRegistry(ToolProfile.Core);
        registry.Detach(ToolCatalog.NewCollection());

        var data = Data(ToolGroupTools.LoadTools(registry));

        Assert.Equal("core", data.GetProperty("profile").GetString());
        var frameworks = data.GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("name").GetString() == "frameworks");
        Assert.False(frameworks.GetProperty("loaded").GetBoolean());
        Assert.Contains("find_endpoints", frameworks.GetProperty("tools").EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public void Load_ReturnsSchemasForInvokeTool()
    {
        var tools = ToolCatalog.NewCollection();
        var registry = new ToolGroupRegistry(ToolProfile.Core);
        registry.Detach(tools);

        var data = Data(ToolGroupTools.Load(registry, ["Project"], tools));

        var graph = data.GetProperty("loaded").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "get_package_graph");
        Assert.Equal("object", graph.GetProperty("inputSchema").GetProperty("type").GetString());
        Assert.Contains("invoke_tool", data.GetProperty("note").GetString());
    }

    [Fact]
    public void Load_UnknownGroup_ReturnsCandidates()
    {
        var root = Root(ToolGroupTools.Load(new ToolGroupRegistry(ToolProfile.Core), ["frameworkz"], ToolCatalog.NewCollection()));

        Assert.Equal("InvalidArgument", root.GetProperty("code").GetString());
        Assert.Contains("frameworks", root.GetProperty("recommendedParams").GetProperty("candidates").EnumerateArray().Select(c => c.GetString()));
    }

    [Fact]
    public void Resolve_NamesTheGroupOfAnUnloadedTool()
    {
        var tools = ToolCatalog.NewCollection();
        new ToolGroupRegistry(ToolProfile.Core).Detach(tools);

        var root = Root(ToolGroupTools.Resolve(tools, "get_nested_types", out _)!);

        Assert.Equal("ToolNotLoaded", root.GetProperty("code").GetString());
        Assert.Equal("metadata", root.GetProperty("recommendedParams").GetProperty("groups")[0].GetString());
    }

    [Fact]
    public void Resolve_SuggestsCloseNamesForAnUnknownTool()
    {
        var root = Root(ToolGroupTools.Resolve(ToolCatalog.NewCollection(), "get_nested_type", out _)!);

        Assert.Equal("ToolNotFound", root.GetProperty("code").GetString());
        Assert.Contains("get_nested_types", root.GetProperty("recommendedParams").GetProperty("candidates").EnumerateArray().Select(c => c.GetString()));
    }

    [Theory]
    [InlineData("invoke_tool")]
    [InlineData("load_tools")]
    public void Resolve_RejectsTheLoaders(string name) =>
        Assert.Equal("InvalidArgument", Root(ToolGroupTools.Resolve(ToolCatalog.NewCollection(), name, out _)!).GetProperty("code").GetString());

    [Fact]
    public void Resolve_FindsALoadedTool()
    {
        Assert.Null(ToolGroupTools.Resolve(ToolCatalog.NewCollection(), "find_handlers", out var tool));
        Assert.Equal("find_handlers", tool.ProtocolTool.Name);
    }

    [Theory]
    [InlineData("DbContext", "find_ef_entities")]
    [InlineData("Microsoft.EntityFrameworkCore.DbContext", "find_ef_entities")]
    [InlineData("IRequestHandler<,>", "find_handlers")]
    [InlineData("MediatR.INotificationHandler`1", "find_handlers")]
    [InlineData("ControllerBase", "find_endpoints")]
    [InlineData("IServiceCollection", "find_service_registrations")]
    public void ForTypeName_PointsAtTheFrameworkTool(string typeName, string tool)
    {
        var hint = Assert.Single(ToolHints.ForTypeName(typeName));

        Assert.Equal(tool, hint.Tool);
        Assert.Equal("frameworks", hint.Group);
    }

    [Theory]
    [InlineData("MyApp.DbContext")]
    [InlineData("IDisposable")]
    public void ForTypeName_IgnoresUnrelatedTypes(string typeName) =>
        Assert.Empty(ToolHints.ForTypeName(typeName));

    [Fact]
    public void ForType_HintsNestedGenericAndFrameworkTools()
    {
        var info = Type(baseType: "Microsoft.EntityFrameworkCore.DbContext", interfaces: ["MediatR.IRequestHandler<A, B>"], nested: true, generic: true);

        Assert.Equal(
            ["get_nested_types", "get_generic_type_info", "find_ef_entities", "find_handlers"],
            ToolHints.ForType(info).Select(h => h.Tool));
    }

    [Fact]
    public void ForType_PlainType_HasNoHints() =>
        Assert.Empty(ToolHints.ForType(Type(baseType: "System.Object", interfaces: [], nested: false, generic: false)));

    [Fact]
    public void Hint_ForCoreTool_HasNoGroup() =>
        Assert.Null(ToolHints.Hint("get_type_members", "reason").Group);

    [Fact]
    public void Envelope_OmitsEmptyHints() =>
        Assert.False(Root(JsonHelpers.Envelope("k", new { }, [])).TryGetProperty("hints", out _));

    private static TypeInfo Type(string baseType, string[] interfaces, bool nested, bool generic) =>
        new("N.T", "T", "N", TypeKind.Class, AccessibilityLevel.Public, false, false, false, generic, false, "A", baseType, interfaces, [],
            generic ? [new GenericParameterInfo("T", 0, System.Reflection.GenericParameterAttributes.None, [], false, false, false)] : [],
            nested ? [new TypeInfo("N.T+I", "I", "N", TypeKind.Class, AccessibilityLevel.Public, false, false, false, false, true, "A", null, [], [], [], [])] : []);

    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement;

    private static JsonElement Data(string json) => Root(json).GetProperty("data");
}
