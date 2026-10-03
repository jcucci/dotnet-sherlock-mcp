using ModelContextProtocol.Server;

namespace Sherlock.MCP.Server.Shared;

public sealed record ToolGroupLoad(IReadOnlyList<McpServerTool> Loaded, IReadOnlyList<string> AlreadyLoaded);

// Holds the tools the active profile leaves out of tools/list until load_tools adds their group back.
// Adding a group to the live collection raises its Changed event, which the SDK turns into
// notifications/tools/list_changed for clients that listen for it.
public sealed class ToolGroupRegistry(ToolProfile profile)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, McpServerTool> _detached = new(StringComparer.Ordinal);

    public ToolProfile Profile => profile;

    public void Detach(McpServerPrimitiveCollection<McpServerTool>? tools)
    {
        if (tools is null) return;

        lock (_gate)
        {
            foreach (var tool in tools.ToArray().Where(tool => !profile.Includes(tool.ProtocolTool.Name)))
            {
                tools.Remove(tool);
                if (ToolGroups.GroupOf(tool.ProtocolTool.Name) is not null)
                    _detached[tool.ProtocolTool.Name] = tool;
            }
        }
    }

    public bool IsLoaded(ToolGroup group)
    {
        lock (_gate) return !group.Tools.Any(_detached.ContainsKey);
    }

    public ToolGroupLoad Load(IEnumerable<ToolGroup> groups, McpServerPrimitiveCollection<McpServerTool> tools)
    {
        var loaded = new List<McpServerTool>();
        var alreadyLoaded = new List<string>();

        lock (_gate)
        using (tools.DeferChangedEvents())
        {
            foreach (var group in groups.Distinct())
            {
                var added = group.Tools
                    .Select(name => _detached.Remove(name, out var tool) && tools.TryAdd(tool) ? tool : null)
                    .OfType<McpServerTool>()
                    .ToArray();
                if (added.Length == 0) alreadyLoaded.Add(group.Name);
                loaded.AddRange(added);
            }
        }

        return new ToolGroupLoad(loaded, alreadyLoaded);
    }
}
