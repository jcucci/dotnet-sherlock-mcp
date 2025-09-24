using System.Collections.Concurrent;

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
    }

    public List<string> SearchRoots { get; }

    public int DefaultMaxItems { get; set; }

    public int CacheTtlSeconds { get; set; }

    public bool EnableStreaming { get; set; }

    public bool IncludeNonPublicByDefault { get; set; }
}

