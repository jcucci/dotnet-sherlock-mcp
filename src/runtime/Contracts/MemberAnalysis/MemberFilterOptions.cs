namespace Sherlock.MCP.Runtime.Contracts.MemberAnalysis;

public class MemberFilterOptions
{
    public bool IncludePublic { get; set; } = true;
    public bool IncludeNonPublic { get; set; } = false;
    public bool IncludeStatic { get; set; } = true;
    public bool IncludeInstance { get; set; } = true;
    public bool IncludeInherited { get; set; } = false;
    public bool IncludeDeclaredOnly { get; set; } = true;
    public bool CaseSensitive { get; set; } = true;
    public string? NameContains { get; set; }
    public string? HasAttributeContains { get; set; }
    public int? Skip { get; set; }
    public int? Take { get; set; }
    public string SortBy { get; set; } = "name"; // name | access | kind
    public string SortOrder { get; set; } = "asc"; // asc | desc

    public static MemberFilterOptions Create(
        bool includePublic = true,
        bool includeNonPublic = false,
        bool includeStatic = true,
        bool includeInstance = true) =>
        new()
        {
            IncludePublic = includePublic,
            IncludeNonPublic = includeNonPublic,
            IncludeStatic = includeStatic,
            IncludeInstance = includeInstance
        };
}
