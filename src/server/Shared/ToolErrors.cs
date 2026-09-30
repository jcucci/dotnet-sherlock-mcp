using System.Reflection;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Server.Shared;

public static class ToolErrors
{
    private static readonly string[] AssemblyDiscoveryTools =
    [
        "find_assembly_by_class_name",
        "find_assembly_by_file_name",
        "find_assembly_by_nuget_package",
        "get_project_output_paths"
    ];

    private static readonly string[] TypeDiscoveryTools = ["search_members", "get_types_from_assembly"];

    private const string AdditionalAssembliesHint =
        "Point assemblyPath at a copy of the assembly in a build-output folder (e.g. bin/Debug/<tfm>/Name.dll) whose sibling DLLs include these dependencies; tools that accept additionalAssemblies can instead be given the dependency DLL paths there.";

    private static readonly string[] AssemblyExtensions = [".dll", ".exe"];

    public static string AssemblyNotFound(string assemblyPath)
    {
        var similarFiles = SimilarFiles(assemblyPath);
        return JsonHelpers.ErrorWithGuidance(
            "AssemblyNotFound",
            $"Assembly file not found: {assemblyPath}",
            suggestion: similarFiles.Length > 0
                ? "Check the path; similar assemblies exist in the same directory (see recommendedParams.similarFiles)."
                : "Locate the assembly first: search by a class it contains, by file name, by NuGet package, or from a project's build output.",
            alternativeTools: AssemblyDiscoveryTools,
            recommendedParams: similarFiles.Length > 0 ? new { similarFiles } : null);
    }

