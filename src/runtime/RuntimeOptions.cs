namespace Sherlock.MCP.Runtime;

public class RuntimeOptions
{
    public RuntimeOptions()
    {
        SearchRoots = new List<string>();
        DefaultMaxItems = 50;
        CacheTtlSeconds = 300;
        EnableStreaming = false;
        IncludeNonPublicByDefault = false;
        MaxLoadedAssemblies = 64;
        MaxCachedResponses = 256;

        ToolSpecificMaxItems = new Dictionary<string, int>
        {
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

    public bool EnableStreaming { get; set; }

    public bool IncludeNonPublicByDefault { get; set; }

    public int MaxLoadedAssemblies { get; set; }

    public int MaxCachedResponses { get; set; }

    public Dictionary<string, int> ToolSpecificMaxItems { get; }

    public int GetMaxItemsForTool(string toolName)
    {
        // If tool has a specific default, use it - unless DefaultMaxItems was explicitly reduced
        if (ToolSpecificMaxItems.TryGetValue(toolName, out var specific))
            return DefaultMaxItems < 50 ? Math.Min(DefaultMaxItems, specific) : specific;

        return DefaultMaxItems;
    }
}

