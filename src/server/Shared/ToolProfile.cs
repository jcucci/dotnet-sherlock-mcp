namespace Sherlock.MCP.Server.Shared;

public sealed class ToolProfile
{
    public const string EnvironmentVariable = "SHERLOCK_TOOL_PROFILE";
    public const string CommandLineFlag = "--profile";

    public static readonly string[] CoreToolNames =
    [
        "find_assembly_by_class_name",
        "find_assembly_by_file_name",
        "find_assembly_by_nuget_package",
        "get_project_output_paths",
        "get_assembly_info",
        "get_types_from_assembly",
        "get_type_info",
        "get_type_hierarchy",
        "get_type_members",
        "search_members",
        "analyze_method",
        "get_xml_docs_for_type",
        "get_xml_docs_for_member",
        "find_implementations_of",
        "find_methods_returning",
        "find_extension_methods_for",
        "find_references_to",
        "get_method_calls"
    ];

    public static readonly ToolProfile Full = new("full", allowedTools: null);
    public static readonly ToolProfile Core = new("core", new HashSet<string>(CoreToolNames, StringComparer.Ordinal));

    private readonly IReadOnlySet<string>? _allowedTools;

    private ToolProfile(string name, IReadOnlySet<string>? allowedTools)
    {
        Name = name;
        _allowedTools = allowedTools;
    }

    public string Name { get; }

    public bool IsRestricted => _allowedTools is not null;

    public bool Includes(string toolName) => _allowedTools?.Contains(toolName) ?? true;

    public static bool TryResolve(IReadOnlyList<string> args, string? environmentValue, out ToolProfile profile, out string? error)
    {
        var fromArguments = FromArguments(args);
        var requested = fromArguments ?? environmentValue;
        profile = Full;
        error = null;

        if (fromArguments is null && string.IsNullOrWhiteSpace(requested))
            return true;

        switch ((requested ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "full":
                return true;
            case "core":
                profile = Core;
                return true;
            default:
                error = $"Unknown tool profile '{requested}'. Valid profiles: full, core (set via {CommandLineFlag} or {EnvironmentVariable}).";
                return false;
        }
    }

    private static string? FromArguments(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].StartsWith(CommandLineFlag + "=", StringComparison.Ordinal))
                return args[i][(CommandLineFlag.Length + 1)..];
            if (args[i] == CommandLineFlag)
                return i + 1 < args.Count ? args[i + 1] : string.Empty;
        }

        return null;
    }
}