    public static string TypeNotFound(IInspectionContextProvider contexts, string assemblyPath, string typeName)
    {
        try
        {
            using var lease = contexts.Acquire(assemblyPath);
            return TypeNotFound(lease.Context, typeName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return TypeNotFound(typeName, candidates: [], unresolvedDependencies: []);
        }
    }

    public static string TypeNotFound(IAssemblyInspectionContext context, string typeName, IEnumerable<Type>? searchedTypes = null)
    {
        var loadableTypes = LoadableTypes(context);
        var candidates = SafeSuggest(() => NameSuggestions.ForTypes(searchedTypes ?? loadableTypes, typeName));
        return TypeNotFound(typeName, candidates, context.UnresolvedDependencies);
    }

    public static string MemberNotFound(
        Type type,
        string memberName,
        string? memberKind = null,
        string? message = null,
        BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
    {
        var candidates = SafeSuggest(() => NameSuggestions.ForMembers(type, memberName, memberKind, bindingFlags));
        return JsonHelpers.ErrorWithGuidance(
            "MemberNotFound",
            message ?? $"Member '{memberName}' not found in type '{type.FullName ?? type.Name}'",
            suggestion: candidates.Count > 0
                ? $"Did you mean {FormatChoices(candidates)}?"
                : "List the type's members to find the exact name.",
            alternativeTools: MemberDiscoveryTools(memberKind),
            recommendedParams: candidates.Count > 0 ? new { candidates } : null);
    }

    public static string FromException(Exception ex, string operation, string fileNotFoundCode = "DependencyNotFound") => ex switch
    {
        DependencyResolutionException dependency => JsonHelpers.ErrorWithGuidance(
            "DependencyResolutionFailed",
            dependency.Message,
            suggestion: DependencyResolutionHint(dependency.UnresolvedDependencies)),
        BadImageFormatException => JsonHelpers.ErrorWithGuidance(
            "InvalidAssembly",
            $"Failed to {operation}: {ex.Message}",
            suggestion: "The file is not a managed .NET assembly (it may be a native DLL or a corrupt file). Point assemblyPath at the managed .dll/.exe produced by the build."),
        FileLoadException => DependencyNotFound(ex, operation),
        FileNotFoundException when fileNotFoundCode == "DependencyNotFound" => DependencyNotFound(ex, operation),
        FileNotFoundException => JsonHelpers.ErrorWithGuidance(
            fileNotFoundCode,
            $"Failed to {operation}: {ex.Message}",
            suggestion: "Check the path; it must point at an existing file."),
        UnauthorizedAccessException => JsonHelpers.ErrorWithGuidance(
            "AccessDenied",
            $"Failed to {operation}: {ex.Message}",
            suggestion: "The server process cannot read this path. Check file permissions or copy the file somewhere readable."),
        ArgumentException => JsonHelpers.Error("InvalidArgument", $"Failed to {operation}: {ex.Message}"),
        _ => JsonHelpers.Error("InternalError", $"Failed to {operation}: {ex.Message}")
    };

    private static string DependencyNotFound(Exception ex, string operation) =>
        JsonHelpers.ErrorWithGuidance(
            "DependencyNotFound",
            $"Failed to {operation}: {ex.Message}",
            suggestion: $"A referenced assembly{FileNameClause(ex)} could not be loaded. {AdditionalAssembliesHint}",
            alternativeTools: AssemblyDiscoveryTools);

    private static string TypeNotFound(string typeName, IReadOnlyList<string> candidates, IReadOnlyList<string> unresolvedDependencies)
    {
        var message = $"Type '{typeName}' not found in assembly";
        if (unresolvedDependencies.Count > 0 && candidates.Count == 0)
            return JsonHelpers.ErrorWithGuidance(
                "DependencyResolutionFailed",
                $"{message}; some of its types could not be loaded because dependencies are missing: {string.Join(", ", unresolvedDependencies)}.",
                suggestion: DependencyResolutionHint(unresolvedDependencies),
                details: new { unresolvedDependencies });

        var suggestion = candidates.Count > 0
            ? $"Did you mean {FormatChoices(candidates)}? Retry with one of recommendedParams.candidates as typeName."
            : "Browse the assembly's types or search by member name to find the declaring type.";
        if (unresolvedDependencies.Count > 0)
            suggestion += $" If none of these is the type you meant, it may have been skipped because dependencies are missing: {DependencyResolutionHint(unresolvedDependencies)}";

        return JsonHelpers.ErrorWithGuidance(
            "TypeNotFound",
            message,
            suggestion: suggestion,
            alternativeTools: TypeDiscoveryTools,
            recommendedParams: candidates.Count > 0 ? new { candidates } : null,
            details: unresolvedDependencies.Count > 0 ? new { unresolvedDependencies } : null);
    }

    private static string DependencyResolutionHint(IReadOnlyList<string> unresolvedDependencies) =>
        $"The dependencies ({string.Join(", ", unresolvedDependencies)}) were not found next to the assembly or in the NuGet cache. {AdditionalAssembliesHint}";

    private static string[] MemberDiscoveryTools(string? memberKind) => memberKind?.ToLowerInvariant() switch
    {
        "method" => ["get_type_methods", "search_members"],
        "property" => ["get_type_properties", "search_members"],
        "field" => ["get_type_fields", "search_members"],
        "event" => ["get_type_events", "search_members"],
        "constructor" => ["get_type_constructors"],
        _ => ["search_members"]
    };

    private static string[] SimilarFiles(string assemblyPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return [];

            var files = Directory.EnumerateFiles(directory)
                .Where(file => AssemblyExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                .GroupBy(file => Path.GetFileNameWithoutExtension(file), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderBy(file => IsDll(file) ? 0 : 1).First(),
                    StringComparer.OrdinalIgnoreCase);
            return NameSuggestions.Closest(files.Keys, Path.GetFileNameWithoutExtension(assemblyPath), compareSimpleNames: false)
                .Select(name => files[name])
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }

    private static bool IsDll(string file) =>
        string.Equals(Path.GetExtension(file), ".dll", StringComparison.OrdinalIgnoreCase);

    private static Type[] LoadableTypes(IAssemblyInspectionContext context)
    {
        try
        {
            return context.GetTypes().ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> SafeSuggest(Func<IReadOnlyList<string>> suggest)
    {
        try
        {
            return suggest();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }
    }

    private static string FormatChoices(IReadOnlyList<string> candidates) =>
        string.Join(" or ", candidates.Take(3).Select(candidate => $"'{candidate}'"));

    private static string FileNameClause(Exception ex) => ex switch
    {
        FileNotFoundException { FileName: { Length: > 0 } name } => $" ('{name}')",
        FileLoadException { FileName: { Length: > 0 } name } => $" ('{name}')",
        _ => ""
    };
}
