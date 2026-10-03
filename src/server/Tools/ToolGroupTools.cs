using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Server.Shared;
using System.ComponentModel;
using System.Text.Json;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class ToolGroupTools
{
    private const string LoadNote =
        "The loaded tools now appear in tools/list. If your client does not show them (it may not refresh its tool list), call them through invoke_tool with the name and arguments listed here.";

    [McpServerTool(Title = "Load Tools", ReadOnly = true, Destructive = false, OpenWorld = false, Idempotent = true)]
    [Description("Loads optional tool groups that the core profile leaves out. Call with no arguments to list every group, its tools and whether it is loaded; pass groups to add them to tools/list. Groups: 'frameworks' (ASP.NET Core endpoints, dependency-injection registrations, EF Core entities, MediatR handlers), 'project' (solution/project structure, package references, the restored NuGet package graph, deps.json), 'metadata' (assembly-wide type listing, generic parameters and constraints, nested types, type/member/parameter attributes), 'decompile' (decompile a whole type), 'config' (server runtime options), 'legacy' (deprecated per-kind member listings). Loaded tools can always be called through invoke_tool, even if the client does not refresh its tool list.")]
    public static string LoadTools(
        ToolGroupRegistry registry,
        [Description("Groups to load (e.g. ['frameworks']). Omit to list the available groups.")] string[]? groups = null,
        RequestContext<CallToolRequestParams>? context = null) =>
        groups is not { Length: > 0 }
            ? ListGroups(registry)
            : Load(registry, groups, context?.Server.ServerOptions.ToolCollection);

    [McpServerTool(Title = "Invoke Tool", Destructive = false, OpenWorld = true)]
    [Description("Calls any loaded Sherlock tool by name, for clients that do not refresh their tool list after load_tools. Pass the tool's wire name (e.g. 'find_endpoints') and its arguments object exactly as you would call it directly; the result is the tool's own result. Tools in a group that is not loaded yet return ToolNotLoaded naming the group to load.")]
    public static async Task<CallToolResult> InvokeTool(
        [Description("Wire name of the tool to call (snake_case, e.g. 'get_package_graph')")] string name,
        [Description("The tool's arguments as an object, e.g. { \"assemblyPath\": \"/abs/App.dll\" }")] Dictionary<string, JsonElement>? arguments = null,
        RequestContext<CallToolRequestParams>? context = null,
        CancellationToken cancellationToken = default)
    {
        var toolName = (name ?? string.Empty).Trim();
        if (Resolve(context?.Server.ServerOptions.ToolCollection, toolName, out var tool) is { } error)
            return ToolResponse.Result(error);

        var request = new RequestContext<CallToolRequestParams>(context!.Server, context.JsonRpcRequest, new CallToolRequestParams
        {
            Name = toolName,
            Arguments = arguments,
            Meta = context.Params?.Meta,
            InputResponses = context.Params?.InputResponses,
            RequestState = context.Params?.RequestState
        })
        {
            Services = context.Services,
            User = context.User,
            Items = context.Items,
            MatchedPrimitive = tool
        };

        return await tool.InvokeAsync(request, cancellationToken);
    }

    public static string? Resolve(McpServerPrimitiveCollection<McpServerTool>? tools, string toolName, out McpServerTool tool)
    {
        tool = null!;
        if (ToolProfile.LoaderToolNames.Contains(toolName, StringComparer.Ordinal))
            return JsonHelpers.Error("InvalidArgument", $"{toolName} cannot be called through invoke_tool; call it directly.");
        if (tools is null)
            return JsonHelpers.Error("InternalError", "invoke_tool needs a live MCP session.");
        return tools.TryGetPrimitive(toolName, out tool!) ? null : Unavailable(toolName, tools);
    }

    public static string ListGroups(ToolGroupRegistry registry) =>
        JsonHelpers.Envelope("toolgroups.list", new
        {
            profile = registry.Profile.Name,
            groups = ToolGroups.All.Select(group => new
            {
                name = group.Name,
                summary = group.Summary,
                tools = group.Tools,
                loaded = registry.IsLoaded(group)
            }).ToArray()
        });

    public static string Load(ToolGroupRegistry registry, string[] groups, McpServerPrimitiveCollection<McpServerTool>? tools)
    {
        var unknown = groups.Where(name => ToolGroups.TryGet(name) is null).ToArray();
        if (unknown.Length > 0)
            return JsonHelpers.ErrorWithGuidance(
                "InvalidArgument",
                $"Unknown tool group(s): {string.Join(", ", unknown)}.",
                suggestion: "Call load_tools with no arguments to list the groups.",
                recommendedParams: new { candidates = ToolGroups.All.Select(group => group.Name).ToArray() });
        if (tools is null)
            return JsonHelpers.Error("InternalError", "load_tools needs a live MCP session to add tools.");

        var result = registry.Load(groups.Select(name => ToolGroups.TryGet(name)!), tools);
        return JsonHelpers.Envelope("toolgroups.load", new
        {
            loaded = result.Loaded.Select(tool => new
            {
                name = tool.ProtocolTool.Name,
                description = tool.ProtocolTool.Description,
                inputSchema = tool.ProtocolTool.InputSchema
            }).ToArray(),
            alreadyLoaded = result.AlreadyLoaded,
            note = LoadNote
        });
    }

    private static string Unavailable(string toolName, McpServerPrimitiveCollection<McpServerTool> tools)
    {
        if (ToolGroups.GroupOf(toolName) is { } group)
            return JsonHelpers.ErrorWithGuidance(
                "ToolNotLoaded",
                $"{toolName} is in the '{group.Name}' tool group, which is not loaded.",
                suggestion: $"Call load_tools with groups=['{group.Name}'], then retry.",
                alternativeTools: ["load_tools"],
                recommendedParams: new { groups = new[] { group.Name } });

        var known = tools.PrimitiveNames.Concat(ToolGroups.All.SelectMany(g => g.Tools)).Distinct(StringComparer.Ordinal);
        return JsonHelpers.ErrorWithGuidance(
            "ToolNotFound",
            $"No Sherlock tool is named '{toolName}'.",
            suggestion: "Tool names are snake_case; retry with one of recommendedParams.candidates.",
            recommendedParams: new { candidates = NameSuggestions.Closest(known, toolName, compareSimpleNames: false) });
    }
}
