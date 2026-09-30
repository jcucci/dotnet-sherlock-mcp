namespace Sherlock.MCP.Runtime;

/// <summary>
/// Treats a tool's C# method name and its snake_case wire name as the same key,
/// so "GetTypeMethods" and "get_type_methods" address one entry.
/// </summary>
public sealed class ToolNameComparer : IEqualityComparer<string>
{
    public static readonly ToolNameComparer Instance = new();

    private ToolNameComparer() { }

    public bool Equals(string? x, string? y) => string.Equals(Normalize(x), Normalize(y), StringComparison.Ordinal);

    public int GetHashCode(string obj) => Normalize(obj)!.GetHashCode(StringComparison.Ordinal);

    private static string? Normalize(string? name) =>
        name?.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
}
