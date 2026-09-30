namespace Sherlock.MCP.Runtime.Inspection;

internal static class PathComparers
{
    private static readonly bool IsCaseInsensitiveFileSystem = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public static StringComparison Comparison =>
        IsCaseInsensitiveFileSystem ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static StringComparer Comparer =>
        IsCaseInsensitiveFileSystem ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
