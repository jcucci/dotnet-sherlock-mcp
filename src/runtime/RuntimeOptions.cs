using Sherlock.MCP.Runtime.SourceLink;

namespace Sherlock.MCP.Runtime;

public class RuntimeOptions
{
    public RuntimeOptions()
    {
        SearchRoots = new List<string>();
        DefaultMaxItems = 50;
        CacheTtlSeconds = 300;
        IncludeNonPublicByDefault = false;
        MaxLoadedAssemblies = 64;
        MaxCachedResponses = 256;
        MaxAssemblyHandles = 256;
        StateDirectory = DefaultStateDirectory();
        SourceFetch = DefaultSourceFetch();
        SourceFetchHosts = KnownSourceHosts;

        ToolSpecificMaxItems = new Dictionary<string, int>(ToolNameComparer.Instance)
        {
            ["get_type_members"] = 50,       // Mixed kinds; summary items are small
            ["get_type_methods"] = 25,       // Methods have parameters, large payloads
            ["get_type_properties"] = 40,    // Properties moderately sized
            ["get_type_fields"] = 75,        // Fields are compact
            ["get_type_events"] = 50,        // Events moderately sized
            ["get_type_constructors"] = 30,  // Constructors have parameters
            ["get_all_type_members"] = 20,    // Combined view, keep small
            ["analyze_assembly"] = 50,      // Type summaries
            ["analyze_type"] = 25,          // Combined members
            ["get_types_from_assembly"] = 50, // Type summaries
            ["find_implementations_of"] = 50,
            ["find_methods_returning"] = 50,
            ["find_references_to"] = 25,     // Broader sweep, keep smaller
            ["search_members"] = 50
        };
    }

    public List<string> SearchRoots { get; }

    public int DefaultMaxItems { get; set; }

    public int CacheTtlSeconds { get; set; }

    public bool IncludeNonPublicByDefault { get; set; }

    public int MaxLoadedAssemblies { get; set; }

    public int MaxCachedResponses { get; set; }

    public int MaxAssemblyHandles { get; set; }

    public string StateDirectory { get; set; }

    public SourceFetchMode SourceFetch { get; set; }

    public IReadOnlyList<string> SourceFetchHosts { get; set; }

    public static readonly IReadOnlyList<string> KnownSourceHosts =
    [
        "raw.githubusercontent.com",
        "gitlab.com",
        "bitbucket.org",
        "api.bitbucket.org",
        "dev.azure.com",
        "*.visualstudio.com"
    ];

    public static bool TryParseSourceFetch(string? value, out SourceFetchMode mode)
    {
        mode = value?.Trim().ToLowerInvariant() switch
        {
            "off" or "false" or "0" or "none" => SourceFetchMode.Off,
            "known-hosts" or "knownhosts" or "known" => SourceFetchMode.KnownHosts,
            "any" or "anyhost" or "any-host" or "true" or "1" => SourceFetchMode.AnyHost,
            _ => (SourceFetchMode)(-1)
        };
        return Enum.IsDefined(mode);
    }

    public Dictionary<string, int> ToolSpecificMaxItems { get; }

    public int GetMaxItemsForTool(string toolName)
    {
        // If tool has a specific default, use it - unless DefaultMaxItems was explicitly reduced
        if (ToolSpecificMaxItems.TryGetValue(toolName, out var specific))
            return DefaultMaxItems < 50 ? Math.Min(DefaultMaxItems, specific) : specific;

        return DefaultMaxItems;
    }

    private static SourceFetchMode DefaultSourceFetch() =>
        TryParseSourceFetch(Environment.GetEnvironmentVariable("SHERLOCK_SOURCE_FETCH"), out var mode)
            ? mode
            : SourceFetchMode.KnownHosts;

    private static string DefaultStateDirectory() =>
        Environment.GetEnvironmentVariable("SHERLOCK_STATE_DIR") is { Length: > 0 } configured
            ? configured
            : Path.Combine(LocalApplicationDataOrTemp(), "sherlock");

    private static string LocalApplicationDataOrTemp() =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) is { Length: > 0 } localData
            ? localData
            : Path.GetTempPath();
}
