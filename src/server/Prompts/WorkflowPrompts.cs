using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Sherlock.MCP.Server.Prompts;

[McpServerPromptType]
public static class WorkflowPrompts
{
    [McpServerPrompt(Name = PromptNames.ExplorePackage, Title = "Explore a NuGet package")]
    [Description("Locates a package in the local NuGet cache, orients on its assembly and summarizes its key public types and entry points.")]
    public static string ExplorePackage(
        [Description("NuGet package id, e.g. 'Newtonsoft.Json'")] string packageId,
        [Description("Package version; omit to use the highest cached version")] string? version = null) =>
        $"""
        Explore the NuGet package {packageId} {VersionClause(version)} with the Sherlock tools and explain what it offers.

        1. Call find_assembly_by_nuget_package with packageId '{packageId}'{VersionArgument(version)} to resolve its assembly path. If the package isn't in the local cache, say so and stop.
        2. Call open_assembly on that path and pass the returned assemblyHandle (instead of assemblyPath) to every later call.
        3. Call get_assembly_info for its identity, target framework and references.
        4. Call get_types_from_assembly with a small maxItems and the default summary projection to list the public types; page with continuationToken only if the first page doesn't reveal the main namespaces.
        5. Pick the handful of types a consumer would start from (clients, builders, options, extension-method classes, primary interfaces) and call get_type_info and get_xml_docs_for_type on each.

        Summarize: what the package is for, its main namespaces and types, the entry points a consumer uses first, and a short usage sketch built only from the signatures you saw.
        """;

    [McpServerPrompt(Name = PromptNames.ExplainType, Title = "Explain a type")]
    [Description("Explains a type's role, inheritance, members, documentation and usages within its assembly.")]
    public static string ExplainType(
        [Description("Path to the .NET assembly file (.dll or .exe) that declares the type")] string assemblyPath,
        [Description("Type name, preferably the full name (Namespace.Type)")] string typeName) =>
        $"""
        Explain the type {typeName} in the assembly {assemblyPath} using the Sherlock tools.

        1. Call open_assembly with assemblyPath '{assemblyPath}' and pass the returned assemblyHandle to every later call.
        2. Call get_type_info for typeName '{typeName}' to get its kind, accessibility, base type and member counts.
        3. Call get_type_hierarchy for its base types and interfaces.
        4. Call get_type_members with the default summary projection; re-call with projection='full' and nameContains only for the few members whose parameters or attributes matter.
        5. Call get_xml_docs_for_type for its documentation.
        6. Call find_references_to for typeName '{typeName}' with includeNonPublic=true to see where the assembly uses it, including internally.

        Explain: what the type is for, the contract it inherits or implements, its most important members and how they fit together, and how the rest of the assembly uses it.
        """;

    [McpServerPrompt(Name = PromptNames.WhoCalls, Title = "Find callers of a member")]
    [Description("Finds the methods whose IL calls or accesses a member, optionally across additional assemblies.")]
    public static string WhoCalls(
        [Description("Path to the .NET assembly file (.dll or .exe) that declares the member")] string assemblyPath,
        [Description("Declaring type name, preferably the full name (Namespace.Type)")] string typeName,
        [Description("Member name, e.g. 'Parse'; use '.ctor' for constructors")] string memberName,
        [Description("Optional comma-separated paths of other assemblies whose callers should be included")] string? additionalAssemblies = null) =>
        $"""
        Find every caller of {typeName}.{memberName} in the assembly {assemblyPath}{AdditionalAssembliesClause(additionalAssemblies)} using the Sherlock tools.

        1. Call open_assembly with assemblyPath '{assemblyPath}'{AdditionalAssembliesArgument(additionalAssemblies)} and pass the returned assemblyHandle to every later call.
        2. Call analyze_method for typeName '{typeName}' and methodName '{memberName}' to confirm the member and its overloads. If it isn't a method, use get_type_members with nameContains '{memberName}' instead.
        3. Call find_references_to for typeName '{typeName}' with analysisDepth='il', projection='full' and includeNonPublic=true (callers inside an assembly are often private or internal). Keep the ilCall, ilFieldRead and ilFieldWrite hits whose signature ('caller -> target') targets {memberName}. Page with continuationToken until there are no more results.
        4. Where it is unclear how a caller reaches the member, call get_method_calls on that caller to confirm the call path.

        Report the callers grouped by declaring type, noting which overload each one calls, and say explicitly if there are none.
        """;

    private static string VersionClause(string? version) =>
        string.IsNullOrWhiteSpace(version) ? "(highest cached version)" : $"version {version}";

    private static string VersionArgument(string? version) =>
        string.IsNullOrWhiteSpace(version) ? "" : $" and version '{version}'";

    private static string AdditionalAssembliesClause(string? additionalAssemblies) =>
        SplitPaths(additionalAssemblies) is { Length: > 0 } paths ? $" and {string.Join(", ", paths)}" : "";

    private static string AdditionalAssembliesArgument(string? additionalAssemblies) =>
        SplitPaths(additionalAssemblies) is { Length: > 0 } paths
            ? $" and additionalAssemblies [{string.Join(", ", paths.Select(path => $"'{path}'"))}]"
            : "";

    private static string[] SplitPaths(string? paths) =>
        (paths ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
