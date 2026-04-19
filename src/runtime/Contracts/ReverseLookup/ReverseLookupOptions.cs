namespace Sherlock.MCP.Runtime.Contracts.ReverseLookup;

public record ReverseLookupOptions(
    bool CaseSensitive = false,
    bool IncludeNonPublic = false,
    int HardCap = 500);
