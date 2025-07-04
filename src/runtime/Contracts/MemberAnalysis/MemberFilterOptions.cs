namespace Sherlock.MCP.Runtime.Contracts.MemberAnalysis;
public class MemberFilterOptions
{
    public bool IncludePublic { get; set; } = true;
    public bool IncludeNonPublic { get; set; } = false;
    public bool IncludeStatic { get; set; } = true;
    public bool IncludeInstance { get; set; } = true;
    public bool IncludeInherited { get; set; } = false;
    public bool IncludeDeclaredOnly { get; set; } = true;
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