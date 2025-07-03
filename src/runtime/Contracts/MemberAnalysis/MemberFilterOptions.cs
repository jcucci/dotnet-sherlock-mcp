namespace Sherlock.MCP.Runtime.Contracts.MemberAnalysis;

public class MemberFilterOptions
{
    public bool IncludePublic { get; set; } = true;
    public bool IncludeNonPublic { get; set; } = false;
    public bool IncludeStatic { get; set; } = true;
    public bool IncludeInstance { get; set; } = true;
    public bool IncludeInherited { get; set; } = false;
    public bool IncludeDeclaredOnly { get; set; } = true;

    /// <summary>
    /// Creates a MemberFilterOptions instance with the specified settings
    /// </summary>
    /// <param name="includePublic">Include public members</param>
    /// <param name="includeNonPublic">Include non-public members</param>
    /// <param name="includeStatic">Include static members</param>
    /// <param name="includeInstance">Include instance members</param>
    /// <returns>Configured MemberFilterOptions</returns>
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