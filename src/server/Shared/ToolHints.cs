using System.Text.Json.Serialization;
using Sherlock.MCP.Runtime.Contracts.TypeAnalysis;

namespace Sherlock.MCP.Server.Shared;

public sealed record ToolHint(
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("group")] string? Group);

// Points core-tool results at related tools an agent may not know about. A hint names the tool's
// group when it has one; a core-profile agent loads that group with load_tools if the tool is not in
// its tool list yet. Hints depend only on the result, never on what is loaded, so they are safe to cache.
public static class ToolHints
{
    private const string Handlers = "lists every MediatR handler with its request and response types";
    private const string Endpoints = "lists controller actions and minimal APIs with their HTTP methods and routes";

    private static readonly (string FullName, string Tool, string Reason)[] FrameworkTypes =
    [
        ("MediatR.IRequestHandler", "find_handlers", Handlers),
        ("MediatR.INotificationHandler", "find_handlers", Handlers),
        ("MediatR.IStreamRequestHandler", "find_handlers", Handlers),
        ("MediatR.IPipelineBehavior", "find_handlers", Handlers),
        ("Mediator.IRequestHandler", "find_handlers", Handlers),
        ("Mediator.INotificationHandler", "find_handlers", Handlers),
        ("Mediator.IStreamRequestHandler", "find_handlers", Handlers),
        ("Mediator.IPipelineBehavior", "find_handlers", Handlers),
        ("Microsoft.EntityFrameworkCore.DbContext", "find_ef_entities", "lists the entities (DbSet<T> properties) of each DbContext"),
        ("Microsoft.AspNetCore.Mvc.ControllerBase", "find_endpoints", Endpoints),
        ("Microsoft.AspNetCore.Mvc.Controller", "find_endpoints", Endpoints),
        ("Microsoft.Extensions.DependencyInjection.IServiceCollection", "find_service_registrations", "lists dependency-injection registrations with lifetimes and implementations")
    ];

    public static ToolHint Hint(string tool, string reason) => new(tool, reason, ToolGroups.GroupOf(tool)?.Name);

    public static IReadOnlyList<ToolHint> ForType(TypeInfo info)
    {
        var hints = new List<ToolHint>();
        if (info.NestedTypes.Length > 0)
            hints.Add(Hint("get_nested_types", "lists this type's nested types with their kinds and accessibility"));
        if (info.GenericParameters.Length > 0)
            hints.Add(Hint("get_generic_type_info", "details the generic parameters, their constraints and variance"));

        var related = new[] { info.BaseType }.Concat(info.Interfaces).OfType<string>();
        hints.AddRange(related.SelectMany(name => ForTypeName(name)));
        return Distinct(hints);
    }

    public static IReadOnlyList<ToolHint> ForTypeName(string typeName)
    {
        var name = StripGenerics(typeName.Trim());
        var isQualified = name.Contains('.');
        return Distinct(FrameworkTypes
            .Where(entry => isQualified ? entry.FullName == name : entry.FullName.EndsWith($".{name}", StringComparison.Ordinal))
            .Select(entry => Hint(entry.Tool, entry.Reason))
            .ToList());
    }

    public static IReadOnlyList<ToolHint> ForDecompiledMember() =>
        [Hint("decompile_type", "decompiles the whole declaring type when one member is not enough context")];

    public static IReadOnlyList<ToolHint> ForProjectOutputs() =>
    [
        Hint("analyze_project", "shows the project's target frameworks, references and build properties"),
        Hint("get_package_graph", "lists the exact package versions the project restored, direct and transitive")
    ];

    private static string StripGenerics(string typeName) =>
        typeName.IndexOfAny(['<', '`', '[']) is var generic and >= 0 ? typeName[..generic] : typeName;

    private static List<ToolHint> Distinct(List<ToolHint> hints) =>
        hints.DistinctBy(hint => hint.Tool, StringComparer.Ordinal).ToList();
}
