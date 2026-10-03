namespace Sherlock.MCP.Server.Shared;

public sealed record ToolGroup(string Name, string Summary, string[] Tools);

// Every tool outside the core profile belongs to exactly one group, so a core-profile agent can
// load just the capability it needs with load_tools. Summaries describe the capability without
// naming tools, because the server instructions may only name core tools.
public static class ToolGroups
{
    public static readonly ToolGroup[] All =
    [
        new("frameworks", "ASP.NET Core endpoints and routes, dependency-injection registrations, EF Core entities, MediatR handlers",
            ["find_endpoints", "find_service_registrations", "find_ef_entities", "find_handlers"]),
        new("project", "solution and project structure, package references, the restored NuGet package graph, deps.json dependencies",
            ["analyze_solution", "analyze_project", "resolve_package_references", "get_package_graph", "find_deps_json_dependencies"]),
        new("metadata", "assembly-wide type listing with counts, generic parameters and constraints, nested types, type/member/parameter attributes",
            ["analyze_assembly", "get_generic_type_info", "get_nested_types", "get_type_attributes", "get_member_attributes", "get_parameter_attributes"]),
        new("decompile", "decompile a whole type to C#",
            ["decompile_type"]),
        new("config", "read and change server runtime options (cache, page sizes, source fetching)",
            ["get_runtime_options", "update_runtime_options"]),
        new("legacy", "deprecated per-kind member listings kept for older clients",
            ["analyze_type", "get_all_type_members", "get_type_methods", "get_type_properties", "get_type_fields", "get_type_events", "get_type_constructors"])
    ];

    public static ToolGroup? TryGet(string name) =>
        All.FirstOrDefault(group => string.Equals(group.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public static ToolGroup? GroupOf(string toolName) =>
        All.FirstOrDefault(group => group.Tools.Contains(toolName, StringComparer.Ordinal));
}
