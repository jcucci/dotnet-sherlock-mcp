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

        ToolSpecificMaxItems = new Dictionary<string, int>
        {
            ["GetTypeMethods"] = 25,       // Methods have parameters, large payloads
            ["GetTypeProperties"] = 40,    // Properties moderately sized
            ["GetTypeFields"] = 75,        // Fields are compact
            ["GetTypeEvents"] = 50,        // Events moderately sized
            ["GetTypeConstructors"] = 30,  // Constructors have parameters
            ["GetAllTypeMembers"] = 20,    // Combined view, keep small
            ["AnalyzeAssembly"] = 50,      // Type summaries
            ["AnalyzeType"] = 25,          // Combined members
            ["GetTypesFromAssembly"] = 50  // Type summaries
        };
    }

    public List<string> SearchRoots { get; }

    public int DefaultMaxItems { get; set; }

    public int CacheTtlSeconds { get; set; }

    public bool EnableStreaming { get; set; }

    public bool IncludeNonPublicByDefault { get; set; }

    public Dictionary<string, int> ToolSpecificMaxItems { get; }

    public int GetMaxItemsForTool(string toolName)
    {
        // If tool has a specific default, use it - unless DefaultMaxItems was explicitly reduced
        if (ToolSpecificMaxItems.TryGetValue(toolName, out var specific))
            return DefaultMaxItems < 50 ? Math.Min(DefaultMaxItems, specific) : specific;

        return DefaultMaxItems;
    }
}

